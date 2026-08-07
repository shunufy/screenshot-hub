using System.IO;

namespace ScreenshotHub.Core;

internal static class PathUtility
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsSameOrDescendant(string candidate, string root)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedRoot = Normalize(root);
        if (normalizedCandidate is null || normalizedRoot is null)
        {
            return false;
        }

        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                       ? normalizedRoot
                       : normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDriveRoot(string path)
    {
        var normalized = Normalize(path);
        var root = normalized is null ? null : Path.GetPathRoot(normalized);
        return normalized is not null &&
               root is not null &&
               string.Equals(
                   normalized.TrimEnd(Path.DirectorySeparatorChar),
                   root.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }
}
