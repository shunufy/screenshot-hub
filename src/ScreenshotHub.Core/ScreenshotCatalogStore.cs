using System.IO;
using System.Text.Json;

namespace ScreenshotHub.Core;

public sealed class ScreenshotCatalogStore
{
    public const int CurrentFormatVersion = 1;
    private const long MaximumCatalogBytes = 128L * 1024 * 1024;
    private const int MaximumScreenshotRecords = 500_000;
    private const int MaximumRoots = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _catalogPath;
    private readonly string _backupPath;
    private bool _primaryKnownGood;
    private long _primaryKnownGoodLength;
    private DateTime _primaryKnownGoodLastWriteUtc;

    public ScreenshotCatalogStore(string? catalogPath = null)
    {
        _catalogPath = Path.GetFullPath(catalogPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotHub",
            $"catalog-v{CurrentFormatVersion}-{AppText.LanguageCode}.json"));
        _backupPath = _catalogPath + ".backup";
    }

    public static string? ResolvePathForSettings(string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            return null;
        }

        var fullSettingsPath = Path.GetFullPath(settingsPath);
        var directory = Path.GetDirectoryName(fullSettingsPath) ??
                        throw new InvalidOperationException(AppText.InvalidSettingsPath);
        return Path.Combine(
            directory,
            $"catalog-v{CurrentFormatVersion}-{AppText.LanguageCode}.json");
    }

    public static ScreenshotCatalog Create(
        IReadOnlyCollection<ScreenshotRecord> screenshots,
        IReadOnlyCollection<ScanRoot> roots,
        DateTime completedUtc)
        => new()
        {
            FormatVersion = CurrentFormatVersion,
            LastSuccessfulScanUtc = completedUtc.Kind == DateTimeKind.Utc
                ? completedUtc
                : completedUtc.ToUniversalTime(),
            Screenshots = screenshots.ToList(),
            Roots = roots.Select(root => new CatalogRoot(
                    root.Path,
                    root.DisplayName,
                    root.IsCustom,
                    root.GameNameHint))
                .ToList()
        };

    public async Task<ScreenshotCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primary = await TryLoadAsync(_catalogPath, cancellationToken).ConfigureAwait(false);
            if (primary is not null)
            {
                MarkPrimaryKnownGood();
            }
            else
            {
                ClearPrimaryKnownGood();
            }

            return primary ?? await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ScreenshotCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var normalized = await Task.Run(
                () => Normalize(catalog),
                cancellationToken)
            .ConfigureAwait(false) ??
                         throw new InvalidDataException("The screenshot catalog is invalid.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_catalogPath) ??
                            throw new InvalidOperationException(AppText.InvalidSettingsPath);
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_catalogPath)}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length > MaximumCatalogBytes)
            {
                throw new InvalidDataException("The screenshot catalog is too large.");
            }

            if (!File.Exists(_catalogPath))
            {
                File.Move(temporaryPath, _catalogPath);
                temporaryPath = null;
                MarkPrimaryKnownGood();
                return;
            }

            var currentIsValid = PrimaryMatchesKnownGood() ||
                                 await TryLoadAsync(_catalogPath, cancellationToken).ConfigureAwait(false) is not null;
            File.Replace(
                temporaryPath,
                _catalogPath,
                currentIsValid ? _backupPath : null,
                ignoreMetadataErrors: true);
            temporaryPath = null;
            MarkPrimaryKnownGood();
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

    private bool PrimaryMatchesKnownGood()
    {
        if (!_primaryKnownGood)
        {
            return false;
        }

        try
        {
            var info = new FileInfo(_catalogPath);
            return info.Exists &&
                   info.Length == _primaryKnownGoodLength &&
                   info.LastWriteTimeUtc == _primaryKnownGoodLastWriteUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void MarkPrimaryKnownGood()
    {
        var info = new FileInfo(_catalogPath);
        info.Refresh();
        _primaryKnownGood = info.Exists;
        _primaryKnownGoodLength = info.Exists ? info.Length : 0;
        _primaryKnownGoodLastWriteUtc = info.Exists ? info.LastWriteTimeUtc : default;
    }

    private void ClearPrimaryKnownGood()
    {
        _primaryKnownGood = false;
        _primaryKnownGoodLength = 0;
        _primaryKnownGoodLastWriteUtc = default;
    }

    private static async Task<ScreenshotCatalog?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaximumCatalogBytes)
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
            var catalog = await JsonSerializer.DeserializeAsync<ScreenshotCatalog>(
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

    private static ScreenshotCatalog? Normalize(ScreenshotCatalog catalog)
    {
        if (catalog.FormatVersion != CurrentFormatVersion ||
            catalog.LastSuccessfulScanUtc == default ||
            catalog.Screenshots is null ||
            catalog.Roots is null ||
            catalog.Screenshots.Count > MaximumScreenshotRecords ||
            catalog.Roots.Count > MaximumRoots)
        {
            return null;
        }

        var roots = new Dictionary<string, CatalogRoot>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in catalog.Roots)
        {
            if (root is null)
            {
                return null;
            }

            var path = PathUtility.Normalize(root.Path);
            if (path is null || PathUtility.IsDriveRoot(path))
            {
                return null;
            }

            var displayName = string.IsNullOrWhiteSpace(root.DisplayName)
                ? Path.GetFileName(path)
                : root.DisplayName.Trim();
            if (!roots.TryGetValue(path, out var existing))
            {
                roots[path] = root with
                {
                    Path = path,
                    DisplayName = displayName
                };
            }
            else
            {
                roots[path] = existing with
                {
                    IsCustom = existing.IsCustom || root.IsCustom,
                    GameNameHint = existing.GameNameHint ?? root.GameNameHint
                };
            }
        }

        var screenshots = new Dictionary<string, ScreenshotRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in catalog.Screenshots)
        {
            if (record is null)
            {
                return null;
            }

            var filePath = PathUtility.Normalize(record.FilePath);
            var libraryFolder = PathUtility.Normalize(record.LibraryFolder);
            if (filePath is null ||
                libraryFolder is null ||
                !PathUtility.IsSameOrDescendant(filePath, libraryFolder) ||
                !ScreenshotScanner.IsSupportedImagePath(filePath) ||
                record.FileSize <= 0)
            {
                return null;
            }

            var lastWriteTimeUtc = record.LastWriteTimeUtc.Kind switch
            {
                DateTimeKind.Utc => record.LastWriteTimeUtc,
                DateTimeKind.Local => record.LastWriteTimeUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(record.LastWriteTimeUtc, DateTimeKind.Utc)
            };
            var gameName = string.IsNullOrWhiteSpace(record.GameName)
                ? Path.GetFileName(libraryFolder)
                : record.GameName.Trim();
            screenshots[filePath] = new ScreenshotRecord(
                filePath,
                libraryFolder,
                gameName,
                lastWriteTimeUtc,
                record.FileSize);
        }

        var completedUtc = catalog.LastSuccessfulScanUtc.Kind switch
        {
            DateTimeKind.Utc => catalog.LastSuccessfulScanUtc,
            DateTimeKind.Local => catalog.LastSuccessfulScanUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(catalog.LastSuccessfulScanUtc, DateTimeKind.Utc)
        };
        return new ScreenshotCatalog
        {
            FormatVersion = CurrentFormatVersion,
            LastSuccessfulScanUtc = completedUtc,
            Roots = roots.Values
                .OrderByDescending(root => root.IsCustom)
                .ThenBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Screenshots = screenshots.Values
                .OrderByDescending(record => record.LastWriteTimeUtc)
                .ThenBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
