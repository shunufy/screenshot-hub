# スクリーンショット・ハブ

[English](README.md) | 日本語

ゲームごとに深い場所へ保存されるスクリーンショットを、Windows上でまとめて探して閲覧するローカル専用アプリです。

## ダウンロード

[v0.2.1 Release](../../releases/tag/v0.2.1) を開き、使用する言語を選びます。

- `ScreenshotHub-0.2.1-ja-JP-win-x64.zip` — 日本語UI
- `ScreenshotHub-0.2.1-en-US-win-x64.zip` — 英語UI

ZIPを展開し、`ScreenshotHub.exe` を起動します。どちらもWindows 10/11 x64向けの自己完結型ビルドなので、.NETを別途インストールする必要はありません。

現在の実行ファイルにはコード署名がないため、Windows SmartScreenの警告が表示される場合があります。このリポジトリのReleasesページから取得したZIPであることを確認し、Release NotesのSHA-256と照合してから実行してください。

## 主な機能

- 初回起動時のみ全体を自動スキャンし、2回目以降は保存済みのローカル一覧を即時表示
- 初回スキャン後の更新は手動再スキャンが基本で、定期再スキャンは既定OFF（任意で有効化可能）
- 保存済みのフォルダー要約だけを読み込み、サムネイルを読み込まず保存先を直接開ける軽量表示
- ゲーム／保存フォルダー別のコレクション表示
- 最新順のサムネイル表示と、ゲーム名・ファイル名・パスの検索
- 既定アプリで開く、エクスプローラーで表示、パスをコピー
- 任意フォルダーの追加・追加したフォルダーを一覧から外す操作と、スキャンのキャンセル
- 大量の画像に対応するページングとキーボード操作

## 自動検出する場所

次のようなローカル保存先を確認します。

- Windowsのスクリーンショット、ピクチャ、Xbox Game Barのキャプチャ、Documents/My Games、Saved Games
- AppDataのLocal、LocalLow、Roamingと、Microsoft Store系ゲームのデータ
- Steamのライブラリと`userdata`
- Epic Gamesのマニフェスト
- HoYoPlayのインストール情報
- Windowsのインストール済みプログラム情報に含まれるゲームらしい保存先
- Minecraft、CurseForge、Prism Launcher、MultiMC、Modrinthのプロファイル

見つかったゲーム内では、`ScreenShot`、`Screenshots`、`Capture`、`PhotoMode`、`Photos`、`Gallery`などを探します。1つのゲームに複数の保存フォルダーがあっても検出します。

## 安全性とプライバシー

- Screenshot Hub自身はプレビュー作成時に画像を書き換えず、移動・改名・編集・削除しません。
- 画像やフォルダー情報を外部へ送信せず、テレメトリも使用しません。
- ドライブ全体は走査せず、深さとフォルダー数に上限を設けています。
- ジャンクション、シンボリックリンク、キャッシュ、一般的な開発用フォルダーは再帰対象から除外します。
- 設定は `%LOCALAPPDATA%\ScreenshotHub\settings.json` に保存します。
- 画像の全一覧と軽量表示用のフォルダー要約は、設定と同じ場所に言語別の `catalog-v1-*.json` と `folders-v1-*.json` としてローカル保存し、外部へは送信しません。

アプリからフォルダーを除外しても、元の画像ファイルは削除されません。

## 対応画像形式

PNG、JPG/JPEG、BMP、GIF、TIF/TIFF。

## 既知の制限

- Windows 10/11 x64専用です。
- v0.2.xではWebPに対応していません。
- 自動検出は完全ではありません。既知の保存先に含まれない個人用フォルダーは、**フォルダーを追加**から指定してください。
- 特殊なフォルダー名、アクセスできない場所、非常に深い保存先は自動検出できない場合があります。

## ビルドとテスト

Windows上の.NET 8 SDKが必要です。

```powershell
dotnet restore .\ScreenshotHub.sln
dotnet build .\ScreenshotHub.sln -c Release --no-restore
dotnet run --project .\tests\ScreenshotHub.SelfTest\ScreenshotHub.SelfTest.csproj -c Release --no-build

# 英語版の開発ビルド
dotnet build .\src\ScreenshotHub\ScreenshotHub.csproj -c Release -p:ScreenshotHubLanguage=en-US
```

現在指定している.NETランタイムのセキュリティパッチで日英両方のRelease ZIPを作成します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

UIを開かず、指定したフォルダーだけを診断スキャンすることもできます。

```powershell
ScreenshotHub.exe --scan-root "C:\path\to\fixture" --diagnostics-json ".\result.json" --exit-after-scan
```
