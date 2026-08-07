using System.IO;
using System.Text.Json;

namespace ScreenshotHub.Core;

public sealed class ScreenshotFolderCatalogStore
{
    public const int CurrentFormatVersion = 1;
    private const long MaximumCatalogBytes = 64L * 1024 * 1024;
    private const int MaximumFolders = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _catalogPath;
    private readonly string _backupPath;

    public ScreenshotFolderCatalogStore(string? catalogPath = null)
    {
        _catalogPath = Path.GetFullPath(catalogPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotHub",
            $"folders-v{CurrentFormatVersion}-{AppText.LanguageCode}.json"));
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
            $"folders-v{CurrentFormatVersion}-{AppText.LanguageCode}.json");
    }

    public static ScreenshotFolderCatalog Create(
        IReadOnlyCollection<ScreenshotRecord> screenshots,
        IReadOnlyCollection<ScanRoot> roots,
        DateTime completedUtc)
    {
        ArgumentNullException.ThrowIfNull(screenshots);
        ArgumentNullException.ThrowIfNull(roots);
        var rootsBySpecificity = roots
            .OrderByDescending(root => root.Path.Length)
            .ToArray();
        var customRoots = roots
            .Where(root => root.IsCustom)
            .Select(root => root.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var folders = screenshots
            .GroupBy(record => record.LibraryFolder, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var matchingRoot = rootsBySpecificity.FirstOrDefault(root =>
                    PathUtility.IsSameOrDescendant(group.Key, root.Path));
                var recordName = group.Select(record => record.GameName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                var displayName = !string.IsNullOrWhiteSpace(matchingRoot?.GameNameHint)
                    ? matchingRoot.GameNameHint!
                    : matchingRoot is not null &&
                      string.Equals(group.Key, matchingRoot.Path, StringComparison.OrdinalIgnoreCase)
                        ? matchingRoot.DisplayName
                        : recordName ?? Path.GetFileName(group.Key);
                return new FolderCatalogEntry(
                    group.Key,
                    displayName,
                    customRoots.Any(root => PathUtility.IsSameOrDescendant(group.Key, root)),
                    group.Count(),
                    group.Max(record => record.LastWriteTimeUtc));
            })
            .ToList();

        foreach (var root in roots.Where(root => root.IsCustom))
        {
            if (folders.Any(folder => PathUtility.IsSameOrDescendant(folder.Path, root.Path)))
            {
                continue;
            }

            folders.Add(new FolderCatalogEntry(
                root.Path,
                root.DisplayName,
                root.IsCustom,
                0,
                null));
        }

        return new ScreenshotFolderCatalog
        {
            FormatVersion = CurrentFormatVersion,
            LastSuccessfulScanUtc = completedUtc.Kind == DateTimeKind.Utc
                ? completedUtc
                : completedUtc.ToUniversalTime(),
            Folders = folders,
            TotalScreenshots = screenshots.Count
        };
    }

    public async Task<ScreenshotFolderCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TryLoadAsync(_catalogPath, cancellationToken).ConfigureAwait(false) ??
                   await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ScreenshotFolderCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var normalized = Normalize(catalog) ??
                         throw new InvalidDataException("The screenshot folder catalog is invalid.");

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
                             32 * 1024,
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
                throw new InvalidDataException("The screenshot folder catalog is too large.");
            }

            if (!File.Exists(_catalogPath))
            {
                File.Move(temporaryPath, _catalogPath);
                temporaryPath = null;
                return;
            }

            var currentIsValid = await TryLoadAsync(_catalogPath, cancellationToken).ConfigureAwait(false) is not null;
            File.Replace(
                temporaryPath,
                _catalogPath,
                currentIsValid ? _backupPath : null,
                ignoreMetadataErrors: true);
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

    private static async Task<ScreenshotFolderCatalog?> TryLoadAsync(
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
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var catalog = await JsonSerializer.DeserializeAsync<ScreenshotFolderCatalog>(
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

    private static ScreenshotFolderCatalog? Normalize(ScreenshotFolderCatalog catalog)
    {
        if (catalog.FormatVersion != CurrentFormatVersion ||
            catalog.LastSuccessfulScanUtc == default ||
            catalog.Folders is null ||
            catalog.Folders.Count > MaximumFolders ||
            catalog.TotalScreenshots < 0)
        {
            return null;
        }

        var folders = new Dictionary<string, FolderCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in catalog.Folders)
        {
            if (folder is null)
            {
                return null;
            }

            var path = PathUtility.Normalize(folder.Path);
            if (path is null || PathUtility.IsDriveRoot(path) || folder.Count < 0)
            {
                return null;
            }

            var displayName = string.IsNullOrWhiteSpace(folder.DisplayName)
                ? Path.GetFileName(path)
                : folder.DisplayName.Trim();
            DateTime? latestUtc = folder.LatestUtc is { } latest
                ? latest.Kind switch
                {
                    DateTimeKind.Utc => latest,
                    DateTimeKind.Local => latest.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(latest, DateTimeKind.Utc)
                }
                : null;
            var normalized = folder with
            {
                Path = path,
                DisplayName = displayName,
                LatestUtc = latestUtc
            };
            if (folders.TryGetValue(path, out var existing))
            {
                return null;
            }

            folders[path] = normalized;
        }

        var totalScreenshots = folders.Values.Sum(folder => (long)folder.Count);
        if (totalScreenshots > int.MaxValue || totalScreenshots != catalog.TotalScreenshots)
        {
            return null;
        }

        var completedUtc = catalog.LastSuccessfulScanUtc.Kind switch
        {
            DateTimeKind.Utc => catalog.LastSuccessfulScanUtc,
            DateTimeKind.Local => catalog.LastSuccessfulScanUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(catalog.LastSuccessfulScanUtc, DateTimeKind.Utc)
        };
        return new ScreenshotFolderCatalog
        {
            FormatVersion = CurrentFormatVersion,
            LastSuccessfulScanUtc = completedUtc,
            Folders = folders.Values
                .OrderByDescending(folder => folder.LatestUtc ?? DateTime.MinValue)
                .ThenBy(folder => folder.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            TotalScreenshots = (int)totalScreenshots
        };
    }
}
