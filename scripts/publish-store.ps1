[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($name in @('STORE_APP_ID', 'STORE_IDENTITY_NAME', 'STORE_PUBLISHER', 'PARTNER_CENTER_TENANT_ID',
    'PARTNER_CENTER_SELLER_ID', 'PARTNER_CENTER_CLIENT_ID', 'PARTNER_CENTER_CLIENT_SECRET')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing required setting: $name" }
}
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
if ($manifest.Package.Identity.Name -cne $env:STORE_IDENTITY_NAME -or
    $manifest.Package.Identity.Publisher -cne $env:STORE_PUBLISHER) { throw 'Package identity does not match the Store configuration.' }
if ($env:STORE_IDENTITY_NAME -eq 'Keypivo.Validation') { throw 'CI validation packages cannot be published.' }

# Credentials are supplied through masked environment secrets, never interpolated into workflow source.
msstore reconfigure --tenantId $env:PARTNER_CENTER_TENANT_ID --sellerId $env:PARTNER_CENTER_SELLER_ID `
    --clientId $env:PARTNER_CENTER_CLIENT_ID --clientSecret $env:PARTNER_CENTER_CLIENT_SECRET
if ($LASTEXITCODE -ne 0) { throw 'Store authentication configuration failed.' }
try {
    $appJson = msstore apps get $env:STORE_APP_ID --output-stream stderr
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the Store application.' }
    $app = ($appJson -join "`n") | ConvertFrom-Json -AsHashtable
    if ($app['PackageIdentityName'] -cne $env:STORE_IDENTITY_NAME -or $app['PublisherName'] -cne $env:STORE_PUBLISHER) {
        throw 'The Store product ID belongs to a different package identity.'
    }
    if (-not $app['LastPublishedApplicationSubmission']) { throw 'Complete the first publication in Partner Center before enabling automatic updates.' }
    if ($app['PendingApplicationSubmission']) {
        throw 'A Store draft or submission already exists. Complete or explicitly discard it in Partner Center; CI will not overwrite it.'
    }

    $submissionJson = msstore submission get $env:STORE_APP_ID --output-stream stderr
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the published version.' }
    $submission = ($submissionJson -join "`n") | ConvertFrom-Json -AsHashtable
    $packages = @($submission['ApplicationPackages'])
    if ($packages.Count -eq 0) { throw 'The published submission has no packages to compare.' }
    foreach ($package in $packages) {
        if (-not $package['Version']) { throw 'A published package has no version.' }
        if ([version]$manifest.Package.Identity.Version -le [version]$package['Version']) {
            throw 'The new package version must be higher than every published package version. Do not rerun an already published release.'
        }
    }

    # Preserve existing Store listing and pricing; do not override priceId or discard a pending draft.
    msstore publish $resolvedPackage --appId $env:STORE_APP_ID
    if ($LASTEXITCODE -ne 0) { throw 'Store submission failed. Inspect Partner Center before retrying.' }
    Write-Output 'Store submission sent. Public availability depends on Microsoft certification and the configured publishing schedule.'
} finally {
    msstore reconfigure --reset
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Could not reset credentials; the hosted runner will be discarded.' }
}
