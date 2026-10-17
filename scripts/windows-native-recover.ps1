# Owner-local finite recovery for the first author attempt only. Never deletes data.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner must launch recovery locally in an administrator terminal.'
}
$root = Join-Path $env:PUBLIC 'SectorWorks-Phase2D-Native'
$image = Join-Path $root 'windows-ordinary-source.vhd'
$report = Join-Path $root 'recovery.json'
foreach ($path in @($root, $image)) {
    $current = [IO.Path]::GetFullPath($path)
    if ($current -notmatch '^[A-Za-z]:\\' -or $current.Substring(3).Contains(':')) { throw 'Expected local test path.' }
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') { throw 'Linked recovery path refused.' }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
if (!(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'Original generated VHD is absent; nothing is detached.' }
if (Test-Path -LiteralPath $report) { throw 'Existing recovery report must not be overwritten.' }
foreach ($entry in @(Get-ChildItem -LiteralPath $root -Force)) {
    if ($entry.Name -notin @('windows-ordinary-source.vhd', 'author-attach.diskpart', 'author-attach.log', 'author-attach.log.stdout', 'author-attach.log.stderr', 'author.json') -or
        $entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $entry.LinkType -eq 'HardLink') {
        throw 'Unexpected original-root entry; recovery refuses to change its attachment or permissions.'
    }
}
$file = Get-Item -LiteralPath $image
if ($file.Length -lt 512 -or $file.Length -gt 160MB) { throw 'Unexpected original generated file size.' }
$images = @(Get-DiskImage -ImagePath $image -ErrorAction Stop)
if ($images.Count -ne 1 -or $images[0].Size -ne 128MB) { throw 'Ambiguous original virtual image identity/size.' }
$wasAttached = $images[0].Attached
if ($wasAttached) {
    $disks = @($images[0] | Get-Disk)
    if ($disks.Count -ne 1 -or $disks[0].IsBoot -or $disks[0].IsSystem -or $disks[0].BusType -ne 'File Backed Virtual' -or $disks[0].Size -ne 128MB) {
        throw 'Refusing to detach a device other than the exact generated virtual disk.'
    }
    Dismount-DiskImage -ImagePath $image -ErrorAction Stop
}
if ((Get-DiskImage -ImagePath $image).Attached) { throw 'Original generated image remains attached.' }
$evidence = [ordered]@{ WasAttached = $wasAttached; Detached = $true; ImageSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash; Evidence = @{} }
foreach ($name in @('author-attach.log', 'author-attach.log.stdout', 'author-attach.log.stderr', 'author.json')) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $item = Get-Item -LiteralPath $path
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 1MB) { throw 'Unsafe evidence path/size.' }
        $evidence.Evidence[$name] = [IO.File]::ReadAllText($path)
    }
}
[IO.File]::WriteAllText($report, ($evidence | ConvertTo-Json -Depth 6))
# Grant read-only access to this generated host test directory/evidence, not to
# any owner image or real disk. Preserve every original file and its contents.
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
Set-Acl -LiteralPath $root -AclObject $acl
foreach ($item in @(Get-ChildItem -LiteralPath $root -Force)) {
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType -eq 'HardLink') { throw 'Unexpected entry: original root ACLs not recursively changed.' }
    $fileAcl = [Security.AccessControl.FileSecurity]::new()
    $fileAcl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)')
    Set-Acl -LiteralPath $item.FullName -AclObject $fileAcl
}
Write-Output 'Original generated image detached; original root/evidence preserved. Retry uses separate Native-R2 root.'
