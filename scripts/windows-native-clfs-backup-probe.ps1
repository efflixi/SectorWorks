<#
.SYNOPSIS
Owner-local, single-use R8 protected-log access diagnostic.
.DESCRIPTION
Copies only the sealed generated Native-R2 VHD to a new disposable R8 root.
Compares ordinary read-only metadata-file opens with read-only CLFS client opens.
Requires explicit owner opt-in. A finite isolated child temporarily enables
only its existing SeBackupPrivilege, compares opens, conditionally collects
bounded CLFS read-oracle records and restores/verifies the original token state.
No account privilege grant, SeRestorePrivilege, protected ACL/authentication
change, transaction authoring, replay/reset/repair or arbitrary image paths.
No real disks, automatic elevation, services or schedules. SSH must remain standard.
API access-denied results are findings, not missing observations or writer passes.
#>
[CmdletBinding()]
param(
    [switch] $EnableOwnerBackupRead,
    [ValidatePattern('^[A-Z]:$')][string] $ProbeDrive,
    [ValidatePattern('^[0-9A-Fa-f]{32}$')][string] $ExpectedDiskId
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner must launch this diagnostic locally as administrator; SSH remains standard.'
}
if (!$EnableOwnerBackupRead) { throw 'Explicit owner opt-in -EnableOwnerBackupRead is required; only a bounded child changes its existing token.' }
if ([bool]$ProbeDrive -ne [bool]$ExpectedDiskId) { throw 'Child drive and expected disk identity must occur together.' }
. (Join-Path $PSScriptRoot 'windows-native-process.ps1')
$root = Join-Path $env:PUBLIC 'SectorWorks-Phase2D-TxF-R8-Access'
$seed = Join-Path $env:PUBLIC 'SectorWorks-Phase2D-Native-R2\windows-ordinary-source.vhd'
$image = Join-Path $root 'generated-access.vhd'
$seedHash = 'CE71BDBE0F656AD32744D1BE3639326963422E9D57EC74ACC7952B56B9148313'

function Assert-AuditPlainPath([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    if ($current -notmatch '^[A-Za-z]:\\' -or $current.Substring(3).Contains(':')) { throw 'Only ordinary local paths are allowed.' }
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') {
                throw 'Linked diagnostic paths are forbidden.'
            }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-AuditDisk([string] $Identity) {
    $images = @(Get-DiskImage -ImagePath $image -ErrorAction Stop)
    if ($images.Count -ne 1 -or !$images[0].Attached) { throw 'Expected one attached generated diagnostic image.' }
    $disks = @($images[0] | Get-Disk)
    if ($disks.Count -ne 1) { throw 'Generated disk identity is ambiguous.' }
    $disk = $disks[0]
    if ($disk.IsBoot -or $disk.IsSystem -or $disk.BusType -ne 'File Backed Virtual' -or
        $disk.Size -ne 128MB -or $disk.PartitionStyle -ne 'MBR' -or $disk.IsReadOnly -or
        ($Identity -and $disk.UniqueId -ne $Identity)) {
        throw 'Refusing anything except the exact disposable 128 MiB MBR virtual disk.'
    }
    return $disk
}

function Test-AuditField($Value, [string] $Name) {
    # Native API rows are Dictionary<string,object>; JSON rows are PSCustomObject.
    # PSObject.Properties does not expose native dictionary entries as properties.
    if ($Value -is [Collections.IDictionary]) { return @($Value.Keys) -ccontains $Name }
    return $null -ne $Value.PSObject.Properties[$Name]
}

function Assert-AuditObservation($Observation) {
    if ($Observation.Schema -ne 1 -or $Observation.WriterQualified -ne $false -or
        $Observation.PersistentQuiescence -ne 'Unproven' -or $Observation.Completed -ne $true) {
        throw 'Unexpected access-diagnostic evidence envelope.'
    }
    $rows = @($Observation.Results)
    $expected = @()
    foreach ($target in @('Root', 'Payload', 'Extend', 'RmMetadata', 'TxfLogDirectory', 'BaseLog', 'Container1', 'Container2')) {
        foreach ($mask in @('0x00000080', '0x80000000')) { $expected += 'CreateFileW|' + $target + '|' + $mask }
    }
    foreach ($client in @('TxfLog', 'KtmLog')) { $expected += 'CreateLogFile|' + $client + '|0x80000000' }
    $actual = @($rows | ForEach-Object { $_.Operation + '|' + $_.Target + '|' + $_.DesiredAccess })
    if ($rows.Count -ne 18 -or @($actual | Select-Object -Unique).Count -ne 18 -or
        @(Compare-Object $expected $actual).Count -ne 0) { throw 'Missing, duplicate or unexpected diagnostic operations.' }
    foreach ($row in $rows) {
        if ($row.Attempted -ne $true -or $row.Succeeded -isnot [bool] -or (Test-AuditField $row 'CloseWin32Error')) {
            throw 'Incomplete operation or failed native-handle cleanup.'
        }
        if (!$row.Succeeded -and !(Test-AuditField $row 'Win32Error')) { throw 'Failed operation lacks its native error.' }
    }
    $positive = @($rows | Where-Object { $_.Operation -eq 'CreateFileW' -and $_.Target -eq 'Payload' })
    if ($positive.Count -ne 2 -or @($positive | Where-Object Succeeded -eq $true).Count -ne 2) {
        throw 'Ordinary generated-file positive control failed; retain evidence.'
    }
}

# Public API declarations; no adjusting token privileges or metadata-writing API.
$api = @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SectorWorksClfsAccessAuditApi
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share,
        IntPtr attributes, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(IntPtr file, [Out] byte[] information);
    [DllImport("clfsw32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateLogFile(string name, uint access, uint share,
        IntPtr attributes, uint disposition, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, uint type, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string system, string name, out long luid);
    private static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public static string[] Targets(string drive)
    {
        if (drive == null || drive.Length != 2 || drive[0] < 'A' || drive[0] > 'Z' || drive[1] != ':')
            throw new ArgumentException("Expected one uppercase drive letter.");
        string log = drive + @"\$Extend\$RmMetadata\$TxfLog\";
        return new string[] { drive + @"\", drive + @"\rename-me.txt", drive + @"\$Extend",
            drive + @"\$Extend\$RmMetadata", log.TrimEnd('\\'), log + "$TxfLog.blf",
            log + "$TxfLogContainer00000000000000000001", log + "$TxfLogContainer00000000000000000002" };
    }
    private static Dictionary<string, object> Row(string operation, string target, uint access)
    {
        return new Dictionary<string, object> { {"Operation", operation}, {"Target", target},
            {"DesiredAccess", "0x" + access.ToString("x8")}, {"ShareMode", 7},
            {"Disposition", "OPEN_EXISTING"}, {"Attempted", true}, {"Succeeded", false} };
    }
    private static void Failure(Dictionary<string, object> row, string operation)
    {
        int error = Marshal.GetLastWin32Error();
        row["FailedOperation"] = operation; row["Win32Error"] = error;
        row["Error"] = new Win32Exception(error).Message;
    }
    private static void Close(IntPtr handle, Dictionary<string, object> row)
    {
        if (handle != new IntPtr(-1) && !CloseHandle(handle)) {
            row["CloseWin32Error"] = Marshal.GetLastWin32Error(); row["Succeeded"] = false;
        }
    }
    public static object FileProbe(string path, string target, uint access, bool directory)
    {
        if (access != 0x80 && access != 0x80000000) throw new ArgumentException("Read-only access masks required.");
        var row = Row("CreateFileW", target, access);
        // BACKUP_SEMANTICS permits directory handles; no privilege is enabled.
        IntPtr file = CreateFileW(path, access, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (file == new IntPtr(-1)) { Failure(row, "CreateFileW"); return row; }
        try {
            row["OpenSucceeded"] = true;
            byte[] info = new byte[52];
            if (!GetFileInformationByHandle(file, info)) { Failure(row, "GetFileInformationByHandle"); return row; }
            uint attributes = BitConverter.ToUInt32(info, 0);
            if ((attributes & 0x400) != 0 || ((attributes & 0x10) != 0) != directory)
                throw new InvalidOperationException("Opened target has unexpected reparse/type attributes.");
            row["Attributes"] = "0x" + attributes.ToString("x8");
            row["VolumeSerial"] = BitConverter.ToUInt32(info, 28).ToString("x8");
            row["FileIndex"] = BitConverter.ToUInt32(info, 44).ToString("x8") + BitConverter.ToUInt32(info, 48).ToString("x8");
            row["Succeeded"] = true;
            return row;
        } finally { Close(file, row); }
    }
    public static object LogProbe(string path, string client)
    {
        if (client != "TxfLog" && client != "KtmLog" && client != "ProbeA" && client != "ProbeB")
            throw new ArgumentException("Unknown diagnostic client.");
        var row = Row("CreateLogFile", client, 0x80000000);
        IntPtr log = CreateLogFile("LOG:" + path + "::" + client, 0x80000000, 7, IntPtr.Zero, 3, 0);
        if (log == new IntPtr(-1)) { Failure(row, "CreateLogFile"); return row; }
        try { row["Succeeded"] = true; return row; }
        finally { Close(log, row); }
    }
    public static object TokenPrivileges()
    {
        IntPtr token; Check(OpenProcessToken(GetCurrentProcess(), 8, out token)); // TOKEN_QUERY only.
        try {
            uint needed; GetTokenInformation(token, 3, IntPtr.Zero, 0, out needed);
            if (Marshal.GetLastWin32Error() != 122 || needed < 4 || needed > 65536)
                throw new InvalidOperationException("Unexpected token privilege buffer bound.");
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try {
                uint returned; Check(GetTokenInformation(token, 3, buffer, needed, out returned));
                uint count = (uint)Marshal.ReadInt32(buffer);
                if (returned > needed || count > 512 || 4UL + 12UL * count > returned)
                    throw new InvalidOperationException("Token privilege layout exceeds bounds.");
                var rows = new List<object>();
                foreach (string name in new string[] { "SeBackupPrivilege", "SeRestorePrivilege" }) {
                    long target; Check(LookupPrivilegeValueW(null, name, out target));
                    bool present = false, enabled = false;
                    for (uint i = 0; i < count; ++i) {
                        int offset = checked(4 + (int)i * 12);
                        if (Marshal.ReadInt64(buffer, offset) == target) {
                            present = true; enabled = ((uint)Marshal.ReadInt32(buffer, offset + 8) & 2) != 0;
                        }
                    }
                    rows.Add(new Dictionary<string, object> { {"Name", name}, {"Present", present}, {"Enabled", enabled} });
                }
                return rows;
            } finally { Marshal.FreeHGlobal(buffer); }
        } finally { Check(CloseHandle(token)); }
    }
}
'@

$scopeHelper = Join-Path $PSScriptRoot 'windows-owner-backup-read-scope.ps1'
$oracleHelper = Join-Path $PSScriptRoot 'windows-native-clfs-oracle.ps1'
foreach ($path in @($root, $seed, $image, $PSCommandPath, $scopeHelper, $oracleHelper, (Join-Path $PSScriptRoot 'windows-native-process.ps1'))) {
    Assert-AuditPlainPath $path
}
if ((Get-FileHash -LiteralPath $scopeHelper -Algorithm SHA256).Hash -ne 'A55E17BCA64146B22B1C5F6A2F34678C9989117885BCEE9F00C6B130CE843F16' -or
    (Get-FileHash -LiteralPath $oracleHelper -Algorithm SHA256).Hash -ne '46357D04A287230656858920ECF2E422D85BE15E82D7DF95FD61AF22AED5DDA5') { throw 'Exact reviewed backup/read-oracle helpers required.' }
if ($ProbeDrive) {
    if (!(Test-Path -LiteralPath (Join-Path $root 'attach.diskpart') -PathType Leaf) -or
        (Test-Path -LiteralPath (Join-Path $root 'access-probe.json'))) { throw 'Child requires the fresh parent diagnostic state.' }
    $disk = Get-AuditDisk $ExpectedDiskId
    $parts = @(Get-Partition -DriveLetter $ProbeDrive.Substring(0, 1))
    if ($parts.Count -ne 1 -or $parts[0].DiskNumber -ne $disk.Number -or
        $parts[0].Offset -ne 65536 -or $parts[0].Size -ne 133103616) { throw 'Probe drive differs from the exact generated partition.' }
    Add-Type -TypeDefinition $api -ErrorAction Stop
    . $scopeHelper # Compilation only. No privilege changes until constructor below.
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($oracleHelper, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw 'Read-oracle declaration parse failed.' }
    $definitions = @($ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$api'
    }, $true))
    if ($definitions.Count -ne 1) { throw 'Expected one literal read-oracle declaration.' }
    Add-Type -TypeDefinition $definitions[0].Right.Expression.Value -ErrorAction Stop
    $paths = [SectorWorksClfsAccessAuditApi]::Targets($ProbeDrive)
    $names = @('Root', 'Payload', 'Extend', 'RmMetadata', 'TxfLogDirectory', 'BaseLog', 'Container1', 'Container2')
    function Invoke-R8Observation {
        $results = @()
        for ($index = 0; $index -lt $paths.Length; ++$index) {
            $directory = $index -in @(0, 2, 3, 4)
            foreach ($mask in @([uint32]0x80, [uint32]2147483648)) {
                $results += [SectorWorksClfsAccessAuditApi]::FileProbe($paths[$index], $names[$index], $mask, $directory)
            }
        }
        $log = $paths[5].Substring(0, $paths[5].Length - 4)
        foreach ($client in @('TxfLog', 'KtmLog')) { $results += [SectorWorksClfsAccessAuditApi]::LogProbe($log, $client) }
        return [pscustomobject]@{ Schema = 1; WriterQualified = $false; PersistentQuiescence = 'Unproven'; Completed = $true; Results = $results }
    }
    $tokenBefore = [SectorWorksClfsAccessAuditApi]::TokenPrivileges() | ConvertTo-Json -Compress
    $before = Invoke-R8Observation
    # Persist the exact native dictionary results BEFORE validation. A checker
    # failure must retain baseline evidence rather than only an outer error.
    [IO.File]::WriteAllText((Join-Path $root 'before-observation.json'), ($before | ConvertTo-Json -Depth 8))
    Assert-AuditObservation $before
    $scope = $null
    $withBackup = $null
    $oracles = @()
    $oracleReason = 'Not attempted because both CLFS client opens did not succeed.'
    try {
        $scope = [SectorWorksOwnerBackupReadScope]::new()
        $tokenEnabled = [SectorWorksClfsAccessAuditApi]::TokenPrivileges()
        $withBackup = Invoke-R8Observation
        Assert-AuditObservation $withBackup
        $opens = @($withBackup.Results | Where-Object { $_.Operation -eq 'CreateLogFile' -and $_.Succeeded })
        if ($opens.Count -eq 2) {
            # Existing byte-verified bounded reader; no reset/replay/repair/write retry.
            $log = $paths[5].Substring(0, $paths[5].Length - 4)
            foreach ($client in @('TxfLog', 'KtmLog')) { $oracles += [SectorWorksClfsOracleApi]::Stream($log, $client) }
            $oracleReason = 'Both clients attempted; inspect inner records/errors/terminal codes and cleanup.'
        }
    } finally {
        try { if ($scope) { $scope.Dispose() } }
        finally {
            # Save partial results even if validation/restore fails. Restored is
            # an explicit state, not inferred from diagnostic process exit.
            $restored = $false
            if ($scope) { $restored = $scope.Restored }
            $partial = [ordered]@{ WithBackup = $withBackup; Oracles = $oracles; BackupPrivilegeRestored = $restored }
            [IO.File]::WriteAllText((Join-Path $root 'with-backup-observation.json'), ($partial | ConvertTo-Json -Depth 12))
        }
    }
    $tokenAfter = [SectorWorksClfsAccessAuditApi]::TokenPrivileges() | ConvertTo-Json -Compress
    if (!$scope.Restored -or $tokenBefore -cne $tokenAfter) { throw 'Original privilege state was not verified after probing.' }
    [ordered]@{
        Schema = 2; Scope = 'Owner-only fresh generated-volume backup-read candidate'
        Completed = $true; WriterQualified = $false; PersistentQuiescence = 'Unproven'
        Before = $before; WithBackup = $withBackup; TokenBefore = ($tokenBefore | ConvertFrom-Json)
        TokenEnabled = $tokenEnabled; TokenAfter = ($tokenAfter | ConvertFrom-Json)
        BackupPrivilegeRestored = $true; TokenStateUnchanged = $true
        OracleReason = $oracleReason; Oracles = $oracles
    } | ConvertTo-Json -Depth 12
    return
}

$ownedRoot = $false
$attachmentAttempted = $false
$report = [ordered]@{
    Schema = 1; Revision = 'R8'; Scope = 'Access diagnosis, not recovery or writer qualification'
    StartedUtc = [DateTime]::UtcNow.ToString('o'); Completed = $false
    WriterQualified = $false; PersistentQuiescence = 'Unproven'
}
try {
    if (Test-Path -LiteralPath $root) { throw 'Single-use R8 root exists; retain evidence and do not rerun.' }
    if (!(Test-Path -LiteralPath $seed -PathType Leaf) -or (Get-Item -LiteralPath $seed).Length -ne 12615680 -or
        (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash -ne $seedHash) { throw 'Exact sealed generated seed is required.' }
    if ((Get-DiskImage -ImagePath $seed -ErrorAction Stop).Attached) { throw 'Seed must remain detached.' }
    New-Item -ItemType Directory -Path $root | Out-Null
    $ownedRoot = $true
    # Set only the NEW evidence directory ACL; never change any volume/seed ACL.
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
    Set-Acl -LiteralPath $root -AclObject $acl
    [IO.File]::Copy($seed, $image, $false)
    if ((Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash -ne $seedHash) { throw 'Fresh copy hash mismatch.' }
    $report.SeedSha256 = $seedHash
    $report.ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    $report.ProcessHelperSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'windows-native-process.ps1') -Algorithm SHA256).Hash
    $report.PowerShell = $PSVersionTable.PSVersion.ToString()
    $report.AuthoringOs = [Environment]::OSVersion.VersionString
    $attach = Join-Path $root 'attach.diskpart'
    [IO.File]::WriteAllLines($attach, @("select vdisk file=`"$image`"", 'attach vdisk', 'exit'), [Text.Encoding]::ASCII)
    $attachmentAttempted = $true
    $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot 'System32\diskpart.exe') ("/s `"$attach`"") 30000
    [IO.File]::WriteAllText((Join-Path $root 'attach.json'), ($result | ConvertTo-Json -Depth 4))
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw 'Generated-image attachment failed; retain evidence.' }
    $disk = Get-AuditDisk ''
    $diskId = $disk.UniqueId
    if ($diskId -notmatch '^[0-9A-Fa-f]{32}$') { throw 'Unexpected generated disk identity encoding.' }
    $parts = @($disk | Get-Partition | Where-Object Type -ne 'Reserved')
    if ($parts.Count -ne 1 -or $parts[0].Offset -ne 65536 -or $parts[0].Size -ne 133103616) { throw 'Unexpected generated partition layout.' }
    $part = $parts[0]
    if (!$part.DriveLetter) {
        $part | Add-PartitionAccessPath -AssignDriveLetter
        $part = Get-Partition -DiskNumber $disk.Number -PartitionNumber $part.PartitionNumber
    }
    if (!$part.DriveLetter) { throw 'Generated partition lacks a drive letter.' }
    $drive = "$($part.DriveLetter):"
    $command = "& '" + $PSCommandPath.Replace("'", "''") + "' -ProbeDrive '" + $drive + "' -ExpectedDiskId '" + $diskId + "' -EnableOwnerBackupRead"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
        ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' + $encoded) 45000
    [IO.File]::WriteAllText((Join-Path $root 'access-probe.json'), ($result | ConvertTo-Json -Depth 4))
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw 'Access-probe child failed; retain its exact report.' }
    $observation = $result.Stdout | ConvertFrom-Json
    if ($observation.Schema -ne 2 -or $observation.BackupPrivilegeRestored -ne $true -or $observation.TokenStateUnchanged -ne $true) { throw 'R8 token restoration/envelope incomplete.' }
    Assert-AuditObservation $observation.Before
    Assert-AuditObservation $observation.WithBackup
    Get-AuditDisk $diskId | Out-Null
    $report.OperationsRecorded = (@($observation.Before.Results).Count + @($observation.WithBackup.Results).Count)
    $report.OperationsSucceeded = @($observation.WithBackup.Results | Where-Object Succeeded -eq $true).Count
    $report.ClfsOpensSucceeded = @($observation.WithBackup.Results | Where-Object { $_.Operation -eq 'CreateLogFile' -and $_.Succeeded }).Count
    $report.BackupPrivilegeRestored = $true
    $report.OracleStreamsObserved = @($observation.Oracles | Where-Object { $_.Observed -eq $true }).Count
    $report.ScopeHelperSha256 = (Get-FileHash -LiteralPath $scopeHelper -Algorithm SHA256).Hash
    $report.OracleHelperSha256 = (Get-FileHash -LiteralPath $oracleHelper -Algorithm SHA256).Hash
    $report.Completed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    if ($attachmentAttempted) {
        try {
            if ((Get-DiskImage -ImagePath $image -ErrorAction Stop).Attached) {
                $detach = Join-Path $root 'detach.diskpart'
                [IO.File]::WriteAllLines($detach, @("select vdisk file=`"$image`"", 'detach vdisk', 'exit'), [Text.Encoding]::ASCII)
                $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot 'System32\diskpart.exe') ("/s `"$detach`"") 30000
                [IO.File]::WriteAllText((Join-Path $root 'detach.json'), ($result | ConvertTo-Json -Depth 4))
                if ($result.TimedOut -or $result.ExitCode -ne 0) { throw 'Finite generated-image detach failed.' }
            }
            if ((Get-DiskImage -ImagePath $image).Attached) { throw 'Generated diagnostic image remains attached.' }
            $report.DetachedImageSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
        } catch { $report.Completed = $false; $report.DetachError = $_.Exception.Message }
    }
    if ($ownedRoot) {
        try {
            $report.SeedAfterSha256 = (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash
            if ($report.SeedAfterSha256 -ne $seedHash) { throw 'Immutable generated seed changed.' }
        } catch { $report.Completed = $false; $report.SeedIntegrityError = $_.Exception.Message }
        $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
        $path = Join-Path $root 'access-audit.json'
        if (Test-Path -LiteralPath $path) { throw 'Refusing existing audit report.' }
        [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 6))
    }
}
if (!$report.Completed) { throw 'Diagnostic cleanup/integrity failed; inspect retained reports.' }
Write-Output 'R8 backup-read candidate observations recorded, token state restored and generated image detached. Inspect inner operation results; this is not successful protected-log access or writer qualification.'
