<#
.SYNOPSIS
Owner-launched, single-use ordinary/committed/rolled-back TxF rename controls.
.DESCRIPTION
Uses ONLY a new copy of the exact retained legal Native-R2 generated VHD.
Launch locally from an administrator PowerShell after review. The SSH account
must remain standard. There is no automatic elevation, arbitrary image/disk
argument, repair, resource reset/stop, service, schedule or real-disk access.
Each Control has its own fresh PUBLIC test root; never delete/reuse old roots.
Live resource queries are observations, NOT a detached recovery predicate.
Clean detached outputs are research inputs, NOT SectorWorks edited outputs or
qualification of active/prepared/indoubt transactions or LFS 2.0 writing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Ordinary', 'Committed', 'RolledBack')]
    [string] $Control,
    [switch] $ObserveClfs
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner must launch locally as administrator; no automatic elevation or remote privileged execution.'
}
. (Join-Path $PSScriptRoot 'windows-native-process.ps1')
$seed = Join-Path $env:PUBLIC 'SectorWorks-Phase2D-Native-R2\windows-ordinary-source.vhd'
$seedHash = 'CE71BDBE0F656AD32744D1BE3639326963422E9D57EC74ACC7952B56B9148313'
$revision = if ($ObserveClfs) { 'R5' } else { 'R3' }
$root = Join-Path $env:PUBLIC ('SectorWorks-Phase2D-TxF-' + $revision + '-' + $Control)
$image = Join-Path $root 'generated-control.vhd'
$attached = $false
$ownedRoot = $false
$transaction = [IntPtr]::Zero
$transactionCompleted = $false
$diskIdentity = $null
$report = [ordered]@{
    Schema = 1; Control = $Control; StartedUtc = [DateTime]::UtcNow.ToString('o')
    Passed = $false; Scope = 'Generated native control observations, not writer qualification'
    WriterQualified = $false; PersistentQuiescence = 'Unproven'
    Revision = $revision; NativeOracleRequested = [bool]$ObserveClfs
}

function Assert-PlainPath([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    if ($current -notmatch '^[A-Za-z]:\\' -or $current.Substring(3).Contains(':')) {
        throw 'Only ordinary local filesystem paths are permitted.'
    }
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') {
                throw 'Linked control/seed/evidence paths are forbidden.'
            }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Invoke-ControlNative([string] $Name, [string] $Arguments, [string] $Label, [int] $Timeout = 15000) {
    $path = Join-Path $root ($Label + '.json')
    if (Test-Path -LiteralPath $path) { throw 'Refusing existing native control evidence.' }
    $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot ('System32\' + $Name)) $Arguments $Timeout
    [IO.File]::WriteAllText($path, ($result | ConvertTo-Json -Depth 4))
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw "Native $Label failed; inspect retained finite command evidence." }
}

function Get-ControlDisk {
    $images = @(Get-DiskImage -ImagePath $image -ErrorAction Stop)
    if ($images.Count -ne 1 -or !$images[0].Attached) { throw 'Control attachment identity is ambiguous.' }
    $disks = @($images[0] | Get-Disk)
    if ($disks.Count -ne 1) { throw 'Expected exactly one generated file-backed disk.' }
    $disk = $disks[0]
    if ($disk.IsBoot -or $disk.IsSystem -or $disk.BusType -ne 'File Backed Virtual' -or $disk.Size -ne 128MB -or
        $disk.PartitionStyle -ne 'MBR' -or $disk.IsReadOnly) {
        throw 'Refusing anything except the exact disposable writable 128 MiB MBR virtual disk.'
    }
    if ($script:diskIdentity -and $disk.UniqueId -ne $script:diskIdentity) { throw 'Control disk identity changed.' }
    return $disk
}

function Invoke-ControlOracle([string] $Stage) {
    if (!$ObserveClfs) { return }
    Get-ControlDisk | Out-Null
    $helper = Join-Path $PSScriptRoot 'windows-native-clfs-oracle.ps1'
    Assert-PlainPath $helper
    $path = Join-Path $root ($Stage + '-clfs-oracle.json')
    if (Test-Path -LiteralPath $path) { throw 'Refusing existing native oracle evidence.' }
    $command = '& ' + "'" + $helper.Replace("'", "''") + "' -Control '" + $Control + "' -Drive '" + $drive + "'"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
        ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' + $encoded) 30000
    [IO.File]::WriteAllText($path, ($result | ConvertTo-Json -Depth 4))
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw "Native $Stage oracle process failed; retain its evidence." }
    $observation = $result.Stdout | ConvertFrom-Json
    if ($observation.Schema -ne 1 -or $observation.WriterQualified -ne $false -or
        @($observation.Streams).Count -ne 2) { throw 'Native oracle returned an unexpected evidence envelope.' }
    # Native API access/sharing errors remain in the child report. Never retry
    # with write access, alter ACLs or turn an incomplete query into a pass.
    $report[$Stage + 'OracleStreamsObserved'] = @($observation.Streams | Where-Object Observed -eq $true).Count
}

# Original test-only declarations for public Windows APIs. No CLFS parser,
# recovery, resource-manager configuration or product dependency is introduced.
$api = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SectorWorksTxfControlApi
{
    [DllImport("KtmW32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateTransaction(IntPtr attributes, IntPtr unitOfWork,
        uint options, uint isolationLevel, uint isolationFlags, uint timeout, string description);
    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTransactionId(IntPtr transaction, out Guid id);
    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CommitTransaction(IntPtr transaction);
    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RollbackTransaction(IntPtr transaction);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileTransactedW(string source, string destination,
        IntPtr progressRoutine, IntPtr data, uint flags, IntPtr transaction);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    public static IntPtr Begin()
    {
        IntPtr handle = CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 60000,
            "SectorWorks generated metadata-only rename control");
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }
    public static void Rename(string source, string destination, IntPtr handle)
    {
        if (!MoveFileTransactedW(source, destination, IntPtr.Zero, IntPtr.Zero, 0, handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static string Id(IntPtr handle)
    {
        Guid id;
        if (!GetTransactionId(handle, out id)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return id.ToString();
    }
    public static void Finish(IntPtr handle, bool commit)
    {
        bool success = commit ? CommitTransaction(handle) : RollbackTransaction(handle);
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static void Close(IntPtr handle)
    {
        if (!CloseHandle(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
'@

try {
    Assert-PlainPath $seed
    Assert-PlainPath $root
    if ($ObserveClfs) {
        $helper = Join-Path $PSScriptRoot 'windows-native-clfs-oracle.ps1'
        Assert-PlainPath $helper
        $report.OracleHelperSha256 = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash
    }
    if (!(Test-Path -LiteralPath $seed -PathType Leaf)) { throw 'Exact generated Native-R2 source is absent.' }
    if ((Get-Item -LiteralPath $seed).Length -gt 160MB -or (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash -ne $seedHash) {
        throw 'Generated seed differs from the independently recorded immutable Native-R2 image.'
    }
    if ((Get-DiskImage -ImagePath $seed -ErrorAction Stop).Attached) { throw 'Seed must be detached; it is never attached by this script.' }
    if (Test-Path -LiteralPath $root) { throw 'Single-use control root already exists; do not reuse or delete it.' }
    New-Item -ItemType Directory -Path $root | Out-Null
    $ownedRoot = $true
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
    Set-Acl -LiteralPath $root -AclObject $acl
    [IO.File]::Copy($seed, $image, $false)
    if ((Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash -ne $seedHash) { throw 'Fresh generated copy hash mismatch.' }
    $report.SeedSha256 = $seedHash
    $report.AuthoringOs = [Environment]::OSVersion.VersionString
    $report.PowerShell = $PSVersionTable.PSVersion.ToString()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $report.ApiSourceSha256 = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($api)))).Replace('-', '')
    } finally { $sha.Dispose() }
    Add-Type -TypeDefinition $api -ErrorAction Stop
    $diskpart = Join-Path $root 'attach.diskpart'
    [IO.File]::WriteAllLines($diskpart, @("select vdisk file=`"$image`"", 'attach vdisk', 'exit'), [Text.Encoding]::ASCII)
    $attached = $true
    Invoke-ControlNative 'diskpart.exe' ("/s `"$diskpart`"") 'attach' 30000
    $disk = Get-ControlDisk
    $diskIdentity = $disk.UniqueId
    $parts = @($disk | Get-Partition | Where-Object Type -ne 'Reserved')
    if ($parts.Count -ne 1 -or $parts[0].Offset -ne 65536 -or $parts[0].Size -ne 133103616) {
        throw 'Control partition differs from the sealed generated layout.'
    }
    $part = $parts[0]
    if (!$part.DriveLetter) {
        $part | Add-PartitionAccessPath -AssignDriveLetter
        $part = Get-Partition -DiskNumber $disk.Number -PartitionNumber $part.PartitionNumber
    }
    if (!$part.DriveLetter) { throw 'Control has no unique drive letter.' }
    $drive = "$($part.DriveLetter):"
    $sourceName = Join-Path ($drive + '\') 'rename-me.txt'
    $destinationName = Join-Path ($drive + '\') 'txf-control-renamed.txt'
    Assert-PlainPath $sourceName
    Assert-PlainPath $destinationName
    if (!(Test-Path -LiteralPath $sourceName -PathType Leaf) -or (Test-Path -LiteralPath $destinationName)) {
        throw 'Unexpected generated rename control namespace.'
    }
    $contentHash = (Get-FileHash -LiteralPath $sourceName -Algorithm SHA256).Hash
    $report.ControlFileSha256 = $contentHash
    $report.DiskUniqueId = $diskIdentity
    Invoke-ControlNative 'fsutil.exe' ('resource info ' + $drive + '\') 'before-resource'
    Invoke-ControlOracle 'before'
    if ($Control -eq 'Ordinary') {
        [IO.File]::Move($sourceName, $destinationName)
    } else {
        $transaction = [SectorWorksTxfControlApi]::Begin()
        $report.TransactionGuid = [SectorWorksTxfControlApi]::Id($transaction)
        [SectorWorksTxfControlApi]::Rename($sourceName, $destinationName, $transaction)
        # A nontransacted view must still see the original name before commit.
        if (!(Test-Path -LiteralPath $sourceName -PathType Leaf) -or (Test-Path -LiteralPath $destinationName)) {
            throw 'Active transaction leaked its private rename into the ordinary namespace.'
        }
        Invoke-ControlNative 'fsutil.exe' ('resource info ' + $drive + '\') 'active-resource'
        Invoke-ControlOracle 'active'
        [SectorWorksTxfControlApi]::Finish($transaction, ($Control -eq 'Committed'))
        $transactionCompleted = $true
        [SectorWorksTxfControlApi]::Close($transaction)
        $transaction = [IntPtr]::Zero
    }
    $renamed = $Control -ne 'RolledBack'
    if ((Test-Path -LiteralPath $destinationName -PathType Leaf) -ne $renamed -or
        (Test-Path -LiteralPath $sourceName -PathType Leaf) -eq $renamed) {
        throw 'Native commit/rollback/ordinary rename outcome disagrees with the control.'
    }
    $retained = if ($renamed) { $destinationName } else { $sourceName }
    if ((Get-FileHash -LiteralPath $retained -Algorithm SHA256).Hash -ne $contentHash) { throw 'Control rename changed payload bytes.' }
    $report.RenameObserved = $renamed
    Invoke-ControlNative 'fsutil.exe' ('resource info ' + $drive + '\') 'after-resource'
    Invoke-ControlOracle 'after'
    Invoke-ControlNative 'fsutil.exe' ('fsinfo ntfsinfo ' + $drive) 'attached-ntfsinfo'
    Invoke-ControlNative 'fsutil.exe' ('dirty query ' + $drive) 'dirty'
    Invoke-ControlNative 'chkdsk.exe' $drive 'chkdsk-readonly' 120000
    $report.Passed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    if ($transaction -ne [IntPtr]::Zero) {
        try {
            if (!$transactionCompleted) { [SectorWorksTxfControlApi]::Finish($transaction, $false) }
            [SectorWorksTxfControlApi]::Close($transaction)
            $transaction = [IntPtr]::Zero
        } catch { $report.Passed = $false; $report.TransactionCleanupError = $_.Exception.Message }
    }
    if ($attached -and $transaction -eq [IntPtr]::Zero) {
        try {
            if ((Get-DiskImage -ImagePath $image -ErrorAction Stop).Attached) {
                Dismount-DiskImage -ImagePath $image -ErrorAction Stop
            }
            if ((Get-DiskImage -ImagePath $image).Attached) { throw 'Generated control did not detach.' }
            $report.DetachedImageSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
        } catch { $report.Passed = $false; $report.DetachError = $_.Exception.Message }
    }
    if ($ownedRoot) {
        try {
            $report.SeedAfterSha256 = (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash
            if ($report.SeedAfterSha256 -ne $seedHash) { throw 'Immutable generated seed changed.' }
        } catch { $report.Passed = $false; $report.SeedIntegrityError = $_.Exception.Message }
        $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
        $json = Join-Path $root 'control.json'
        if (Test-Path -LiteralPath $json) { throw 'Refusing existing control report.' }
        [IO.File]::WriteAllText($json, ($report | ConvertTo-Json -Depth 6))
    }
}
if (!$report.Passed) { throw 'Native control failed; retain all evidence and inspect cleanup fields.' }
Write-Output "Native $Control control complete; generated output detached and retained. This is not writer qualification."
