using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScreenshotHub.Core;

public enum GameInstallSource
{
    Unknown,
    Steam,
    Epic,
    HoYoPlay,
    WindowsUninstall
}

public sealed record GameInstallLocation(
    string Path,
    string DisplayName,
    GameInstallSource Source = GameInstallSource.Unknown);

public sealed record DetectedCaptureRoot(string Path, string DisplayName);

public static class InstalledGameDiscovery
{
    private const int MaxInstallLocationsPerSource = 256;
    private const int MaxDefaultInstallLocations = 1_024;
    private const int MaxCaptureRootsPerInstall = 16;
    private const int MaxManifestFiles = 2_048;
    private const int MaxRegistryEntriesPerView = 4_096;
    private const int MaxHoYoRegistryKeys = 1_024;
    private const int MaxHoYoRegistryDepth = 8;
    private const int MaxTextFileBytes = 4 * 1024 * 1024;
    private const int MaxJsonFileBytes = 1024 * 1024;

    private static readonly object CacheLock = new();
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);
    private static IReadOnlyList<DetectedCaptureRoot> _cachedDefaultCaptureRoots = [];
    private static DateTime _defaultCaptureRootsLoadedUtc = DateTime.MinValue;

    private static readonly Regex VdfPairRegex = new(
        "^\\s*\\\"(?<key>[^\\\"]+)\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> CaptureDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "screenshot", "screenshots", "screencapture", "screencaptures",
        "capture", "captures", "photomode", "photos", "gallery",
        "スクリーンショット", "スクリーンショット一覧", "キャプチャ", "キャプチャー", "写真"
    };

    private static readonly HashSet<string> ProbeExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin", "systemvolumeinformation", ".egstore", ".git", ".svn",
        "assets", "asset", "binaries", "bin", "content", "engine", "locales",
        "movies", "node_modules", "paks", "plugins", "redist", "redistributables",
        "resources", "shadercache", "shaders", "sounds", "streamingassets",
        "support", "textures", "thirdparty", "webcache"
    };

    private static readonly string[][] CommonCapturePaths =
    [
        ["ScreenShot"],
        ["Screenshots"],
        ["Capture"],
        ["Captures"],
        ["PhotoMode"],
        ["Saved", "ScreenShot"],
        ["Saved", "Screenshots"],
        ["Client", "Saved", "ScreenShot"],
        ["Client", "Saved", "Screenshots"],
        ["Game", "Saved", "ScreenShot"],
        ["Game", "Saved", "Screenshots"]
    ];

    public static IReadOnlyList<DetectedCaptureRoot> DiscoverDefaultCaptureRoots()
    {
        lock (CacheLock)
        {
            if (DateTime.UtcNow - _defaultCaptureRootsLoadedUtc < CacheLifetime)
            {
                return _cachedDefaultCaptureRoots;
            }

            var installs = new List<GameInstallLocation>();
            AddSource(DiscoverSteamInstalls(KnownScanRoots.FindSteamPaths()));
            AddSource(DiscoverEpicInstalls(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "EpicGamesLauncher",
                "Data",
                "Manifests")));
            AddSource(DiscoverHoYoPlayInstalls());
            AddSource(DiscoverWindowsUninstallInstalls());

            var deduplicatedInstalls = DeduplicateInstalls(installs)
                .Take(MaxDefaultInstallLocations)
                .ToArray();
            _cachedDefaultCaptureRoots = DiscoverCaptureRoots(deduplicatedInstalls);
            _defaultCaptureRootsLoadedUtc = DateTime.UtcNow;
            return _cachedDefaultCaptureRoots;

            void AddSource(IEnumerable<GameInstallLocation> source)
                => installs.AddRange(source.Take(MaxInstallLocationsPerSource));
        }
    }

    public static IReadOnlyList<GameInstallLocation> DiscoverSteamInstalls(
        IEnumerable<string> steamPaths)
    {
        ArgumentNullException.ThrowIfNull(steamPaths);

        var steamAppsFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamPath in steamPaths)
        {
            var normalizedSteamPath = NormalizeInstallRoot(steamPath);
            if (normalizedSteamPath is null)
            {
                continue;
            }

            var steamApps = Path.Combine(normalizedSteamPath, "steamapps");
            if (Directory.Exists(steamApps))
            {
                steamAppsFolders.Add(steamApps);
            }

            var libraryFoldersFile = Path.Combine(steamApps, "libraryfolders.vdf");
            if (!TryReadSmallTextFile(libraryFoldersFile, MaxTextFileBytes, out var libraryText))
            {
                continue;
            }

            foreach (var pair in ReadVdfPairs(libraryText))
            {
                if (!pair.Key.Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var libraryPath = pair.Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                var librarySteamApps = Path.Combine(libraryPath, "steamapps");
                if (Directory.Exists(librarySteamApps))
                {
                    steamAppsFolders.Add(librarySteamApps);
                }
            }
        }

        var results = new List<GameInstallLocation>();
        foreach (var steamApps in steamAppsFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string[] manifests;
            try
            {
                manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxManifestFiles)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var manifest in manifests)
            {
                if (!TryReadSmallTextFile(manifest, MaxTextFileBytes, out var manifestText))
                {
                    continue;
                }

                var pairs = ReadVdfPairs(manifestText)
                    .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
                if (!pairs.TryGetValue("installdir", out var installDirectory) ||
                    string.IsNullOrWhiteSpace(installDirectory))
                {
                    continue;
                }

                var commonDirectory = Path.Combine(steamApps, "common");
                var installPath = NormalizeInstallRoot(Path.Combine(commonDirectory, installDirectory));
                if (installPath is null || !PathUtility.IsSameOrDescendant(installPath, commonDirectory))
                {
                    continue;
                }

                var displayName = pairs.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : Path.GetFileName(installPath);
                results.Add(new GameInstallLocation(installPath, displayName, GameInstallSource.Steam));
            }
        }

        return DeduplicateInstalls(results);
    }

    public static IReadOnlyList<GameInstallLocation> DiscoverEpicInstalls(string manifestDirectory)
    {
        var normalizedManifestDirectory = PathUtility.Normalize(manifestDirectory);
        if (normalizedManifestDirectory is null || !Directory.Exists(normalizedManifestDirectory))
        {
            return [];
        }

        string[] manifests;
        try
        {
            manifests = Directory.GetFiles(normalizedManifestDirectory, "*.item", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(MaxManifestFiles)
                .ToArray();
        }
        catch
        {
            return [];
        }

        var results = new List<GameInstallLocation>();
        foreach (var manifest in manifests)
        {
            try
            {
                var info = new FileInfo(manifest);
                if (!info.Exists || info.Length is <= 0 or > MaxJsonFileBytes)
                {
                    continue;
                }

                var json = File.ReadAllText(manifest, Encoding.UTF8);
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                var root = document.RootElement;
                if (TryGetBoolean(root, "bIsApplication", out var isApplication) && !isApplication ||
                    TryGetBoolean(root, "bIsIncompleteInstall", out var isIncomplete) && isIncomplete ||
                    !TryGetString(root, "InstallLocation", out var installLocation))
                {
                    continue;
                }

                var installPath = NormalizeInstallRoot(installLocation);
                if (installPath is null)
                {
                    continue;
                }

                var displayName = TryGetString(root, "DisplayName", out var title) &&
                                  !string.IsNullOrWhiteSpace(title)
                    ? title
                    : Path.GetFileName(installPath);
                results.Add(new GameInstallLocation(installPath, displayName, GameInstallSource.Epic));
            }
            catch
            {
                // A stale or concurrently updated manifest must not stop other launchers.
            }
        }

        return DeduplicateInstalls(results);
    }

    public static IReadOnlyList<DetectedCaptureRoot> DiscoverCaptureRoots(
        IEnumerable<GameInstallLocation> installLocations)
    {
        ArgumentNullException.ThrowIfNull(installLocations);

        var captures = new Dictionary<string, DetectedCaptureRoot>(StringComparer.OrdinalIgnoreCase);
        foreach (var install in DeduplicateInstalls(installLocations).Take(MaxDefaultInstallLocations))
        {
            var installPath = NormalizeInstallRoot(install.Path);
            if (installPath is null)
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(install.DisplayName)
                ? Path.GetFileName(installPath)
                : install.DisplayName.Trim();
            var probeDirectoryLimit = install.Source == GameInstallSource.WindowsUninstall ? 160 : 512;
            var probeDepth = install.Source == GameInstallSource.WindowsUninstall ? 5 : 7;
            foreach (var capturePath in ProbeCaptureDirectories(
                         installPath,
                         probeDepth,
                         probeDirectoryLimit))
            {
                captures.TryAdd(capturePath, new DetectedCaptureRoot(capturePath, displayName));
            }
        }

        return captures.Values
            .OrderBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<GameInstallLocation> DiscoverHoYoPlayInstalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var results = new List<GameInstallLocation>();
        foreach (var basePath in new[]
                 {
                     @"Software\Cognosphere\HYP\standalone",
                     @"Software\miHoYo\HYP\standalone"
                 })
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(basePath, writable: false);
                if (root is null)
                {
                    continue;
                }

                var visited = 0;
                VisitHoYoRegistryKey(root, 0, ref visited, results);
            }
            catch
            {
                // Launcher metadata is optional and may be protected or changing.
            }
        }

        return DeduplicateInstalls(results);
    }

    private static void VisitHoYoRegistryKey(
        RegistryKey key,
        int depth,
        ref int visited,
        List<GameInstallLocation> results)
    {
        if (++visited > MaxHoYoRegistryKeys)
        {
            return;
        }

        try
        {
            if (key.GetValue("GameInstallPath") is string rawInstallPath)
            {
                var installPath = NormalizeInstallRoot(rawInstallPath);
                if (installPath is not null)
                {
                    var displayName = key.GetValue("DisplayName") as string ??
                                      key.GetValue("GameName") as string ??
                                      Path.GetFileName(installPath);
                    results.Add(new GameInstallLocation(
                        installPath,
                        displayName,
                        GameInstallSource.HoYoPlay));
                }
            }

            if (depth >= MaxHoYoRegistryDepth || visited >= MaxHoYoRegistryKeys)
            {
                return;
            }

            foreach (var childName in key.GetSubKeyNames())
            {
                if (visited >= MaxHoYoRegistryKeys)
                {
                    break;
                }

                try
                {
                    using var child = key.OpenSubKey(childName, writable: false);
                    if (child is not null)
                    {
                        VisitHoYoRegistryKey(child, depth + 1, ref visited, results);
                    }
                }
                catch
                {
                    // Skip one inaccessible launcher entry and continue with siblings.
                }
            }
        }
        catch
        {
            // Ignore one malformed or concurrently updated registry branch.
        }
    }

    private static IReadOnlyList<GameInstallLocation> DiscoverWindowsUninstallInstalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var results = new List<GameInstallLocation>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                        writable: false);
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (var childName in uninstall.GetSubKeyNames().Take(MaxRegistryEntriesPerView))
                    {
                        try
                        {
                            using var entry = uninstall.OpenSubKey(childName, writable: false);
                            if (entry is null ||
                                IsRegistryOne(entry.GetValue("SystemComponent")) ||
                                entry.GetValue("ParentKeyName") is string { Length: > 0 } ||
                                IsUpdateEntry(entry.GetValue("ReleaseType") as string) ||
                                entry.GetValue("InstallLocation") is not string installLocation ||
                                entry.GetValue("DisplayName") is not string displayName ||
                                string.IsNullOrWhiteSpace(displayName))
                            {
                                continue;
                            }

                            var installPath = NormalizeInstallRoot(installLocation.Trim().Trim('"'));
                            if (installPath is null)
                            {
                                continue;
                            }

                            var isSteamInstall = childName.StartsWith(
                                                     "Steam App ",
                                                     StringComparison.OrdinalIgnoreCase) &&
                                                 installPath.Contains(
                                                     $"{Path.DirectorySeparatorChar}steamapps{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}",
                                                     StringComparison.OrdinalIgnoreCase);
                            if (!isSteamInstall && !IsLikelyGameInstall(installPath))
                            {
                                continue;
                            }

                            var source = isSteamInstall
                                ? GameInstallSource.Steam
                                : GameInstallSource.WindowsUninstall;
                            results.Add(new GameInstallLocation(installPath, displayName, source));
                        }
                        catch
                        {
                            // One broken uninstall entry must not stop discovery.
                        }
                    }
                }
                catch
                {
                    // A registry view can be unavailable on some Windows editions.
                }
            }
        }

        return DeduplicateInstalls(results);
    }

    private static IReadOnlyList<string> ProbeCaptureDirectories(
        string installPath,
        int maxDepth,
        int directoryLimit)
    {
        if (IsCaptureDirectoryName(Path.GetFileName(installPath)))
        {
            return [installPath];
        }

        var captures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segments in CommonCapturePaths)
        {
            var candidate = segments.Aggregate(installPath, Path.Combine);
            var normalized = NormalizeCaptureRoot(candidate, installPath);
            if (normalized is not null)
            {
                captures.Add(normalized);
            }
        }

        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((installPath, 0));

        while (queue.Count > 0 &&
               visited.Count < directoryLimit &&
               captures.Count < MaxCaptureRootsPerInstall)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current.Path) || current.Depth >= maxDepth)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(current.Path);
            }
            catch
            {
                continue;
            }

            foreach (var child in children
                         .OrderByDescending(IsLikelyCaptureBranch)
                         .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (visited.Count + queue.Count >= directoryLimit)
                {
                    break;
                }

                var normalized = NormalizeCaptureRoot(child, installPath);
                if (normalized is not null && IsCaptureDirectoryName(Path.GetFileName(normalized)))
                {
                    captures.Add(normalized);
                    continue;
                }

                if (IsProbeExcludedDirectory(child) || IsReparsePoint(child))
                {
                    continue;
                }

                queue.Enqueue((child, current.Depth + 1));
            }
        }

        return captures
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<GameInstallLocation> DeduplicateInstalls(
        IEnumerable<GameInstallLocation> installs)
    {
        var deduplicated = new Dictionary<string, GameInstallLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var install in installs)
        {
            var path = NormalizeInstallRoot(install.Path);
            if (path is null)
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(install.DisplayName)
                ? Path.GetFileName(path)
                : install.DisplayName.Trim();
            deduplicated.TryAdd(path, install with { Path = path, DisplayName = displayName });
        }

        return deduplicated.Values.ToArray();
    }

    private static string? NormalizeInstallRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!Path.IsPathFullyQualified(expanded) || expanded.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var normalized = PathUtility.Normalize(expanded);
        if (normalized is null ||
            !Directory.Exists(normalized) ||
            PathUtility.IsDriveRoot(normalized) ||
            IsProtectedContainer(normalized) ||
            IsReparsePoint(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static string? NormalizeCaptureRoot(string path, string installPath)
    {
        var normalized = PathUtility.Normalize(path);
        if (normalized is null ||
            !Directory.Exists(normalized) ||
            !PathUtility.IsSameOrDescendant(normalized, installPath) ||
            IsReparsePoint(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static bool IsProtectedContainer(string path)
    {
        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        return protectedPaths
            .Select(PathUtility.Normalize)
            .Where(candidate => candidate is not null)
            .Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsCaptureDirectoryName(string? name)
        => name is not null && CaptureDirectoryNames.Contains(NormalizeName(name));

    private static bool IsProbeExcludedDirectory(string path)
        => ProbeExcludedDirectoryNames.Contains(NormalizeName(Path.GetFileName(path)));

    private static bool IsLikelyCaptureBranch(string path)
    {
        var name = NormalizeName(Path.GetFileName(path));
        return name is "saved" or "save" or "client" or "game" or "games" or "userdata" or "profiles";
    }

    private static bool IsLikelyGameInstall(string installPath)
    {
        var segments = installPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => NormalizeName(segment) is
                "games" or "epicgames" or "goggames" or "steamapps" or "xboxgames"))
        {
            return true;
        }

        try
        {
            if (File.Exists(Path.Combine(installPath, "steam_appid.txt")))
            {
                return true;
            }

            var directories = Directory.GetDirectories(installPath, "*", SearchOption.TopDirectoryOnly);
            var names = directories
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hasExecutable = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly).Length > 0;
            var hasUnrealLayout =
                names.Contains("Engine") &&
                (names.Contains("Binaries") || names.Contains("Content")) ||
                names.Contains("Client") &&
                (Directory.Exists(Path.Combine(installPath, "Client", "Binaries")) ||
                 Directory.Exists(Path.Combine(installPath, "Client", "Content"))) ||
                names.Contains("Engine") && directories.Any(directory =>
                    !string.Equals(
                        Path.GetFileName(directory),
                        "Engine",
                        StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(Path.Combine(directory, "Binaries")) &&
                    Directory.Exists(Path.Combine(directory, "Content")));
            var hasUnityLayout = hasExecutable && names.Any(name =>
                name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase));
            return hasUnrealLayout || hasUnityLayout;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeName(string value)
        => string.Concat(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => !char.IsWhiteSpace(character) && character is not '-' and not '_'));

    private static bool TryReadSmallTextFile(string path, int maxBytes, out string text)
    {
        text = string.Empty;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 || info.Length > maxBytes)
            {
                return false;
            }

            text = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ReadVdfPairs(string text)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var match = VdfPairRegex.Match(line);
            if (match.Success)
            {
                pairs.Add(new KeyValuePair<string, string>(
                    match.Groups["key"].Value,
                    match.Groups["value"].Value));
            }
        }

        return pairs;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return value.Length > 0;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) ||
                property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                continue;
            }

            value = property.Value.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool IsRegistryOne(object? value)
    {
        try
        {
            return value is not null && Convert.ToInt32(value) == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUpdateEntry(string? releaseType)
        => releaseType?.Contains("update", StringComparison.OrdinalIgnoreCase) == true ||
           releaseType?.Contains("hotfix", StringComparison.OrdinalIgnoreCase) == true;
}
