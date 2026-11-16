[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Publisher,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PublisherDisplayName,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.0$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$OutputDirectory = 'artifacts/store'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageVersion = [version]$Version
if ($packageVersion.Major -lt 1 -or $packageVersion.Major -gt 65535 -or
    $packageVersion.Minor -gt 65535 -or $packageVersion.Build -gt 65535) {
    throw 'Store versions must use 1..65535 for the major component, 0..65535 for minor/build, and zero for revision.'
}
[void][Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
if ($PublisherDisplayName.Length -gt 256) { throw 'Publisher display name is too long.' }

$projectRoot = Split-Path $PSScriptRoot -Parent
$outputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$packagePath = Join-Path $outputPath "keypivo-$Version-$Architecture.msix"
if (Test-Path -LiteralPath $packagePath) { throw "Package already exists: $packagePath" }

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$makeAppx = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $makeAppx) { throw 'Install the Windows 10/11 SDK with MakeAppx before packaging.' }

# Each invocation has a fresh payload. Never mix old binaries into a new package or delete an existing output.
$payloadPath = Join-Path $outputPath ("payload-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payloadPath -Force | Out-Null
Push-Location $projectRoot
try {
    dotnet publish src/Keypivo/Keypivo.csproj -c Release -r "win-$Architecture" --self-contained true -m:1 `
        -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false `
        "-p:Version=$Version" -o $payloadPath
    if ($LASTEXITCODE -ne 0) { throw 'Store payload build failed.' }

    [xml]$manifest = Get-Content -LiteralPath packaging/AppxManifest.xml -Raw
    $manifest.Package.Identity.SetAttribute('Name', $IdentityName)
    $manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
    $manifest.Package.Identity.SetAttribute('Version', $Version)
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Architecture)
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $manifest.Save((Join-Path $payloadPath 'AppxManifest.xml'))

    Add-Type -AssemblyName System.Drawing
    $assetsPath = Join-Path $payloadPath 'Assets'
    New-Item -ItemType Directory -Path $assetsPath | Out-Null
    foreach ($asset in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
        $size = [int]$asset[1]
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(61, 108, 84))
        $pen = [Drawing.Pen]::new([Drawing.Color]::White, [single]($size * 0.09))
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.FillEllipse($brush, [single]($size * 0.04), [single]($size * 0.04), [single]($size * 0.92), [single]($size * 0.92))
            $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawLine($pen, [single]($size * 0.34), [single]($size * 0.27), [single]($size * 0.34), [single]($size * 0.73))
            $graphics.DrawLine($pen, [single]($size * 0.68), [single]($size * 0.27), [single]($size * 0.34), [single]($size * 0.51))
            $graphics.DrawLine($pen, [single]($size * 0.34), [single]($size * 0.51), [single]($size * 0.68), [single]($size * 0.73))
            $bitmap.Save((Join-Path $assetsPath $asset[0]), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }

    # Keep semantic validation enabled. Store ingestion signs the submitted package.
    & $makeAppx pack /d $payloadPath /p $packagePath /no
    if ($LASTEXITCODE -ne 0) { throw 'MSIX creation or manifest validation failed.' }
    & (Join-Path $PSScriptRoot 'test-package.ps1') -PackagePath $packagePath -IdentityName $IdentityName `
        -Publisher $Publisher -Version $Version -Architecture $Architecture
    Write-Output "Store package: $packagePath"
} finally { Pop-Location }
