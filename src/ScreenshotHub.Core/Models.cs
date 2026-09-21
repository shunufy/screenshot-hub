namespace ScreenshotHub.Core;

public sealed record ScanRoot(string Path, string DisplayName, bool IsCustom = false)
{
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];

    public string? GameNameHint { get; init; }
}

public sealed class HubSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; }

    public bool AutoRefresh { get; set; }

    public int AutoRefreshMinutes { get; set; } = 30;

    public bool FolderOnlyMode { get; set; }

    public string GalleryFilter { get; set; } = "all";

    public string? TagFilter { get; set; }

    public string DatePeriod { get; set; } = "all-time";

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public string SortOrder { get; set; } = "newest";

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
    IReadOnlyList<string> Warnings)
{
    public IncrementalScanSummary Incremental { get; init; } = new(0, 0, 0);
}

public sealed record IncrementalScanSummary(
    int Reused,
    int AddedOrUpdated,
    int Removed);

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

public sealed class ScreenshotUserDataCatalog
{
    public int FormatVersion { get; set; }

    public List<ScreenshotUserData> Items { get; set; } = [];
}

public sealed class ScreenshotUserData
{
    public string FilePath { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    public List<string> Tags { get; set; } = [];

    public DateTime UpdatedUtc { get; set; }
}

public sealed class ScreenshotAnalysisCatalog
{
    public int FormatVersion { get; set; }

    public DateTime LastAnalysisUtc { get; set; }

    public List<ScreenshotAnalysisRecord> Items { get; set; } = [];
}

public sealed record ScreenshotAnalysisRecord(
    string FilePath,
    DateTime LastWriteTimeUtc,
    long FileSize,
    string? Sha256,
    ulong? DifferenceHash,
    int PixelWidth,
    int PixelHeight);

public sealed record DuplicateMembership(
    string? ExactGroupId,
    int ExactGroupCount,
    string? SimilarGroupId,
    int SimilarGroupCount)
{
    public bool IsExactDuplicate => ExactGroupCount > 1;

    public bool IsSimilar => SimilarGroupCount > 1;
}
