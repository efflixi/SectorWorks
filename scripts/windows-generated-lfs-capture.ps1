<#
.SYNOPSIS
Finite generated-only LFS raw lifecycle capture; no repair/reset/replay.
.DESCRIPTION
Copies the hash-pinned legal Native-R2 seed into a fresh single-use root.
Captures the generated volume before and after FSCTL_LOCK_VOLUME on the same
handle, then closes/unlocks, detaches and hashes. These are observations, not
a portable writer predicate. No physical-disk argument or account change.
Owner authorization: docs/sectorworks_phase_2d_and_phase_3_autonomous_sol_prompt.txt.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This finite generated-media capture needs a normal administrator UAC launch. The testing account stays standard.'
}
. (Join-Path $PSScriptRoot 'windows-native-process.ps1')
$seed = Join-Path $env:PUBLIC 'SectorWorks-Phase2D-Native-R2\windows-ordinary-source.vhd'
$expectedSeed = 'CE71BDBE0F656AD32744D1BE3639326963422E9D57EC74ACC7952B56B9148313'
$root = Join-Path $env:PUBLIC 'SectorWorks-Autonomous-20261116-LfsR2'
$image = Join-Path $root 'generated-lfs.vhd'
$owned = $false
$attachmentAttempted = $false
$report = [ordered]@{ Schema = 1; StartedUtc = [DateTime]::UtcNow.ToString('o'); Completed = $false; WriterQualified = $false; CacheSafetyQualified = $false }

function Assert-PlainPath([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    if ($current -notmatch '^[A-Za-z]:\\' -or $current.Substring(3).Contains(':')) { throw 'Only ordinary local paths are allowed.' }
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') { throw 'Linked capture paths are forbidden.' }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
function Save-Report {
    if ($owned) { [IO.File]::WriteAllText((Join-Path $root 'capture-report.json'), ($report | ConvertTo-Json -Depth 8)) }
}
function Invoke-Finite([string] $Name, [string] $Arguments, [string] $Label) {
    $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot ('System32\' + $Name)) $Arguments 30000
    [IO.File]::WriteAllText((Join-Path $root ($Label + '.json')), ($result | ConvertTo-Json -Depth 4))
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw "Native $Label failed; retain its report." }
}
function Get-OwnedDisk {
    $images = @(Get-DiskImage -ImagePath $image)
    if ($images.Count -ne 1 -or !$images[0].Attached) { throw 'Expected one attached owned virtual image.' }
    $disks = @($images[0] | Get-Disk)
    if ($disks.Count -ne 1) { throw 'Ambiguous generated disk.' }
    $disk = $disks[0]
    if ($disk.IsBoot -or $disk.IsSystem -or $disk.BusType -ne 'File Backed Virtual' -or $disk.Size -ne 128MB -or $disk.PartitionStyle -ne 'MBR' -or $disk.IsReadOnly) {
        throw 'Refusing anything except the expected generated 128 MiB writable MBR virtual disk.'
    }
    return $disk
}

$api = @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
public static class SectorWorksGeneratedLfsCapture
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool DeviceIoControl(IntPtr handle, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", EntryPoint="DeviceIoControl", SetLastError=true)]
    static extern bool QueryDiskExtents(IntPtr handle, uint code, IntPtr input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool SetFilePointerEx(IntPtr handle, long distance, out long position, uint method);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool ReadFile(IntPtr handle, byte[] buffer, uint length, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool CloseHandle(IntPtr handle);
    static void Check(bool result, string operation) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }
    static void Copy(IntPtr handle, string path)
    {
        long position;
        Check(SetFilePointerEx(handle, 0, out position, 0), "Seek generated volume");
        if (position != 0) throw new IOException("Wrong generated-volume offset.");
        long remaining = 133103616;
        byte[] buffer = new byte[1048576];
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            while (remaining > 0) {
                uint read;
                uint request = (uint)Math.Min(buffer.Length, remaining);
                Check(ReadFile(handle, buffer, request, out read, IntPtr.Zero), "Read generated volume");
                if (read == 0 || read > request || read % 512 != 0) throw new IOException("Incomplete or misaligned generated-volume capture.");
                output.Write(buffer, 0, checked((int)read));
                remaining -= read;
            }
            output.Flush(true);
        }
    }
    public static void Capture(string drive, string directory, uint expectedDiskNumber)
    {
        if (drive == null || drive.Length != 2 || drive[0] < 'A' || drive[0] > 'Z' || drive[1] != ':') throw new ArgumentException("Expected one drive letter.");
        IntPtr handle = CreateFileW(@"\\.\" + drive, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Open positively identified generated volume");
        bool locked = false;
        try {
            uint returned;
            byte[] extents = new byte[32];
            Check(QueryDiskExtents(handle, 0x00560000, IntPtr.Zero, 0, extents, 32, out returned, IntPtr.Zero), "Query opened volume disk extent");
            if (returned != 32 || BitConverter.ToUInt32(extents, 0) != 1 ||
                BitConverter.ToUInt32(extents, 8) != expectedDiskNumber ||
                BitConverter.ToInt64(extents, 16) != 65536 || BitConverter.ToInt64(extents, 24) != 133103616)
                throw new IOException("Opened handle does not identify the guarded generated partition.");
            // Microsoft documents a filesystem tail restriction on volume
            // handles. Permit partition-tail reads on this exact handle while
            // retaining the explicit byte bound and never issuing data writes.
            Check(DeviceIoControl(handle, 0x00090083, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero), "Allow bounded partition-tail reads");
            Copy(handle, Path.Combine(directory, "mounted-before-lock.raw"));
            Check(DeviceIoControl(handle, 0x00090018, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero), "Lock generated volume (flush/disMount)");
            locked = true;
            Copy(handle, Path.Combine(directory, "locked-after-flush.raw"));
        }
        finally {
            try {
                if (locked) {
                    uint returned;
                    Check(DeviceIoControl(handle, 0x0009001c, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero), "Unlock generated volume");
                }
            }
            finally { Check(CloseHandle(handle), "Close generated volume"); }
        }
    }
}
'@

try {
    foreach ($path in @($seed, $root, $PSCommandPath, (Join-Path $PSScriptRoot 'windows-native-process.ps1'))) { Assert-PlainPath $path }
    if (Test-Path -LiteralPath $root) { throw 'Single-use capture root already exists. Preserve it; do not rerun.' }
    if (!(Test-Path -LiteralPath $seed -PathType Leaf) -or (Get-Item -LiteralPath $seed).Length -ne 12615680 -or (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash -ne $expectedSeed) { throw 'Expected sealed generated seed is unavailable or changed.' }
    if ((Get-DiskImage -ImagePath $seed).Attached) { throw 'Sealed seed must remain detached.' }
    Add-Type -TypeDefinition $api
    New-Item -ItemType Directory -Path $root | Out-Null
    $owned = $true
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
    Set-Acl -LiteralPath $root -AclObject $acl
    [IO.File]::Copy($seed, $image, $false)
    if ((Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash -ne $expectedSeed) { throw 'Fresh generated copy differs from its seed.' }
    $report.ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    $report.SeedSha256 = $expectedSeed
    $report.PowerShell = $PSVersionTable.PSVersion.ToString()
    $report.Os = [Environment]::OSVersion.VersionString
    Save-Report
    $attach = Join-Path $root 'attach.diskpart'
    [IO.File]::WriteAllLines($attach, @("select vdisk file=`"$image`"", 'attach vdisk', 'exit'), [Text.Encoding]::ASCII)
    $attachmentAttempted = $true
    Invoke-Finite 'diskpart.exe' ("/s `"$attach`"") 'attach'
    $disk = Get-OwnedDisk
    $parts = @($disk | Get-Partition | Where-Object Type -ne 'Reserved')
    if ($parts.Count -ne 1 -or $parts[0].Offset -ne 65536 -or $parts[0].Size -ne 133103616) { throw 'Generated partition differs from sealed layout.' }
    $part = $parts[0]
    if (!$part.DriveLetter) {
        $part | Add-PartitionAccessPath -AssignDriveLetter
        $part = Get-Partition -DiskNumber $disk.Number -PartitionNumber $part.PartitionNumber
    }
    if (!$part.DriveLetter) { throw 'No generated-volume drive letter.' }
    $drive = "$($part.DriveLetter):"
    $report.DiskId = $disk.UniqueId
    $report.PartitionOffset = $part.Offset
    $report.PartitionLength = $part.Size
    Save-Report
    Invoke-Finite 'fsutil.exe' ("fsinfo ntfsinfo $drive") 'mounted-ntfsinfo'
    # Recheck immediately before opening the volume. The C# capture accepts no
    # physical device name, writes no volume data and uses only the guarded drive.
    if ((Get-OwnedDisk).UniqueId -ne $disk.UniqueId) { throw 'Generated disk identity changed.' }
    [SectorWorksGeneratedLfsCapture]::Capture($drive, $root, [uint32]$disk.Number)
    $report.Captures = @(Get-ChildItem -LiteralPath $root -Filter '*.raw' | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $report.Completed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    try {
        if ($attachmentAttempted -and (Get-DiskImage -ImagePath $image -ErrorAction Stop).Attached) {
            $detach = Join-Path $root 'detach.diskpart'
            [IO.File]::WriteAllLines($detach, @("select vdisk file=`"$image`"", 'detach vdisk', 'exit'), [Text.Encoding]::ASCII)
            Invoke-Finite 'diskpart.exe' ("/s `"$detach`"") 'detach'
            if ((Get-DiskImage -ImagePath $image).Attached) { throw 'Owned generated image remains attached.' }
        }
        if ($owned) { $report.DetachedVhdSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash }
        if ((Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash -ne $expectedSeed) { throw 'Sealed generated seed changed.' }
        $report.SeedUnchanged = $true
    } catch { $report.CleanupError = $_.Exception.Message; throw }
    finally {
        if ($owned) {
            $report.Captures = @(Get-ChildItem -LiteralPath $root -Filter '*.raw' | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            })
        }
        $report.FinishedUtc = [DateTime]::UtcNow.ToString('o'); Save-Report
    }
}
