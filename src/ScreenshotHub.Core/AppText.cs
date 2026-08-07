using System.Reflection;

namespace ScreenshotHub.Core;

public static class AppText
{
    private static readonly bool English = ResolveEnglishBuild();

    public static string LanguageCode => English ? "en-US" : "ja-JP";
    public static string FontFamily => English ? "Segoe UI, Yu Gothic UI" : "Yu Gothic UI, Segoe UI";
    public static string WindowTitle => English ? "Screenshot Hub" : "スクリーンショット・ハブ";
    public static string Tagline => English ? "Your game memories, all in one place" : "ゲームの思い出を、ひとつの場所に";
    public static string Collections => English ? "Collections" : "コレクション";
    public static string DetectedAndAddedFolders => English ? "Auto-detected + added folders" : "自動検出 + 追加フォルダー";
    public static string RemoveAddedFolderTooltip => English
        ? "Remove this added folder from the list (images are not deleted)"
        : "この追加フォルダーを一覧から外す（画像は削除しません）";
    public static string FolderOnlyMode => English ? "Lightweight folder view" : "軽量表示（フォルダーのみ）";
    public static string FolderOnlyModeHint => English
        ? "Skips thumbnail loading and lists locations for quick access."
        : "サムネイルを読み込まず、保存フォルダーだけを一覧表示します。";
    public static string AutoRescan => English ? "Scheduled rescans" : "定期的に再スキャン";
    public static string AutoRescanHint => English
        ? "Off by default. Use Rescan above whenever you want to check for changes."
        : "初期設定はオフです。変更を確認したいときは上の「再スキャン」を使います。";
    public static string Interval => English ? "Interval" : "間隔";
    public static string Minutes => English ? "min" : "分";
    public static string SearchTooltip => English
        ? "Search by file name, game name, or folder path"
        : "ファイル名・ゲーム名・フォルダーパスから検索";
    public static string SearchPrompt => English ? "Search games and file names" : "ゲーム名・ファイル名で検索";
    public static string SearchFoldersPrompt => English ? "Search folders" : "フォルダーを検索";
    public static string SearchFoldersTooltip => English
        ? "Search by game name or folder path"
        : "ゲーム名・フォルダーパスから検索";
    public static string Cancel => English ? "Cancel" : "キャンセル";
    public static string Rescan => English ? "↻  Rescan" : "↻  再スキャン";
    public static string RescanTooltip => English
        ? "Rescan to update the image list (F5)"
        : "再スキャンして画像一覧を更新（F5）";
    public static string AddFolder => English ? "+  Add folder" : "＋  フォルダーを追加";
    public static string AddFolderPlain => English ? "Add folder" : "フォルダーを追加";
    public static string OpenFolder => English ? "Open folder" : "フォルダーを開く";
    public static string FolderOnlyTitle => English ? "Screenshot folders" : "スクリーンショットフォルダー";
    public static string OpenDefaultApp => English ? "Open in default app" : "既定のアプリで開く";
    public static string ShowInExplorer => English ? "Show in File Explorer" : "エクスプローラーで表示";
    public static string CopyPath => English ? "Copy path" : "パスをコピー";
    public static string CreatingPreview => English ? "Creating preview" : "プレビューを作成中";
    public static string PreviewUnavailable => English ? "Preview unavailable" : "プレビューを表示できません";
    public static string SearchingScreenshots => English ? "Searching for screenshots" : "スクリーンショットを探しています";
    public static string SearchingDeepLocations => English
        ? "Checking deep save locations in the background"
        : "深い保存先もバックグラウンドで確認します";
    public static string LoadingSavedList => English ? "Loading the saved list" : "保存済みの一覧を開いています";
    public static string LoadingSavedListDetail => English
        ? "Screenshot folders are not being scanned"
        : "スクリーンショットフォルダーは走査していません";

    public static string WindowsScreenshots => English ? "Windows Screenshots" : "Windows スクリーンショット";
    public static string Pictures => English ? "Pictures" : "ピクチャ";
    public static string AddedFolder => English ? "Added folder" : "追加フォルダー";
    public static string AllScreenshots => English ? "All screenshots" : "すべてのスクリーンショット";
    public static string AllDetectedFolders => English ? "All detected folders" : "すべての検出フォルダー";
    public static string NoImages => English ? "No images" : "画像なし";
    public static string NotScanned => English ? "Not scanned" : "未スキャン";
    public static string Preparing => English ? "Preparing…" : "準備しています…";
    public static string PreparingDetail => English
        ? "Checking known screenshot locations"
        : "スクリーンショットの保存先を確認します";
    public static string NoScreenshotsYet => English ? "No screenshots yet" : "スクリーンショットはまだありません";
    public static string EmptyNotFound => English ? "No screenshots found" : "スクリーンショットが見つかりませんでした";
    public static string EmptySearch => English ? "No images match your search" : "検索に一致する画像がありません";
    public static string EmptyCollection => English ? "This collection is empty" : "このコレクションは空です";
    public static string EmptyInitialHint => English
        ? "Use Add folder to choose a save location, or run another scan."
        : "「フォルダーを追加」で保存先を指定するか、再スキャンしてください。";
    public static string EmptyFilteredHint => English
        ? "Try changing the search text or the collection on the left."
        : "検索語や左側のコレクションを変更してみてください。";
    public static string EmptyFoldersNotFound => English ? "No screenshot folders found" : "スクリーンショットフォルダーがありません";
    public static string EmptyFolderSearch => English ? "No folders match your search" : "検索に一致するフォルダーがありません";
    public static string EmptyFoldersHint => English
        ? "Use Add folder, or run Rescan to find save locations."
        : "「フォルダーを追加」で保存先を指定するか、再スキャンしてください。";
    public static string EmptyFolderSearchHint => English
        ? "Try changing the search text or the collection on the left."
        : "検索語や左側のコレクションを変更してみてください。";
    public static string GalleryListUnavailable => English ? "The saved gallery list is unavailable" : "保存済みの画像一覧を利用できません";
    public static string GalleryListUnavailableHint => English
        ? "Use Lightweight folder view, or select Rescan to rebuild the gallery."
        : "軽量表示でフォルダーを開くか、「再スキャン」で画像一覧を作り直してください。";
    public static string SettingsLoadFailed => English ? "Could not load settings" : "設定を読み込めませんでした";
    public static string WaitingToScan => English ? "Waiting to scan" : "スキャン待機中";
    public static string WaitingToScanDetail => English
        ? "Select Rescan above to search for images"
        : "上の「再スキャン」を押すと画像を探します";
    public static string CannotAddWhileScanning => English
        ? "Folders cannot be added during a scan"
        : "スキャン中はフォルダーを追加できません";
    public static string CannotAddWhileScanningDetail => English
        ? "Cancel the scan or wait for it to finish"
        : "スキャンをキャンセルするか、完了するまでお待ちください";
    public static string AddFolderFailed => English ? "Could not add the folder" : "フォルダーを追加できませんでした";
    public static string FolderAccessFailed => English
        ? "The selected folder cannot be accessed"
        : "選択したフォルダーにアクセスできません";
    public static string CannotAddDriveRoot => English ? "A whole drive cannot be added" : "ドライブ全体は追加できません";
    public static string CannotAddDriveRootDetail => English
        ? "For safety, select the folder where screenshots are saved"
        : "安全のため、スクリーンショットが保存されるフォルダーを選択してください";
    public static string SearchingScreenshotsProgress => English
        ? "Searching for screenshots…"
        : "スクリーンショットを探しています…";
    public static string CheckingLocations => English ? "Checking save locations" : "保存先を確認しています";
    public static string ScanCancelled => English ? "Scan cancelled" : "スキャンをキャンセルしました";
    public static string ListNotUpdated => English ? "The image list was not updated" : "画像一覧は更新されていません";
    public static string ScanFailed => English ? "Could not complete the scan" : "スキャンを完了できませんでした";
    public static string SettingsSaveFailed => English ? "Could not save settings" : "設定を保存できませんでした";
    public static string CatalogSaveFailed => English ? "Could not save the image list" : "画像一覧を保存できませんでした";
    public static string RescanToUpdate => English
        ? "Showing the saved list · use Rescan to check for changes"
        : "保存済みの一覧を表示中 · 変更を確認するには「再スキャン」を使用";
    public static string SelectScreenshotFolder => English
        ? "Select a screenshot save folder"
        : "スクリーンショットの保存フォルダーを選択";
    public static string MissingImageMessage => English
        ? "The original image could not be found. Rescan to update the list."
        : "元の画像が見つかりません。再スキャンすると一覧が更新されます。";
    public static string MissingFolderMessage => English
        ? "The folder could not be found. Rescan to update the list."
        : "フォルダーが見つかりません。再スキャンすると一覧が更新されます。";
    public static string ActionFailedTitle => English ? "Could not complete the action" : "操作を完了できませんでした";

    public static string InvalidSettingsPath => English
        ? "The settings file location is invalid."
        : "設定ファイルの保存先が不正です。";
    public static string ScreenshotFallback => English ? "Screenshots" : "スクリーンショット";

    public static string Latest(DateTime local) => English ? $"Latest {local:M/d  HH:mm}" : $"最新 {local:M/d  HH:mm}";
    public static string CollectionSummary(int count) => English ? $"{count:N0} {ImageWord(count)} · Newest first" : $"{count:N0} 枚 · 新しい順";
    public static string FolderSummary(int count) => English ? $"{count:N0} {FolderWord(count)} · No thumbnails" : $"{count:N0} フォルダー · サムネイルなし";
    public static string ImageCount(int count) => English ? $"{count:N0} {ImageWord(count)}" : $"{count:N0} 枚";
    public static string PageRange(int start, int end, int total) => English
        ? $"{start:N0}–{end:N0} / {total:N0} {ImageWord(total)}"
        : $"{start:N0}–{end:N0} / {total:N0} 枚";
    public static string CheckingPath(string path) => English ? $"Checking: {path}" : $"確認中: {path}";
    public static string ScanProgress(int directories, int screenshots) => English
        ? $"{directories:N0} {FolderWord(directories)} · {screenshots:N0} {ImageWord(screenshots)}"
        : $"{directories:N0} フォルダー · {screenshots:N0} 枚";
    public static string LastScanned(DateTime local) => English ? $"Last scan {local:M/d HH:mm}" : $"最終スキャン {local:M/d HH:mm}";
    public static string ScreenshotsFound(int count) => English ? $"{count:N0} {(count == 1 ? "screenshot" : "screenshots")}" : $"{count:N0} 枚のスクリーンショット";
    public static string SavedCatalogLoaded(int count) => English
        ? $"{count:N0} saved {(count == 1 ? "screenshot" : "screenshots")}"
        : $"保存済みのスクリーンショット {count:N0} 枚";
    public static string SavedFoldersLoaded(int count) => English
        ? $"{count:N0} saved {FolderWord(count)}"
        : $"保存済みのフォルダー {count:N0} 件";
    public static string UseLightweightOrRescan => English
        ? "Use Lightweight folder view, or select Rescan to rebuild the gallery"
        : "軽量表示を使うか、「再スキャン」で画像一覧を作り直してください";
    public static string GalleryListOlderThanFolders => English
        ? "Showing the previous gallery list · select Rescan when you want to update it"
        : "前回の画像一覧を表示中 · 更新したいときに「再スキャン」を使用";
    public static string FoldersChecked(int directories, string duration) => English
        ? $"Checked {directories:N0} {FolderWord(directories)} in {duration}"
        : $"{directories:N0} フォルダーを {duration} で確認";
    public static string CompletedWithWarnings(int count) => English
        ? $"Complete · {count:N0} {(count == 1 ? "skipped item or warning" : "skipped items or warnings")}"
        : $"完了 · スキップまたは警告 {count:N0} 件";
    public static string CatalogKeptAfterWarnings(int count) => English
        ? $"The saved list was kept because the scan had {count:N0} {(count == 1 ? "warning" : "warnings")}"
        : $"警告が {count:N0} 件あったため、前回の正常な一覧を保持しました";
    public static string PreviousImages(int count) => English
        ? $"Showing the previous {count:N0} {ImageWord(count)}"
        : $"前回の {count:N0} 枚を表示しています";
    public static string Seconds(double value) => English ? $"{value:0.0} sec" : $"{value:0.0} 秒";
    public static string OpenFolderNamed(string displayName) => English
        ? $"Open {displayName} folder"
        : $"{displayName} フォルダーを開く";

    public static string ScanLimitReached(string path) => English
        ? $"Stopped after reaching the scan limit: {path}"
        : $"走査上限に達したため途中で停止: {path}";
    public static string CannotInspectFolder(string path) => English
        ? $"Cannot inspect folder: {path}"
        : $"フォルダーを確認できません: {path}";
    public static string MissingScanRoot(string path) => English
        ? $"Save location not found: {path}"
        : $"保存先が見つかりません: {path}";
    public static string DriveRootSkipped(string path) => English
        ? $"A whole drive is not scanned for safety: {path}"
        : $"ドライブ全体は安全のため走査しません: {path}";
    public static string ReparsePointSkipped(string path) => English
        ? $"Links and junctions are not scanned for safety: {path}"
        : $"リンク／ジャンクションは安全のため走査しません: {path}";
    public static string CannotInspectRoot(string path) => English
        ? $"Cannot inspect save location: {path}"
        : $"保存先を確認できません: {path}";
    public static string CannotInspectImage(string path) => English
        ? $"Cannot inspect image: {path}"
        : $"画像を確認できません: {path}";
    public static string CannotListFolder(string path) => English
        ? $"Cannot list folder contents: {path}"
        : $"フォルダー内を確認できません: {path}";
    public static string CannotListImages(string path) => English
        ? $"Cannot list images: {path}"
        : $"画像一覧を確認できません: {path}";

    public static string MissingScanRootArgument => English ? "--scan-root requires a folder path" : "--scan-root にはフォルダーパスが必要です";
    public static string InvalidScanRootArgument => English ? "--scan-root contains an invalid path" : "--scan-root のパスが無効です";
    public static string MissingDiagnosticsArgument => English ? "--diagnostics-json requires an output path" : "--diagnostics-json には出力パスが必要です";
    public static string InvalidDiagnosticsArgument => English ? "--diagnostics-json contains an invalid path" : "--diagnostics-json のパスが無効です";
    public static string MissingDataDirectoryArgument => English ? "--data-dir requires a folder path" : "--data-dir にはフォルダーパスが必要です";
    public static string InvalidDataDirectoryArgument => English ? "--data-dir contains an invalid path" : "--data-dir のパスが無効です";
    public static string InvalidMaxDepthArgument => English ? "--max-depth requires an integer from 1 to 64" : "--max-depth には 1～64 の整数が必要です";
    public static string UnknownArgument(string argument) => English ? $"Unknown argument: {argument}" : $"不明な引数です: {argument}";

    private static bool ResolveEnglishBuild()
    {
        try
        {
            var language = Assembly.GetEntryAssembly()?
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "ScreenshotHubLanguage")?
                .Value;
            return string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ImageWord(int count) => count == 1 ? "image" : "images";

    private static string FolderWord(int count) => count == 1 ? "folder" : "folders";
}
