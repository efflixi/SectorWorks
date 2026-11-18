[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SourceRoot,
    [Parameter(Mandatory = $true)][string] $Dotnet,
    [Parameter(Mandatory = $true)][string] $Inputs,
    [Parameter(Mandatory = $true)][string] $Outputs,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $CatalogSha256,
    [ValidateSet('saveas', 'commit')][string] $Workflow = 'saveas',
    [string] $Prerequisite,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $PrerequisiteSha256
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Expanded product workflows require the unchanged standard account.'
}
. (Join-Path $PSScriptRoot 'windows-native-evidence.ps1')
. (Join-Path $PSScriptRoot 'windows-native-process.ps1')

function Assert-OrdinaryPath([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Linked handoff/source/tool paths are forbidden.'
        }
        if ($item -is [IO.FileInfo]) { $item = $item.Directory } else { $item = $item.Parent }
    }
}
foreach ($path in @($SourceRoot, $Dotnet, $Inputs)) { Assert-OrdinaryPath $path }
foreach ($path in @($SourceRoot, $Dotnet, $Inputs, $Outputs)) {
    if ($path.Contains('"') -or $path -match '[\r\n]') { throw 'Unsafe command argument path.' }
}
if (Test-Path -LiteralPath $Outputs) { throw 'Output/evidence root must not already exist.' }
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$Inputs = [IO.Path]::GetFullPath($Inputs)
$Outputs = [IO.Path]::GetFullPath($Outputs)
Assert-OrdinaryPath ([IO.Path]::GetDirectoryName($Outputs))
$catalogPath = Join-Path $Inputs 'catalog.json'
Assert-OrdinaryPath $catalogPath
if ((Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash -ne $CatalogSha256) {
    throw 'Transferred input catalog SHA256 mismatch.'
}
$catalog = Read-SectorWorksNativeEvidence $catalogPath
if ($catalog.schema -ne 2 -or $catalog.status -ne 'linux-expanded-inputs-verified' -or $catalog.inputs.Count -ne 441) {
    throw 'Incomplete/unqualified Linux input catalog.'
}
foreach ($file in $catalog.sourceFiles) {
    if ($file.file -match '(^/|\\|:|(^|/)\.\.(/|$))') { throw 'Invalid source manifest path.' }
    $path = Join-Path $SourceRoot $file.file
    Assert-OrdinaryPath $path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Source snapshot differs from independently prepared catalog: $($file.file)"
    }
}
if ($Workflow -eq 'commit') {
    if (!$Prerequisite -or !$PrerequisiteSha256) { throw 'Commit requires the separately sealed successful Save As independent-verification report.' }
    Assert-OrdinaryPath $Prerequisite
    if ((Get-Item -LiteralPath $Prerequisite).Length -gt 32MB -or
        (Get-FileHash -LiteralPath $Prerequisite -Algorithm SHA256).Hash -ne $PrerequisiteSha256) { throw 'Prerequisite report differs from its separate seal.' }
    $proof = Read-SectorWorksNativeEvidence $Prerequisite
    if ($proof.schema -ne 2 -or $proof.status -ne 'passed' -or $proof.workflow -ne 'saveas' -or
        $proof.catalogSha256 -ne $CatalogSha256 -or $proof.outputs.Count -ne 441 -or
        @($proof.outputs | Where-Object status -NE 'passed').Count -ne 0) { throw 'Expanded staged/Save As prerequisites are incomplete.' }
}
foreach ($name in @('SECTORWORKS_WRITER_EXPORT', 'SECTORWORKS_WRITER_INPUTS', 'SECTORWORKS_WRITER_OUTPUTS',
        'SECTORWORKS_WRITER_CATALOG_SHA256', 'SECTORWORKS_EXPANDED_EXPORT', 'SECTORWORKS_EXPANDED_INPUTS',
        'SECTORWORKS_EXPANDED_OUTPUTS', 'SECTORWORKS_EXPANDED_CATALOG_SHA256')) {
    if ($null -ne [Environment]::GetEnvironmentVariable($name)) { throw 'Clear existing handoff environment before orchestration.' }
}
[IO.Directory]::CreateDirectory($Outputs) | Out-Null
$expected = 441
$pending = 441
$record = [ordered]@{
    schema = 2
    status = 'pending'
    platform = [Environment]::OSVersion.Platform.ToString()
    catalogSha256 = $CatalogSha256.ToUpperInvariant()
    exitCode = $null
    timedOut = $false
    trxSha256 = $null
    managedPassed = 0
    independentPassed = 0
    independentPending = $pending
    workflow = $Workflow
}
$process = $null
try {
    if ($record.platform -ne 'Win32NT') { throw 'This stage requires actual Windows.' }
    $sdk = Invoke-SectorWorksNativeProcess -Executable $Dotnet -Arguments '--version' -TimeoutMilliseconds 10000
    if ($sdk.TimedOut -or $sdk.ExitCode -ne 0 -or $sdk.Stdout.Trim() -ne '10.0.402') {
        throw 'The qualified user-local SDK 10.0.402 is required.'
    }
    $record.sdkVersion = $sdk.Stdout.Trim()
    $assembly = Join-Path $SourceRoot 'tests\SectorWorks.StorageTests\bin\Release\net10.0\SectorWorks.StorageTests.dll'
    Assert-OrdinaryPath $assembly
    $record.storageTestAssemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    $record.dotnetSha256 = (Get-FileHash -LiteralPath $Dotnet -Algorithm SHA256).Hash
    $env:SECTORWORKS_EXPANDED_INPUTS = $Inputs
    $env:SECTORWORKS_EXPANDED_OUTPUTS = $Outputs
    $env:SECTORWORKS_EXPANDED_CATALOG_SHA256 = $CatalogSha256
    $filter = 'FullyQualifiedName~NtfsExpandedWorkflowTests.EveryCandidateDiskStagesAndSavesWithIndependentChecks'
    if ($Workflow -eq 'commit') { $filter = 'FullyQualifiedName~NtfsExpandedWorkflowTests.EveryCandidateDiskDirectlyCommitsAfterValidatedSaveAs' }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Dotnet
    $start.WorkingDirectory = $SourceRoot
    $start.Arguments = 'test tests\SectorWorks.StorageTests\SectorWorks.StorageTests.csproj --configuration Release --no-build --no-restore --filter "' +
        $filter + '" --logger "trx;LogFileName=windows.trx" --results-directory "' + $Outputs + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $record.command = $start.Arguments
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (!$process.Start()) { throw 'Could not start managed Windows matrix.' }
    $handle = $process.Handle
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $record.timedOut = !$process.WaitForExit(2700000)
    if ($record.timedOut) {
        # Kill the owned process tree only, never processes selected by name.
        $kill = [Diagnostics.Process]::Start('taskkill.exe', "/PID $($process.Id) /T /F")
        if (!$kill.WaitForExit(10000) -or !$process.WaitForExit(10000)) { throw 'Timed-out matrix did not stop.' }
    }
    if (!$stdout.Wait(10000) -or !$stderr.Wait(10000)) { throw 'Matrix output streams did not close.' }
    [IO.File]::WriteAllText((Join-Path $Outputs 'windows.stdout.log'), $stdout.Result)
    [IO.File]::WriteAllText((Join-Path $Outputs 'windows.stderr.log'), $stderr.Result)
    $record.exitCode = $process.ExitCode
    if ($null -eq $record.exitCode -or $record.timedOut -or $record.exitCode -ne 0) {
        throw "Windows managed matrix failed or timed out (exit $($record.exitCode))."
    }
    $trxPath = Join-Path $Outputs 'windows.trx'
    [xml]$trx = [IO.File]::ReadAllText($trxPath, [Text.Encoding]::UTF8)
    $counters = $trx.TestRun.ResultSummary.Counters
    $results = @($trx.TestRun.Results.UnitTestResult)
    if ([int]$counters.total -ne $expected -or [int]$counters.passed -ne $expected -or
        $results.Count -ne $expected -or @($results | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
        throw "Expected exactly $expected passed managed workflows with zero skips/failures."
    }
    $record.trxSha256 = (Get-FileHash -LiteralPath $trxPath -Algorithm SHA256).Hash
    $record.managedPassed = $expected
    $record.status = 'pending-independent-linux-validation'
    Write-Output "$expected managed Windows workflows passed; $pending independent Linux outputs remain PENDING."
} catch {
    $record.status = 'failed'
    $record.error = $_.Exception.Message
    throw
} finally {
    if ($null -ne $process) { $process.Dispose() }
    foreach ($name in @('SECTORWORKS_EXPANDED_INPUTS', 'SECTORWORKS_EXPANDED_OUTPUTS', 'SECTORWORKS_EXPANDED_CATALOG_SHA256')) {
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    [IO.File]::WriteAllText((Join-Path $Outputs 'windows-run.json'), ($record | ConvertTo-Json -Depth 10))
}
