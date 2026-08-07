namespace ScreenshotHub.Core;

public sealed record ScanRoot(string Path, string DisplayName, bool IsCustom = false)
{
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];

    public string? GameNameHint { get; init; }
}

public sealed class HubSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }

    public bool AutoRefresh { get; set; }

    public int AutoRefreshMinutes { get; set; } = 30;

    public bool FolderOnlyMode { get; set; }

    public int MaxDepth { get; set; } = 8;

    public List<string> CustomRoots { get; set; } = [];

    public List<string> IgnoredRoots { get; set; } = [];

    public static HubSettings CreateDefault() => new()
    {
        SchemaVersion = CurrentSchemaVersion
    };
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

public sealed record CatalogRoot(
    string Path,
    string DisplayName,
    bool IsCustom,
    string? GameNameHint);

public sealed class ScreenshotCatalog
{
    public int FormatVersion { get; set; }

    public DateTime LastSuccessfulScanUtc { get; set; }

    public List<CatalogRoot> Roots { get; set; } = [];

    public List<ScreenshotRecord> Screenshots { get; set; } = [];
}

public sealed record FolderCatalogEntry(
    string Path,
    string DisplayName,
    bool IsCustom,
    int Count,
    DateTime? LatestUtc);

public sealed class ScreenshotFolderCatalog
{
    public int FormatVersion { get; set; }

    public DateTime LastSuccessfulScanUtc { get; set; }

    public List<FolderCatalogEntry> Folders { get; set; } = [];

    public int TotalScreenshots { get; set; }
}
