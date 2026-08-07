# Screenshot Hub

English | [日本語](README_JA.md)

Screenshot Hub is a private local Windows app that finds game screenshots buried across different folders and brings them together in one gallery.

## Download

Open the [v0.2.1 release](../../releases/tag/v0.2.1), then choose a language:

- `ScreenshotHub-0.2.1-en-US-win-x64.zip` — English UI
- `ScreenshotHub-0.2.1-ja-JP-win-x64.zip` — Japanese UI

Extract the ZIP and run `ScreenshotHub.exe`. Both packages are self-contained builds for Windows 10/11 x64, so no separate .NET installation is required.

The executable is currently unsigned, so Windows SmartScreen may display a warning. Confirm that the ZIP came from this repository's Releases page and compare its SHA-256 value with the release notes before running it.

## Features

- One automatic full scan on first launch; later launches load the saved local catalogs
- Manual rescans after the initial scan; scheduled rescans are available as an opt-in setting and are off by default
- Lightweight folder-only view that loads only saved folder summaries, never loads thumbnails, and opens save locations directly
- Collections grouped by game and capture folder
- Newest-first thumbnail gallery and search by game, filename, or path
- Open images with the default app, reveal them in File Explorer, or copy their paths
- Add folders, remove folders you added, and cancel an active scan
- Keyboard controls and paging for large libraries

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
- Images and folder information are not uploaded, and the app does not use telemetry.
- Entire drives are not scanned. Scan depth and directory counts are bounded.
- Junctions, symbolic links, caches, and common development folders are skipped.
- Settings are stored locally at `%LOCALAPPDATA%\ScreenshotHub\settings.json`.
- The full image catalog and lightweight folder summaries are stored locally beside the settings as language-specific `catalog-v1-*.json` and `folders-v1-*.json` files. They are never uploaded.

Removing a folder from Screenshot Hub only removes it from the app's scan list; the original files remain untouched.

## Supported formats

PNG, JPG/JPEG, BMP, GIF, and TIF/TIFF.

## Known limits

- Windows 10/11 x64 only
- WebP is not supported in v0.2.x.
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

Create both release ZIPs with the current pinned .NET runtime patch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

A folder can also be scanned without opening the UI:

```powershell
ScreenshotHub.exe --scan-root "C:\path\to\fixture" --diagnostics-json ".\result.json" --exit-after-scan
```
