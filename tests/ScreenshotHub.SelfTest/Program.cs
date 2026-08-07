using ScreenshotHub.Core;
using System.Text.Json;

var testRoot = Path.Combine(
    Path.GetTempPath(),
    "ScreenshotHub.SelfTest",
    Guid.NewGuid().ToString("N"));

try
{
    Directory.CreateDirectory(testRoot);
    await RunScannerTestsAsync(testRoot);
    await RunOverlappingDepthBoundaryTestAsync(testRoot);
    await RunPackagesDiscoveryTestAsync(testRoot);
    await RunReparseRootTestWhenSupportedAsync(testRoot);
    await RunInstalledGameDiscoveryTestsAsync(testRoot);
    await RunSettingsTestsAsync(testRoot);
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
        AutoRefresh = false,
        AutoRefreshMinutes = 10,
        MaxDepth = 12,
        CustomRoots = [Path.Combine(testRoot, "First")]
    };
    await store.SaveAsync(first);
    var firstLoaded = await store.LoadAsync();
    Assert(!firstLoaded.AutoRefresh && firstLoaded.MaxDepth == 12, "Settings round trip failed.");

    var second = new HubSettings
    {
        AutoRefresh = true,
        AutoRefreshMinutes = 30,
        MaxDepth = 6,
        CustomRoots = [Path.Combine(testRoot, "Second")]
    };
    await store.SaveAsync(second);
    var secondLoaded = await store.LoadAsync();
    Assert(
        secondLoaded.AutoRefresh && secondLoaded.AutoRefreshMinutes == 30 && secondLoaded.MaxDepth == 6,
        "A later settings save did not become current.");
    await File.WriteAllTextAsync(settingsPath, "{ invalid json");

    var recovered = await store.LoadAsync();
    Assert(!recovered.AutoRefresh && recovered.MaxDepth == 12, "Known-good settings backup was not recovered.");

    var third = new HubSettings
    {
        AutoRefresh = false,
        AutoRefreshMinutes = 60,
        MaxDepth = 15,
        CustomRoots = [Path.Combine(testRoot, "Third")]
    };
    await store.SaveAsync(third);
    var fourth = new HubSettings
    {
        AutoRefresh = true,
        AutoRefreshMinutes = 1,
        MaxDepth = 3,
        CustomRoots = [Path.Combine(testRoot, "Fourth")]
    };
    await store.SaveAsync(fourth);
    await File.WriteAllTextAsync(settingsPath, "not json");
    var rotatedBackup = await store.LoadAsync();
    Assert(
        !rotatedBackup.AutoRefresh && rotatedBackup.AutoRefreshMinutes == 60 && rotatedBackup.MaxDepth == 15,
        "The known-good backup did not rotate after later saves.");
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
