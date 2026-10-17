[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'windows-native-evidence.ps1')
$directory = Join-Path $env:LOCALAPPDATA 'SectorWorks-Phase2D'
if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
$path = Join-Path $directory ('unicode-evidence-' + [Guid]::NewGuid().ToString('N') + '.json')
$roundtrip = Join-Path $directory ('unicode-roundtrip-' + [Guid]::NewGuid().ToString('N') + '.json')
$name = 'unicode-' + [char]0x00e9 + '-' + [char]0x03a9 + '.txt'
$key = 'metadata-' + [char]0x00e9 + '-' + [char]0x03a9
$metadata = [ordered]@{
    Path = $name
    Length = 52
    Directory = $false
    Sha256 = ('A1' * 32)
    CreationUtc = '2026-10-17T00:00:00.0000000Z'
    ModifiedUtc = '2026-10-17T01:02:03.0000000Z'
    Attributes = 'Archive'
    Sddl = 'O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;BU)'
}
$properties = @{}
$properties[$key] = $name
try {
    $json = @{ Files = @($metadata); Properties = $properties } | ConvertTo-Json -Depth 5
    # Use the exact writer overload used by the native harness.
    [IO.File]::WriteAllText($path, $json)
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) {
        throw 'Regression requires the harness UTF-8-without-BOM writer.'
    }
    foreach ($evidencePath in @($path, $roundtrip)) {
        $loaded = Read-SectorWorksNativeEvidence $evidencePath
        if ($loaded.Files.Count -ne 1) { throw 'Namespace count changed.' }
        foreach ($field in $metadata.Keys) {
            if ($loaded.Files[0].$field -cne $metadata[$field]) { throw "Round-trip metadata changed: $field" }
        }
        $property = $loaded.Properties.PSObject.Properties[$key]
        if (!$property -or ![string]::Equals($property.Name, $key, [StringComparison]::Ordinal) -or
            ![string]::Equals($property.Value, $name, [StringComparison]::Ordinal)) {
            throw 'Non-ASCII JSON property key/value changed.'
        }
        $expected = @{}
        $expected[$loaded.Files[0].Path] = $loaded.Files[0]
        if (!$expected.ContainsKey($name) -or $expected.Count -ne 1) { throw 'Unicode namespace lookup failed.' }
        if ($expected.ContainsKey($name + '.unexpected')) { throw 'Unexpected namespace was accepted.' }
        if ($evidencePath -eq $path) {
            [IO.File]::WriteAllText($roundtrip, ($loaded | ConvertTo-Json -Depth 5))
        }
    }
    # Invalid encoding must fail, not normalize bytes into a replacement name.
    [IO.File]::WriteAllBytes($roundtrip, [byte[]]@(0xc3, 0x28))
    try {
        $null = Read-SectorWorksNativeEvidence $roundtrip
        throw 'Malformed UTF-8 was accepted.'
    } catch [Text.DecoderFallbackException] {
    } catch [Management.Automation.MethodInvocationException] {
        if ($_.Exception.InnerException -isnot [Text.DecoderFallbackException]) { throw }
    }
    Write-Output 'UTF8 no-BOM Unicode keys, complete metadata roundtrip, namespace mismatch and malformed-encoding regression PASS'
} finally {
    if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    if ([IO.File]::Exists($roundtrip)) { [IO.File]::Delete($roundtrip) }
}
