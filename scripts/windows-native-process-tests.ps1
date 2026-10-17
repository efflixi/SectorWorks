[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'windows-native-process.ps1')
$cmd = Join-Path $env:SystemRoot 'System32\cmd.exe'
foreach ($code in @(0, 7)) {
    $result = Invoke-SectorWorksNativeProcess $cmd "/d /c exit $code" 5000
    if ($result.TimedOut -or $result.ExitCode -ne $code) { throw "Expected exact native exit $code." }
    Write-Output "Exit $code PASS"
}
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$result = Invoke-SectorWorksNativeProcess $powershell '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"' 200
if (!$result.TimedOut -or $null -eq $result.ExitCode) { throw 'Expected bounded timeout with exact termination status.' }
Write-Output "Timeout PASS (termination exit $($result.ExitCode))"
