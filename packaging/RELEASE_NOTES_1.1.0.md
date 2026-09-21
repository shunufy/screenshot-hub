## 日本語

Screenshot Hub 1.1.0は、スクリーンショットを日付で探し、タグやお気に入りで整理できるローカル専用のWindowsアプリです。前回の公開版v0.2.1からの更新をまとめて収録しています。

- 保存日で絞り込み：今日・過去7日・過去30日・カレンダーで指定した期間。開始日・終了日を含み、片側を空欄にした検索にも対応。
- 新しい順・古い順・ファイル名順・容量が大きい順の並び替え。ビューアーの前後移動にも反映し、期間と並び順を保存。
- お気に入りとタグ、ゲーム名・ファイル名・パス・タグの検索、各種フィルター。
- 任意実行の完全重複・類似画像解析。結果は表示上のグループ化に使い、画像を削除・統合しません。
- 拡大縮小・原寸・ウィンドウ合わせ・ドラッグ移動・前後移動に対応した内蔵ビューアー。
- 手動の差分再スキャンで未変更の一覧情報を再利用。初回のみ自動スキャンし、定期再スキャンは既定OFF。軽量フォルダー表示も引き続き利用可能。
- 設定・タグ・お気に入り・解析結果のローカル保存と、アプリの隣の`Data`に保存するポータブル版。

「保存日」はPCの現地時間でのファイル更新日です。撮影日時と異なる場合があります。日付検索・並び替えは保存済みの一覧を使い、追加スキャンや画像解析を起動しません。画像の移動・改名・編集・削除・アップロードは行いません。

## English

Screenshot Hub 1.1.0 is a local Windows gallery for finding screenshots by date and organizing them with tags and favorites. This release includes all updates since the previously published v0.2.1.

- Saved-date filters: today, last 7 days, last 30 days, and a custom calendar range. Both endpoints are included; either can be left blank.
- Sort by newest, oldest, filename, or largest file. Viewer navigation follows the same order, and date/sort preferences are saved.
- Favorites, tags, search by game/filename/path/tag, and gallery filters.
- Optional exact-duplicate and similar-image analysis. Results group the view only; images are never deleted or consolidated.
- Built-in viewer with zoom, actual size, fit-to-window, drag-to-pan, and previous/next navigation.
- Manual incremental rescans reuse unchanged catalog records. Automatic scanning happens only on first launch; scheduled rescans are OFF by default. Lightweight folder-only view remains available.
- Local persistence for settings, tags, favorites, and analysis, plus portable builds that save app data in the adjacent `Data` folder.

Saved dates use each file's last-modified time in your local time zone and may differ from capture dates. Date filtering and sorting use the saved catalog without triggering additional scans or analysis. The app does not move, rename, edit, delete, or upload images.

## Downloads / ダウンロード

Windows 10/11 x64. Self-contained: no separate .NET installation is required. ZIPを展開し、`ScreenshotHub.exe`を起動してください。

| Language / 言語 | Standard / 通常版 | Portable / ポータブル版 |
| --- | --- | --- |
| 日本語 | [ja-JP ZIP](https://github.com/shunufy/screenshot-hub/releases/download/v1.1.0/ScreenshotHub-1.1.0-ja-JP-win-x64.zip) | [ja-JP portable ZIP](https://github.com/shunufy/screenshot-hub/releases/download/v1.1.0/ScreenshotHub-1.1.0-ja-JP-win-x64-portable.zip) |
| English | [en-US ZIP](https://github.com/shunufy/screenshot-hub/releases/download/v1.1.0/ScreenshotHub-1.1.0-en-US-win-x64.zip) | [en-US portable ZIP](https://github.com/shunufy/screenshot-hub/releases/download/v1.1.0/ScreenshotHub-1.1.0-en-US-win-x64-portable.zip) |

Standard builds use `%LOCALAPPDATA%\ScreenshotHub`. Portable builds use `Data` beside the executable. 実行ファイルは未署名のため、Windows SmartScreenが警告する場合があります。The executable is unsigned; Windows SmartScreen may display a warning.

## Verification / 検証

Release builds and regression tests passed. Japanese and English WPF UI smoke tests covered date selection, sorting, persistence, existing favorites/duplicate filters, and lightweight mode. All four packaged executables passed diagnostic startup. Test-image hashes and modification times were unchanged.

## SHA-256

```text
68D9F2F07390CD197851AA64C9053BB4B73B5E3C39A907EC98362E54ECC1837F  ScreenshotHub-1.1.0-ja-JP-win-x64.zip
2D36E5864F13674F550B35E2D7B4CAF1620EB3F9D427CA63B0EAE3314111E3DA  ScreenshotHub-1.1.0-ja-JP-win-x64-portable.zip
01E75AD56B0D72EE65ACA9DF4D17AF42E550AF13EEAE383307085AAC54CA8A1C  ScreenshotHub-1.1.0-en-US-win-x64.zip
46377D98B4EA514290A689F30DE5F345DEDE10E9288C15EB1847D312F056DFD3  ScreenshotHub-1.1.0-en-US-win-x64-portable.zip
```
