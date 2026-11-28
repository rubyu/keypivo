[CmdletBinding()]
param([ValidateSet(1, 2)][int]$Repeat = 1)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$comparisonRoot = Join-Path $PSScriptRoot '../artifacts/memory-optimization'
$resultRoot = Join-Path $comparisonRoot 'results'
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
$runtimePath = Join-Path $comparisonRoot 'fixed/keypivo.uitests.runtimeconfig.json'
$originalConfig = Get-Content -LiteralPath $runtimePath -Raw
$cases = @('baseline', 'allocations', 'conserve', 'limit64')
if ($Repeat -eq 2) { [array]::Reverse($cases) }
$summaries = @()
try {
    foreach ($case in $cases) {
        $config = $originalConfig | ConvertFrom-Json
        $properties = $config.runtimeOptions.configProperties
        $properties.PSObject.Properties.Remove('System.GC.ConserveMemory')
        $properties.PSObject.Properties.Remove('System.GC.HeapHardLimit')
        if ($case -eq 'conserve') { $properties | Add-Member -NotePropertyName 'System.GC.ConserveMemory' -NotePropertyValue 5 }
        if ($case -eq 'limit64') { $properties | Add-Member -NotePropertyName 'System.GC.HeapHardLimit' -NotePropertyValue 67108864 }
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $runtimePath -Encoding utf8
        $directory = if ($case -eq 'baseline') { 'baseline' } else { 'fixed' }
        $executable = Join-Path $comparisonRoot "$directory/keypivo.uitests.exe"
        $output = Join-Path $resultRoot "$case-$Repeat.json"
        Write-Output "OPTIMIZATION_CASE $case repeat=$Repeat"
        & $executable --memory-growth process $output 60 2400
        if ($LASTEXITCODE -ne 0) { throw "Memory measurement failed: $case" }
        $data = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $before = $data.Samples | Where-Object Phase -eq 'before-accelerated'
        $after = $data.Samples | Where-Object Phase -eq 'accelerated-2400'
        $settled = @($data.Samples | Where-Object Phase -like 'settled-*')
        $preGc = $data.Samples | Where-Object Phase -eq 'before-gc'
        $latencies = @($data.ReadLatencyMilliseconds | Sort-Object)
        $summary = [pscustomobject]@{
            Case = $case; Repeat = $Repeat; Runtime = $data.Runtime; Configuration = $data.Configuration
            AllocationPerRead = ($after.TotalAllocated - $before.TotalAllocated) / ($after.Reads - $before.Reads)
            SettledPrivateWorkingSet = ($settled.PrivateWorkingSet | Measure-Object -Average).Average
            PeakPrivateWorkingSet = ($data.Samples | Where-Object { $_.Phase -notlike 'after-gc*' } | Measure-Object PrivateWorkingSet -Maximum).Maximum
            SettledPrivateCommit = ($settled.PrivateCommit | Measure-Object -Average).Average
            ManagedHeapAfterGc = ($data.Samples | Where-Object Phase -eq 'after-gc').ManagedHeap
            CpuMilliseconds = $preGc.CpuMilliseconds - $before.CpuMilliseconds
            PauseMilliseconds = $preGc.PauseMilliseconds - $before.PauseMilliseconds
            Gen0 = $preGc.Gen0 - $before.Gen0; Gen1 = $preGc.Gen1 - $before.Gen1; Gen2 = $preGc.Gen2 - $before.Gen2
            MeanReadMilliseconds = ($latencies | Measure-Object -Average).Average
            P95ReadMilliseconds = $latencies[[int]($latencies.Count * 0.95)]
        }
        $summaries += $summary
        Write-Output ('OPTIMIZATION_RESULT ' + ($summary | ConvertTo-Json -Compress -Depth 10))
        if ($case -eq 'limit64') {
            # Exercise settings, setup, switching simulation, rendering, and large
            # device descriptions under the candidate memory policy as well.
            & $executable
            if ($LASTEXITCODE -ne 0) { throw 'Native UI checks failed under the candidate GC policy.' }
        }
    }
}
finally { Set-Content -LiteralPath $runtimePath -Value $originalConfig -Encoding utf8 }
$summaries | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $resultRoot "summary-$Repeat.json")
