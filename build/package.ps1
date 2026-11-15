<#
  Builds S-Dock for release as one self-contained folder (nothing is installed or written outside it):

    releases\S-Dock-<version>-win-x64.zip   the download: unzip it and you get an S-Dock folder with
                                            S-Dock.exe, the app (current\), Update.exe and emulators\.
                                            data\ appears beside them on first start.
    releases\*.nupkg, releases.*.json       update packages (for a future in-place update feed)
    publish\S-Dock\                         the plain published app (for testing)

  Velopack's Setup.exe is not part of the release: it would install into %LOCALAPPDATA%, and S-Dock is
  meant to live entirely in its own folder.

  Usage (from the repository folder):
    powershell -ExecutionPolicy Bypass -File build\package.ps1
    powershell -ExecutionPolicy Bypass -File build\package.ps1 -Version 0.1.0-beta.2

  The version defaults to the one in Directory.Build.props.
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "Packaging S-Dock $Version" -ForegroundColor Cyan

dotnet tool restore | Out-Null

# releases\ holds only build output: start it fresh (Velopack won't repackage a version already there).
New-Item -ItemType Directory -Force releases | Out-Null
Get-ChildItem releases -Force | Remove-Item -Recurse -Force

$publish = Join-Path $root 'publish\S-Dock'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish src\SDock.App -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed" }

dotnet vpk pack `
    --packId SDock `
    --packVersion $Version `
    --packTitle "S-Dock" `
    --packAuthors "S-Dock" `
    --packDir $publish `
    --mainExe S-Dock.exe `
    --runtime win-x64 `
    --icon src\SDock.App\Assets\sdock.ico `
    --outputDir releases
if ($LASTEXITCODE) { throw "vpk pack failed" }

# The release: a ready S-Dock folder (Velopack's portable layout plus an emulators folder), zipped.
$stage = Join-Path $root 'publish\release'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$folder = Join-Path $stage 'S-Dock'
Expand-Archive (Join-Path 'releases' 'SDock-win-Portable.zip') -DestinationPath $folder
New-Item -ItemType Directory (Join-Path $folder 'emulators') | Out-Null
Copy-Item emulators\README.md (Join-Path $folder 'emulators\README.md')
$zip = Join-Path 'releases' "S-Dock-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
# Not part of the release: the installer (it installs into AppData) and Velopack's own zip (replaced above).
Remove-Item (Join-Path 'releases' 'SDock-win-Setup.exe'), (Join-Path 'releases' 'SDock-win-Portable.zip') -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done:" -ForegroundColor Green
Get-ChildItem releases | ForEach-Object { "  {0,-45} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Write-Host ""
Write-Host "SHA-256 (for the release notes):" -ForegroundColor Green
Write-Host "  $hash  $(Split-Path $zip -Leaf)"
