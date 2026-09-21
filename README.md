# Screenshot Hub

English | [日本語](README_JA.md)

Screenshot Hub is a private local Windows app that finds game screenshots buried across different folders and brings them together in one gallery.

## Download

Open the [v1.1.0 release](../../releases/tag/v1.1.0), then choose a language and storage mode:

- `ScreenshotHub-1.1.0-en-US-win-x64.zip` — English UI, standard local settings
- `ScreenshotHub-1.1.0-ja-JP-win-x64.zip` — Japanese UI, standard local settings
- `ScreenshotHub-1.1.0-en-US-win-x64-portable.zip` — English UI, data beside the app
- `ScreenshotHub-1.1.0-ja-JP-win-x64-portable.zip` — Japanese UI, data beside the app

Extract the ZIP and run `ScreenshotHub.exe`. Both packages are self-contained builds for Windows 10/11 x64, so no separate .NET installation is required.

The executable is currently unsigned, so Windows SmartScreen may display a warning. Confirm that the ZIP came from this repository's Releases page and compare its SHA-256 value with the release notes before running it.

## Features

- One automatic full scan on first launch; later launches load the saved local catalogs
- Incremental manual rescans reuse unchanged catalog records; scheduled rescans remain opt-in and off by default
- Lightweight folder-only view that loads only saved folder summaries, never loads thumbnails, and opens save locations directly
- Collections grouped by game and capture folder
- Favorites and tags stored inside Screenshot Hub, plus search and filters for tags, favorites, tagged/untagged images, and duplicate groups
- User-triggered exact-copy and visually-similar analysis with a bounded difference-hash comparison
- Built-in viewer with previous/next navigation, fit, actual-size, zoom, and drag-to-pan controls
- Newest-first thumbnail gallery and search by game, filename, path, or tag
- Saved-date filters (today, last 7 days, last 30 days, or a custom range) and sorting by newest, oldest, filename, or largest file
- Open images in the built-in viewer or default app, reveal them in File Explorer, or copy their paths
- Add folders, remove folders you added, and cancel an active scan
- Keyboard controls and paging for large libraries

## 1.1.0: Find by date and change the order

Use **Saved date** above the gallery to choose a period. **Custom range** provides calendar pickers; leave either date blank for an open-ended range. Both dates are included, and **Clear dates** restores the full period. Dates use each file's last-modified time in your local time zone, which may differ from the original capture date.

**Sort** also sets previous/next order in the viewer. Duplicate and similar-image views keep each group together and sort within it. Period and sort preferences are saved. They combine with search, tags, and favorites without additional scanning or analysis. They do not apply to lightweight folder view.

## Automatic discovery

Screenshot Hub checks common local locations, including:

- Windows Screenshots, Pictures, Xbox Game Bar Captures, Documents/My Games, and Saved Games
- AppData Local, LocalLow, and Roaming locations, including Microsoft Store game data
- Steam libraries and `userdata`
- Epic Games manifests
- HoYoPlay installation information
- Game-like entries in Windows installed-program information
- Minecraft, CurseForge, Prism Launcher, MultiMC, and Modrinth profiles

For detected games, it looks for folders such as `ScreenShot`, `Screenshots`, `Capture`, `PhotoMode`, `Photos`, and `Gallery`. A game can contribute more than one capture folder.

## Safety and privacy

- Screenshot Hub itself does not modify images while creating previews, and it does not move, rename, edit, or delete them.
- Favorites, tags, and duplicate results are app metadata only. Duplicate analysis never removes or consolidates files.
- Images and folder information are not uploaded, and the app does not use telemetry.
- Entire drives are not scanned. Scan depth and directory counts are bounded.
- Junctions, symbolic links, caches, and common development folders are skipped.
- Settings are stored locally at `%LOCALAPPDATA%\ScreenshotHub\settings.json`.
- The full image catalog and lightweight folder summaries are stored locally beside the settings as language-specific `catalog-v1-*.json` and `folders-v1-*.json` files. They are never uploaded.
- Favorites/tags are stored in `user-data-v1.json`, and optional duplicate-analysis results in `analysis-v1.json`.
- Portable packages contain `portable.flag` and keep all of this app data in a `Data` folder beside the executable.

Removing a folder from Screenshot Hub only removes it from the app's scan list; the original files remain untouched.

## Supported formats

PNG, JPG/JPEG, BMP, GIF, and TIF/TIFF.

## Known limits

- Windows 10/11 x64 only
- WebP is not supported in v1.1.0.
- Automatic discovery is best effort. Arbitrary personal folders outside known locations must be added with **Add folder**.
- Games that use unusual folder names or inaccessible/deeper locations may not be detected automatically.

## Build and test

Requires the .NET 8 SDK on Windows.

```powershell
dotnet restore .\ScreenshotHub.sln
dotnet build .\ScreenshotHub.sln -c Release --no-restore
dotnet run --project .\tests\ScreenshotHub.SelfTest\ScreenshotHub.SelfTest.csproj -c Release --no-build

# English development build
dotnet build .\src\ScreenshotHub\ScreenshotHub.csproj -c Release -p:ScreenshotHubLanguage=en-US
```

Create standard and portable ZIPs for both languages with the current pinned .NET runtime patch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

A folder can also be scanned without opening the UI:

```powershell
ScreenshotHub.exe --scan-root "C:\path\to\fixture" --diagnostics-json ".\result.json" --exit-after-scan
```
