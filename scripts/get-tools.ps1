<#
.SYNOPSIS
  Downloads the third-party tools VideoCreator needs into ./tools (git-ignored):
  - FFmpeg (BtbN static GPL build, includes libass/libx264) for the current architecture
  - M PLUS 1p (SIL Open Font License) for Japanese telops
  - UltraFace RFB-320 ONNX face detector (MIT) for face-following vertical crops
#>
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Runs on Windows PowerShell 5.1 as well as PowerShell 7.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $root 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null

# Native OS architecture from the registry (the PowerShell process itself may be emulated on ARM64).
$arch = if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'winarm64' } else { 'win64' }
$ffDir = Join-Path $tools 'ffmpeg'
if ($Force -or -not (Test-Path (Join-Path $ffDir 'bin/ffmpeg.exe'))) {
    $zip = Join-Path $tools 'ffmpeg.zip'
    $url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-$arch-gpl.zip"
    Write-Host "Downloading FFmpeg ($arch)..."
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    $tmp = Join-Path $tools 'ffmpeg-tmp'
    Expand-Archive $zip -DestinationPath $tmp -Force
    Remove-Item $ffDir -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName $ffDir
    Remove-Item $tmp, $zip -Recurse -Force
}

$fontDir = Join-Path $tools 'fonts'
New-Item -ItemType Directory -Force $fontDir | Out-Null
foreach ($f in 'MPLUS1p-Bold.ttf', 'MPLUS1p-ExtraBold.ttf', 'OFL.txt') {
    $dst = Join-Path $fontDir $f
    if ($Force -or -not (Test-Path $dst)) {
        Write-Host "Downloading $f..."
        Invoke-WebRequest "https://github.com/google/fonts/raw/main/ofl/mplus1p/$f" -OutFile $dst -UseBasicParsing
    }
}

$modelDir = Join-Path $tools 'models'
New-Item -ItemType Directory -Force $modelDir | Out-Null
$model = Join-Path $modelDir 'ultraface.onnx'
if ($Force -or -not (Test-Path $model)) {
    Write-Host 'Downloading ultraface.onnx (face detector, MIT)...'
    Invoke-WebRequest 'https://github.com/onnx/models/raw/main/validated/vision/body_analysis/ultraface/models/version-RFB-320.onnx' -OutFile $model -UseBasicParsing
    if ((Get-FileHash $model).Hash -ne '34CD7E60AEFF28744C657DE7A3DC64E872D506741DE66987F3426F2B79F88017') { Remove-Item $model; throw 'ultraface.onnx hash mismatch' }
}

& (Join-Path $ffDir 'bin/ffmpeg.exe') -hide_banner -version | Select-Object -First 1
Get-ChildItem $fontDir | Select-Object Name, Length | Format-Table -AutoSize
