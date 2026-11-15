<#
.SYNOPSIS
  Publishes one self-contained Windows EXE, including media tools and UI assets.
  Run get-tools.ps1 first. The downloaded FFmpeg must match RuntimeIdentifier.
#>
param(
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = $(if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' })
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$required = @(
    'tools\ffmpeg\bin\ffmpeg.exe', 'tools\ffmpeg\bin\ffprobe.exe',
    'tools\ffmpeg\LICENSE.txt', 'tools\fonts\MPLUS1p-Bold.ttf',
    'tools\fonts\MPLUS1p-ExtraBold.ttf', 'tools\fonts\NotoSansCJKsc-Bold.otf',
    'tools\fonts\NotoSansCJKkr-Bold.otf', 'tools\fonts\OFL.txt',
    'tools\fonts\NotoSansCJK-LICENSE.txt', 'tools\models\ultraface.onnx',
    'tools\models\realesr-animevideov3.onnx',
    'tools\models\UltraFace-LICENSE.txt', 'tools\models\Real-ESRGAN-LICENSE.txt',
    'src\VideoCreator.App\wwwroot\js\fluent.bundle.js'
)
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) {
        throw "Missing $file. Run scripts\get-tools.ps1 (or the UI build for fluent.bundle.js)."
    }
}
$expectedMachine = if ($RuntimeIdentifier -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
foreach ($file in 'ffmpeg.exe', 'ffprobe.exe') {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $root "tools\ffmpeg\bin\$file")))
    try {
        $reader.BaseStream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $reader.BaseStream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne $expectedMachine) {
            throw "$file does not match $RuntimeIdentifier. Supply FFmpeg tools for the target architecture."
        }
    }
    finally { $reader.Dispose() }
}
$output = Join-Path $root "artifacts\single-file\$RuntimeIdentifier"
& dotnet publish (Join-Path $root 'src\VideoCreator.App') -c Release -r $RuntimeIdentifier `
    -p:PublishProfile=WindowsSingleFile -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
$exe = Join-Path $output 'VideoCreator.App.exe'
$files = @(Get-ChildItem -LiteralPath $output -Force)
if ($files.Count -ne 1 -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Expected only VideoCreator.App.exe in $output. Inspect the publish output."
}
Get-Item -LiteralPath $exe | Select-Object FullName, Length
Get-FileHash -LiteralPath $exe -Algorithm SHA256 | Format-List
