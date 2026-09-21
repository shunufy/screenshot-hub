using System.IO;
using System.Text.Json;

namespace ScreenshotHub.Core;

public sealed class ScreenshotUserDataStore
{
    public const int CurrentFormatVersion = 1;
    private const long MaximumStoreBytes = 64L * 1024 * 1024;
    private const int MaximumEntries = 500_000;
    private const int MaximumTagsPerImage = 32;
    private const int MaximumTagLength = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _storePath;
    private readonly string _backupPath;

    public ScreenshotUserDataStore(string? storePath = null)
    {
        _storePath = Path.GetFullPath(storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotHub",
            $"user-data-v{CurrentFormatVersion}.json"));
        _backupPath = _storePath + ".backup";
    }

    public static string? ResolvePathForSettings(string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ??
                        throw new InvalidOperationException(AppText.InvalidSettingsPath);
        return Path.Combine(directory, $"user-data-v{CurrentFormatVersion}.json");
    }

    public async Task<ScreenshotUserDataCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TryLoadAsync(_storePath, cancellationToken).ConfigureAwait(false) ??
                   await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false) ??
                   new ScreenshotUserDataCatalog { FormatVersion = CurrentFormatVersion };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ScreenshotUserDataCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var normalized = Normalize(catalog) ??
                         throw new InvalidDataException("The screenshot user data is invalid.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_storePath) ??
                            throw new InvalidOperationException(AppText.InvalidSettingsPath);
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_storePath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             32 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length > MaximumStoreBytes)
            {
                throw new InvalidDataException("The screenshot user data is too large.");
            }

            if (!File.Exists(_storePath))
            {
                File.Move(temporaryPath, _storePath);
                temporaryPath = null;
                return;
            }

            var currentIsValid = await TryLoadAsync(_storePath, cancellationToken).ConfigureAwait(false) is not null;
            File.Replace(temporaryPath, _storePath, currentIsValid ? _backupPath : null, ignoreMetadataErrors: true);
            temporaryPath = null;
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
            _gate.Release();
        }
    }

    public static ScreenshotUserDataCatalog Create(IEnumerable<ScreenshotUserData> items)
        => Normalize(new ScreenshotUserDataCatalog
        {
            FormatVersion = CurrentFormatVersion,
            Items = items.ToList()
        }) ?? throw new InvalidDataException("The screenshot user data is invalid.");

    private static async Task<ScreenshotUserDataCatalog?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaximumStoreBytes)
            {
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var catalog = await JsonSerializer.DeserializeAsync<ScreenshotUserDataCatalog>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return catalog is null ? null : Normalize(catalog);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static ScreenshotUserDataCatalog? Normalize(ScreenshotUserDataCatalog catalog)
    {
        if (catalog.FormatVersion != CurrentFormatVersion ||
            catalog.Items is null ||
            catalog.Items.Count > MaximumEntries)
        {
            return null;
        }

        var items = new Dictionary<string, ScreenshotUserData>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            if (item is null)
            {
                return null;
            }

            var path = PathUtility.Normalize(item.FilePath);
            if (path is null || PathUtility.IsDriveRoot(path) || !ScreenshotScanner.IsSupportedImagePath(path))
            {
                return null;
            }

            var tags = (item.Tags ?? [])
                .Select(NormalizeTag)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(MaximumTagsPerImage)
                .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var updatedUtc = item.UpdatedUtc == default
                ? DateTime.UnixEpoch
                : item.UpdatedUtc.Kind switch
                {
                    DateTimeKind.Utc => item.UpdatedUtc,
                    DateTimeKind.Local => item.UpdatedUtc.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(item.UpdatedUtc, DateTimeKind.Utc)
                };

            if (!item.IsFavorite && tags.Count == 0)
            {
                continue;
            }

            items[path] = new ScreenshotUserData
            {
                FilePath = path,
                IsFavorite = item.IsFavorite,
                Tags = tags,
                UpdatedUtc = updatedUtc
            };
        }

        return new ScreenshotUserDataCatalog
        {
            FormatVersion = CurrentFormatVersion,
            Items = items.Values
                .OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var safe = new string(tag.Trim().Where(character => !char.IsControl(character)).ToArray());
        return safe.Length <= MaximumTagLength ? safe : safe[..MaximumTagLength];
    }

    private static void DeleteTemporaryFile(string? temporaryPath)
    {
        if (temporaryPath is null)
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch
        {
            // A later save uses a unique temporary name.
        }
    }
}
