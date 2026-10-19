param([switch]$SkipTests, [switch]$SkipUiTests, [switch]$SkipInstaller, [string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    $dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
    # Keep SDK first-run caches within this checkout.
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
    $env:DOTNET_ROOT = Split-Path $dotnetCommand -Parent
    function Invoke-Dotnet([string[]]$Arguments) {
        & $dotnetCommand @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
    }
    & (Join-Path $PSScriptRoot 'Get-OcrModels.ps1')
    Invoke-Dotnet @('restore','ScanOnWIN.sln','--locked-mode')
    Invoke-Dotnet @('build','ScanOnWIN.sln','-c','Release','--no-restore','-m:1')
    if (!$SkipTests) {
        Invoke-Dotnet @('run','--project','tests\ScanOnWin.Tests','-c','Release','--no-build','--','artifacts\test-results')
        if (!$SkipUiTests) { Invoke-Dotnet @('run','--project','tests\ScanOnWin.UiTests','-c','Release','--no-build','--','artifacts\ui-tests') }
    }
    $publishFolder = Join-Path $projectRoot 'artifacts\portable'
    Invoke-Dotnet @('publish','src\ScanOnWin.App','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=false','-p:RestoreLockedMode=true','-o',$publishFolder,'-m:1')
    foreach ($required in 'ScanOnWIN.exe','NAPS2.Worker.exe','_win32\twaindsm.dll','_win64\twaindsm.dll','_win64\tesseract.exe','tessdata\rus.traineddata','tessdata\eng.traineddata','coreclr.dll') {
        if (!(Test-Path -LiteralPath (Join-Path $publishFolder $required))) { throw "Missing runtime asset: $required" }
    }
    & (Join-Path $PSScriptRoot 'Collect-Notices.ps1') -PublishFolder $publishFolder
    $distFolder = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Force $distFolder | Out-Null
    $portableZip = Join-Path $distFolder 'ScanOnWIN-Portable.zip'
    if (Test-Path -LiteralPath $portableZip) { Remove-Item -LiteralPath $portableZip }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishFolder, $portableZip)
    if (!$SkipInstaller) {
        if (!$InnoCompiler) {
            $candidates = @((Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'),(Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'))
            $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
        if (!$InnoCompiler) { throw 'Install Inno Setup 6 and provide -InnoCompiler, or use -SkipInstaller.' }
        & $InnoCompiler /Q "/DPublishDir=$publishFolder" (Join-Path $projectRoot 'installer\ScanOnWIN.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
    & (Join-Path $PSScriptRoot 'Package-Source.ps1')
    Get-ChildItem -LiteralPath $distFolder -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash  $($_.Name)"
    } | Set-Content -LiteralPath (Join-Path $distFolder 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Ready: $distFolder"
} finally { Pop-Location }
