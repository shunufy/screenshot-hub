# スクリーンショット・ハブ

[English](README.md) | 日本語

ゲームごとに深い場所へ保存されるスクリーンショットを、Windows上でまとめて探して閲覧するローカル専用アプリです。

## ダウンロード

[v1.1.0 Release](../../releases/tag/v1.1.0) を開き、言語と保存方式を選びます。

- `ScreenshotHub-1.1.0-ja-JP-win-x64.zip` — 日本語UI・通常のローカル設定
- `ScreenshotHub-1.1.0-en-US-win-x64.zip` — 英語UI・通常のローカル設定
- `ScreenshotHub-1.1.0-ja-JP-win-x64-portable.zip` — 日本語UI・アプリの隣にデータ保存
- `ScreenshotHub-1.1.0-en-US-win-x64-portable.zip` — 英語UI・アプリの隣にデータ保存

ZIPを展開し、`ScreenshotHub.exe` を起動します。どちらもWindows 10/11 x64向けの自己完結型ビルドなので、.NETを別途インストールする必要はありません。

現在の実行ファイルにはコード署名がないため、Windows SmartScreenの警告が表示される場合があります。このリポジトリのReleasesページから取得したZIPであることを確認し、Release NotesのSHA-256と照合してから実行してください。

## 主な機能

- 初回起動時のみ全体を自動スキャンし、2回目以降は保存済みのローカル一覧を即時表示
- 2回目以降の手動再スキャンでは変更のない一覧情報を再利用し、定期再スキャンは既定OFF（任意で有効化可能）
- 保存済みのフォルダー要約だけを読み込み、サムネイルを読み込まず保存先を直接開ける軽量表示
- ゲーム／保存フォルダー別のコレクション表示
- Screenshot Hub内だけに保存するお気に入りとタグ、タグ・お気に入り・タグ有無・重複グループによる検索／絞り込み
- ユーザーが明示的に実行したときだけ動く、完全一致と見た目の近い画像の上限付き解析
- 前後移動、ウィンドウ合わせ、原寸、拡大縮小、ドラッグ移動に対応した内蔵ビューアー
- 最新順のサムネイル表示と、ゲーム名・ファイル名・パス・タグの検索
- 保存日での絞り込み（今日・過去7日・過去30日・指定期間）と、新しい順／古い順／ファイル名順／容量が大きい順の並び替え
- 内蔵ビューアー／既定アプリで開く、エクスプローラーで表示、パスをコピー
- 任意フォルダーの追加・追加したフォルダーを一覧から外す操作と、スキャンのキャンセル
- 大量の画像に対応するページングとキーボード操作

## 1.1.0: 日付で探す・並べ替える

ギャラリー上部の「保存日」で期間を選べます。「期間を指定」ではカレンダーから開始日・終了日を選択し、片側を空欄にするとその側の制限を外せます。両方の日付を含めて検索し、「期間を解除」で全期間へ戻ります。保存日はファイルの更新日をPCの現地時間で表示したもので、撮影日時とは異なる場合があります。

「並び順」はビューアーでの前後移動にも反映され、重複・類似表示では各グループ内を並べ替えます。期間と並び順は保存されます。タグ・お気に入り・検索との組み合わせに対応し、追加のスキャンや画像解析は行いません。軽量表示では日付・並び順は適用されません。

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
- お気に入り・タグ・重複結果はアプリ内メタデータだけです。重複解析でファイルを削除・統合することはありません。
- 画像やフォルダー情報を外部へ送信せず、テレメトリも使用しません。
- ドライブ全体は走査せず、深さとフォルダー数に上限を設けています。
- ジャンクション、シンボリックリンク、キャッシュ、一般的な開発用フォルダーは再帰対象から除外します。
- 設定は `%LOCALAPPDATA%\ScreenshotHub\settings.json` に保存します。
- 画像の全一覧と軽量表示用のフォルダー要約は、設定と同じ場所に言語別の `catalog-v1-*.json` と `folders-v1-*.json` としてローカル保存し、外部へは送信しません。
- お気に入り／タグは `user-data-v1.json`、任意実行の解析結果は `analysis-v1.json` に保存します。
- ポータブル版は `portable.flag` を含み、これらのアプリデータを実行ファイルの隣の `Data` フォルダーへ保存します。

アプリからフォルダーを除外しても、元の画像ファイルは削除されません。

## 対応画像形式

PNG、JPG/JPEG、BMP、GIF、TIF/TIFF。

## 既知の制限

- Windows 10/11 x64専用です。
- v1.1.0ではWebPに対応していません。
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

現在指定している.NETランタイムのセキュリティパッチで日英両方の通常版／ポータブル版Release ZIPを作成します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

UIを開かず、指定したフォルダーだけを診断スキャンすることもできます。

```powershell
ScreenshotHub.exe --scan-root "C:\path\to\fixture" --diagnostics-json ".\result.json" --exit-after-scan
```
