<#
.SYNOPSIS
Owner-launched native checks for one disposable Windows-authored NTFS VHD.
.DESCRIPTION
Independent interoperability infrastructure, never product image access.
Review this script and launch locally from an administrator terminal. No
elevation, service, scheduled task or arbitrary command/disk argument exists.
Author requires an absent PUBLIC\SectorWorks-Phase2D-Native-R2 directory.
ReopenSource verifies the immutable authored image. ReopenOutput checks only
windows-ordinary-output.vhd, placed there by the owner from generated Save As.
Output contract: add added.txt ("SectorWorks staged addition.\n"), replace
resident.txt ("SectorWorks staged replacement.\n"), delete delete-me.txt,
rename rename-me.txt to renamed.txt, and create directory Created.
All other enumerated namespace, content and retained metadata must agree.
Native service-owned System Volume Information/$RECYCLE.BIN are not traversed,
ACL-normalized or deleted. No log reset, replay, repair or chkdsk fix occurs.
Every mode is single-use: existing evidence logs are never overwritten.
An authoring failure leaves evidence for inspection; do not delete/reuse it
automatically. Native log/allocation/health interpretation still requires review.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Author', 'ReopenSource', 'ReopenSourceRetry', 'ReopenOutput')]
    [string] $Mode
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'windows-native-process.ps1')
. (Join-Path $PSScriptRoot 'windows-native-evidence.ps1')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner must launch locally in an administrator terminal; no automatic elevation is performed.'
}
$root = Join-Path $env:PUBLIC 'SectorWorks-Phase2D-Native-R2'
$source = Join-Path $root 'windows-ordinary-source.vhd'
$output = Join-Path $root 'windows-ordinary-output.vhd'
$state = Join-Path $root 'author.json'
$image = if ($Mode -eq 'ReopenOutput') { $output } else { $source }
$size = 128MB
$attached = $false
$ownedRoot = $false
$diskIdentity = $null
$report = [ordered]@{ Mode = $Mode; StartedUtc = [DateTime]::UtcNow.ToString('o'); Passed = $false }

function Assert-PlainPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^[A-Za-z]:\\' -or $full.Substring(3).Contains(':')) {
        throw 'Only ordinary local filesystem paths are permitted.'
    }
    $current = $full
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked paths are forbidden.' }
            if ($item.LinkType -eq 'HardLink') { throw 'Hard-linked paths are forbidden.' }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Invoke-Native([string] $Name, [string[]] $Arguments, [string] $LogName) {
    $log = Join-Path $root $LogName
    if (Test-Path -LiteralPath $log) { throw 'Refusing existing native evidence log.' }
    $stdout = $log + '.stdout'
    $stderr = $log + '.stderr'
    foreach ($path in @($stdout, $stderr)) {
        if (Test-Path -LiteralPath $path) { throw 'Refusing existing process output.' }
    }
    $result = Invoke-SectorWorksNativeProcess (Join-Path $env:SystemRoot "System32\$Name") ($Arguments -join ' ')
    $code = $result.ExitCode
    [IO.File]::WriteAllText($stdout, $result.Stdout)
    [IO.File]::WriteAllText($stderr, $result.Stderr)
    [IO.File]::WriteAllText($log, "ExitCode=$code`r`nTimedOut=$($result.TimedOut)`r`n" + $result.Stdout + $result.Stderr)
    if ($result.TimedOut) { throw "Native command deadline exceeded: $Name (termination exit $code)" }
    if ($code -ne 0) { throw "$Name returned $code; inspect exact native log." }
}

function Invoke-DiskPart([string[]] $Commands, [string] $Name) {
    $path = Join-Path $root "$Name.diskpart"
    if (Test-Path -LiteralPath $path) { throw 'Refusing existing diskpart input.' }
    [IO.File]::WriteAllLines($path, $Commands, [Text.Encoding]::ASCII)
    Invoke-Native 'diskpart.exe' @('/s', "`"$path`"") "$Name.log"
}

function Get-OwnedDisk {
    $images = @(Get-DiskImage -ImagePath $image -ErrorAction Stop)
    if ($images.Count -ne 1 -or !$images[0].Attached) { throw 'Image attachment identity is ambiguous.' }
    $disks = @($images[0] | Get-Disk)
    if ($disks.Count -ne 1) { throw 'Image does not identify exactly one disk.' }
    $d = $disks[0]
    if ($d.IsBoot -or $d.IsSystem -or $d.BusType -ne 'File Backed Virtual' -or $d.Size -ne $size) {
        throw 'Refusing disk: not the exact disposable virtual device.'
    }
    if ($script:diskIdentity -and $d.UniqueId -ne $script:diskIdentity) { throw 'Virtual disk identity changed.' }
    return $d
}

function Set-BasicAcl([string] $Path, [bool] $Directory) {
    # Deliberately basic image ACL: no host-specific SID, conditional/object ACE or SACL.
    $acl = if ($Directory) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
    $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;BU)')
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-TestEntries([string] $VolumeRoot) {
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($VolumeRoot)
    $count = 0
    while ($queue.Count) {
        $directory = $queue.Dequeue()
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            # Native service-owned metadata is preserved, not traversed or ACL-normalized.
            if ($directory -eq $VolumeRoot -and $entry.Name -in @('System Volume Information', '$RECYCLE.BIN')) { continue }
            if (++$count -gt 128) { throw 'Disposable fixture namespace budget exceeded.' }
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected linked fixture entry.' }
            $entry
            if ($entry.PSIsContainer) { $queue.Enqueue($entry.FullName) }
        }
    }
}

try {
    Assert-PlainPath $root
    if ($Mode -eq 'Author') {
        if (Test-Path -LiteralPath $root) { throw 'Author requires an absent dedicated test root; never reuse or delete existing data.' }
        New-Item -ItemType Directory -Path $root | Out-Null
        $ownedRoot = $true
        $hostAcl = [Security.AccessControl.DirectorySecurity]::new()
        $hostAcl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
        Set-Acl -LiteralPath $root -AclObject $hostAcl
        # A failed native command may already have attached this newly created
        # image. The finally block must inspect it even if diskpart fails.
        $attached = $true
        Invoke-DiskPart @("create vdisk file=`"$source`" maximum=128 type=expandable", "select vdisk file=`"$source`"", 'attach vdisk', 'exit') 'author-attach'
        $disk = Get-OwnedDisk
        if ($disk.PartitionStyle -ne 'RAW') { throw 'New virtual disk was not blank.' }
        $disk | Initialize-Disk -PartitionStyle MBR -PassThru | Out-Null
        $disk = Get-OwnedDisk
        $diskIdentity = $disk.UniqueId
        $part = $disk | New-Partition -UseMaximumSize -AssignDriveLetter
        if ($part.DiskNumber -ne $disk.Number -or !$part.DriveLetter) { throw 'Partition identity mismatch.' }
        $volume = $part | Format-Volume -FileSystem NTFS -AllocationUnitSize 4096 -NewFileSystemLabel 'SW2D_NATIVE' -Confirm:$false
        $drive = "$($part.DriveLetter):"
        $volumeRoot = "$drive\"
        Set-BasicAcl $volumeRoot $true
        foreach ($dir in @('Nested', 'Nested\Child')) {
            New-Item -ItemType Directory -Path (Join-Path $volumeRoot $dir) | Out-Null
        }
        $small = [Text.Encoding]::UTF8.GetBytes("SectorWorks legal synthetic Windows native fixture.`n")
        [IO.File]::WriteAllBytes((Join-Path $volumeRoot 'resident.txt'), $small)
        [IO.File]::WriteAllBytes((Join-Path $volumeRoot 'delete-me.txt'), $small)
        [IO.File]::WriteAllBytes((Join-Path $volumeRoot 'rename-me.txt'), $small)
        $large = [byte[]]::new(1MB)
        for ($i = 0; $i -lt $large.Length; $i++) { $large[$i] = [byte](($i * 31 + 7) % 251) }
        [IO.File]::WriteAllBytes((Join-Path $volumeRoot 'Nested\Child\nonresident.bin'), $large)
        $unicode = 'unicode-' + [char]0x00e9 + '-' + [char]0x03a9 + '.txt'
        [IO.File]::WriteAllBytes((Join-Path $volumeRoot $unicode), $small)
        foreach ($entry in @(Get-TestEntries $volumeRoot)) {
            Set-BasicAcl $entry.FullName $entry.PSIsContainer
            $entry.CreationTimeUtc = [DateTime]'2026-10-17T00:00:00Z'
            $entry.LastWriteTimeUtc = [DateTime]'2026-10-17T00:00:00Z'
        }
        $report.AuthoringOs = [Environment]::OSVersion.VersionString
        $report.PowerShell = $PSVersionTable.PSVersion.ToString()
        $report.DiskUniqueId = $diskIdentity
        $report.PartitionOffset = $part.Offset
        $report.PartitionSize = $part.Size
        $report.SecuritySubset = 'Explicit protected basic allow ACLs for SYSTEM, Administrators and Users; owner/group Administrators. Not generic Windows default ACL qualification.'
    } else {
        foreach ($path in @($root, $image, $state)) { Assert-PlainPath $path }
        if (!(Test-Path -LiteralPath $state -PathType Leaf)) { throw 'Missing author provenance.' }
        $author = Read-SectorWorksNativeEvidence $state
        if (!$author.Passed -or $author.Mode -ne 'Author') { throw 'Authoring did not complete successfully.' }
        $ownedRoot = $true
        if (!(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'Expected explicitly named generated VHD is absent.' }
        $imageLength = (Get-Item -LiteralPath $image).Length
        if ($imageLength -lt 512 -or $imageLength -gt 160MB) { throw 'Generated VHD file-size budget exceeded.' }
        if ((Get-DiskImage -ImagePath $image -ErrorAction Stop).Attached) { throw 'Refusing a pre-existing image attachment.' }
        $report.BeforeSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
        if ($Mode -like 'ReopenSource*' -and $report.BeforeSha256 -ne $author.ImageSha256) { throw 'Immutable source hash changed.' }
        $diskIdentity = $author.DiskUniqueId
        $attached = $true
        Invoke-DiskPart @("select vdisk file=`"$image`"", 'attach vdisk readonly', 'exit') "$Mode-attach"
        $disk = Get-OwnedDisk
        $parts = @($disk | Get-Partition | Where-Object Type -ne 'Reserved')
        if ($parts.Count -ne 1 -or $parts[0].Offset -ne $author.PartitionOffset -or $parts[0].Size -ne $author.PartitionSize) { throw 'Partition identity/layout mismatch.' }
        $part = $parts[0]
        if (!$disk.IsReadOnly) { throw 'Reopen must be read-only.' }
        if (!$part.DriveLetter) {
            $part | Add-PartitionAccessPath -AssignDriveLetter
            $part = Get-Partition -DiskNumber $disk.Number -PartitionNumber $part.PartitionNumber
        }
        if (!$part.DriveLetter) { throw 'No unique generated-volume drive letter.' }
        $drive = "$($part.DriveLetter):"
        $volumeRoot = "$drive\"
    }
    $disk = Get-OwnedDisk
    $report.EnumerationExclusions = @('System Volume Information', '$RECYCLE.BIN')
    $report.Files = @(Get-TestEntries $volumeRoot | ForEach-Object {
        if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected reparse point on disposable fixture.' }
        [ordered]@{
            Path = $_.FullName.Substring($volumeRoot.Length)
            Directory = $_.PSIsContainer
            Length = if ($_.PSIsContainer) { 0 } else { $_.Length }
            Sha256 = if ($_.PSIsContainer) { $null } else { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            CreationUtc = $_.CreationTimeUtc.ToString('o')
            ModifiedUtc = $_.LastWriteTimeUtc.ToString('o')
            Attributes = $_.Attributes.ToString()
            Sddl = (Get-Acl -LiteralPath $_.FullName).Sddl
        }
    })
    $report.RootSddl = (Get-Acl -LiteralPath $volumeRoot).Sddl
    if ($Mode -ne 'Author') {
        $expected = @{}
        foreach ($entry in $author.Files) { $expected[$entry.Path] = $entry }
        if ($Mode -eq 'ReopenOutput') {
            $expected.Remove('delete-me.txt')
            $renamed = $expected['rename-me.txt']
            $expected.Remove('rename-me.txt')
            $expected['renamed.txt'] = $renamed
            $expected['Created'] = @{ Directory = $true; Length = 0; Sha256 = $null }
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $replacementHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("SectorWorks staged replacement.`n")))).Replace('-', '')
                $addedHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("SectorWorks staged addition.`n")))).Replace('-', '')
            } finally { $sha.Dispose() }
            $expected['resident.txt'] = @{ Directory = $false; Length = 32; Sha256 = $replacementHash }
            $expected['added.txt'] = @{ Directory = $false; Length = 29; Sha256 = $addedHash }
        }
        if ($report.Files.Count -ne $expected.Count) { throw 'Native reopened namespace count differs from required operation contract.' }
        foreach ($entry in $report.Files) {
            if (!$expected.ContainsKey($entry.Path)) { throw 'Native reopened namespace differs from operation contract.' }
            $wanted = $expected[$entry.Path]
            if ($entry.Directory -ne $wanted.Directory -or $entry.Length -ne $wanted.Length -or $entry.Sha256 -ne $wanted.Sha256) {
                throw 'Native reopened file content/type differs from operation contract.'
            }
            if ($wanted -isnot [Collections.IDictionary]) {
                if ($entry.CreationUtc -ne $wanted.CreationUtc -or $entry.ModifiedUtc -ne $wanted.ModifiedUtc -or $entry.Sddl -ne $wanted.Sddl -or $entry.Attributes -ne $wanted.Attributes) {
                    throw 'Native reopened retained metadata differs.'
                }
            }
        }
    }
    Invoke-Native 'fsutil.exe' @('fsinfo', 'ntfsinfo', $drive) "$Mode-ntfsinfo.log"
    Invoke-Native 'fsutil.exe' @('dirty', 'query', $drive) "$Mode-dirty.log"
    # No /f, /r, /x, log reset or repair switches are ever accepted.
    Invoke-Native 'chkdsk.exe' @($drive) "$Mode-chkdsk.log"
    $report.Passed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    if ($attached -and (Test-Path -LiteralPath $image -PathType Leaf)) {
        # Detach only the exact file attached by this invocation, never a disk number or host volume.
        try {
            if ((Get-DiskImage -ImagePath $image -ErrorAction Stop).Attached) {
                Dismount-DiskImage -ImagePath $image -ErrorAction Stop
            }
            $attached = $false
            if ((Get-DiskImage -ImagePath $image).Attached) { throw 'Generated image did not detach cleanly.' }
            $report.ImageSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
            if ($Mode -ne 'Author' -and $report.BeforeSha256 -ne $report.ImageSha256) {
                $report.Passed = $false
                $report.Error = 'Read-only native reopen changed image bytes.'
            }
        } catch {
            $report.Passed = $false
            $report.CleanupError = $_.Exception.Message
        }
    }
    if ($ownedRoot -and (Test-Path -LiteralPath $root -PathType Container)) {
        $result = if ($Mode -eq 'Author') { $state } else { Join-Path $root "$Mode.json" }
        if (Test-Path -LiteralPath $result) { throw 'Refusing existing evidence report.' }
        [IO.File]::WriteAllText($result, ($report | ConvertTo-Json -Depth 8))
    }
}
if (!$report.Passed) { throw 'Native qualification failed; inspect retained evidence.' }
Write-Output "Native $Mode completed. Generated VHD detached; evidence retained in dedicated test root."
