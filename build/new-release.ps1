<#
  Starts a new release: sets the version and creates its release notes from the template.

  Usage (from the repository folder):
    powershell -ExecutionPolicy Bypass -File build\new-release.ps1 -Version 0.1.0-beta.2

  Then:
    1. Fill in "What's new" (and anything else) in docs\releases\v<version>.md.
    2. Commit in Sourcetree.
    3. Run build\package.ps1. It builds the zip, adds the checksum and commit to the notes, and saves
       releases\RELEASE-NOTES.md, ready to paste into the GitHub release.
#>
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$utf8 = New-Object System.Text.UTF8Encoding($false)

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Use a version like 0.1.0 or 0.1.0-beta.2, not '$Version'." }

# 1. The version the app and the release use.
$props = Join-Path $root 'Directory.Build.props'
$text = [IO.File]::ReadAllText($props)
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($props, $text, $utf8)
Write-Host "Version set to $Version in Directory.Build.props" -ForegroundColor Green

# 2. The release notes, from the template.
. (Join-Path $PSScriptRoot 'release-notes.ps1')
$notes = New-ReleaseNotes -Root $root -Version $Version
Write-Host "Release notes: $notes" -ForegroundColor Green
Write-Host "Next: fill in What's new, commit, then run build\package.ps1."
