<#
.SYNOPSIS
Read-only native observations inside the owner-launched R5 generated controls.
.DESCRIPTION
Requests only read handles/queries, with native context/handle cleanup. CLFS
marshalling-area cleanup may flush pending I/O; no explicit append/flush/reset/
repair or elevation is requested. The parent runs a finite child. It observes LIVE
Windows state, not detached persistent quiescence or offline write safety.
API errors (including sharing/access denial) are retained, never worked around.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Ordinary', 'Committed', 'RolledBack')][string] $Control,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Z]:$')][string] $Drive
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner-local generated-control observation only; no elevation is attempted.'
}
$image = Join-Path $env:PUBLIC ('SectorWorks-Phase2D-TxF-R5-' + $Control + '\generated-control.vhd')
$current = [IO.Path]::GetFullPath($image)
while ($current) {
    $item = Get-Item -LiteralPath $current -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') {
        throw 'Linked generated control paths are forbidden.'
    }
    $parent = Split-Path -Parent $current
    if ($parent -eq $current) { break }
    $current = $parent
}
$images = @(Get-DiskImage -ImagePath $image)
if ($images.Count -ne 1 -or !$images[0].Attached) { throw 'Exact R5 generated control is not attached.' }
$disks = @($images[0] | Get-Disk)
if ($disks.Count -ne 1 -or $disks[0].IsBoot -or $disks[0].IsSystem -or
    $disks[0].BusType -ne 'File Backed Virtual' -or $disks[0].Size -ne 128MB -or
    $disks[0].PartitionStyle -ne 'MBR' -or $disks[0].IsReadOnly) {
    throw 'Only the parent-owned generated 128 MiB writable virtual disk is observable.'
}
$parts = @(Get-Partition -DriveLetter $Drive.Substring(0, 1))
if ($parts.Count -ne 1 -or $parts[0].DiskNumber -ne $disks[0].Number -or
    $parts[0].Offset -ne 65536 -or $parts[0].Size -ne 133103616) {
    throw 'Observed drive does not identify the exact generated partition.'
}

# Original public API declarations; references are recorded in dependency-evaluation.md.
$api = @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
public static class SectorWorksClfsOracleApi
{
    [DllImport("clfsw32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateLogFile(string name, uint access, uint share,
        IntPtr attributes, uint disposition, uint flags);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogFileInformation(IntPtr log, [Out] byte[] info, ref uint size);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateLogMarshallingArea(IntPtr log, IntPtr allocate, IntPtr free,
        IntPtr context, uint bufferSize, uint writeBuffers, uint readBuffers, out IntPtr marshal);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteLogMarshallingArea(IntPtr marshal);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadLogRestartArea(IntPtr marshal, out IntPtr buffer,
        out uint size, out ulong lsn, out IntPtr context, IntPtr overlapped);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadPreviousLogRestartArea(IntPtr context, out IntPtr buffer,
        out uint size, out ulong lsn, IntPtr overlapped);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadLogRecord(IntPtr marshal, ref ulong first, uint mode,
        out IntPtr buffer, out uint size, out byte type, out ulong undo, out ulong previous,
        out IntPtr context, IntPtr overlapped);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadNextLogRecord(IntPtr context, out IntPtr buffer, out uint size,
        ref byte type, IntPtr userLsn, out ulong undo, out ulong previous, out ulong lsn, IntPtr overlapped);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateReadLog(IntPtr context);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share,
        IntPtr attributes, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr file, uint code, IntPtr input, uint inputSize,
        [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    public static string NativeTxfLogPath(string drive)
    {
        if (drive == null || drive.Length != 2 || drive[0] < 'A' || drive[0] > 'Z' || drive[1] != ':')
            throw new ArgumentException("Expected one uppercase drive letter.");
        return drive + @"\$Extend\$RmMetadata\$TxfLog\$TxfLog";
    }
    private static Dictionary<string, object> Payload(IntPtr pointer, uint length, ulong lsn)
    {
        if (length > 16384 || (length != 0 && pointer == IntPtr.Zero))
            throw new InvalidOperationException("Native payload exceeds the 16 KiB observation budget.");
        byte[] bytes = new byte[length];
        if (length != 0) Marshal.Copy(pointer, bytes, 0, (int)length);
        string digest;
        using (SHA256 hash = SHA256.Create()) digest = Hex(hash.ComputeHash(bytes));
        return new Dictionary<string, object> {
            {"Lsn", "0x" + lsn.ToString("x16")}, {"PayloadBytes", length},
            {"PayloadSha256", digest}, {"PayloadHex", Hex(bytes)} };
    }
    private static void Error(Dictionary<string, object> result, Exception error)
    {
        result["Observed"] = false;
        result["Error"] = error.Message;
        Win32Exception native = error as Win32Exception;
        if (native != null) result["Win32Error"] = native.NativeErrorCode;
    }
    public static object FileMetadata(string path)
    {
        var result = new Dictionary<string, object>();
        IntPtr file = new IntPtr(-1);
        try {
            // GENERIC_READ, share read/write/delete, OPEN_EXISTING, directory-compatible.
            file = CreateFileW(path, 0x80000000, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (file == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            byte[] output = new byte[48]; uint returned;
            // CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 91, METHOD_BUFFERED, FILE_READ_DATA).
            Check(DeviceIoControl(file, 0x0009416c, IntPtr.Zero, 0, output, 48, out returned, IntPtr.Zero));
            if (returned < 44 || returned > 48) throw new InvalidOperationException("Unexpected TXFS metadata size.");
            byte[] guid = new byte[16]; Array.Copy(output, 16, guid, 0, 16);
            result["Observed"] = true; result["ReturnedBytes"] = returned;
            result["TxfFileIdLow"] = BitConverter.ToUInt64(output, 0).ToString("x16");
            result["TxfFileIdHigh"] = BitConverter.ToUInt64(output, 8).ToString("x16");
            result["LockingTransaction"] = new Guid(guid).ToString();
            result["LastLsn"] = "0x" + BitConverter.ToUInt64(output, 32).ToString("x16");
            result["TransactionState"] = BitConverter.ToUInt32(output, 40);
        } catch (Exception error) { Error(result, error); }
        finally { if (file != new IntPtr(-1) && !CloseHandle(file)) result["CloseWin32Error"] = Marshal.GetLastWin32Error(); }
        return result;
    }
    public static object Stream(string path, string client)
    {
        var result = new Dictionary<string, object> { {"Client", client} };
        IntPtr log = new IntPtr(-1), marshal = IntPtr.Zero, context = IntPtr.Zero;
        try {
            // Read-only OPEN_EXISTING. No retry with write/delete or altered security.
            log = CreateLogFile("LOG:" + path + "::" + client, 0x80000000, 7, IntPtr.Zero, 3, 0);
            if (log == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            byte[] info = new byte[120]; uint infoSize = 120;
            Check(GetLogFileInformation(log, info, ref infoSize));
            if (infoSize != 120) throw new InvalidOperationException("Unexpected CLFS_INFORMATION size.");
            result["InformationHex"] = Hex(info);
            string[] fields = {"MinArchiveTailLsn", "BaseLsn", "LastFlushedLsn", "LastLsn", "RestartLsn"};
            for (int i = 0; i < fields.Length; ++i)
                result[fields[i]] = "0x" + BitConverter.ToUInt64(info, 64 + i * 8).ToString("x16");
            Check(CreateLogMarshallingArea(log, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 65536, 1, 2, out marshal));
            var restarts = new List<object>(); result["RestartAreas"] = restarts;
            IntPtr buffer; uint size; ulong lsn;
            Check(ReadLogRestartArea(marshal, out buffer, out size, out lsn, out context, IntPtr.Zero));
            restarts.Add(Payload(buffer, size, lsn));
            for (int i = 1; i < 4; ++i) {
                if (!ReadPreviousLogRestartArea(context, out buffer, out size, out lsn, IntPtr.Zero)) {
                    result["PreviousRestartStopWin32"] = Marshal.GetLastWin32Error(); break;
                }
                restarts.Add(Payload(buffer, size, lsn));
                if (i == 3) result["PreviousRestartLimitReached"] = true;
            }
            Check(TerminateReadLog(context)); context = IntPtr.Zero;
            var records = new List<object>(); result["ForwardRecords"] = records;
            ulong first = BitConverter.ToUInt64(info, 72), undo, previous;
            byte type; int total = 0;
            Check(ReadLogRecord(marshal, ref first, 3, out buffer, out size, out type,
                out undo, out previous, out context, IntPtr.Zero));
            lsn = first;
            for (int i = 0; i < 128; ++i) {
                total = checked(total + (int)size);
                if (total > 65536) throw new InvalidOperationException("Forward payloads exceed 64 KiB budget.");
                var record = Payload(buffer, size, lsn);
                record["RecordType"] = type;
                record["UndoNextLsn"] = "0x" + undo.ToString("x16");
                record["PreviousLsn"] = "0x" + previous.ToString("x16");
                records.Add(record);
                type = 3; // SDK UCHAR ClfsClientRecord: restart and data records.
                if (!ReadNextLogRecord(context, out buffer, out size, ref type, IntPtr.Zero,
                    out undo, out previous, out lsn, IntPtr.Zero)) {
                    result["ForwardStopWin32"] = Marshal.GetLastWin32Error(); break;
                }
                if (i == 127) throw new InvalidOperationException("Forward record count exceeds 128 budget.");
            }
            result["Observed"] = true;
        } catch (Exception error) { Error(result, error); }
        finally {
            if (context != IntPtr.Zero && !TerminateReadLog(context)) result["TerminateWin32Error"] = Marshal.GetLastWin32Error();
            if (marshal != IntPtr.Zero && !DeleteLogMarshallingArea(marshal)) result["MarshalCleanupWin32Error"] = Marshal.GetLastWin32Error();
            if (log != new IntPtr(-1) && !CloseHandle(log)) result["CloseWin32Error"] = Marshal.GetLastWin32Error();
        }
        return result;
    }
}
'@
Add-Type -TypeDefinition $api -ErrorAction Stop
$log = [SectorWorksClfsOracleApi]::NativeTxfLogPath($Drive)
$result = [ordered]@{
    Schema = 1; Scope = 'Live generated-control native observations only'
    WriterQualified = $false; PersistentQuiescence = 'Unproven'
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    Streams = @([SectorWorksClfsOracleApi]::Stream($log, 'TxfLog'), [SectorWorksClfsOracleApi]::Stream($log, 'KtmLog'))
    Root = [SectorWorksClfsOracleApi]::FileMetadata($Drive + '\')
    OriginalName = [SectorWorksClfsOracleApi]::FileMetadata($Drive + '\rename-me.txt')
    RenamedName = [SectorWorksClfsOracleApi]::FileMetadata($Drive + '\txf-control-renamed.txt')
}
$result.FinishedUtc = [DateTime]::UtcNow.ToString('o')
$result | ConvertTo-Json -Depth 10
