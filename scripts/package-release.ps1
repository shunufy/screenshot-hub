[CmdletBinding()]
param(
    [string]$Version = "1.1.0",
    [string]$RuntimeVersion = "8.0.29",
    [string]$DestinationRoot,
    [switch]$SkipPortable
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repositoryRoot "outputs"
}

$destinationFullPath = [IO.Path]::GetFullPath($DestinationRoot)
[IO.Directory]::CreateDirectory($destinationFullPath) | Out-Null

$nugetPackages = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $env:USERPROFILE ".nuget\packages"
} else {
    [IO.Path]::GetFullPath($env:NUGET_PACKAGES)
}

$runtimePackage = Join-Path $nugetPackages "microsoft.netcore.app.runtime.win-x64\$RuntimeVersion"
$desktopPackage = Join-Path $nugetPackages "microsoft.windowsdesktop.app.runtime.win-x64\$RuntimeVersion"
$project = Join-Path $repositoryRoot "src\ScreenshotHub\ScreenshotHub.csproj"

$languages = @(
    @{ Code = "ja-JP"; Readme = "README_ja-JP.txt"; PortableReadme = "PORTABLE_ja-JP.txt" },
    @{ Code = "en-US"; Readme = "README_en-US.txt"; PortableReadme = "PORTABLE_en-US.txt" }
)

$results = foreach ($language in $languages) {
    $folderName = "ScreenshotHub-$Version-$($language.Code)-win-x64"
    $publishDirectory = Join-Path $destinationFullPath $folderName
    $zipPath = "$publishDirectory.zip"
    $portableFolderName = "$folderName-portable"
    $portableDirectory = Join-Path $destinationFullPath $portableFolderName
    $portableZipPath = "$portableDirectory.zip"
    if ((Test-Path -LiteralPath $publishDirectory) -or
        (Test-Path -LiteralPath $zipPath) -or
        (!$SkipPortable -and ((Test-Path -LiteralPath $portableDirectory) -or
                             (Test-Path -LiteralPath $portableZipPath)))) {
        throw "Release output already exists: $folderName"
    }

    & dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:ScreenshotHubLanguage=$($language.Code) `
        -p:RuntimeFrameworkVersion=$RuntimeVersion `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($language.Code)."
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "packaging\$($language.Readme)") `
        -Destination (Join-Path $publishDirectory "README.txt")

    $runtimeLicense = Join-Path $runtimePackage "LICENSE.TXT"
    $runtimeNotices = Join-Path $runtimePackage "THIRD-PARTY-NOTICES.TXT"
    $desktopLicense = @(
        (Join-Path $desktopPackage "LICENSE"),
        (Join-Path $desktopPackage "LICENSE.TXT")
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    foreach ($requiredFile in @($runtimeLicense, $runtimeNotices, $desktopLicense)) {
        if ([string]::IsNullOrWhiteSpace($requiredFile) -or !(Test-Path -LiteralPath $requiredFile)) {
            throw "Required .NET redistribution notice was not found: $requiredFile"
        }
    }

    Copy-Item -LiteralPath $runtimeLicense -Destination (Join-Path $publishDirectory "DOTNET-LICENSE.txt")
    Copy-Item -LiteralPath $runtimeNotices -Destination (Join-Path $publishDirectory "DOTNET-THIRD-PARTY-NOTICES.txt")
    Copy-Item -LiteralPath $desktopLicense -Destination (Join-Path $publishDirectory "DOTNET-WINDOWS-DESKTOP-LICENSE.txt")

    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $zip = Get-Item -LiteralPath $zipPath
    $hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
    [pscustomobject]@{
        Language = $language.Code
        Mode = "Standard"
        Zip = $zip.FullName
        Bytes = $zip.Length
        SHA256 = $hash.Hash
    }

    if (!$SkipPortable) {
        [IO.Directory]::CreateDirectory($portableDirectory) | Out-Null
        Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $portableDirectory -Recurse
        New-Item -Path (Join-Path $portableDirectory "portable.flag") -ItemType File | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "packaging\$($language.PortableReadme)") `
            -Destination (Join-Path $portableDirectory "PORTABLE.txt")
        Compress-Archive -Path (Join-Path $portableDirectory "*") `
            -DestinationPath $portableZipPath -CompressionLevel Optimal
        $portableZip = Get-Item -LiteralPath $portableZipPath
        $portableHash = Get-FileHash -LiteralPath $portableZipPath -Algorithm SHA256
        [pscustomobject]@{
            Language = $language.Code
            Mode = "Portable"
            Zip = $portableZip.FullName
            Bytes = $portableZip.Length
            SHA256 = $portableHash.Hash
        }
    }
}

$results
