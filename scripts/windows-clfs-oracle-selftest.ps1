<#
.SYNOPSIS
Standard-user positive test of the CLFS read oracle on a new synthetic log.
.DESCRIPTION
Creates only a fresh user-local, single-use log with known application bytes.
No disk attachment, TxF transaction, existing-log modification, repair/reset,
elevation or product capability change. Retains synthetic files and the report.
Run inside the finite native-process wrapper; never reuse its output root.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This self-test must run as the standard testing account.'
}
$root = Join-Path $env:LOCALAPPDATA 'SectorWorks\CLFS-Oracle-SelfTest-R1'
$current = [IO.Path]::GetFullPath($root)
while ($current) {
    if (Test-Path -LiteralPath $current) {
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') {
            throw 'Linked synthetic-log paths are forbidden.'
        }
    }
    $parent = Split-Path -Parent $current
    if ($parent -eq $current) { break }
    $current = $parent
}
if (Test-Path -LiteralPath $root) { throw 'Single-use synthetic log root exists; retain it and do not retry here.' }
$oracle = Join-Path $PSScriptRoot 'windows-native-clfs-oracle.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($oracle, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Read-oracle source did not parse.' }
# Compile only the literal declarations. Do not execute the owner-only script.
$definitions = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$api'
}, $true))
if ($definitions.Count -ne 1) { throw 'Expected one literal native read-oracle declaration.' }
$api = $definitions[0].Right.Expression.Value
Add-Type -TypeDefinition $api -ErrorAction Stop
if ([SectorWorksClfsOracleApi]::NativeTxfLogPath('Q:') -cne 'Q:\$Extend\$RmMetadata\$TxfLog\$TxfLog') {
    throw 'Native metadata filename regression: preserve every literal dollar sign.'
}
foreach ($invalid in @('', 'q:', 'Q:\', 'Q:other', '\\host\share')) {
    $refused = $false
    try { $null = [SectorWorksClfsOracleApi]::NativeTxfLogPath($invalid) } catch { $refused = $true }
    if (!$refused) { throw 'Invalid native drive prefix was accepted.' }
}
# SDK CLFS_RECORD_TYPE is UCHAR, not the historical third-party enum/ULONG.
foreach ($entry in @(@('ReadLogRecord', 5), @('ReadNextLogRecord', 3))) {
    $method = [SectorWorksClfsOracleApi].GetMethod($entry[0], [Reflection.BindingFlags]'NonPublic, Static')
    if ($method.GetParameters()[$entry[1]].ParameterType -ne [byte].MakeByRefType()) {
        throw 'Native CLFS record-type ABI must be byte-by-reference.'
    }
}
$author = @'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SectorWorksSyntheticClfsApi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WriteEntry { public IntPtr Buffer; public uint ByteLength; }
    [DllImport("clfsw32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateLogFile(string name, uint access, uint share,
        IntPtr attributes, uint disposition, uint flags);
    [DllImport("clfsw32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddLogContainer(IntPtr log, ref ulong size, string path, IntPtr reserved);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateLogMarshallingArea(IntPtr log, IntPtr allocate, IntPtr free,
        IntPtr context, uint size, uint writes, uint reads, out IntPtr marshal);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReserveAndAppendLog(IntPtr marshal, ref WriteEntry entries, uint count,
        IntPtr undo, IntPtr previous, uint reservations, IntPtr reservationSizes,
        uint flags, out ulong lsn, IntPtr overlapped);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteLogRestartArea(IntPtr marshal, IntPtr buffer, uint size,
        IntPtr baseLsn, uint flags, out uint written, out ulong lsn, IntPtr overlapped);
    [DllImport("clfsw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteLogMarshallingArea(IntPtr marshal);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    private static void Check(bool success, string operation)
    {
        if (!success) {
            int code = Marshal.GetLastWin32Error();
            throw new Win32Exception(code, operation + "; Win32=" + code);
        }
    }
    public static object Create(string root)
    {
        string path = Path.Combine(root, "$TxfLog");
        var expected = new Dictionary<string, object>();
        IntPtr first = new IntPtr(-1), second = new IntPtr(-1);
        try {
            first = CreateLogFile("LOG:" + path + "::ProbeA", 0xc0000000, 7, IntPtr.Zero, 1, 0);
            Check(first != new IntPtr(-1), "create fresh synthetic client A");
            for (int i = 0; i < 2; ++i) {
                ulong size = 2097152;
                Check(AddLogContainer(first, ref size, Path.Combine(root, "container" + i), IntPtr.Zero), "add synthetic container");
                if (size != 2097152) throw new InvalidOperationException("Unexpected synthetic container size.");
            }
            second = CreateLogFile("LOG:" + path + "::ProbeB", 0xc0000000, 7, IntPtr.Zero, 1, 0);
            Check(second != new IntPtr(-1), "create fresh synthetic client B");
            int index = 0;
            foreach (IntPtr log in new IntPtr[] {first, second}) {
                string client = index++ == 0 ? "ProbeA" : "ProbeB";
                var records = new List<object>(); expected[client] = records;
                IntPtr marshal = IntPtr.Zero;
                try {
                    Check(CreateLogMarshallingArea(log, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        65536, 1, 2, out marshal), "create synthetic writer marshalling area");
                    for (int number = 1; number <= 4; ++number) {
                        bool restart = number % 2 == 0;
                        string value = "SectorWorks synthetic " + client + " " + (restart ? "restart " : "data ") + number;
                        byte[] bytes = Encoding.UTF8.GetBytes(value);
                        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
                        try {
                            Marshal.Copy(bytes, 0, buffer, bytes.Length); ulong lsn;
                            if (restart) {
                                uint written;
                                Check(WriteLogRestartArea(marshal, buffer, (uint)bytes.Length,
                                    IntPtr.Zero, 0, out written, out lsn, IntPtr.Zero), "write synthetic restart");
                            } else {
                                var entry = new WriteEntry { Buffer = buffer, ByteLength = (uint)bytes.Length };
                                Check(ReserveAndAppendLog(marshal, ref entry, 1, IntPtr.Zero, IntPtr.Zero,
                                    0, IntPtr.Zero, 2, out lsn, IntPtr.Zero), "append/flush synthetic data");
                            }
                            records.Add(new Dictionary<string, object> {
                                {"Lsn", "0x" + lsn.ToString("x16")}, {"PayloadHex", BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()},
                                {"RecordType", restart ? 2 : 1} });
                        } finally { Marshal.FreeHGlobal(buffer); }
                    }
                } finally { if (marshal != IntPtr.Zero) Check(DeleteLogMarshallingArea(marshal), "dispose synthetic writer area"); }
            }
        } finally {
            if (second != new IntPtr(-1)) Check(CloseHandle(second), "close synthetic client B");
            if (first != new IntPtr(-1)) Check(CloseHandle(first), "close synthetic client A");
        }
        return expected;
    }
}
'@
Add-Type -TypeDefinition $author -ErrorAction Stop
New-Item -ItemType Directory -Path $root | Out-Null
$report = [ordered]@{
    Schema = 1; Passed = $false; StandardAccount = $true
    Scope = 'Known-byte synthetic CLFS API self-test, not native TxF qualification'
    OracleSourceSha256 = (Get-FileHash -LiteralPath $oracle -Algorithm SHA256).Hash
    WriterQualified = $false; PersistentQuiescence = 'Unproven'
}
try {
    $expected = [SectorWorksSyntheticClfsApi]::Create($root)
    $report.Expected = $expected
    $observed = [ordered]@{}
    foreach ($client in @('ProbeA', 'ProbeB')) {
        $observation = [SectorWorksClfsOracleApi]::Stream((Join-Path $root '$TxfLog'), $client)
        $observed[$client] = $observation
        if (!$observation['Observed']) { throw ('Synthetic CLFS reader failed: ' + $observation['Error']) }
        $records = @($observation['ForwardRecords']); $wanted = @($expected[$client])
        if ($records.Count -ne 4 -or $wanted.Count -ne 4) { throw 'Synthetic forward record count differs.' }
        for ($i = 0; $i -lt 4; ++$i) {
            foreach ($field in @('Lsn', 'PayloadHex', 'RecordType')) {
                if ($records[$i][$field] -cne $wanted[$i][$field]) { throw ('Synthetic forward record differs: ' + $field) }
            }
        }
        $restarts = @($observation['RestartAreas'])
        if ($restarts.Count -ne 2 -or $restarts[0]['Lsn'] -cne $wanted[3]['Lsn'] -or
            $restarts[1]['Lsn'] -cne $wanted[1]['Lsn'] -or $observation['RestartLsn'] -cne $wanted[3]['Lsn']) {
            throw 'Synthetic restart selection/previous-restart LSNs differ.'
        }
        foreach ($pair in @(@(0, 3), @(1, 1))) {
            if ($restarts[$pair[0]]['PayloadHex'] -cne $wanted[$pair[1]]['PayloadHex']) { throw 'Synthetic restart payload differs.' }
        }
        foreach ($field in @('CloseWin32Error', 'MarshalCleanupWin32Error', 'TerminateWin32Error')) {
            if ($observation.ContainsKey($field)) { throw 'Synthetic native reader cleanup failed.' }
        }
    }
    $report.Observed = $observed
    $report.Passed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    [IO.File]::WriteAllText((Join-Path $root 'selftest.json'), ($report | ConvertTo-Json -Depth 15))
}
$report | ConvertTo-Json -Depth 15
