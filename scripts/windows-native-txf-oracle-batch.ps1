<#
.SYNOPSIS
Owner-local, single-use R5 native CLFS observations for all three TxF controls.
.DESCRIPTION
Creates three fresh generated virtual files from the sealed Native-R2 seed.
No real disks, automatic elevation, reset/repair, service or schedule. R3 evidence
and R4 are retained. Each control uses a fresh PowerShell process and stops on failure.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner must launch this batch locally as administrator; SSH remains standard.'
}
function Assert-SectorWorksOracleCoverage([object[]] $Reports) {
    if ($Reports.Count -ne 3) { throw 'Expected exactly three completed native control reports.' }
    $observed = 0
    foreach ($name in @('Ordinary', 'Committed', 'RolledBack')) {
        $matches = @($Reports | Where-Object Control -eq $name)
        if ($matches.Count -ne 1 -or !$matches[0].Passed) { throw 'Missing, duplicate or failed native control report.' }
        $stages = @('before', 'after')
        if ($name -ne 'Ordinary') { $stages += 'active' }
        foreach ($stage in $stages) {
            $property = $matches[0].PSObject.Properties[$stage + 'OracleStreamsObserved']
            if (!$property -or $property.Value -ne 2) {
                throw "Native $name $stage CLFS observations are incomplete. Retain reports; do not rerun or alter permissions."
            }
            $observed += 2
        }
    }
    return $observed
}
$controlScript = Join-Path $PSScriptRoot 'windows-native-txf-controls.ps1'
foreach ($control in @('Ordinary', 'Committed', 'RolledBack')) {
    $root = Join-Path $env:PUBLIC ('SectorWorks-Phase2D-TxF-R5-' + $control)
    if (Test-Path -LiteralPath $root) { throw 'An R5 control root already exists; do not reuse or delete it.' }
}
foreach ($control in @('Ordinary', 'Committed', 'RolledBack')) {
    & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
        -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $controlScript -Control $control -ObserveClfs
    if ($LASTEXITCODE -ne 0) { throw "R5 $control failed; retain evidence and stop before the next control." }
}
$reports = @()
foreach ($control in @('Ordinary', 'Committed', 'RolledBack')) {
    $path = Join-Path $env:PUBLIC ('SectorWorks-Phase2D-TxF-R5-' + $control + '\control.json')
    $reports += Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}
$observed = Assert-SectorWorksOracleCoverage $reports
Write-Output 'All three R5 controls completed and detached; 16 native CLFS stream observations succeeded. Recovery/update/writer qualification remains separate.'
