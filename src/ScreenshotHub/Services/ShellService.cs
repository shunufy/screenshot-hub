using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ScreenshotHub.Services;

internal static class ShellService
{
    public static bool OpenFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
        return true;
    }

    public static bool ShowInExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        });
        return true;
    }

    public static void CopyPath(string filePath) => Clipboard.SetText(filePath);
}
