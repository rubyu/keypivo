param(
    [switch]$SelfContained,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet build Keypivo.slnx -c Release -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    if (-not $SkipTests) {
        dotnet run --project tests/Keypivo.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed; publishing stopped.' }
    } else {
        Write-Warning 'Tests were skipped. The package has not passed runtime verification.'
    }

    $publishPath = Join-Path $projectRoot 'artifacts\publish'
    $publishArguments = @('publish', 'src/Keypivo/Keypivo.csproj', '-c', 'Release', '-m:1',
        '-o', $publishPath, '-p:DebugType=None', '-p:DebugSymbols=false')
    if ($SelfContained) {
        $publishArguments += @('-r', $Runtime, '--self-contained', 'true')
    } else {
        $publishArguments += @('--self-contained', 'false')
    }
    # Avoid stale runtime files when switching between package types; never delete an existing output.
    if (Test-Path -LiteralPath $publishPath) {
        throw 'artifacts/publish already exists. Move it aside before creating a new package.'
    }
    dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item -LiteralPath 'README.md' -Destination $publishPath
    Copy-Item -LiteralPath 'docs' -Destination $publishPath -Recurse
    $zipPath = Join-Path $projectRoot 'artifacts\keypivo-win.zip'
    Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -Force
    Write-Output "Package: $zipPath"
} finally {
    Pop-Location
}
