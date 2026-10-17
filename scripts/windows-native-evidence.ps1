function Read-SectorWorksNativeEvidence {
    param([Parameter(Mandatory = $true)][string] $Path)
    # Windows PowerShell 5.1 Get-Content defaults to the active ANSI code page;
    # evidence is UTF-8 without a BOM, including literal Unicode filenames.
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    return ([IO.File]::ReadAllText($Path, $utf8) | ConvertFrom-Json)
}
