# User-local prerequisite only; no PATH, registry, service or machine-wide changes.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version = '10.0.402'
$sha512 = 'AE4FD528CB7E9F6AF6AAF7DE36715A69F761F183744F1BB3025D19F5A92447090EC561410E46266C1D10A63781ED6F3DE771760BC76C876D87DAABC367B898CD'
$base = Join-Path $env:LOCALAPPDATA 'SectorWorks-Phase2D'
$sdk = Join-Path $base 'dotnet'
foreach ($path in @($env:LOCALAPPDATA, $base, $sdk)) {
    if (Test-Path -LiteralPath $path) {
        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Refusing linked SDK directory.'
        }
    }
}
if (Test-Path -LiteralPath (Join-Path $sdk 'dotnet.exe')) {
    $actual = & (Join-Path $sdk 'dotnet.exe') --version
    if ($LASTEXITCODE -ne 0 -or $actual -ne $version) { throw 'Existing SDK is not the pinned version.' }
    Write-Output "SDK $actual already present."
    exit 0
}
if (Test-Path -LiteralPath $sdk) { throw 'Refusing existing incomplete SDK directory.' }
New-Item -ItemType Directory -Path $base -Force | Out-Null
$archive = Join-Path $base "dotnet-sdk-$version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { throw 'SDK archive already exists; inspect before retrying.' }
Invoke-WebRequest -UseBasicParsing -Uri "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip" -OutFile $archive
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
if ($actualHash -ne $sha512) { throw 'SDK SHA512 mismatch; refusing extraction.' }
Expand-Archive -LiteralPath $archive -DestinationPath $sdk
$actual = & (Join-Path $sdk 'dotnet.exe') --version
if ($LASTEXITCODE -ne 0 -or $actual -ne $version) { throw 'Installed SDK version mismatch.' }
Write-Output "SDK $actual win-x64 SHA512=$actualHash"
