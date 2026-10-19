param([Parameter(Mandatory=$true)][string]$PublishFolder)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$assetsPath = Join-Path $projectRoot 'src\ScanOnWin.App\obj\project.assets.json'
$assetsData = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$licenseFolder = Join-Path $PublishFolder 'licenses'
New-Item -ItemType Directory -Force $licenseFolder | Out-Null
$packageReport = @()
foreach ($library in $assetsData.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $packagePath = Join-Path (Join-Path $projectRoot '.packages') $library.Value.path
    $nuspecFile = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' | Select-Object -First 1
    if (!$nuspecFile) { throw "Missing package metadata: $($library.Name)" }
    [xml]$spec = Get-Content -LiteralPath $nuspecFile.FullName -Raw
    $metadata = $spec.package.metadata
    $identifier = [string]$metadata.id
    $packageLicenseFolder = Join-Path $licenseFolder $identifier
    New-Item -ItemType Directory -Force $packageLicenseFolder | Out-Null
    Copy-Item -LiteralPath $nuspecFile.FullName -Destination $packageLicenseFolder
    $licenseFiles = Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match 'license|notice|copying|authors' }
    foreach ($file in $licenseFiles) { Copy-Item -LiteralPath $file.FullName -Destination $packageLicenseFolder }
    if ($metadata.license.type -eq 'file') {
        $licenseSource = Join-Path $packagePath ([string]$metadata.license.'#text')
        if (Test-Path -LiteralPath $licenseSource -PathType Leaf) { Copy-Item -LiteralPath $licenseSource -Destination $packageLicenseFolder -Force }
    }
    $packageReport += [ordered]@{ name=$identifier; version=[string]$metadata.version; authors=[string]$metadata.authors;
        license=[string]$metadata.license.'#text'; licenseType=[string]$metadata.license.type; licenseUrl=[string]$metadata.licenseUrl;
        projectUrl=[string]$metadata.projectUrl; repository=[string]$metadata.repository.url; sourceRevision=[string]$metadata.repository.commit }
}
$packageReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PublishFolder 'DEPENDENCIES.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $PublishFolder
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $PublishFolder -Recurse -Force
$modelNotices = Join-Path $licenseFolder 'tessdata_fast'
New-Item -ItemType Directory -Force $modelNotices | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\tessdata\LICENSE') -Destination $modelNotices
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\native\LICENSE-TWAIN-DSM.txt') -Destination $licenseFolder
Write-Host "Collected notices for $($packageReport.Count) packages."
