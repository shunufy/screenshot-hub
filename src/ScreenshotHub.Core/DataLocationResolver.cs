using System.IO;

namespace ScreenshotHub.Core;

public static class DataLocationResolver
{
    public static string? ResolveSettingsPath(
        string? explicitSettingsPath,
        string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        if (!string.IsNullOrWhiteSpace(explicitSettingsPath))
        {
            return Path.GetFullPath(explicitSettingsPath);
        }

        var baseDirectory = Path.GetFullPath(applicationDirectory);
        return File.Exists(Path.Combine(baseDirectory, "portable.flag"))
            ? Path.Combine(baseDirectory, "Data", "settings.json")
            : null;
    }
}
