<#
.SYNOPSIS
  Downloads the third-party tools VideoCreator needs into ./tools (git-ignored):
  - FFmpeg for the current OS/architecture, including libass/libx264:
      Windows: BtbN static GPL build (latest)
      macOS (Apple Silicon): Martin Riedl static build, pinned release with SHA-256 check
  - M PLUS 1p and Noto Sans CJK SC/KR (SIL Open Font License) for multilingual telops
  - UltraFace RFB-320 ONNX face detector (MIT) for face-following vertical crops
  - Real-ESRGAN realesr-animevideov3 ONNX (BSD-3-Clause) for upscaling low-resolution opening/closing videos
  Runs on Windows PowerShell 5.1 and on PowerShell 7 (Windows and macOS).
#>
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $root 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null
$onMac = (Test-Path variable:IsMacOS) -and $IsMacOS
$exe = if ($onMac) { '' } else { '.exe' }

$ffDir = Join-Path $tools 'ffmpeg'
if ($onMac) {
    if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::Arm64) {
        throw 'VideoCreator on macOS needs an Apple Silicon Mac: ONNX Runtime ships no Intel macOS build.'
    }
    if ($Force -or -not (Test-Path (Join-Path $ffDir 'bin/ffmpeg'))) {
        # Pinned build: https://ffmpeg.martin-riedl.de/ (configuration: .../versions.txt next to the zips)
        $base = 'https://ffmpeg.martin-riedl.de/download/macos/arm64/1789931890_9.0.2'
        $pinned = @{
            'ffmpeg'  = 'C8ED4C4E6978A03C485EDBFE4E0A5DC2380F8A30BBA5150531B31B094492D924'
            'ffprobe' = 'FCBE839537485EAEE7A7A8BC5CBC0F90D53617E80943E8A5B2E31CB851197EA6'
        }
        $tmp = Join-Path $tools 'ffmpeg-tmp'
        Remove-Item $tmp, $ffDir -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force $tmp, (Join-Path $ffDir 'bin') | Out-Null
        foreach ($name in 'ffmpeg', 'ffprobe') {
            Write-Host "Downloading $name 9.0.2 (macOS arm64)..."
            $zip = Join-Path $tmp "$name.zip"
            Invoke-WebRequest "$base/$name.zip" -OutFile $zip -UseBasicParsing
            if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $pinned[$name]) { throw "$name.zip hash mismatch" }
            Expand-Archive $zip -DestinationPath (Join-Path $tmp $name) -Force
            $bin = Join-Path $ffDir "bin/$name"
            Move-Item (Get-ChildItem (Join-Path $tmp $name) -Recurse -File -Filter $name | Select-Object -First 1).FullName $bin
            & chmod 755 $bin
            # Apple Silicon only runs signed code: keep the publisher's signature, add an ad-hoc one only if it is missing.
            & codesign --verify $bin 2>$null
            if ($LASTEXITCODE -ne 0) { & codesign --force --sign - $bin; if ($LASTEXITCODE -ne 0) { throw "codesign $name failed" } }
        }
        Invoke-WebRequest 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/n9.0.2/COPYING.GPLv3' -OutFile (Join-Path $ffDir 'LICENSE.txt') -UseBasicParsing
        Remove-Item $tmp -Recurse -Force
    }
}
elseif ($Force -or -not (Test-Path (Join-Path $ffDir 'bin/ffmpeg.exe'))) {
    # Native OS architecture from the registry (the PowerShell process itself may be emulated on ARM64).
    $arch = if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'winarm64' } else { 'win64' }
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

$cjkFonts = @{
    'NotoSansCJKsc-Bold.otf' = 'SimplifiedChinese'
    'NotoSansCJKkr-Bold.otf' = 'Korean'
}
foreach ($file in $cjkFonts.Keys) {
    $dst = Join-Path $fontDir $file
    if ($Force -or -not (Test-Path $dst)) {
        Write-Host "Downloading $file..."
        Invoke-WebRequest "https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF/$($cjkFonts[$file])/$file" -OutFile $dst -UseBasicParsing
    }
}
$cjkLicense = Join-Path $fontDir 'NotoSansCJK-LICENSE.txt'
if ($Force -or -not (Test-Path $cjkLicense)) {
    Invoke-WebRequest 'https://github.com/notofonts/noto-cjk/raw/main/Sans/LICENSE' -OutFile $cjkLicense -UseBasicParsing
}

$modelDir = Join-Path $tools 'models'
New-Item -ItemType Directory -Force $modelDir | Out-Null
$modelLicenses = @{
    'UltraFace-LICENSE.txt' = 'https://raw.githubusercontent.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB/master/LICENSE'
    'Real-ESRGAN-LICENSE.txt' = 'https://raw.githubusercontent.com/xinntao/Real-ESRGAN/master/LICENSE'
}
foreach ($file in $modelLicenses.Keys) {
    $dst = Join-Path $modelDir $file
    if ($Force -or -not (Test-Path $dst)) {
        Invoke-WebRequest $modelLicenses[$file] -OutFile $dst -UseBasicParsing
    }
}
$model = Join-Path $modelDir 'ultraface.onnx'
if ($Force -or -not (Test-Path $model)) {
    Write-Host 'Downloading ultraface.onnx (face detector, MIT)...'
    Invoke-WebRequest 'https://github.com/onnx/models/raw/main/validated/vision/body_analysis/ultraface/models/version-RFB-320.onnx' -OutFile $model -UseBasicParsing
    if ((Get-FileHash $model).Hash -ne '34CD7E60AEFF28744C657DE7A3DC64E872D506741DE66987F3426F2B79F88017') { Remove-Item $model; throw 'ultraface.onnx hash mismatch' }
}
# Learned 4x upscaler for low-resolution opening/closing videos: Real-ESRGAN realesr-animevideov3 (BSD-3-Clause, Xintao Wang), ONNX export
$sr = Join-Path $modelDir 'realesr-animevideov3.onnx'
if ($Force -or -not (Test-Path $sr)) {
    Write-Host 'Downloading realesr-animevideov3.onnx (upscaler, BSD-3-Clause)...'
    Invoke-WebRequest 'https://huggingface.co/skillsafe-ai/realesr-animevideov3/resolve/main/model.onnx' -OutFile $sr -UseBasicParsing
    if ((Get-FileHash $sr).Hash -ne '78BAA685A1A92CAC6E14AB2AF2A8AD0EF56FA48629563B1414E3A7ACC54D86E2') { Remove-Item $sr; throw 'realesr-animevideov3.onnx hash mismatch' }
}

& (Join-Path $ffDir "bin/ffmpeg$exe") -hide_banner -version | Select-Object -First 1
Get-ChildItem $fontDir | Select-Object Name, Length | Format-Table -AutoSize
