$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$distFolder = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force $distFolder | Out-Null
& (Join-Path $PSScriptRoot 'Get-UpstreamSource.ps1')
$zipPath = Join-Path $distFolder 'ScanOnWIN-Source.zip'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $upstream = Get-ChildItem -LiteralPath (Join-Path $projectRoot '.tools\upstream') -Filter 'NAPS2-*.zip' | Select-Object -First 1
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $upstream.FullName, 'third-party-sources/' + $upstream.Name) | Out-Null
    $dsmSource = Join-Path $projectRoot '.tools\upstream\TWAIN-DSM-2.3.2.tar.gz'
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $dsmSource, 'third-party-sources/TWAIN-DSM-2.3.2.tar.gz') | Out-Null
    $paths = 'src','tests','scripts','installer','assets','.github','docs','Directory.Build.props','global.json','ScanOnWIN.sln','README.md','LICENSE','LICENSE-NAPS2.txt','THIRD-PARTY-NOTICES.md','.gitignore'
    foreach ($relative in $paths) {
        $itemPath = Join-Path $projectRoot $relative
        if (!(Test-Path -LiteralPath $itemPath)) { continue }
        $files = if (Test-Path -LiteralPath $itemPath -PathType Container) { Get-ChildItem -LiteralPath $itemPath -Recurse -File } else { Get-Item -LiteralPath $itemPath }
        foreach ($file in $files) {
            $entryName = $file.FullName.Substring($projectRoot.Length + 1).Replace('\','/')
            if ($entryName -match '(^|/)(bin|obj)(/|$)') { continue }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName) | Out-Null
        }
    }
} finally { $archive.Dispose() }
