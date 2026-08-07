using System.IO;
using Microsoft.Win32;

namespace ScreenshotHub.Core;

public static class KnownScanRoots
{
    public static IReadOnlyList<ScanRoot> Discover(HubSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var roots = new List<ScanRoot>();
        var ignored = (settings.IgnoredRoots ?? [])
            .Select(PathUtility.Normalize)
            .Where(path => path is not null)
            .Cast<string>()
            .ToArray();

        void Add(
            string? path,
            string displayName,
            bool isCustom = false,
            string? gameNameHint = null)
        {
            var normalized = PathUtility.Normalize(path);
            if (normalized is null ||
                !Directory.Exists(normalized) ||
                PathUtility.IsDriveRoot(normalized) ||
                ignored.Any(entry => PathUtility.IsSameOrDescendant(normalized, entry)))
            {
                return;
            }

            var existingIndex = roots.FindIndex(root =>
                string.Equals(root.Path, normalized, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                if ((isCustom && !roots[existingIndex].IsCustom) ||
                    (roots[existingIndex].GameNameHint is null && gameNameHint is not null))
                {
                    roots[existingIndex] = roots[existingIndex] with
                    {
                        IsCustom = roots[existingIndex].IsCustom || isCustom,
                        GameNameHint = roots[existingIndex].GameNameHint ?? gameNameHint
                    };
                }

                return;
            }

            roots.Add(new ScanRoot(normalized, displayName, isCustom)
            {
                IgnoredPaths = ignored,
                GameNameHint = gameNameHint
            });
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Add(Path.Combine(pictures, "Screenshots"), AppText.WindowsScreenshots);
        Add(pictures, AppText.Pictures);
        Add(Path.Combine(videos, "Captures"), "Xbox Game Bar");
        Add(Path.Combine(documents, "My Games"), "Documents / My Games");
        Add(Path.Combine(profile, "Saved Games"), "Saved Games");

        // Broad profile containers are bounded by ScreenshotScanner's depth and directory limits.
        Add(local, "AppData / Local");
        Add(Path.Combine(profile, "AppData", "LocalLow"), "AppData / LocalLow");
        Add(roaming, "AppData / Roaming");

        Add(Path.Combine(roaming, ".minecraft", "screenshots"), "Minecraft");
        Add(Path.Combine(profile, "curseforge", "minecraft", "Instances"), "CurseForge");
        Add(Path.Combine(roaming, "PrismLauncher", "instances"), "Prism Launcher");
        Add(Path.Combine(roaming, "MultiMC", "instances"), "MultiMC");
        Add(Path.Combine(roaming, "com.modrinth.theseus", "profiles"), "Modrinth");

        foreach (var steamPath in FindSteamPaths())
        {
            Add(Path.Combine(steamPath, "userdata"), "Steam");
        }

        foreach (var captureRoot in InstalledGameDiscovery.DiscoverDefaultCaptureRoots())
        {
            Add(
                captureRoot.Path,
                captureRoot.DisplayName,
                gameNameHint: captureRoot.DisplayName);
        }

        foreach (var customRoot in settings.CustomRoots ?? [])
        {
            var name = Path.GetFileName(customRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            Add(customRoot, string.IsNullOrWhiteSpace(name) ? AppText.AddedFolder : name, true);
        }

        // Prefer precise roots before broad containers, while keeping all roots for discovery.
        return roots
            .OrderByDescending(root => root.IsCustom)
            .ThenByDescending(root => root.Path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<string> FindSteamPaths()
    {
        var candidates = new List<string?>
        {
            ReadRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            ReadRegistryValue(Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };

        return candidates
            .Select(PathUtility.Normalize)
            .Where(path => path is not null && Directory.Exists(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadRegistryValue(RegistryKey root, string keyPath, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(keyPath, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
