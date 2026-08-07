using System.IO;
using System.Text.Json;
using System.Windows;
using ScreenshotHub.Core;

namespace ScreenshotHub;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = DiagnosticOptions.Parse(e.Args);
        if (!options.IsValid)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Environment.ExitCode = await ReportInvalidOptionsAsync(options);
            Shutdown(Environment.ExitCode);
            return;
        }

        if (options.IsDiagnosticMode)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Environment.ExitCode = await RunDiagnosticsAsync(options);
            Shutdown(Environment.ExitCode);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow(options.ScanRoots, options.SettingsPath);
        MainWindow = window;
        window.Show();
    }

    private static async Task<int> ReportInvalidOptionsAsync(DiagnosticOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.JsonPath))
        {
            try
            {
                var outputPath = Path.GetFullPath(options.JsonPath);
                var parent = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                await File.WriteAllTextAsync(
                    outputPath,
                    JsonSerializer.Serialize(
                        new { fatal = true, error = options.ParseError },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Invalid options still return a nonzero code if the report cannot be written.
            }
        }

        return 2;
    }

    private static async Task<int> RunDiagnosticsAsync(DiagnosticOptions options)
    {
        try
        {
            var roots = options.ScanRoots
                .Select(path => new ScanRoot(
                    Path.GetFullPath(path),
                    new DirectoryInfo(path).Name is { Length: > 0 } name ? name : path,
                    true))
                .ToArray();

            if (roots.Length == 0)
            {
                var settings = options.SettingsPath is null
                    ? new HubSettings()
                    : await new SettingsStore(options.SettingsPath).LoadAsync();
                roots = KnownScanRoots.Discover(settings).ToArray();
            }

            var result = await ScreenshotScanner.ScanAsync(roots, options.MaxDepth);
            if (!string.IsNullOrWhiteSpace(options.JsonPath))
            {
                var outputPath = Path.GetFullPath(options.JsonPath);
                var parent = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                var payload = new
                {
                    fatal = false,
                    roots = result.ScannedRoots,
                    screenshots = result.Screenshots
                        .OrderBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase),
                    result.DirectoriesVisited,
                    durationMilliseconds = result.Duration.TotalMilliseconds,
                    result.Warnings
                };

                await File.WriteAllTextAsync(
                    outputPath,
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }

            return 0;
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(options.JsonPath))
            {
                try
                {
                    var outputPath = Path.GetFullPath(options.JsonPath);
                    var parent = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    await File.WriteAllTextAsync(
                        outputPath,
                        JsonSerializer.Serialize(
                            new { fatal = true, error = exception.Message },
                            new JsonSerializerOptions { WriteIndented = true }));
                }
                catch
                {
                    // The process exit code still communicates the failure.
                }
            }

            return 1;
        }
    }

    private sealed record DiagnosticOptions(
        IReadOnlyList<string> ScanRoots,
        string? JsonPath,
        string? DataDirectory,
        bool ExitAfterScan,
        int MaxDepth,
        string? ParseError)
    {
        public bool IsValid => ParseError is null;
        public bool IsDiagnosticMode => ExitAfterScan || !string.IsNullOrWhiteSpace(JsonPath);

        public string? SettingsPath => string.IsNullOrWhiteSpace(DataDirectory)
            ? null
            : Path.Combine(Path.GetFullPath(DataDirectory), "settings.json");

        public static DiagnosticOptions Parse(IReadOnlyList<string> args)
        {
            var roots = new List<string>();
            string? jsonPath = null;
            string? dataDirectory = null;
            var exitAfterScan = false;
            var maxDepth = 12;
            var errors = new List<string>();

            for (var index = 0; index < args.Count; index++)
            {
                switch (args[index])
                {
                    case "--scan-root":
                        if (!TryReadValue(args, ref index, out var scanRoot))
                        {
                            errors.Add(AppText.MissingScanRootArgument);
                        }
                        else if (!IsValidPath(scanRoot))
                        {
                            errors.Add(AppText.InvalidScanRootArgument);
                        }
                        else
                        {
                            roots.Add(scanRoot);
                        }
                        break;
                    case "--diagnostics-json":
                        if (!TryReadValue(args, ref index, out var diagnosticsPath))
                        {
                            errors.Add(AppText.MissingDiagnosticsArgument);
                        }
                        else if (!IsValidPath(diagnosticsPath))
                        {
                            errors.Add(AppText.InvalidDiagnosticsArgument);
                        }
                        else
                        {
                            jsonPath = diagnosticsPath;
                        }
                        break;
                    case "--data-dir":
                        if (!TryReadValue(args, ref index, out var dataPath))
                        {
                            errors.Add(AppText.MissingDataDirectoryArgument);
                        }
                        else if (!IsValidPath(dataPath))
                        {
                            errors.Add(AppText.InvalidDataDirectoryArgument);
                        }
                        else
                        {
                            dataDirectory = dataPath;
                        }
                        break;
                    case "--exit-after-scan":
                        exitAfterScan = true;
                        break;
                    case "--max-depth":
                        if (!TryReadValue(args, ref index, out var depthValue) ||
                            !int.TryParse(depthValue, out var parsed) ||
                            parsed is < 1 or > 64)
                        {
                            errors.Add(AppText.InvalidMaxDepthArgument);
                        }
                        else
                        {
                            maxDepth = parsed;
                        }
                        break;
                    default:
                        errors.Add(AppText.UnknownArgument(args[index]));
                        break;
                }
            }

            return new DiagnosticOptions(
                roots,
                jsonPath,
                dataDirectory,
                exitAfterScan,
                maxDepth,
                errors.Count == 0 ? null : string.Join(" / ", errors));
        }

        private static bool TryReadValue(
            IReadOnlyList<string> args,
            ref int index,
            out string value)
        {
            if (index + 1 >= args.Count ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = string.Empty;
                return false;
            }

            value = args[++index];
            return true;
        }

        private static bool IsValidPath(string path)
        {
            try
            {
                _ = Path.GetFullPath(path);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }
}
