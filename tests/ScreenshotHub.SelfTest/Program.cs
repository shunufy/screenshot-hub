using ScreenshotHub.Core;
using System.Security.Cryptography;
using System.Text.Json;

var testRoot = Path.Combine(
    Path.GetTempPath(),
    "ScreenshotHub.SelfTest",
    Guid.NewGuid().ToString("N"));

try
{
    Directory.CreateDirectory(testRoot);
    await RunScannerTestsAsync(testRoot);
    await RunIncrementalScannerTestsAsync(testRoot);
    await RunOverlappingDepthBoundaryTestAsync(testRoot);
    await RunPackagesDiscoveryTestAsync(testRoot);
    await RunReparseRootTestWhenSupportedAsync(testRoot);
    await RunInstalledGameDiscoveryTestsAsync(testRoot);
    await RunCatalogStoreTestsAsync(testRoot);
    await RunFolderCatalogStoreTestsAsync(testRoot);
    await RunFullCatalogSemanticValidationTestsAsync(testRoot);
    await RunSettingsTestsAsync(testRoot);
    await RunUserDataStoreTestsAsync(testRoot);
    await RunAnalysisStoreTestsAsync(testRoot);
    RunDataLocationTests(testRoot);
    RunDuplicateGroupingTests(testRoot);
    RunBrowseQueryTests(testRoot);
    Console.WriteLine("SCREENSHOT_HUB_SELF_TEST_SUCCESS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("SCREENSHOT_HUB_SELF_TEST_FAILURE");
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, recursive: true);
    }
}

static async Task RunScannerTestsAsync(string testRoot)
{
    var scanRoot = Path.Combine(testRoot, "Scan Root");
    var japaneseShot = Path.Combine(
        scanRoot,
        "Game Library",
        "日本語ゲーム",
        "Saved",
        "Screenshots",
        "Windows",
        "Shot 01.JPG");
    var steamShot = Path.Combine(
        scanRoot,
        "Steam",
        "userdata",
        "1001",
        "760",
        "remote",
        "18446744073709551615",
        "screenshots",
        "old.png");
    var deepShot = Path.Combine(
        scanRoot,
        "Deep",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j",
        "Shot.UPPER.PNG");
    var sharedShot = Path.Combine(scanRoot, "Overlap", "shared.png");
    var modpackShot = Path.Combine(
        scanRoot,
        "Launchers",
        "instances",
        "Modpack A",
        ".minecraft",
        "screenshots",
        "pack.png");
    var excludedShot = Path.Combine(scanRoot, "Cache", "Screenshots", "ignored.png");
    var ignoredByRuleShot = Path.Combine(scanRoot, "Ignored Game", "Screenshots", "hidden.png");
    var outsideShot = Path.Combine(testRoot, "OutsideRoot", "must-not-appear.png");

    foreach (var path in new[] { japaneseShot, steamShot, deepShot, sharedShot, modpackShot, excludedShot, ignoredByRuleShot, outsideShot })
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, OnePixelPng());
    }

    File.SetLastWriteTimeUtc(steamShot, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    File.SetLastWriteTimeUtc(japaneseShot, new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
    File.SetLastWriteTimeUtc(modpackShot, new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc));
    File.SetLastWriteTimeUtc(deepShot, new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));
    File.SetLastWriteTimeUtc(sharedShot, new DateTime(2025, 1, 4, 0, 0, 0, DateTimeKind.Utc));

    var roots = new[]
    {
        new ScanRoot(scanRoot, "Fixture", true)
        {
            IgnoredPaths = [Path.Combine(scanRoot, "Ignored Game")]
        },
        new ScanRoot(Path.Combine(scanRoot, "Overlap"), "Overlap", true)
    };
    var result = await ScreenshotScanner.ScanAsync(roots, maxDepth: 16);
    var paths = result.Screenshots.Select(record => record.FilePath).ToArray();

    Assert(paths.Length == 5, $"Expected 5 screenshots, found {paths.Length}: {string.Join(", ", paths)}");
    Assert(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == paths.Length, "Overlapping roots produced duplicates.");
    Assert(paths.Contains(japaneseShot, StringComparer.OrdinalIgnoreCase), "Japanese deep screenshot was not found.");
    Assert(paths.Contains(steamShot, StringComparer.OrdinalIgnoreCase), "Steam screenshot was not found.");
    Assert(paths.Contains(deepShot, StringComparer.OrdinalIgnoreCase), "Deep mixed-case screenshot was not found.");
    Assert(paths.Contains(sharedShot, StringComparer.OrdinalIgnoreCase), "Overlapping-root screenshot was not found.");
    Assert(paths.Contains(modpackShot, StringComparer.OrdinalIgnoreCase), "Launcher modpack screenshot was not found.");
    Assert(!paths.Contains(excludedShot, StringComparer.OrdinalIgnoreCase), "Cache exclusion failed.");
    Assert(!paths.Contains(ignoredByRuleShot, StringComparer.OrdinalIgnoreCase), "Ignored descendant was rediscovered.");
    Assert(!paths.Contains(outsideShot, StringComparer.OrdinalIgnoreCase), "Scanner escaped the configured root.");
    Assert(result.Screenshots[0].FilePath.Equals(sharedShot, StringComparison.OrdinalIgnoreCase), "Newest-first ordering failed.");

    var japanese = result.Screenshots.Single(record =>
        record.FilePath.Equals(japaneseShot, StringComparison.OrdinalIgnoreCase));
    Assert(japanese.GameName == "日本語ゲーム", $"Unexpected game name: {japanese.GameName}");

    var steam = result.Screenshots.Single(record =>
        record.FilePath.Equals(steamShot, StringComparison.OrdinalIgnoreCase));
    Assert(
        steam.GameName == "Steam 18446744073709551615",
        $"Steam AppID fallback failed: {steam.GameName}");

    var modpack = result.Screenshots.Single(record =>
        record.FilePath.Equals(modpackShot, StringComparison.OrdinalIgnoreCase));
    Assert(modpack.GameName == "Modpack A", $"Modpack title resolution failed: {modpack.GameName}");
}

static async Task RunIncrementalScannerTestsAsync(string testRoot)
{
    var root = Path.Combine(testRoot, "Incremental Scan", "Screenshots");
    var firstPath = Path.Combine(root, "first.png");
    var removedPath = Path.Combine(root, "removed.png");
    Directory.CreateDirectory(root);
    await File.WriteAllBytesAsync(firstPath, OnePixelPng());
    await File.WriteAllBytesAsync(removedPath, OnePixelPng());
    var firstUtc = new DateTime(2026, 8, 8, 1, 2, 3, DateTimeKind.Utc);
    var removedUtc = firstUtc.AddMinutes(1);
    File.SetLastWriteTimeUtc(firstPath, firstUtc);
    File.SetLastWriteTimeUtc(removedPath, removedUtc);
    var originalBytes = await File.ReadAllBytesAsync(firstPath);
    var originalHash = Convert.ToHexString(SHA256.HashData(originalBytes));

    var roots = new[] { new ScanRoot(root, "Incremental fixture", true) };
    var initial = await ScreenshotScanner.ScanAsync(roots, maxDepth: 4);
    Assert(initial.Screenshots.Count == 2, "The incremental fixture was not indexed.");
    Assert(
        initial.Incremental.Reused == 0 && initial.Incremental.AddedOrUpdated == 2 && initial.Incremental.Removed == 0,
        "The initial scan reported incorrect incremental counters.");

    var unchanged = await ScreenshotScanner.ScanIncrementalAsync(
        roots,
        maxDepth: 4,
        initial.Screenshots);
    Assert(
        unchanged.Incremental.Reused == 2 &&
        unchanged.Incremental.AddedOrUpdated == 0 &&
        unchanged.Incremental.Removed == 0,
        "An unchanged incremental scan did not reuse both catalog records.");
    var initialFirst = initial.Screenshots.Single(record =>
        record.FilePath.Equals(firstPath, StringComparison.OrdinalIgnoreCase));
    var unchangedFirst = unchanged.Screenshots.Single(record =>
        record.FilePath.Equals(firstPath, StringComparison.OrdinalIgnoreCase));
    Assert(ReferenceEquals(initialFirst, unchangedFirst), "An unchanged screenshot record was rebuilt instead of reused.");
    Assert(File.GetLastWriteTimeUtc(firstPath) == firstUtc, "Scanning changed the image timestamp.");
    Assert(
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(firstPath))) == originalHash,
        "Scanning changed the image bytes.");

    await File.WriteAllBytesAsync(firstPath, [.. OnePixelPng(), 0]);
    File.SetLastWriteTimeUtc(firstPath, firstUtc.AddHours(1));
    File.Delete(removedPath);
    var changed = await ScreenshotScanner.ScanIncrementalAsync(
        roots,
        maxDepth: 4,
        unchanged.Screenshots);
    Assert(
        changed.Screenshots.Count == 1 &&
        changed.Incremental.Reused == 0 &&
        changed.Incremental.AddedOrUpdated == 1 &&
        changed.Incremental.Removed == 1,
        "Changed and removed images were not reflected in the incremental counters.");
}

static async Task RunPackagesDiscoveryTestAsync(string testRoot)
{
    var localRoot = Path.Combine(testRoot, "Local AppData");
    var screenshot = Path.Combine(
        localRoot,
        "Packages",
        "Microsoft.MinecraftUWP_fixture",
        "LocalState",
        "games",
        "com.mojang",
        "minecraftpe",
        "Screenshots",
        "bedrock.png");
    Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
    await File.WriteAllBytesAsync(screenshot, OnePixelPng());

    var result = await ScreenshotScanner.ScanAsync(
        [new ScanRoot(localRoot, "Local", false)],
        maxDepth: 8);
    Assert(
        result.Screenshots.Any(record =>
            record.FilePath.Equals(screenshot, StringComparison.OrdinalIgnoreCase)),
        "A Microsoft Store Packages screenshot path was pruned.");
}

static async Task RunOverlappingDepthBoundaryTestAsync(string testRoot)
{
    var parent = Path.Combine(testRoot, "Depth Boundary");
    var nestedRoot = Path.Combine(parent, "Nested");
    var screenshot = Path.Combine(nestedRoot, "Screenshots", "Child", "boundary.png");
    Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
    await File.WriteAllBytesAsync(screenshot, OnePixelPng());

    var result = await ScreenshotScanner.ScanAsync(
        [
            new ScanRoot(parent, "Parent", true),
            new ScanRoot(nestedRoot, "Nested", false)
        ],
        maxDepth: 2);

    Assert(
        result.Screenshots.Any(record =>
            record.FilePath.Equals(screenshot, StringComparison.OrdinalIgnoreCase)),
        "An overlapping parent root consumed the precise root at its depth boundary.");
}

static async Task RunReparseRootTestWhenSupportedAsync(string testRoot)
{
    var target = Path.Combine(testRoot, "Link Target");
    var screenshot = Path.Combine(target, "Screenshots", "linked.png");
    var link = Path.Combine(testRoot, "Linked Root");
    Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
    await File.WriteAllBytesAsync(screenshot, OnePixelPng());

    try
    {
        Directory.CreateSymbolicLink(link, target);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
    {
        return;
    }

    try
    {
        var result = await ScreenshotScanner.ScanAsync(
            [new ScanRoot(link, "Linked", true)],
            maxDepth: 8);
        Assert(result.Screenshots.Count == 0, "A reparse-point root was traversed.");
        Assert(result.Warnings.Count > 0, "A skipped reparse-point root produced no explanation.");
    }
    finally
    {
        Directory.Delete(link);
    }
}

static async Task RunInstalledGameDiscoveryTestsAsync(string testRoot)
{
    var metadataRoot = Path.Combine(testRoot, "Installed Game Discovery");
    var steamRoot = Path.Combine(metadataRoot, "Steam");
    var steamApps = Path.Combine(steamRoot, "steamapps");
    var steamGameRoot = Path.Combine(steamApps, "common", "Unknown Steam Adventure");
    var steamShot = Path.Combine(steamGameRoot, "Client", "Saved", "ScreenShot", "steam.png");
    var steamPhoto = Path.Combine(steamGameRoot, "PhotoMode", "steam-photo.png");
    Directory.CreateDirectory(Path.GetDirectoryName(steamShot)!);
    Directory.CreateDirectory(Path.Combine(steamGameRoot, "Screenshots"));
    Directory.CreateDirectory(Path.GetDirectoryName(steamPhoto)!);
    await File.WriteAllBytesAsync(steamShot, OnePixelPng());
    await File.WriteAllBytesAsync(steamPhoto, OnePixelPng());
    await File.WriteAllTextAsync(
        Path.Combine(steamApps, "appmanifest_987654.acf"),
        "\"AppState\"\n{\n\t\"appid\"\t\t\"987654\"\n\t\"name\"\t\t\"架空Steamゲーム\"\n\t\"installdir\"\t\t\"Unknown Steam Adventure\"\n}\n");

    var epicManifestRoot = Path.Combine(metadataRoot, "Epic Manifests");
    var epicGameRoot = Path.Combine(metadataRoot, "Epic Library", "Unlisted Game");
    var epicShot = Path.Combine(epicGameRoot, "runtime", "profile", "PhotoMode", "epic.jpg");
    Directory.CreateDirectory(Path.GetDirectoryName(epicShot)!);
    Directory.CreateDirectory(epicManifestRoot);
    await File.WriteAllBytesAsync(epicShot, OnePixelPng());
    await File.WriteAllBytesAsync(Path.Combine(epicGameRoot, "asset.png"), OnePixelPng());
    await File.WriteAllTextAsync(
        Path.Combine(epicManifestRoot, "valid.item"),
        JsonSerializer.Serialize(new
        {
            DisplayName = "架空Epicゲーム",
            InstallLocation = epicGameRoot,
            bIsApplication = true,
            bIsIncompleteInstall = false
        }));
    await File.WriteAllTextAsync(Path.Combine(epicManifestRoot, "broken.item"), "{ broken json");

    var steamInstalls = InstalledGameDiscovery.DiscoverSteamInstalls([steamRoot]);
    var epicInstalls = InstalledGameDiscovery.DiscoverEpicInstalls(epicManifestRoot);
    Assert(steamInstalls.Count == 1, $"Expected one Steam install, found {steamInstalls.Count}.");
    Assert(epicInstalls.Count == 1, $"Expected one Epic install, found {epicInstalls.Count}.");
    Assert(steamInstalls[0].DisplayName == "架空Steamゲーム", "Steam Unicode title was not preserved.");
    Assert(epicInstalls[0].DisplayName == "架空Epicゲーム", "Epic Unicode title was not preserved.");

    var captureRoots = InstalledGameDiscovery.DiscoverCaptureRoots(
        steamInstalls.Concat(epicInstalls));
    Assert(
        captureRoots.Any(root => root.Path.Equals(
            Path.GetDirectoryName(steamShot),
            StringComparison.OrdinalIgnoreCase)),
        "An empty shallow Screenshots folder suppressed a populated deep ScreenShot path.");
    Assert(
        captureRoots.Any(root => root.Path.Equals(
            Path.GetDirectoryName(steamPhoto),
            StringComparison.OrdinalIgnoreCase)),
        "A second capture folder in the same game was not detected.");
    Assert(
        captureRoots.Any(root => root.Path.Equals(
            Path.GetDirectoryName(epicShot),
            StringComparison.OrdinalIgnoreCase)),
        "An unknown title's PhotoMode folder was not detected.");

    var scanRoots = captureRoots
        .Select(root => new ScanRoot(root.Path, root.DisplayName)
        {
            GameNameHint = root.DisplayName
        })
        .ToArray();
    var result = await ScreenshotScanner.ScanAsync(scanRoots, maxDepth: 8);
    Assert(result.Screenshots.Count == 3, $"Expected three discovered screenshots, found {result.Screenshots.Count}.");
    Assert(
        result.Screenshots.Any(record =>
            record.FilePath.Equals(steamShot, StringComparison.OrdinalIgnoreCase) &&
            record.GameName == "架空Steamゲーム"),
        "Steam title metadata did not reach the gallery record.");
    Assert(
        result.Screenshots.Any(record =>
            record.FilePath.Equals(epicShot, StringComparison.OrdinalIgnoreCase) &&
            record.GameName == "架空Epicゲーム"),
        "Epic title metadata did not reach the gallery record.");
    Assert(
        result.Screenshots.All(record => !record.FilePath.EndsWith("asset.png", StringComparison.OrdinalIgnoreCase)),
        "A non-screenshot image in a game install was indexed.");
}

static async Task RunSettingsTestsAsync(string testRoot)
{
    var settingsPath = Path.Combine(testRoot, "AppData", "settings.json");
    var store = new SettingsStore(settingsPath);
    var first = new HubSettings
    {
        SchemaVersion = HubSettings.CurrentSchemaVersion,
        AutoRefresh = false,
        AutoRefreshMinutes = 10,
        FolderOnlyMode = true,
        GalleryFilter = "favorites",
        TagFilter = "夜景",
        DatePeriod = "custom",
        DateFrom = new DateTime(2026, 9, 1, 12, 34, 0),
        DateTo = new DateTime(2026, 9, 21),
        SortOrder = "oldest",
        MaxDepth = 12,
        CustomRoots = [Path.Combine(testRoot, "First")]
    };
    await store.SaveAsync(first);
    var firstLoaded = await store.LoadAsync();
    Assert(
        !firstLoaded.AutoRefresh &&
        firstLoaded.FolderOnlyMode &&
        firstLoaded.MaxDepth == 12 &&
        firstLoaded.GalleryFilter == "favorites" &&
        firstLoaded.TagFilter == "夜景",
        "Settings round trip, including folder-only mode, failed.");
    Assert(firstLoaded.DatePeriod == "custom" && firstLoaded.SortOrder == "oldest" &&
           firstLoaded.DateFrom == new DateTime(2026, 9, 1) &&
           firstLoaded.DateFrom.Value.Kind == DateTimeKind.Unspecified &&
           firstLoaded.DateTo == new DateTime(2026, 9, 21),
        "Browse settings did not round-trip as local calendar dates.");

    var second = new HubSettings
    {
        SchemaVersion = HubSettings.CurrentSchemaVersion,
        AutoRefresh = true,
        AutoRefreshMinutes = 30,
        FolderOnlyMode = false,
        MaxDepth = 6,
        CustomRoots = [Path.Combine(testRoot, "Second")]
    };
    await store.SaveAsync(second);
    var secondLoaded = await store.LoadAsync();
    Assert(
        secondLoaded.AutoRefresh &&
        !secondLoaded.FolderOnlyMode &&
        secondLoaded.AutoRefreshMinutes == 30 &&
        secondLoaded.MaxDepth == 6,
        "A later settings save did not become current.");
    await File.WriteAllTextAsync(settingsPath, "{ invalid json");

    var recovered = await store.LoadAsync();
    Assert(
        !recovered.AutoRefresh && recovered.FolderOnlyMode && recovered.MaxDepth == 12,
        "Known-good settings backup, including folder-only mode, was not recovered.");

    var third = new HubSettings
    {
        SchemaVersion = HubSettings.CurrentSchemaVersion,
        AutoRefresh = false,
        AutoRefreshMinutes = 60,
        FolderOnlyMode = true,
        MaxDepth = 15,
        CustomRoots = [Path.Combine(testRoot, "Third")]
    };
    await store.SaveAsync(third);
    var fourth = new HubSettings
    {
        SchemaVersion = HubSettings.CurrentSchemaVersion,
        AutoRefresh = true,
        AutoRefreshMinutes = 1,
        FolderOnlyMode = false,
        MaxDepth = 3,
        CustomRoots = [Path.Combine(testRoot, "Fourth")]
    };
    await store.SaveAsync(fourth);
    await File.WriteAllTextAsync(settingsPath, "not json");
    var rotatedBackup = await store.LoadAsync();
    Assert(
        !rotatedBackup.AutoRefresh &&
        rotatedBackup.FolderOnlyMode &&
        rotatedBackup.AutoRefreshMinutes == 60 &&
        rotatedBackup.MaxDepth == 15,
        "The known-good backup did not rotate after later saves.");

    var legacyPath = Path.Combine(testRoot, "Legacy AppData", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
    await File.WriteAllTextAsync(
        legacyPath,
        "{\"scanOnStartup\":true,\"autoRefresh\":true,\"autoRefreshMinutes\":5}");
    var migrated = await new SettingsStore(legacyPath).LoadAsync();
    Assert(
        migrated.SchemaVersion == HubSettings.CurrentSchemaVersion &&
        !migrated.AutoRefresh &&
        migrated.AutoRefreshMinutes == 30,
        "Legacy automatic rescans were not disabled during migration.");

    var v2Path = Path.Combine(testRoot, "Version 2 AppData", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(v2Path)!);
    await File.WriteAllTextAsync(
        v2Path,
        "{\"schemaVersion\":2,\"autoRefresh\":true,\"autoRefreshMinutes\":5,\"galleryFilter\":\"similar-images\"}");
    var v2Migrated = await new SettingsStore(v2Path).LoadAsync();
    Assert(
        v2Migrated.SchemaVersion == HubSettings.CurrentSchemaVersion &&
        v2Migrated.AutoRefresh &&
        v2Migrated.AutoRefreshMinutes == 5 &&
        v2Migrated.GalleryFilter == "similar-images",
        "Version 2 settings lost the user's rescan or gallery-filter choice during migration.");
    Assert(v2Migrated.DatePeriod == "all-time" && v2Migrated.SortOrder == "newest" &&
           v2Migrated.DateFrom is null && v2Migrated.DateTo is null,
        "Older settings unexpectedly hid images using a date filter.");

    var invalidBrowse = HubSettings.CreateDefault();
    invalidBrowse.DatePeriod = "unknown";
    invalidBrowse.SortOrder = "unknown";
    var browseStore = new SettingsStore(Path.Combine(testRoot, "Browse Settings", "settings.json"));
    await browseStore.SaveAsync(invalidBrowse);
    var normalizedBrowse = await browseStore.LoadAsync();
    Assert(normalizedBrowse.DatePeriod == "all-time" && normalizedBrowse.SortOrder == "newest",
        "Invalid browse settings were not restored to safe defaults.");
}

static async Task RunCatalogStoreTestsAsync(string testRoot)
{
    var catalogPath = Path.Combine(testRoot, "Catalog AppData", "catalog-v1-ja-JP.json");
    var store = new ScreenshotCatalogStore(catalogPath);
    Assert(await store.LoadAsync() is null, "A missing catalog was not treated as an initial launch.");

    var library = Path.Combine(testRoot, "Catalog", "原神", "ScreenShot");
    var firstPath = Path.Combine(library, "最初.png");
    var secondPath = Path.Combine(library, "次.jpg");
    var firstUtc = new DateTime(2026, 8, 7, 1, 2, 3, DateTimeKind.Utc);
    var firstCatalog = ScreenshotCatalogStore.Create(
        [new ScreenshotRecord(firstPath, library, "原神", firstUtc, 1234)],
        [new ScanRoot(library, "原神") { GameNameHint = "原神" }],
        firstUtc);
    await store.SaveAsync(firstCatalog);

    var loaded = await store.LoadAsync() ??
                 throw new InvalidOperationException("A saved catalog could not be loaded.");
    Assert(loaded.Screenshots.Count == 1, "Catalog screenshot round trip failed.");
    Assert(loaded.Screenshots[0].GameName == "原神", "Catalog Unicode text was not preserved.");
    Assert(loaded.Screenshots[0].LastWriteTimeUtc == firstUtc, "Catalog UTC timestamp changed.");
    Assert(loaded.Roots.Count == 1 && loaded.Roots[0].GameNameHint == "原神", "Catalog roots round trip failed.");

    var secondUtc = firstUtc.AddMinutes(1);
    var secondCatalog = ScreenshotCatalogStore.Create(
        [
            new ScreenshotRecord(firstPath, library, "原神", firstUtc, 1234),
            new ScreenshotRecord(secondPath, library, "原神", secondUtc, 5678)
        ],
        [new ScanRoot(library, "原神") { GameNameHint = "原神" }],
        secondUtc);
    await store.SaveAsync(secondCatalog);
    await File.WriteAllTextAsync(catalogPath, "{ invalid json");

    var recovered = await store.LoadAsync() ??
                    throw new InvalidOperationException("The catalog backup was not used after primary corruption.");
    Assert(recovered.Screenshots.Count == 1, "Catalog backup did not contain the prior known-good snapshot.");

    var emptyPath = Path.Combine(testRoot, "Empty Catalog", "catalog.json");
    var emptyStore = new ScreenshotCatalogStore(emptyPath);
    await emptyStore.SaveAsync(ScreenshotCatalogStore.Create([], [], DateTime.UtcNow));
    var empty = await emptyStore.LoadAsync();
    Assert(empty is not null && empty.Screenshots.Count == 0, "A completed zero-image scan was mistaken for an initial launch.");
}

static async Task RunFolderCatalogStoreTestsAsync(string testRoot)
{
    var catalogPath = Path.Combine(testRoot, "Folder Catalog AppData", "folders-v1-ja-JP.json");
    var store = new ScreenshotFolderCatalogStore(catalogPath);
    Assert(await store.LoadAsync() is null, "A missing folder catalog was not treated as an initial launch.");

    var library = Path.Combine(testRoot, "フォルダー一覧", "原神", "スクリーンショット");
    var firstUtc = new DateTime(2026, 8, 7, 4, 5, 6, DateTimeKind.Utc);
    var latestUtc = firstUtc.AddMinutes(2);
    var catalog = ScreenshotFolderCatalogStore.Create(
        [
            new ScreenshotRecord(Path.Combine(library, "最初.png"), library, "原神・撮影記録", firstUtc, 100),
            new ScreenshotRecord(Path.Combine(library, "次.jpg"), library, "原神・撮影記録", latestUtc, 200)
        ],
        [new ScanRoot(library, "原神・スクリーンショット", true) { GameNameHint = "原神・撮影記録" }],
        latestUtc);

    await store.SaveAsync(catalog);
    var loaded = await store.LoadAsync() ??
                 throw new InvalidOperationException("A saved folder catalog could not be loaded.");
    Assert(loaded.TotalScreenshots == 2, "Folder catalog total screenshot count changed.");
    Assert(loaded.Folders.Count == 1, "Folder catalog unexpectedly changed the folder count.");
    var summary = loaded.Folders[0];
    Assert(summary.Path == Path.GetFullPath(library), "Folder catalog Unicode path was not preserved.");
    Assert(summary.DisplayName == "原神・撮影記録", "Folder catalog Unicode display name was not preserved.");
    Assert(summary.IsCustom && summary.Count == 2, "Folder catalog summary fields changed.");
    Assert(summary.LatestUtc == latestUtc, "Folder catalog latest UTC timestamp changed.");
}

static async Task RunFullCatalogSemanticValidationTestsAsync(string testRoot)
{
    var catalogPath = Path.Combine(testRoot, "Semantic Catalog", "catalog.json");
    var store = new ScreenshotCatalogStore(catalogPath);
    var library = Path.Combine(testRoot, "Semantic Catalog", "原神", "Screenshots");
    var firstUtc = new DateTime(2026, 8, 7, 7, 8, 9, DateTimeKind.Utc);
    var validPath = Path.Combine(library, "known-good.png");
    var currentPath = Path.Combine(library, "newer.png");

    await store.SaveAsync(ScreenshotCatalogStore.Create(
        [new ScreenshotRecord(validPath, library, "原神", firstUtc, 123)],
        [new ScanRoot(library, "原神") { GameNameHint = "原神" }],
        firstUtc));
    await store.SaveAsync(ScreenshotCatalogStore.Create(
        [new ScreenshotRecord(currentPath, library, "原神", firstUtc.AddMinutes(1), 456)],
        [new ScanRoot(library, "原神") { GameNameHint = "原神" }],
        firstUtc.AddMinutes(1)));

    var semanticallyInvalid = ScreenshotCatalogStore.Create(
        [new ScreenshotRecord(Path.Combine(library, "not-an-image.txt"), library, "原神", firstUtc, 123)],
        [new ScanRoot(library, "原神") { GameNameHint = "原神" }],
        firstUtc);
    Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
    await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(semanticallyInvalid));

    var recovered = await store.LoadAsync() ??
                    throw new InvalidOperationException("A semantic catalog error did not fall back to the backup.");
    Assert(
        recovered.Screenshots.Count == 1 &&
        recovered.Screenshots[0].FilePath.Equals(validPath, StringComparison.OrdinalIgnoreCase),
        "A semantic catalog error did not recover the prior known-good catalog.");

    var noBackupPath = Path.Combine(testRoot, "Semantic Catalog No Backup", "catalog.json");
    Directory.CreateDirectory(Path.GetDirectoryName(noBackupPath)!);
    await File.WriteAllTextAsync(noBackupPath, JsonSerializer.Serialize(semanticallyInvalid));
    Assert(
        await new ScreenshotCatalogStore(noBackupPath).LoadAsync() is null,
        "A semantically invalid catalog without a backup was not rejected as null.");
}

static async Task RunUserDataStoreTestsAsync(string testRoot)
{
    var dataDirectory = Path.Combine(testRoot, "User Data Store");
    var storePath = Path.Combine(dataDirectory, "user-data-v1.json");
    var imagePath = Path.Combine(testRoot, "User Data Images", "思い出.png");
    Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
    await File.WriteAllBytesAsync(imagePath, OnePixelPng());
    var imageUtc = new DateTime(2026, 8, 8, 2, 3, 4, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(imagePath, imageUtc);
    var imageBytes = await File.ReadAllBytesAsync(imagePath);

    var store = new ScreenshotUserDataStore(storePath);
    var first = ScreenshotUserDataStore.Create(
    [
        new ScreenshotUserData
        {
            FilePath = imagePath,
            IsFavorite = true,
            Tags = [" 夜景 ", "夜景", "\u0001アクション"],
            UpdatedUtc = new DateTime(2026, 8, 8, 3, 4, 5, DateTimeKind.Utc)
        }
    ]);
    await store.SaveAsync(first);
    var loaded = await store.LoadAsync();
    Assert(loaded.Items.Count == 1, "Favorite and tag metadata was not saved.");
    Assert(loaded.Items[0].IsFavorite, "Favorite metadata changed during the round trip.");
    Assert(
        loaded.Items[0].Tags.SequenceEqual(["アクション", "夜景"]),
        $"Tags were not normalized and de-duplicated: {string.Join(", ", loaded.Items[0].Tags)}");
    Assert(File.GetLastWriteTimeUtc(imagePath) == imageUtc, "Saving metadata changed the image timestamp.");
    Assert(
        (await File.ReadAllBytesAsync(imagePath)).SequenceEqual(imageBytes),
        "Saving metadata changed the image bytes.");

    await store.SaveAsync(ScreenshotUserDataStore.Create(
    [
        new ScreenshotUserData
        {
            FilePath = imagePath,
            IsFavorite = false,
            Tags = ["second snapshot"],
            UpdatedUtc = DateTime.UtcNow
        }
    ]));
    await File.WriteAllTextAsync(storePath, "{ invalid json");
    var recovered = await store.LoadAsync();
    Assert(
        recovered.Items.Count == 1 &&
        recovered.Items[0].IsFavorite &&
        recovered.Items[0].Tags.Contains("夜景"),
        "User metadata did not recover its previous known-good backup.");

    var resolved = ScreenshotUserDataStore.ResolvePathForSettings(
        Path.Combine(testRoot, "Portable Data", "settings.json"));
    Assert(
        string.Equals(Path.GetFileName(resolved), "user-data-v1.json", StringComparison.Ordinal),
        "User metadata was incorrectly split by UI language.");
}

static async Task RunAnalysisStoreTestsAsync(string testRoot)
{
    var storePath = Path.Combine(testRoot, "Analysis Store", "analysis-v1.json");
    var imagePath = Path.Combine(testRoot, "Analysis Images", "shot.png");
    var otherPath = Path.Combine(testRoot, "Analysis Images", "other.png");
    var analyzedUtc = new DateTime(2026, 8, 8, 4, 5, 6, DateTimeKind.Utc);
    var firstRecord = new ScreenshotAnalysisRecord(
        imagePath,
        analyzedUtc,
        123,
        new string('a', 64),
        0x0123456789ABCDEF,
        1920,
        1080);
    var store = new ScreenshotAnalysisStore(storePath);
    await store.SaveAsync(ScreenshotAnalysisStore.Create([firstRecord], analyzedUtc));
    var loaded = await store.LoadAsync();
    Assert(loaded.Items.Count == 1, "The image-analysis cache was not saved.");
    Assert(loaded.Items[0].Sha256 == new string('A', 64), "SHA-256 text was not normalized.");
    Assert(loaded.Items[0].DifferenceHash == firstRecord.DifferenceHash, "The difference hash changed.");

    await store.SaveAsync(ScreenshotAnalysisStore.Create(
    [
        firstRecord with { DifferenceHash = 42 },
        new ScreenshotAnalysisRecord(otherPath, analyzedUtc, 456, null, 99, 1280, 720)
    ], analyzedUtc.AddMinutes(1)));
    await File.WriteAllTextAsync(storePath, "not json");
    var recovered = await store.LoadAsync();
    Assert(
        recovered.Items.Count == 1 && recovered.Items[0].DifferenceHash == firstRecord.DifferenceHash,
        "The analysis cache did not recover its previous known-good backup.");

    var rejectedInvalidHash = false;
    try
    {
        _ = ScreenshotAnalysisStore.Create(
            [firstRecord with { Sha256 = "not-a-sha256" }],
            analyzedUtc);
    }
    catch (InvalidDataException)
    {
        rejectedInvalidHash = true;
    }

    Assert(rejectedInvalidHash, "An invalid SHA-256 value was accepted into the analysis cache.");
}

static void RunDuplicateGroupingTests(string testRoot)
{
    var directory = Path.Combine(testRoot, "Duplicate Grouping");
    var timestamp = new DateTime(2026, 8, 8, 5, 6, 7, DateTimeKind.Utc);
    var exactHash = new string('B', 64);
    var a = new ScreenshotAnalysisRecord(Path.Combine(directory, "a.png"), timestamp, 100, exactHash, 0, 1920, 1080);
    var b = new ScreenshotAnalysisRecord(Path.Combine(directory, "b.png"), timestamp, 100, exactHash, 1, 1920, 1080);
    var spreadNearHash = (1UL << 0) | (1UL << 16) | (1UL << 32) | (1UL << 48);
    var c = new ScreenshotAnalysisRecord(Path.Combine(directory, "c.png"), timestamp, 101, null, spreadNearHash, 1920, 1080);
    var distant = new ScreenshotAnalysisRecord(Path.Combine(directory, "distant.png"), timestamp, 102, null, ulong.MaxValue, 1920, 1080);
    var wrongAspect = new ScreenshotAnalysisRecord(Path.Combine(directory, "portrait.png"), timestamp, 103, null, 1, 1080, 1920);
    var groups = DuplicateGrouper.Build([a, b, c, distant, wrongAspect]);

    Assert(groups[a.FilePath].IsExactDuplicate && groups[a.FilePath].ExactGroupCount == 2,
        "Exact SHA-256 duplicates were not grouped.");
    Assert(groups[b.FilePath].ExactGroupId == groups[a.FilePath].ExactGroupId,
        "Exact duplicates received different group identifiers.");
    Assert(groups[a.FilePath].IsSimilar && groups[a.FilePath].SimilarGroupCount == 3,
        "Near difference hashes were not grouped as similar images.");
    Assert(!groups[distant.FilePath].IsSimilar, "A visually distant hash was grouped as similar.");
    Assert(!groups[wrongAspect.FilePath].IsSimilar, "A portrait image was grouped with landscape images.");
    Assert(DuplicateGrouper.HammingDistance(0, ulong.MaxValue) == 64,
        "Difference-hash Hamming distance is incorrect.");
}

static void RunDataLocationTests(string testRoot)
{
    var applicationDirectory = Path.Combine(testRoot, "Portable Resolution", "app");
    Directory.CreateDirectory(applicationDirectory);
    Assert(
        DataLocationResolver.ResolveSettingsPath(null, applicationDirectory) is null,
        "A standard build unexpectedly selected the portable data folder.");

    File.WriteAllText(Path.Combine(applicationDirectory, "portable.flag"), string.Empty);
    var portablePath = DataLocationResolver.ResolveSettingsPath(null, applicationDirectory);
    Assert(
        portablePath == Path.Combine(Path.GetFullPath(applicationDirectory), "Data", "settings.json"),
        "The portable marker did not select the Data folder beside the executable.");

    var explicitPath = Path.Combine(testRoot, "Explicit Data", "settings.json");
    Assert(
        DataLocationResolver.ResolveSettingsPath(explicitPath, applicationDirectory) == Path.GetFullPath(explicitPath),
        "An explicit data directory did not override portable mode.");
}

static void RunBrowseQueryTests(string testRoot)
{
    var today = new DateTime(2026, 9, 21);
    var zone = TimeZoneInfo.CreateCustomTimeZone("Test JST", TimeSpan.FromHours(9), "Test JST", "Test JST");
    var library = Path.Combine(testRoot, "Metadata Only");
    ScreenshotRecord Shot(string name, DateTime utc, long size) => new(
        Path.Combine(library, name), library, "Test game", utc, size);
    var start = new DateTime(2026, 9, 14, 15, 0, 0, DateTimeKind.Utc);
    var before = Shot("b-before.png", start.AddTicks(-1), 10);
    var first = Shot("c-start.png", start, 30);
    var last = Shot("a-end.png", start.AddDays(7).AddTicks(-1), 20);
    var tomorrow = Shot("d-tomorrow.png", start.AddDays(7), 40);
    ScreenshotRecord[] records = [last, tomorrow, before, first];
    ScreenshotRecord[] Filter(string period, DateTime? from = null, DateTime? to = null) =>
        ScreenshotBrowseQuery.FilterByDate(records, period, from, to, today, zone).ToArray();

    Assert(Filter("today").SequenceEqual([last]), "Today did not respect the local date boundary.");
    Assert(Filter("last-7-days").SequenceEqual([last, first]), "The last seven days were not inclusive calendar days.");
    Assert(Filter("last-30-days").SequenceEqual([last, before, first]), "A future image leaked into a relative period.");
    Assert(Filter("custom", today.AddDays(-6), today).SequenceEqual([last, first]),
        "A custom range excluded its start or end day.");
    Assert(Filter("custom", today).SequenceEqual([last, tomorrow]), "A start-only range failed.");
    Assert(Filter("custom", to: today.AddDays(-6)).SequenceEqual([before, first]), "An end-only range failed.");
    Assert(Filter("custom", today, today.AddDays(-1)).Length == 0, "A reversed range was treated as valid.");
    Assert(Filter("all-time", today, today).SequenceEqual(records), "Inactive custom dates affected all-time results.");
    Assert(Filter("custom", DateTime.MinValue, DateTime.MaxValue).Length == records.Length,
        "A date limit overflowed or excluded a valid record.");

    Assert(ScreenshotBrowseQuery.Sort(records, "newest").SequenceEqual([tomorrow, last, first, before]),
        "Newest-first sorting failed.");
    Assert(ScreenshotBrowseQuery.Sort(records, "oldest").SequenceEqual([before, first, last, tomorrow]),
        "Oldest-first sorting failed.");
    Assert(ScreenshotBrowseQuery.Sort(records, "name").SequenceEqual([last, before, first, tomorrow]),
        "Filename sorting failed.");
    Assert(ScreenshotBrowseQuery.Sort(records, "largest").SequenceEqual([tomorrow, first, last, before]),
        "Size sorting failed.");
    Assert(ScreenshotBrowseQuery.Sort(records, "oldest", record =>
        record == before || record == tomorrow ? "A" : "B").SequenceEqual([before, tomorrow, first, last]),
        "Changing sort order split duplicate groups apart.");
    var tieB = first with { FilePath = Path.Combine(library, "z", "same.png") };
    var tieA = first with { FilePath = Path.Combine(library, "a", "same.png") };
    Assert(ScreenshotBrowseQuery.Sort([tieB, tieA], "name").SequenceEqual([tieA, tieB]),
        "Equal sort values were not given a stable path-based order.");

    // The US spring-forward day has 23 hours; inclusive local dates must still work.
    var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    var dstStart = new DateTime(2026, 3, 8, 8, 0, 0, DateTimeKind.Utc);
    ScreenshotRecord[] dstRecords =
    [
        Shot("before.png", dstStart.AddTicks(-1), 1),
        Shot("first.png", dstStart, 1),
        Shot("last.png", dstStart.AddHours(23).AddTicks(-1), 1),
        Shot("after.png", dstStart.AddHours(23), 1)
    ];
    Assert(ScreenshotBrowseQuery.FilterByDate(dstRecords, "today", null, null,
        new DateTime(2026, 3, 8), pacific).SequenceEqual([dstRecords[1], dstRecords[2]]),
        "Daylight-saving time caused a local-day boundary error.");
    Assert(!Directory.Exists(library), "Browsing unexpectedly touched a screenshot directory.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static byte[] OnePixelPng() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2Z7sAAAAASUVORK5CYII=");
