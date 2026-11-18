<#
.SYNOPSIS
Standard-account R8 regression for native dictionary and JSON evidence rows.
.DESCRIPTION
Reproduces the retained R7 checker defect, tests both real result shapes and
malformed coverage/cleanup cases, and checks owner refusal/compilation.
Only a fresh user-local directory/file and missing user-local paths are probed.
No disk attachment, protected log, transaction or privilege adjustment is run.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Standard account required.' }
function Read-ProbeAst([string] $Path) {
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($Path,[ref]$tokens,[ref]$errors)
    if ($errors.Count) { throw ($errors.Message -join '; ') }
    return $ast
}
function Read-ProbeLiteral($Ast,[string] $Name) {
    $key='$'+$Name
    $nodes=@($Ast.FindAll({param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq $key},$true))
    if ($nodes.Count -ne 1) { throw 'Expected one literal declaration.' }
    return $nodes[0].Right.Expression.Value
}
$files=@();$asts=@{}
foreach ($name in @('windows-native-clfs-backup-probe.ps1','windows-owner-backup-read-scope.ps1',
    'windows-native-clfs-oracle.ps1','windows-native-process.ps1','test-windows-native-clfs-backup-probe.ps1','previous-r7.ps1')) {
    $path=Join-Path $PSScriptRoot $name
    $asts[$name]=Read-ProbeAst $path
    $files+=@{Name=$name;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;Parse='passed'}
}
$previous=Join-Path $PSScriptRoot 'previous-r7.ps1'
if ((Get-FileHash -LiteralPath $previous -Algorithm SHA256).Hash -ne '2350DEF36599E3C4B6B3D9044843EEE01CD242F7FD47136F60AD4198C74D05DB') {
    throw 'Exact retained as-executed R7 source required for reproduction.'
}
$currentAst=$asts['windows-native-clfs-backup-probe.ps1']
Add-Type -TypeDefinition (Read-ProbeLiteral $currentAst 'api')
Add-Type -TypeDefinition (Read-ProbeLiteral $asts['windows-owner-backup-read-scope.ps1'] 'backupApi')
Add-Type -TypeDefinition (Read-ProbeLiteral $asts['windows-native-clfs-oracle.ps1'] 'api')
foreach ($name in @('Test-AuditField','Assert-AuditObservation','Assert-AuditPlainPath')) {
    $node=$currentAst.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if (!$node) { throw 'Missing checker helper.' }
    . ([ScriptBlock]::Create($node.Extent.Text))
}
$node=$asts['previous-r7.ps1'].Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-AuditObservation'},$true)
. ([ScriptBlock]::Create($node.Extent.Text.Replace('function Assert-AuditObservation','function Assert-PreviousAuditObservation')))
$driver=Join-Path $PSScriptRoot 'windows-native-clfs-backup-probe.ps1'
foreach ($arguments in @(@{},@{EnableOwnerBackupRead=$true},@{EnableOwnerBackupRead=$true;ProbeDrive='Q:';ExpectedDiskId=('0'*32)})) {
    $refused=$false
    try { & $driver @arguments } catch {
        if ($_.Exception.Message -notmatch 'Owner must launch.*locally') { throw }
        $refused=$true
    }
    if (!$refused) { throw 'Owner-only driver guard failed.' }
}
$refused=$false
try { & (Join-Path $PSScriptRoot 'windows-owner-backup-read-scope.ps1') } catch {
    if ($_.Exception.Message -notmatch 'Owner must launch.*locally') { throw }
    $refused=$true
}
if (!$refused) { throw 'Owner-only helper guard failed.' }
$refused=$false
try { $scope=[SectorWorksOwnerBackupReadScope]::new();$scope.Dispose() } catch {
    if ($_.Exception.Message -notmatch 'Owner administrator token required') { throw }
    $refused=$true
}
if (!$refused) { throw 'Standard account reached privilege adjustment.' }
$root=Join-Path $env:LOCALAPPDATA 'SectorWorks\CLFS-Backup-Probe-Validation-R8'
Assert-AuditPlainPath $root
if (Test-Path -LiteralPath $root) { throw 'Single-use validation root exists; retain it.' }
New-Item -ItemType Directory -Path $root | Out-Null
$report=[ordered]@{Passed=$false;StandardAccount=$true;Files=$files;GuardRefusals=5
    NativeDiskOrProtectedLogCallsInvoked=$false;NativePrivilegeAdjustmentInvoked=$false
    Scope='Native user-local result-shape regression; owner activation/restoration remains untested'}
try {
    $payload=Join-Path $root 'ordinary.bin'
    [IO.File]::WriteAllBytes($payload,[byte[]]@(65,0,255))
    $hash=(Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
    $tokenBefore=[SectorWorksClfsAccessAuditApi]::TokenPrivileges()|ConvertTo-Json -Compress
    $rows=@()
    foreach ($target in @('Root','Payload','Extend','RmMetadata','TxfLogDirectory','BaseLog','Container1','Container2')) {
        $path=Join-Path $root ('absent-'+$target)
        $directory=$false
        if ($target -eq 'Root') {$path=$root;$directory=$true}
        if ($target -eq 'Payload') {$path=$payload}
        foreach ($mask in @([uint32]0x80,[uint32]2147483648)) {
            $row=[SectorWorksClfsAccessAuditApi]::FileProbe($path,$target,$mask,$directory)
            if ($target -in @('Root','Payload')) {
                if (!$row['Succeeded']) {throw 'Ordinary file positive control failed.'}
            } elseif ($row['Succeeded'] -or $row['Win32Error'] -ne 2) {throw 'Missing-file native error was not recorded.'}
            $rows+=,$row
        }
    }
    foreach ($client in @('TxfLog','KtmLog')) {
        $row=[SectorWorksClfsAccessAuditApi]::LogProbe((Join-Path $root 'absent-log'),$client)
        if ($row['Succeeded'] -or $row['Win32Error'] -ne 2) {throw 'Missing-log native error was not recorded.'}
        $rows+=,$row
    }
    $native=[pscustomobject]@{Schema=1;WriterQualified=$false;PersistentQuiescence='Unproven';Completed=$true;Results=$rows}
    $reproduced=$false
    try {Assert-PreviousAuditObservation $native} catch {
        if ($_.Exception.Message -ne 'Failed operation lacks its native error.') {throw}
        $reproduced=$true
    }
    if (!$reproduced) {throw 'Original R7 checker defect did not reproduce on actual native rows.'}
    Assert-AuditObservation $native
    $fixtureJson=$native|ConvertTo-Json -Depth 8
    Assert-AuditObservation ($fixtureJson|ConvertFrom-Json)
    function New-CheckerFixture([bool] $Raw) {
        $fixture=$fixtureJson|ConvertFrom-Json
        if ($Raw) {
            $items=@()
            foreach ($row in $fixture.Results) {
                $item=[Collections.Generic.Dictionary[string,object]]::new()
                foreach ($property in $row.PSObject.Properties) {$item.Add($property.Name,$property.Value)}
                $items+=,$item
            }
            $fixture.Results=$items
        }
        return $fixture
    }
    $cases=@('missing','duplicate','unattempted','missing-error','close-failure','wrong-target','writer-claim','incomplete','failed-positive')
    foreach ($raw in @($true,$false)) {
        foreach ($case in $cases) {
            $fixture=New-CheckerFixture $raw
            switch ($case) {
                'missing' {$fixture.Results=@($fixture.Results|Select-Object -Skip 1)}
                'duplicate' {$fixture.Results[0]=$fixture.Results[1]}
                'unattempted' {if ($raw) {$fixture.Results[0]['Attempted']=$false} else {$fixture.Results[0].Attempted=$false}}
                'missing-error' {if ($raw) {$null=$fixture.Results[4].Remove('Win32Error')} else {$fixture.Results[4].PSObject.Properties.Remove('Win32Error')}}
                'close-failure' {if ($raw) {$fixture.Results[0]['CloseWin32Error']=6} else {$fixture.Results[0]|Add-Member NoteProperty CloseWin32Error 6}}
                'wrong-target' {if ($raw) {$fixture.Results[0]['Target']='Other'} else {$fixture.Results[0].Target='Other'}}
                'writer-claim' {$fixture.WriterQualified=$true}
                'incomplete' {$fixture.Completed=$false}
                'failed-positive' {
                    if ($raw) {$fixture.Results[2]['Succeeded']=$false;$fixture.Results[2]['Win32Error']=5}
                    else {$fixture.Results[2].Succeeded=$false;$fixture.Results[2]|Add-Member NoteProperty Win32Error 5}
                }
            }
            $refused=$false
            try {Assert-AuditObservation $fixture} catch {$refused=$true}
            if (!$refused) {throw ('Malformed evidence accepted: '+$case+' raw='+$raw)}
        }
    }
    $tokenAfter=[SectorWorksClfsAccessAuditApi]::TokenPrivileges()|ConvertTo-Json -Compress
    if ($tokenBefore -cne $tokenAfter -or $hash -ne (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -or
        @(Get-ChildItem -LiteralPath $root -File -Force).Count -ne 1) {throw 'User-local sources/token changed.'}
    $report.OriginalRawFailureReproduced=$true
    $report.NativeRows=$native
    $report.RawAndJsonAccepted=$true
    $report.MalformedCasesRejected=18
    $report.SourceAndTokenUnchanged=$true
    $report.Passed=$true
} catch {$report.Error=$_.Exception.Message;throw}
finally {[IO.File]::WriteAllText((Join-Path $root 'validation.json'),($report|ConvertTo-Json -Depth 12))}
$report|ConvertTo-Json -Depth 12
