<#
.SYNOPSIS
Standard-user CLFS queries on complete copies of reviewed generated log streams.
.DESCRIPTION
No mount, privilege activation, authentication change, log append/reset/replay or
metadata patching. The sealed inputs must match an independently supplied manifest
hash, including their original full-file lengths. Reviewed container paths use
the documented %BLF% relative prefix with exact sibling basenames. Each client
gets a fresh complete copy, because even native read-context cleanup may flush
internal CLFS state. Copy changes are reported, never hidden or called proof of
persistent transaction semantics or writer safety. Run in a finite parent.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $InputsRoot,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ManifestSha256,
    [Parameter(Mandatory = $true)][string] $OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ([Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This detached-copy observation requires the unchanged standard testing account.'
}
function Assert-PlainPath([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    if (!$current.StartsWith($env:USERPROFILE + '\', [StringComparison]::OrdinalIgnoreCase) -and
        !$current.StartsWith($env:LOCALAPPDATA + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Only user-local generated test paths are accepted.'
    }
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') {
                throw 'Linked observation paths are forbidden.'
            }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
$InputsRoot = [IO.Path]::GetFullPath($InputsRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
Assert-PlainPath $InputsRoot
Assert-PlainPath $OutputRoot
if (Test-Path -LiteralPath $OutputRoot) { throw 'Single-use output already exists; retain it and select a fresh run.' }
$manifestPath = Join-Path $InputsRoot 'manifest.json'
Assert-PlainPath $manifestPath
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $ManifestSha256) { throw 'Input manifest hash mismatch.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$names = @('$TxfLog.blf', '$TxfLogContainer00000000000000000001', '$TxfLogContainer00000000000000000002')
if (!$manifest.ContainerNamesUseDocumentedBlfRelativePrefix -or @($manifest.Files).Count -ne 3) { throw 'Unreviewed generated log layout.' }
function Verify-Inputs {
    foreach ($name in $names) {
        $rows = @($manifest.Files | Where-Object Name -CEQ $name)
        if ($rows.Count -ne 1) { throw 'Input file identity mismatch.' }
        $path = Join-Path $InputsRoot $name
        Assert-PlainPath $path
        $row = $rows[0]
        if ($row.Length -gt 2097152 -or (Get-Item -LiteralPath $path).Length -ne $row.Length -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $row.Sha256) { throw 'Complete input stream hash/length mismatch.' }
    }
}
Verify-Inputs
$oracle = Join-Path $PSScriptRoot 'windows-native-clfs-oracle.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($oracle, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Read oracle failed to parse.' }
$definitions = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$api'
}, $true))
if ($definitions.Count -ne 1) { throw 'Expected one literal native declaration.' }
Add-Type -TypeDefinition $definitions[0].Right.Expression.Value -ErrorAction Stop
$null = New-Item -ItemType Directory -Path $OutputRoot
$report = [ordered]@{ Schema = 1; StartedUtc = [DateTime]::UtcNow.ToString('o');
    Scope = 'Generated detached-copy standard-user observations only'; WriterQualified = $false;
    PersistentQuiescence = 'Unproven'; Completed = $false; Streams = @(); InputsUnchanged = $false }
function Save-Report { [IO.File]::WriteAllText((Join-Path $OutputRoot 'observation.json'), ($report | ConvertTo-Json -Depth 12)) }
try {
    Save-Report
    foreach ($client in @('TxfLog', 'KtmLog')) {
        $copy = Join-Path $OutputRoot $client
        $null = New-Item -ItemType Directory -Path $copy
        foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $InputsRoot $name) -Destination (Join-Path $copy $name) }
        $report.CurrentClient = $client
        Save-Report
        $row = [SectorWorksClfsOracleApi]::Stream((Join-Path $copy '$TxfLog'), $client)
        $report.Streams += $row
        $report[$client + 'FilesAfter'] = @(Get-ChildItem -LiteralPath $copy -File | ForEach-Object {
            [ordered]@{ Name = $_.Name; Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        Save-Report
    }
    $report.Completed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    try { Verify-Inputs; $report.InputsUnchanged = $true } catch { $report.InputIntegrityError = $_.Exception.Message }
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
    Save-Report
}
$report | ConvertTo-Json -Depth 12
