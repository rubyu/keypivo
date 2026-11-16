$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
$fixtureRoot = Join-Path $projectRoot ("artifacts/store-tests-" + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $fixtureRoot 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'packaging/AppxManifest.xml') -Raw
$manifest.Package.Identity.SetAttribute('Name', '12345.Keypivo')
$manifest.Package.Identity.SetAttribute('Publisher', 'CN=Store Test')
$manifest.Package.Identity.SetAttribute('Version', '1.0.2.0')
$manifest.Save((Join-Path $payload 'AppxManifest.xml'))
$packagePath = Join-Path $fixtureRoot 'keypivo.msix'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $packagePath)

$settings = @{
    STORE_APP_ID = '9TESTKEYPIVO'; STORE_IDENTITY_NAME = '12345.Keypivo'; STORE_PUBLISHER = 'CN=Store Test'
    PARTNER_CENTER_TENANT_ID = 'test-tenant'; PARTNER_CENTER_SELLER_ID = 'test-seller'
    PARTNER_CENTER_CLIENT_ID = 'test-client'; PARTNER_CENTER_CLIENT_SECRET = 'TEST_ONLY_NOT_A_SECRET'
}
$previous = @{}
foreach ($name in $settings.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$global:KeypivoStoreTestContext = @{ Commands = [Collections.Generic.List[string]]::new(); Scenario = '' }


# This fixture never invokes the Store CLI or sends a network request.
function msstore {
    $global:KeypivoStoreTestContext.Commands.Add(($args[0..([Math]::Min(1, $args.Count - 1))] -join ' '))
    $global:LASTEXITCODE = 0
    switch ($args[0]) {
        'reconfigure' { return }
        'apps' {
            if ($global:KeypivoStoreTestContext.Scenario -eq 'query-error') { $global:LASTEXITCODE = 1; return }
            $app = @{
                PackageIdentityName = '12345.Keypivo'; PublisherName = 'CN=Store Test'
                LastPublishedApplicationSubmission = @{ Id = 'published' }; PendingApplicationSubmission = $null
            }
            if ($global:KeypivoStoreTestContext.Scenario -eq 'first-publication') { $app.LastPublishedApplicationSubmission = $null }
            if ($global:KeypivoStoreTestContext.Scenario -eq 'pending') { $app.PendingApplicationSubmission = @{ Id = 'existing-draft' } }
            $app | ConvertTo-Json -Depth 5
            return
        }
        'submission' {
            $version = if ($global:KeypivoStoreTestContext.Scenario -eq 'duplicate-version') { '1.0.2.0' } else { '1.0.1.0' }
            @{ ApplicationPackages = @(@{ Version = $version }) } | ConvertTo-Json -Depth 5
            return
        }
        'publish' { return }
        default { throw 'Unexpected CLI call in test.' }
    }
}

try {
    $cases = @(
        @('success', $null),
        @('first-publication', 'Complete the first publication'),
        @('pending', 'draft or submission already exists'),
        @('duplicate-version', 'version must be higher'),
        @('wrong-identity', 'Package identity does not match'),
        @('missing-secret', 'Missing required setting'),
        @('query-error', 'Could not read the Store application')
    )
    foreach ($case in $cases) {
        $global:KeypivoStoreTestContext.Scenario = $case[0]
        $global:KeypivoStoreTestContext.Commands.Clear()
        foreach ($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name, $settings[$name]) }
        if ($global:KeypivoStoreTestContext.Scenario -eq 'wrong-identity') { $env:STORE_IDENTITY_NAME = '12345.AnotherApp' }
        if ($global:KeypivoStoreTestContext.Scenario -eq 'missing-secret') { [Environment]::SetEnvironmentVariable('PARTNER_CENTER_CLIENT_SECRET', $null) }
        $failure = $null
        try { & (Join-Path $PSScriptRoot 'publish-store.ps1') -PackagePath $packagePath | Out-Null }
        catch { $failure = $_.Exception.Message }
        if ($null -eq $case[1] -and $failure) { throw "Unexpected failure in $($case[0]): $failure" }
        if ($null -ne $case[1] -and ($null -eq $failure -or -not $failure.Contains($case[1]))) {
            throw "Expected rejection in $($case[0]), got: $failure"
        }
        $publishCalls = @($global:KeypivoStoreTestContext.Commands | Where-Object { $_.StartsWith('publish ') }).Count
        if ($publishCalls -ne [int]($global:KeypivoStoreTestContext.Scenario -eq 'success')) { throw 'Unexpected Store mutation.' }
        if ('reconfigure --tenantId' -in $global:KeypivoStoreTestContext.Commands -and 'reconfigure --reset' -notin $global:KeypivoStoreTestContext.Commands) {
            throw 'Credentials were not reset.'
        }
        Write-Output "PASS Store publishing: $($case[0])"
    }
    Write-Output '7/7 Store publishing guard tests passed.'
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Remove-Variable -Name KeypivoStoreTestContext -Scope Global
    $global:LASTEXITCODE = 0
}
