$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$modelFolder = Join-Path $projectRoot 'assets\tessdata'
New-Item -ItemType Directory -Force $modelFolder | Out-Null
$revision = '87416418657359cb625c412a48b6e1d6d41c29bd'
$hashes = @{
    eng = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
    rus = 'E16E5E036CCE1D9EC2B00063CF8B54472625B9E14D893A169E2B0DEDEB4DF225'
}
foreach ($language in $hashes.Keys) {
    $modelPath = Join-Path $modelFolder "$language.traineddata"
    if (!(Test-Path -LiteralPath $modelPath)) {
        Invoke-WebRequest "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$revision/$language.traineddata" -OutFile $modelPath
    }
    if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne $hashes[$language]) {
        throw "OCR model checksum mismatch: $language"
    }
}
Write-Host 'OCR models verified: rus, eng.'
