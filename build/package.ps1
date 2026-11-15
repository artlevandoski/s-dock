<#
  Builds S-Dock for release:
    publish\S-Dock\        the standalone app (S-Dock.exe and everything it needs; no .NET install required)
    releases\              the installer and update packages made by Velopack:
      SDock-win-Setup.exe       the installer to share
      SDock-win-Portable.zip    the same app as a zip, for running from any folder
      *.nupkg, releases.*.json  update packages (for a future auto-update feed)

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

Write-Host ""
Write-Host "Done:" -ForegroundColor Green
Get-ChildItem releases | Where-Object { $_.Name -like "*$Version*" -or $_.Name -like '*Setup*' -or $_.Name -like '*Portable*' } |
    ForEach-Object { "  {0,-45} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
