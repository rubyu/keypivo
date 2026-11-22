[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^1\.\d+\.\d+\.0$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Commit,
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][ValidatePattern('^https://github\.com/[^/]+/[^/]+/actions/runs/\d+$')][string]$RunUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Gh([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed: $($Arguments[0]) $($Arguments[1])" }
    return $output
}

function Read-Release {
    Invoke-Gh @('release', 'view', $tag, '--repo', $Repository, '--json', 'databaseId,targetCommitish,isDraft,assets,url') |
        ConvertFrom-Json
}

function Assert-Assets($Release) {
    foreach ($name in @($package.Name, 'SHA256SUMS.txt')) {
        $matches = @($Release.assets | Where-Object { $_.name -ceq $name -and $_.size -gt 0 })
        if ($matches.Count -ne 1) { throw "Release asset is missing or empty: $name" }
    }
}

$package = Get-Item -LiteralPath $PackagePath
if ($package.PSIsContainer -or $package.Name -cne "keypivo-$Version-x64.msix" -or $package.Length -eq 0) {
    throw 'Expected exactly the validated x64 MSIX for this package version.'
}
$tag = "v$Version"
# Never associate this package with an unrelated pre-existing tag.
$tagJson = & gh api "repos/$Repository/git/ref/tags/$tag" 2>$null
if ($LASTEXITCODE -eq 0) {
    $reference = $tagJson | ConvertFrom-Json
    if ($reference.object.type -ne 'commit' -or $reference.object.sha -cne $Commit) {
        throw "Tag $tag does not point to the tested commit."
    }
}
$releaseJson = & gh release view $tag --repo $Repository --json databaseId,targetCommitish,isDraft,assets,url 2>$null
$release = if ($LASTEXITCODE -eq 0) { $releaseJson | ConvertFrom-Json } else { $null }
if ($null -ne $release) {
    if ($release.targetCommitish -cne $Commit) { throw 'Existing release targets a different commit.' }
    if (-not $release.isDraft) {
        Assert-Assets $release
        Write-Output "Release already published; existing files retained: $($release.url)"
        return
    }
}

$hash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $package.DirectoryName 'SHA256SUMS.txt'
[IO.File]::WriteAllText($checksumPath, "$hash  $($package.Name)`n")
$notesPath = Join-Path $package.DirectoryName 'release-notes.md'
$notes = @"
Download **$($package.Name)** and upload it to Keypivo's update submission in [Microsoft Partner Center](https://partner.microsoft.com/dashboard).

This unsigned MSIX is prepared for Microsoft Store submission. To install Keypivo, use the [Microsoft Store](https://apps.microsoft.com/detail/9P9S5NCN2DQ4).

- Package version: $Version (x64)
- Source: [$Commit](https://github.com/$Repository/commit/$Commit)
- Validation: [CI build, tests, and package checks]($RunUrl)
- SHA-256: $hash

GitHub Release publication does not submit or update the Microsoft Store listing.
"@
[IO.File]::WriteAllText($notesPath, $notes)
if ($null -eq $release) {
    Invoke-Gh @('release', 'create', $tag, '--repo', $Repository, '--target', $Commit,
        '--title', "Keypivo $Version", '--notes-file', $notesPath, '--draft') | Out-Null
    $release = Read-Release
}
# Upload while still a draft so an interrupted upload never exposes an incomplete release.
Invoke-Gh @('release', 'upload', $tag, $package.FullName, $checksumPath, '--repo', $Repository, '--clobber') | Out-Null
$release = Read-Release
Assert-Assets $release
Invoke-Gh @('api', '--method', 'PATCH', "repos/$Repository/releases/$($release.databaseId)",
    '-F', 'draft=false', '-f', 'make_latest=legacy') | Out-Null
$release = Read-Release
if ($release.isDraft) { throw 'The release is still a draft.' }
Write-Output "GitHub Release: $($release.url)"
if ($env:GITHUB_STEP_SUMMARY) { "GitHub Release: $($release.url)" >> $env:GITHUB_STEP_SUMMARY }
