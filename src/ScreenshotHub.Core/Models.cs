namespace ScreenshotHub.Core;

public sealed record ScanRoot(string Path, string DisplayName, bool IsCustom = false)
{
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];

    public string? GameNameHint { get; init; }
}

public sealed class HubSettings
{
    public bool ScanOnStartup { get; set; } = true;

    public bool AutoRefresh { get; set; } = true;

    public int AutoRefreshMinutes { get; set; } = 5;

    public int MaxDepth { get; set; } = 8;

    public List<string> CustomRoots { get; set; } = [];

    public List<string> IgnoredRoots { get; set; } = [];
}

public sealed record ScreenshotRecord(
    string FilePath,
    string LibraryFolder,
    string GameName,
    DateTime LastWriteTimeUtc,
    long FileSize);

public sealed record ScanProgress(
    string CurrentPath,
    int DirectoriesVisited,
    int ScreenshotCount);

public sealed record ScanResult(
    IReadOnlyList<ScreenshotRecord> Screenshots,
    IReadOnlyList<ScanRoot> ScannedRoots,
    int DirectoriesVisited,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings);
