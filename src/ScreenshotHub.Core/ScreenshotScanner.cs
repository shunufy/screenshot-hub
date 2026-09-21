using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ScreenshotHub.Core;

public static partial class ScreenshotScanner
{
    private const int DirectoryLimitPerRoot = 20_000;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    private static readonly HashSet<string> CaptureDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "screenshot", "screenshots", "screencapture", "screencaptures",
        "capture", "captures", "photomode", "photos", "gallery",
        "スクリーンショット", "スクリーンショット一覧", "キャプチャ", "キャプチャー", "写真"
    };

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin", "systemvolumeinformation", ".git", ".svn", "node_modules",
        "bin", "obj", "cache", "caches", "codecache", "gpucache", "webcache",
        "temp", "tmp", "crashdumps", "crashes", "shadercache", "shaders",
        "workshop", "assets", "textures", "thumbnails", ".nuget"
    };

    public static Task<ScanResult> ScanAsync(
        IReadOnlyCollection<ScanRoot> roots,
        int maxDepth,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ScanIncrementalAsync(roots, maxDepth, [], progress, cancellationToken);

    public static Task<ScanResult> ScanIncrementalAsync(
        IReadOnlyCollection<ScanRoot> roots,
        int maxDepth,
        IReadOnlyCollection<ScreenshotRecord> previousScreenshots,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(previousScreenshots);
        var safeDepth = Math.Clamp(maxDepth, 1, 64);
        return Task.Run(
            () => ScanCore(roots, safeDepth, previousScreenshots, progress, cancellationToken),
            cancellationToken);
    }

    public static bool IsSupportedImagePath(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path));

    private static ScanResult ScanCore(
        IReadOnlyCollection<ScanRoot> requestedRoots,
        int maxDepth,
        IReadOnlyCollection<ScreenshotRecord> previousScreenshots,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var scannedRoots = NormalizeRoots(requestedRoots, warnings);
        var screenshots = new Dictionary<string, ScreenshotRecord>(StringComparer.OrdinalIgnoreCase);
        var gameNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previousByPath = previousScreenshots
            .Select(record => (Path: PathUtility.Normalize(record.FilePath), Record: record))
            .Where(entry => entry.Path is not null)
            .GroupBy(entry => entry.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Record, StringComparer.OrdinalIgnoreCase);
        var matchedPreviousPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reusedRecords = 0;
        var addedOrUpdatedRecords = 0;
        var directoriesVisited = 0;

        foreach (var root in scannedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignoredPaths = root.IgnoredPaths
                .Select(PathUtility.Normalize)
                .Where(path => path is not null)
                .Cast<string>()
                .ToArray();
            var stack = new Stack<DirectoryNode>();
            stack.Push(new DirectoryNode(
                root.Path,
                0,
                root.IsCustom || IsCaptureDirectory(root.Path) ? root.Path : null));
            var rootDirectoryCount = 0;

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++rootDirectoryCount > DirectoryLimitPerRoot)
                {
                    AddWarning(warnings, AppText.ScanLimitReached(root.Path));
                    break;
                }

                var node = stack.Pop();
                var directoryPath = PathUtility.Normalize(node.Path);
                if (directoryPath is null ||
                    ignoredPaths.Any(ignored => PathUtility.IsSameOrDescendant(directoryPath, ignored)) ||
                    !visitedDirectories.Add(directoryPath))
                {
                    continue;
                }

                FileAttributes directoryAttributes;
                try
                {
                    directoryAttributes = File.GetAttributes(directoryPath);
                }
                catch (Exception exception) when (IsRecoverableFileSystemError(exception))
                {
                    AddWarning(warnings, AppText.CannotInspectFolder(directoryPath));
                    continue;
                }

                if (node.Depth > 0 && directoryAttributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                directoriesVisited++;
                if (directoriesVisited == 1 || directoriesVisited % 40 == 0)
                {
                    progress?.Report(new ScanProgress(
                        directoryPath,
                        directoriesVisited,
                        screenshots.Count));
                }

                var captureAnchor = IsCaptureDirectory(directoryPath)
                    ? directoryPath
                    : node.CaptureAnchor;

                if (captureAnchor is not null)
                {
                    IndexImages(
                        directoryPath,
                        captureAnchor,
                        root.GameNameHint,
                        screenshots,
                        gameNames,
                        previousByPath,
                        matchedPreviousPaths,
                        ref reusedRecords,
                        ref addedOrUpdatedRecords,
                        warnings,
                        cancellationToken);
                }

                if (node.Depth >= maxDepth)
                {
                    continue;
                }

                foreach (var child in GetDirectories(directoryPath, warnings))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsExcludedDirectory(child))
                    {
                        continue;
                    }

                    try
                    {
                        if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                        {
                            continue;
                        }
                    }
                    catch (Exception exception) when (IsRecoverableFileSystemError(exception))
                    {
                        continue;
                    }

                    var childAnchor = IsCaptureDirectory(child) ? child : captureAnchor;
                    stack.Push(new DirectoryNode(child, node.Depth + 1, childAnchor));
                }
            }
        }

        stopwatch.Stop();
        var ordered = screenshots.Values
            .OrderByDescending(record => record.LastWriteTimeUtc)
            .ThenBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new ScanProgress(string.Empty, directoriesVisited, ordered.Length));
        return new ScanResult(
            ordered,
            scannedRoots,
            directoriesVisited,
            stopwatch.Elapsed,
            warnings)
        {
            Incremental = new IncrementalScanSummary(
                reusedRecords,
                addedOrUpdatedRecords,
                Math.Max(0, previousByPath.Count - matchedPreviousPaths.Count))
        };
    }

    private static IReadOnlyList<ScanRoot> NormalizeRoots(
        IReadOnlyCollection<ScanRoot> roots,
        List<string> warnings)
    {
        var normalized = new Dictionary<string, ScanRoot>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var path = PathUtility.Normalize(root.Path);
            if (path is null || !Directory.Exists(path))
            {
                AddWarning(warnings, AppText.MissingScanRoot(root.Path));
                continue;
            }

            if (PathUtility.IsDriveRoot(path))
            {
                AddWarning(warnings, AppText.DriveRootSkipped(path));
                continue;
            }

            try
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                {
                    AddWarning(warnings, AppText.ReparsePointSkipped(path));
                    continue;
                }
            }
            catch (Exception exception) when (IsRecoverableFileSystemError(exception))
            {
                AddWarning(warnings, AppText.CannotInspectRoot(path));
                continue;
            }

            if (normalized.TryGetValue(path, out var existing))
            {
                normalized[path] = existing with
                {
                    IsCustom = existing.IsCustom || root.IsCustom,
                    GameNameHint = existing.GameNameHint ?? root.GameNameHint,
                    IgnoredPaths = existing.IgnoredPaths
                        .Concat(root.IgnoredPaths)
                        .Select(PathUtility.Normalize)
                        .Where(entry => entry is not null)
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };

                continue;
            }

            normalized[path] = root with { Path = path };
        }

        return normalized.Values
            .OrderByDescending(root => root.IsCustom)
            .ThenByDescending(root => root.Path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void IndexImages(
        string directoryPath,
        string captureAnchor,
        string? gameNameHint,
        Dictionary<string, ScreenshotRecord> screenshots,
        Dictionary<string, string> gameNames,
        IReadOnlyDictionary<string, ScreenshotRecord> previousByPath,
        HashSet<string> matchedPreviousPaths,
        ref int reusedRecords,
        ref int addedOrUpdatedRecords,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in GetFiles(directoryPath, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedImagePath(filePath))
            {
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(filePath);
                if (attributes.HasFlag(FileAttributes.Offline))
                {
                    continue;
                }

                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length <= 0)
                {
                    continue;
                }

                var normalizedFile = PathUtility.Normalize(info.FullName);
                var normalizedAnchor = PathUtility.Normalize(captureAnchor);
                if (normalizedFile is null || normalizedAnchor is null || screenshots.ContainsKey(normalizedFile))
                {
                    continue;
                }

                if (previousByPath.TryGetValue(normalizedFile, out var previous))
                {
                    matchedPreviousPaths.Add(normalizedFile);
                    if (previous.FileSize == info.Length &&
                        previous.LastWriteTimeUtc == info.LastWriteTimeUtc &&
                        string.Equals(previous.LibraryFolder, normalizedAnchor, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(gameNameHint) ||
                         string.Equals(previous.GameName, gameNameHint.Trim(), StringComparison.CurrentCulture)))
                    {
                        screenshots[normalizedFile] = previous;
                        reusedRecords++;
                        continue;
                    }
                }

                if (!gameNames.TryGetValue(normalizedAnchor, out var gameName))
                {
                    gameName = GameNameResolver.Resolve(normalizedAnchor, gameNameHint);
                    gameNames[normalizedAnchor] = gameName;
                }

                screenshots[normalizedFile] = new ScreenshotRecord(
                    normalizedFile,
                    normalizedAnchor,
                    gameName,
                    info.LastWriteTimeUtc,
                    info.Length);
                addedOrUpdatedRecords++;
            }
            catch (Exception exception) when (IsRecoverableFileSystemError(exception))
            {
                AddWarning(warnings, AppText.CannotInspectImage(filePath));
            }
        }
    }

    private static string[] GetDirectories(string path, List<string> warnings)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception exception) when (IsRecoverableFileSystemError(exception))
        {
            AddWarning(warnings, AppText.CannotListFolder(path));
            return [];
        }
    }

    private static string[] GetFiles(string path, List<string> warnings)
    {
        try
        {
            return Directory.GetFiles(path);
        }
        catch (Exception exception) when (IsRecoverableFileSystemError(exception))
        {
            AddWarning(warnings, AppText.CannotListImages(path));
            return [];
        }
    }

    private static bool IsCaptureDirectory(string path)
        => CaptureDirectoryNames.Contains(NormalizeName(Path.GetFileName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));

    private static bool IsExcludedDirectory(string path)
        => ExcludedDirectoryNames.Contains(NormalizeName(Path.GetFileName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => !char.IsWhiteSpace(character) && character is not '-' and not '_'));
    }

    private static bool IsRecoverableFileSystemError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;

    private static void AddWarning(List<string> warnings, string warning)
    {
        const int warningLimit = 100;
        if (warnings.Count < warningLimit)
        {
            warnings.Add(warning);
        }
    }

    private sealed record DirectoryNode(string Path, int Depth, string? CaptureAnchor);
}

internal static partial class GameNameResolver
{
    private static readonly object SteamCatalogLock = new();
    private static readonly TimeSpan SteamCatalogLifetime = TimeSpan.FromMinutes(1);
    private static IReadOnlyDictionary<string, string> _steamGames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static DateTime _steamCatalogLoadedUtc = DateTime.MinValue;

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "screenshot", "screenshots", "screencapture", "screencaptures", "capture", "captures",
        "photomode", "photos", "gallery", "saved", "save", "client", "game", "games",
        "windows", "windowsnoeditor",
        "win64", "pictures", "videos", "documents", "mygames", "savedgames", "userdata",
        "remote", "760", "instances", "profiles", "スクリーンショット", "キャプチャ", "写真"
    };

    public static string Resolve(string captureAnchor, string? gameNameHint = null)
    {
        if (!string.IsNullOrWhiteSpace(gameNameHint))
        {
            return gameNameHint.Trim();
        }

        var segments = captureAnchor
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length; index++)
        {
            if (!segments[index].Equals("screenshots", StringComparison.OrdinalIgnoreCase) || index < 4)
            {
                continue;
            }

            var appId = segments[index - 1];
            if (segments[index - 2].Equals("remote", StringComparison.OrdinalIgnoreCase) &&
                segments[index - 3].Equals("760", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(appId, out _))
            {
                var steamGames = GetSteamCatalog();
                return steamGames.TryGetValue(appId, out var title)
                    ? title
                    : $"Steam {appId}";
            }
        }

        if (captureAnchor.EndsWith(
                Path.Combine("Videos", "Captures"),
                StringComparison.OrdinalIgnoreCase))
        {
            return "Xbox Game Bar";
        }

        var minecraftIndex = Array.FindIndex(
            segments,
            segment => segment.Equals(".minecraft", StringComparison.OrdinalIgnoreCase));
        if (minecraftIndex >= 0)
        {
            if (minecraftIndex > 1 &&
                (segments[..minecraftIndex].Any(segment =>
                     segment.Equals("instances", StringComparison.OrdinalIgnoreCase)) ||
                 segments[..minecraftIndex].Any(segment =>
                     segment.Equals("profiles", StringComparison.OrdinalIgnoreCase))))
            {
                return segments[minecraftIndex - 1];
            }

            return "Minecraft";
        }

        var current = new DirectoryInfo(captureAnchor);
        for (var level = 0; level < 7 && current is not null; level++, current = current.Parent)
        {
            var name = current.Name;
            var normalized = NormalizeName(name);
            if (normalized.Length == 0 || GenericNames.Contains(normalized) || ulong.TryParse(normalized, out _))
            {
                continue;
            }

            if (name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            return name;
        }

        return AppText.ScreenshotFallback;
    }

    private static IReadOnlyDictionary<string, string> GetSteamCatalog()
    {
        var now = DateTime.UtcNow;
        var catalog = Volatile.Read(ref _steamGames);
        if (now - _steamCatalogLoadedUtc < SteamCatalogLifetime)
        {
            return catalog;
        }

        lock (SteamCatalogLock)
        {
            now = DateTime.UtcNow;
            if (now - _steamCatalogLoadedUtc < SteamCatalogLifetime)
            {
                return _steamGames;
            }

            catalog = BuildSteamCatalog();
            Volatile.Write(ref _steamGames, catalog);
            _steamCatalogLoadedUtc = now;
            return catalog;
        }
    }

    private static IReadOnlyDictionary<string, string> BuildSteamCatalog()
    {
        var catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var steamAppsFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamPath in KnownScanRoots.FindSteamPaths())
        {
            var steamApps = Path.Combine(steamPath, "steamapps");
            if (Directory.Exists(steamApps))
            {
                steamAppsFolders.Add(steamApps);
            }

            var librariesFile = Path.Combine(steamApps, "libraryfolders.vdf");
            try
            {
                if (!File.Exists(librariesFile))
                {
                    continue;
                }

                var text = File.ReadAllText(librariesFile);
                foreach (Match match in SteamLibraryPathRegex().Matches(text))
                {
                    var libraryPath = match.Groups["path"].Value.Replace("\\\\", "\\");
                    var librarySteamApps = Path.Combine(libraryPath, "steamapps");
                    if (Directory.Exists(librarySteamApps))
                    {
                        steamAppsFolders.Add(librarySteamApps);
                    }
                }
            }
            catch
            {
                // Game names are optional metadata; AppID remains a stable fallback.
            }
        }

        foreach (var steamApps in steamAppsFolders)
        {
            string[] manifests;
            try
            {
                manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var manifest in manifests)
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    var appId = AppIdRegex().Match(text).Groups["value"].Value;
                    var name = AppNameRegex().Match(text).Groups["value"].Value;
                    if (appId.Length > 0 && name.Length > 0)
                    {
                        catalog[appId] = name;
                    }
                }
                catch
                {
                    // Ignore individual malformed or concurrently updated manifests.
                }
            }
        }

        return catalog;
    }

    private static string NormalizeName(string value)
        => string.Concat(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => !char.IsWhiteSpace(character) && character is not '-' and not '_'));

    [GeneratedRegex("\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryPathRegex();

    [GeneratedRegex("\\\"appid\\\"\\s+\\\"(?<value>\\d+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdRegex();

    [GeneratedRegex("\\\"name\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex AppNameRegex();
}
