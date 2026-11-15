# Release-notes helpers shared by new-release.ps1 and package.ps1.

$script:Utf8 = New-Object System.Text.UTF8Encoding($false)

function Get-ReleaseTitle([string]$Version) {
    # 0.1.0-beta.2 -> "0.1.0 beta 2"
    return ($Version -replace '-beta\.', ' beta ' -replace '-alpha\.', ' alpha ' -replace '-rc\.', ' release candidate ')
}

function Get-ReleaseZipName([string]$Version) { "S-Dock-$Version-win-x64.zip" }

# Creates docs\releases\v<version>.md from TEMPLATE.md if it doesn't exist yet. Returns its path.
function New-ReleaseNotes([string]$Root, [string]$Version) {
    $dir = Join-Path $Root 'docs\releases'
    $notes = Join-Path $dir "v$Version.md"
    if (-not (Test-Path $notes)) {
        $text = [IO.File]::ReadAllText((Join-Path $dir 'TEMPLATE.md'))
        $text = $text.Replace('{{TITLE}}', (Get-ReleaseTitle $Version)).Replace('{{ZIP}}', (Get-ReleaseZipName $Version))
        [IO.File]::WriteAllText($notes, $text, $script:Utf8)
    }
    return $notes
}

# Writes the checksum and commit into the notes, and saves a copy ready to paste into GitHub.
function Update-ReleaseNotes([string]$Root, [string]$Version, [string]$Hash) {
    $notes = New-ReleaseNotes -Root $Root -Version $Version
    $commit = (git -C $Root rev-parse --short HEAD).Trim()
    $dirty = git -C $Root status --porcelain -- src Directory.Build.props Directory.Packages.props
    $built = "Built from commit ``$commit``" + $(if ($dirty) { ' with uncommitted changes (commit, then build again for the real release)' } else { '' }) + '.'
    $fence = '```'
    $info = @(
        '<!-- build-info: filled in by build\package.ps1 -->',
        '## Checksum (SHA-256)',
        '',
        $fence,
        "$Hash  $(Get-ReleaseZipName $Version)",
        $fence,
        '',
        $built,
        '<!-- /build-info -->'
    ) -join "`n"

    $text = [IO.File]::ReadAllText($notes)
    $pattern = '(?s)<!-- build-info.*?<!-- /build-info -->'
    if ($text -match $pattern) { $text = [regex]::Replace($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator] { param($m) $info }) }
    else { $text = $text.TrimEnd() + "`n`n" + $info + "`n" }
    [IO.File]::WriteAllText($notes, $text, $script:Utf8)

    # For GitHub: the same text without the title line (GitHub shows the release title separately)
    # and without the hidden markers.
    $paste = ($text -split "`n" | Select-Object -Skip 1 | Where-Object { $_ -notmatch '^<!-- /?build-info' }) -join "`n"
    $out = Join-Path $Root 'releases\RELEASE-NOTES.md'
    [IO.File]::WriteAllText($out, $paste.Trim() + "`n", $script:Utf8)
    return @{ Notes = $notes; Paste = $out; Title = "S-Dock $(Get-ReleaseTitle $Version)"; Dirty = [bool]$dirty }
}
