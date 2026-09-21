using System.IO;
using System.Text.Json;

namespace ScreenshotHub.Core;

public sealed class ScreenshotAnalysisStore
{
    public const int CurrentFormatVersion = 1;
    private const long MaximumStoreBytes = 192L * 1024 * 1024;
    private const int MaximumEntries = 500_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _storePath;
    private readonly string _backupPath;

    public ScreenshotAnalysisStore(string? storePath = null)
    {
        _storePath = Path.GetFullPath(storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotHub",
            $"analysis-v{CurrentFormatVersion}.json"));
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
        return Path.Combine(directory, $"analysis-v{CurrentFormatVersion}.json");
    }

    public async Task<ScreenshotAnalysisCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TryLoadAsync(_storePath, cancellationToken).ConfigureAwait(false) ??
                   await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false) ??
                   new ScreenshotAnalysisCatalog { FormatVersion = CurrentFormatVersion };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ScreenshotAnalysisCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var normalized = Normalize(catalog) ??
                         throw new InvalidDataException("The screenshot analysis cache is invalid.");

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
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length > MaximumStoreBytes)
            {
                throw new InvalidDataException("The screenshot analysis cache is too large.");
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
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A later save uses a unique temporary name.
                }
            }

            _gate.Release();
        }
    }

    public static ScreenshotAnalysisCatalog Create(
        IEnumerable<ScreenshotAnalysisRecord> items,
        DateTime completedUtc)
        => Normalize(new ScreenshotAnalysisCatalog
        {
            FormatVersion = CurrentFormatVersion,
            LastAnalysisUtc = completedUtc,
            Items = items.ToList()
        }) ?? throw new InvalidDataException("The screenshot analysis cache is invalid.");

    private static async Task<ScreenshotAnalysisCatalog?> TryLoadAsync(
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
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var catalog = await JsonSerializer.DeserializeAsync<ScreenshotAnalysisCatalog>(
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

    private static ScreenshotAnalysisCatalog? Normalize(ScreenshotAnalysisCatalog catalog)
    {
        if (catalog.FormatVersion != CurrentFormatVersion ||
            catalog.Items is null ||
            catalog.Items.Count > MaximumEntries)
        {
            return null;
        }

        var items = new Dictionary<string, ScreenshotAnalysisRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            if (item is null)
            {
                return null;
            }

            var path = PathUtility.Normalize(item.FilePath);
            if (path is null ||
                !ScreenshotScanner.IsSupportedImagePath(path) ||
                item.FileSize <= 0 ||
                item.PixelWidth < 0 ||
                item.PixelHeight < 0 ||
                (item.Sha256 is not null && !IsSha256(item.Sha256)))
            {
                return null;
            }

            var lastWriteUtc = item.LastWriteTimeUtc.Kind switch
            {
                DateTimeKind.Utc => item.LastWriteTimeUtc,
                DateTimeKind.Local => item.LastWriteTimeUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(item.LastWriteTimeUtc, DateTimeKind.Utc)
            };
            items[path] = item with
            {
                FilePath = path,
                LastWriteTimeUtc = lastWriteUtc,
                Sha256 = item.Sha256?.ToUpperInvariant()
            };
        }

        var completedUtc = catalog.LastAnalysisUtc == default
            ? DateTime.UnixEpoch
            : catalog.LastAnalysisUtc.Kind switch
            {
                DateTimeKind.Utc => catalog.LastAnalysisUtc,
                DateTimeKind.Local => catalog.LastAnalysisUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(catalog.LastAnalysisUtc, DateTimeKind.Utc)
            };
        return new ScreenshotAnalysisCatalog
        {
            FormatVersion = CurrentFormatVersion,
            LastAnalysisUtc = completedUtc,
            Items = items.Values
                .OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
