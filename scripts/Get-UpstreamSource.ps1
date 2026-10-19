$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$upstreamFolder = Join-Path $projectRoot '.tools\upstream'
New-Item -ItemType Directory -Force $upstreamFolder | Out-Null
$revision = '8ae3e82203115754e804fe9c14f00f6bd86ee192'
$sourcePath = Join-Path $upstreamFolder "NAPS2-$revision.zip"
if (!(Test-Path -LiteralPath $sourcePath)) {
    Invoke-WebRequest "https://codeload.github.com/cyanfish/naps2/zip/$revision" -OutFile $sourcePath
}
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne '6B3C37FC8BF7DD831C74EC971D054EE1323CDAA95B17E157891A7BBE62194D04') {
    throw 'NAPS2 upstream source checksum mismatch.'
}
Write-Host 'Corresponding LGPL source verified.'
$dsmSourcePath = Join-Path $upstreamFolder 'TWAIN-DSM-2.3.2.tar.gz'
if (!(Test-Path -LiteralPath $dsmSourcePath)) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/twain/twain-dsm/3491bec2960e856f8dd4823dc54245b37beed51d/Releases/dsm_020302/tarball/twaindsm_2.3.2.orig.tar.gz' -OutFile $dsmSourcePath
}
if ((Get-FileHash -LiteralPath $dsmSourcePath -Algorithm SHA256).Hash -ne '051DAC757C91814EE4E1A0E7896E61086E0E2D87CCCB80BBC8150558107F729E') {
    throw 'TWAIN DSM source checksum mismatch.'
}
