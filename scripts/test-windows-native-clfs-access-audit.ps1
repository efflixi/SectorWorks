<#
.SYNOPSIS
Standard-account validation of the owner-local R6 access diagnostic.
.DESCRIPTION
Parses scripts, compiles literal API declarations, verifies owner-only refusal,
path bounds and coverage failures, then exercises read-only probe methods against
a NEW user-local ordinary file and synthetic CLFS log. No disk or TxF operations.
Use the finite native-process wrapper. Never reuse the single-use validation root.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Validation must use the standard account.' }

function Read-AuditAst([string] $Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors.Message -join '; ') }
    return $ast
}
function Get-AuditLiteral($Ast, [string] $Name) {
    $nodes = @($Ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq ('$' + $Name)
    }, $true))
    if ($nodes.Count -ne 1) { throw 'Expected exactly one literal declaration.' }
    return $nodes[0].Right.Expression.Value
}
$audit = Join-Path $PSScriptRoot 'windows-native-clfs-access-audit.ps1'
$files = @()
foreach ($name in @('windows-native-clfs-access-audit.ps1', 'test-windows-native-clfs-access-audit.ps1',
    'windows-native-process.ps1', 'windows-clfs-oracle-selftest.ps1')) {
    $path = Join-Path $PSScriptRoot $name
    $null = Read-AuditAst $path
    $files += [ordered]@{ Name = $name; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; Parse = 'passed' }
}
$ast = Read-AuditAst $audit
Add-Type -TypeDefinition (Get-AuditLiteral $ast 'api') -ErrorAction Stop
$authorAst = Read-AuditAst (Join-Path $PSScriptRoot 'windows-clfs-oracle-selftest.ps1')
Add-Type -TypeDefinition (Get-AuditLiteral $authorAst 'author') -ErrorAction Stop
foreach ($name in @('Assert-AuditObservation', 'Assert-AuditPlainPath')) {
    $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
    if (!$node) { throw 'Missing validation helper.' }
    . ([ScriptBlock]::Create($node.Extent.Text))
}
foreach ($arguments in @(@{}, @{ ProbeDrive = 'Q:'; ExpectedDiskId = ('0' * 32) })) {
    $refused = $false
    try { & $audit @arguments } catch {
        if ($_.Exception.Message -notmatch 'Owner must launch.*locally') { throw }
        $refused = $true
    }
    if (!$refused) { throw 'Owner-only guard did not refuse the standard account.' }
}
$targets = [SectorWorksClfsAccessAuditApi]::Targets('Q:')
if ($targets.Count -ne 8 -or $targets[5] -cne 'Q:\$Extend\$RmMetadata\$TxfLog\$TxfLog.blf' -or
    $targets[6] -cne 'Q:\$Extend\$RmMetadata\$TxfLog\$TxfLogContainer00000000000000000001') {
    throw 'Literal-dollar/drive-prefix regression.'
}
foreach ($bad in @('', 'q:', 'Q:\', 'Q:other', '\\host\share')) {
    $refused = $false
    try { $null = [SectorWorksClfsAccessAuditApi]::Targets($bad) } catch { $refused = $true }
    if (!$refused) { throw 'Invalid drive prefix accepted.' }
}
$rows = @()
foreach ($target in @('Root', 'Payload', 'Extend', 'RmMetadata', 'TxfLogDirectory', 'BaseLog', 'Container1', 'Container2')) {
    foreach ($mask in @('0x00000080', '0x80000000')) {
        $rows += @{ Operation = 'CreateFileW'; Target = $target; DesiredAccess = $mask; Attempted = $true
            Succeeded = ($target -eq 'Payload'); Win32Error = 5 }
    }
}
foreach ($client in @('TxfLog', 'KtmLog')) {
    $rows += @{ Operation = 'CreateLogFile'; Target = $client; DesiredAccess = '0x80000000'; Attempted = $true; Succeeded = $false; Win32Error = 5 }
}
$fixtureJson = @{ Schema = 1; WriterQualified = $false; PersistentQuiescence = 'Unproven'; Completed = $true; Results = $rows } | ConvertTo-Json -Depth 6
Assert-AuditObservation ($fixtureJson | ConvertFrom-Json)
$coverageCases = @('missing', 'duplicate', 'unattempted', 'missing-error', 'close-failure', 'wrong-target', 'writer-claim', 'incomplete', 'failed-positive')
foreach ($case in $coverageCases) {
    $fixture = $fixtureJson | ConvertFrom-Json
    switch ($case) {
        'missing' { $fixture.Results = @($fixture.Results | Select-Object -Skip 1) }
        'duplicate' { $fixture.Results[0] = $fixture.Results[1] }
        'unattempted' { $fixture.Results[0].Attempted = $false }
        'missing-error' { $fixture.Results[0].PSObject.Properties.Remove('Win32Error') }
        'close-failure' { $fixture.Results[0] | Add-Member NoteProperty CloseWin32Error 6 }
        'wrong-target' { $fixture.Results[0].Target = 'Other' }
        'writer-claim' { $fixture.WriterQualified = $true }
        'incomplete' { $fixture.Completed = $false }
        'failed-positive' { $fixture.Results[2].Succeeded = $false }
    }
    $refused = $false
    try { Assert-AuditObservation $fixture } catch { $refused = $true }
    if (!$refused) { throw ('Coverage regression failed: ' + $case) }
}
$root = Join-Path $env:LOCALAPPDATA 'SectorWorks\CLFS-Access-Audit-Validation-R6'
Assert-AuditPlainPath $root
if (Test-Path -LiteralPath $root) { throw 'Single-use validation root exists; retain evidence and do not rerun.' }
New-Item -ItemType Directory -Path $root | Out-Null
$report = [ordered]@{
    Schema = 1; Passed = $false; StandardAccount = $true; Files = $files
    Scope = 'User-local generated controls, not protected-volume access'
    WriterQualified = $false; DiskAttachmentInvoked = $false; TxFInvoked = $false
    GuardRefusals = 2; InvalidDriveRefusals = 5; CoverageCases = 'Complete18 accepted, nine incomplete/invalid reports rejected'
}
try {
    $payload = Join-Path $root 'ordinary.bin'
    [IO.File]::WriteAllBytes($payload, [byte[]]@(65, 0, 255))
    $null = [SectorWorksSyntheticClfsApi]::Create($root)
    $before = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Force) { $before[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    $privilegesBefore = [SectorWorksClfsAccessAuditApi]::TokenPrivileges() | ConvertTo-Json -Compress
    $observed = @()
    foreach ($name in @('ordinary.bin', '$TxfLog.blf', 'container0', 'container1')) {
        $path = Join-Path $root $name
        foreach ($mask in @([uint32]0x80, [uint32]2147483648)) {
            $row = [SectorWorksClfsAccessAuditApi]::FileProbe($path, $name, $mask, $false)
            if (!$row['Succeeded'] -or $row.ContainsKey('CloseWin32Error')) { throw ('Native ordinary file control failed: ' + $name) }
            $observed += $row
        }
    }
    foreach ($client in @('ProbeA', 'ProbeB')) {
        $row = [SectorWorksClfsAccessAuditApi]::LogProbe((Join-Path $root '$TxfLog'), $client)
        if (!$row['Succeeded'] -or $row.ContainsKey('CloseWin32Error')) { throw ('Native generic CLFS control failed: ' + $client) }
        $observed += $row
    }
    $missing = [SectorWorksClfsAccessAuditApi]::FileProbe((Join-Path $root 'absent.bin'), 'Absent', 0x80, $false)
    if ($missing['Succeeded'] -or $missing['Win32Error'] -ne 2 -or $missing['FailedOperation'] -ne 'CreateFileW') { throw 'Native file failure was misreported.' }
    $missingLog = [SectorWorksClfsAccessAuditApi]::LogProbe((Join-Path $root 'absent-log'), 'ProbeA')
    if ($missingLog['Succeeded'] -or $missingLog['Win32Error'] -ne 2 -or $missingLog['FailedOperation'] -ne 'CreateLogFile') { throw 'Native CLFS failure was misreported.' }
    $refused = $false
    try { $null = [SectorWorksClfsAccessAuditApi]::FileProbe($payload, 'BadMask', [uint32]3221225472, $false) } catch { $refused = $true }
    if (!$refused) { throw 'Write-access mask was accepted.' }
    $privilegesAfter = [SectorWorksClfsAccessAuditApi]::TokenPrivileges() | ConvertTo-Json -Compress
    if ($privilegesBefore -cne $privilegesAfter) { throw 'Token privilege state changed.' }
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Force) {
        if (!$before.ContainsKey($file.Name) -or $before[$file.Name] -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw 'Synthetic source set/bytes changed during read-only probing.'
        }
    }
    if (@(Get-ChildItem -LiteralPath $root -File -Force).Count -ne $before.Count) { throw 'Synthetic file disappeared.' }
    $report.PositiveOperations = $observed
    $report.ExpectedNativeFailures = @($missing, $missingLog)
    $report.TokenPrivileges = $privilegesAfter | ConvertFrom-Json
    $report.SourceHashesUnchanged = $before
    $report.Passed = $true
} catch { $report.Error = $_.Exception.Message; throw }
finally {
    [IO.File]::WriteAllText((Join-Path $root 'validation.json'), ($report | ConvertTo-Json -Depth 8))
}
$report | ConvertTo-Json -Depth 8
