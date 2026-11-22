$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) ("artifacts/release-tests/" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$previousSummary = $env:GITHUB_STEP_SUMMARY
$env:GITHUB_STEP_SUMMARY = Join-Path $testDirectory 'summary.md'
$packagePath = Join-Path $testDirectory 'keypivo-1.0.123.0-x64.msix'
[IO.File]::WriteAllText($packagePath, 'Simulated package; never uploaded.')
$commit = '1' * 40
$parameters = @{
    Repository = 'rubyu/keypivo'; Version = '1.0.123.0'; Commit = $commit; PackagePath = $packagePath
    RunUrl = 'https://github.com/rubyu/keypivo/actions/runs/123'
}

# Shadow the executable only inside this test script. No request reaches GitHub.
function gh {
    $arguments = @($args)
    $global:KeypivoReleaseTestState.Calls.Add(($arguments -join ' '))
    $global:LASTEXITCODE = 0
    switch ("$($arguments[0]) $($arguments[1])") {
        'api repos/rubyu/keypivo/git/ref/tags/v1.0.123.0' {
            if ($global:KeypivoReleaseTestState.Tag) { @{ object = @{ type = 'commit'; sha = $global:KeypivoReleaseTestState.Tag } } | ConvertTo-Json -Depth 5 }
            else { $global:LASTEXITCODE = 1 }
        }
        'release view' {
            if ($null -eq $global:KeypivoReleaseTestState.Release) { $global:LASTEXITCODE = 1 }
            else { $global:KeypivoReleaseTestState.Release | ConvertTo-Json -Depth 5 }
        }
        'release create' {
            if ('--draft' -notin $arguments) { throw 'Release must be created as a draft.' }
            $global:KeypivoReleaseTestState.Release = @{ databaseId = 123; targetCommitish = $arguments[[array]::IndexOf($arguments, '--target') + 1];
                isDraft = $true; assets = @(); url = 'https://github.com/rubyu/keypivo/releases/tag/v1.0.123.0' }
        }
        'release upload' {
            if (-not $global:KeypivoReleaseTestState.Release.isDraft) { throw 'Must never replace published assets.' }
            if ($global:KeypivoReleaseTestState.FailUpload) { $global:LASTEXITCODE = 1; return }
            $global:KeypivoReleaseTestState.Release.assets = @($arguments[3], $arguments[4]) | ForEach-Object {
                $file = Get-Item -LiteralPath $_
                @{ name = $file.Name; size = $file.Length }
            }
        }
        'api --method' {
            if ($arguments[2] -ne 'PATCH' -or 'draft=false' -notin $arguments) { throw 'Unexpected release update.' }
            if ($global:KeypivoReleaseTestState.Release.assets.Count -ne 2) { throw 'Publication must wait for both assets.' }
            $global:KeypivoReleaseTestState.Release.isDraft = $false
        }
        default { throw "Unexpected GitHub CLI call: $($arguments -join ' ')" }
    }
}

function Reset-State {
    $global:KeypivoReleaseTestState = @{ Release = $null; Tag = $null; FailUpload = $false; Calls = [Collections.Generic.List[string]]::new() }
}
function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Publish { & (Join-Path $PSScriptRoot 'publish-release.ps1') @parameters }
function Expect-Failure([string]$message) {
    try { Publish | Out-Null } catch { if ($_.Exception.Message -like "*$message*") { return }; throw }
    throw "Expected failure: $message"
}

Reset-State
Publish
Assert (-not $global:KeypivoReleaseTestState.Release.isDraft) 'A successful upload must publish the release.'
$checksum = Get-Content -LiteralPath (Join-Path $testDirectory 'SHA256SUMS.txt') -Raw
Assert ($checksum.Trim() -ceq ((Get-FileHash $packagePath).Hash.ToLowerInvariant() + '  keypivo-1.0.123.0-x64.msix')) 'Checksum must match the package.'
$notes = Get-Content -LiteralPath (Join-Path $testDirectory 'release-notes.md') -Raw
Assert ($notes.Contains($parameters.RunUrl) -and $notes.Contains($commit) -and $notes.Contains('Microsoft Store submission')) 'Notes must identify the tested source and Store submission purpose.'
Write-Output 'PASS Release: draft, package/checksum upload, then publication with source and CI links.'

$global:KeypivoReleaseTestState.Tag = $commit
$global:KeypivoReleaseTestState.Calls.Clear()
Publish
Assert ($global:KeypivoReleaseTestState.Calls.Count -eq 2) 'Rerunning a published version must only read existing state.'
Write-Output 'PASS Release: reruns preserve published files.'

$global:KeypivoReleaseTestState.Release.isDraft = $true
$global:KeypivoReleaseTestState.Release.assets = @()
$global:KeypivoReleaseTestState.Calls.Clear()
Publish
Assert (-not $global:KeypivoReleaseTestState.Release.isDraft -and -not ($global:KeypivoReleaseTestState.Calls | Where-Object { $_ -like 'release create*' })) 'Retry must resume the draft.'
Write-Output 'PASS Release: interrupted drafts can resume.'

$global:KeypivoReleaseTestState.Tag = '2' * 40
Expect-Failure 'does not point to the tested commit'
$global:KeypivoReleaseTestState.Tag = $commit
$global:KeypivoReleaseTestState.Release.targetCommitish = '2' * 40
Expect-Failure 'targets a different commit'
$global:KeypivoReleaseTestState.Release.targetCommitish = $commit
$global:KeypivoReleaseTestState.Release.assets = @()
Expect-Failure 'asset is missing or empty'
Write-Output 'PASS Release: mismatched tags/commits and incomplete published releases are rejected.'

Reset-State
$global:KeypivoReleaseTestState.FailUpload = $true
Expect-Failure 'GitHub CLI failed: release upload'
Assert ($global:KeypivoReleaseTestState.Release.isDraft -and -not ($global:KeypivoReleaseTestState.Calls | Where-Object { $_ -like 'api --method PATCH*' })) 'Upload failure must leave the release unpublished.'
Write-Output 'PASS Release: upload failure never publishes a partial release.'

$parameters.Version = '1.0.124.0'
$global:KeypivoReleaseTestState.Calls.Clear()
Expect-Failure 'Expected exactly the validated x64 MSIX'
Assert ($global:KeypivoReleaseTestState.Calls.Count -eq 0) 'Wrong package versions must fail before accessing GitHub.'
Write-Output 'PASS Release: package filename/version validation runs before any GitHub operation.'
$env:GITHUB_STEP_SUMMARY = $previousSummary
Remove-Variable -Name KeypivoReleaseTestState -Scope Global
$global:LASTEXITCODE = 0
