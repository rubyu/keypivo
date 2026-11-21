[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('keypivo.exe', 'keypivo.dll', 'keypivo.core.dll', 'keypivo.runtimeconfig.json',
        'coreclr.dll', 'System.Private.CoreLib.dll', 'LICENSE', 'AppxManifest.xml', 'AppxBlockMap.xml',
        'Assets/StoreLogo.png', 'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png')) {
        if ($required -cnotin $names) { throw "Package is missing: $required" }
    }
    if (@($names | Where-Object { $_ -match '(^|/)(YSwitch|Y-Switch)' }).Count -gt 0) { throw 'Obsolete binaries found in MSIX.' }
    $manifestEntry = $archive.Entries | Where-Object FullName -eq 'AppxManifest.xml' | Select-Object -First 1
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    foreach ($pair in @(@('Name', $IdentityName), @('Publisher', $Publisher), @('Version', $Version), @('ProcessorArchitecture', $Architecture))) {
        if ($manifest.Package.Identity.GetAttribute($pair[0]) -cne $pair[1]) { throw "MSIX identity mismatch: $($pair[0])" }
    }
    if ($manifest.Package.Applications.Application.Executable -cne 'keypivo.exe') { throw 'Incorrect application entry point.' }
    if ($manifest.Package.Properties.DisplayName -cne 'Keypivo') { throw 'Incorrect product display name.' }
    $capabilities = @($manifest.Package.Capabilities.Capability | ForEach-Object { $_.Name })
    if ('allowElevation' -in $capabilities) { throw 'MSIX must not request allowElevation.' }
    foreach ($required in @('runFullTrust')) {
        if ($required -notin $capabilities) { throw "Missing desktop capability: $required" }
    }
    Add-Type -AssemblyName System.Drawing
    foreach ($asset in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
        $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq "Assets/$($asset[0])" } | Select-Object -First 1
        $stream = $entry.Open()
        try {
            $image = [Drawing.Image]::FromStream($stream)
            try { if ($image.Width -ne $asset[1] -or $image.Height -ne $asset[1]) { throw "Incorrect logo dimensions: $($asset[0])" } }
            finally { $image.Dispose() }
        } finally { $stream.Dispose() }
    }
    Write-Output 'MSIX verification passed: identity, entry point, runtime, license, capabilities, and logos.'
} finally { $archive.Dispose() }
