<#
.SYNOPSIS
  Publishes one self-contained executable (win-x64, win-arm64 or osx-arm64), including media tools and UI assets.
  Run get-tools.ps1 first on a machine of the same OS and CPU: the downloaded FFmpeg must match RuntimeIdentifier.
  -NeutralSettings bundles appsettings.json without cloud endpoints (for distribution); users then put their own
  appsettings.json next to the executable. Runs on Windows PowerShell 5.1 and PowerShell 7 (Windows and macOS).
#>
param(
    [ValidateSet('win-arm64', 'win-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [string]$VersionSuffix,
    [switch]$NeutralSettings
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$onMac = (Test-Path variable:IsMacOS) -and $IsMacOS
if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = if ($onMac) { 'osx-arm64' }
    elseif ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' }
    else { 'win-x64' }
}
$windows = $RuntimeIdentifier.StartsWith('win-')
if ($windows -eq $onMac) { throw "Publish $RuntimeIdentifier on its own OS (the build machine is $(if ($onMac) { 'macOS' } else { 'Windows' }))." }
$exe = if ($windows) { '.exe' } else { '' }
$required = @(
    "tools/ffmpeg/bin/ffmpeg$exe", "tools/ffmpeg/bin/ffprobe$exe",
    'tools/ffmpeg/LICENSE.txt', 'tools/fonts/MPLUS1p-Bold.ttf',
    'tools/fonts/MPLUS1p-ExtraBold.ttf', 'tools/fonts/NotoSansCJKsc-Bold.otf',
    'tools/fonts/NotoSansCJKkr-Bold.otf', 'tools/fonts/OFL.txt',
    'tools/fonts/NotoSansCJK-LICENSE.txt', 'tools/models/ultraface.onnx',
    'tools/models/realesr-animevideov3.onnx',
    'tools/models/UltraFace-LICENSE.txt', 'tools/models/Real-ESRGAN-LICENSE.txt',
    'src/VideoCreator.App/wwwroot/js/fluent.bundle.js'
)
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) {
        throw "Missing $file. Run scripts/get-tools.ps1 (or the UI build for fluent.bundle.js)."
    }
}

# The bundled FFmpeg must be built for the target CPU: PE machine (Windows) or Mach-O CPU type (macOS).
foreach ($file in "ffmpeg$exe", "ffprobe$exe") {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $root "tools/ffmpeg/bin/$file")))
    try {
        if ($windows) {
            $expected = if ($RuntimeIdentifier -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
            $reader.BaseStream.Position = 0x3c
            $reader.BaseStream.Position = $reader.ReadInt32()
            $ok = $reader.ReadUInt32() -eq 0x00004550 -and $reader.ReadUInt16() -eq $expected
        }
        else {
            # MH_MAGIC_64 (0xFEEDFACF) followed by CPU_TYPE_ARM64 (thin binary only)
            $ok = $reader.ReadUInt32() -eq [uint32]4277009103 -and $reader.ReadUInt32() -eq 0x0100000C
        }
        if (-not $ok) { throw "$file does not match $RuntimeIdentifier. Run get-tools.ps1 -Force on a $RuntimeIdentifier machine." }
    }
    finally { $reader.Dispose() }
}

$output = Join-Path $root "artifacts/single-file/$RuntimeIdentifier"
Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
$publishArgs = @((Join-Path $root 'src/VideoCreator.App'), '-c', 'Release', '-r', $RuntimeIdentifier,
    '-p:PublishProfile=SingleFile', '-o', $output)
if ($VersionSuffix) { $publishArgs += "-p:VersionSuffix=$VersionSuffix" }
if ($NeutralSettings) {
    # Same settings as the repository, minus anyone's endpoints: the app reports "not configured" until the user sets them.
    $settings = Get-Content (Join-Path $root 'src/VideoCreator.App/appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings.VideoCreator.Llm.Endpoint = ''
    $neutral = Join-Path $root "artifacts/single-file/appsettings.neutral.json"
    New-Item -ItemType Directory -Force (Split-Path $neutral) | Out-Null
    [IO.File]::WriteAllText($neutral, ($settings | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $publishArgs += "-p:BundledAppSettings=$neutral"
}
& dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
$app = Join-Path $output "VideoCreator.App$exe"
$files = @(Get-ChildItem -LiteralPath $output -Force)
if ($files.Count -ne 1 -or -not (Test-Path -LiteralPath $app -PathType Leaf)) {
    throw "Expected only VideoCreator.App$exe in $output, found: $($files.Name -join ', ')"
}
if (-not $windows) {
    # The SDK signs the macOS bundle ad hoc; Apple Silicon refuses to run unsigned code.
    & codesign --verify --verbose=2 $app
    if ($LASTEXITCODE -ne 0) { throw "The macOS executable is not validly signed." }
}
Get-Item -LiteralPath $app | Select-Object FullName, Length
Get-FileHash -LiteralPath $app -Algorithm SHA256 | Format-List
