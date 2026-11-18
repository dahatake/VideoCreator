<#
.SYNOPSIS
  Verifies a published single-file executable outside the repository without using cloud services.
  Starts it from an empty folder with a minimal PATH, checks the bundled runtime/UI/models/fonts/FFmpeg,
  the user settings file next to the executable, the API (ffprobe + SQLite) and project deletion.
  -RequireNeutralSettings fails if the bundled appsettings.json carries ASR/LLM endpoints (release builds).
  Runs on Windows PowerShell 5.1 and PowerShell 7 (Windows and macOS).
#>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [switch]$RequireNeutralSettings
)
$ErrorActionPreference = 'Stop'
$onMac = (Test-Path variable:IsMacOS) -and $IsMacOS
$source = (Resolve-Path -LiteralPath $ExePath).Path
$name = Split-Path $source -Leaf
$temp = Join-Path ([IO.Path]::GetTempPath()) ('vc-single-file-' + [Guid]::NewGuid().ToString('N'))
$appDir = Join-Path $temp 'app'
$cwd = Join-Path $temp 'unrelated'
New-Item -ItemType Directory -Path $appDir, $cwd | Out-Null
$exe = Join-Path $appDir $name
Copy-Item -LiteralPath $source -Destination $exe
if ($onMac) {
    & chmod 755 $exe
    & codesign --verify --verbose=2 $exe
    if ($LASTEXITCODE -ne 0) { throw 'The executable is not validly signed (Apple Silicon will refuse it).' }
}
# User settings next to the executable must override the bundled ones (endpoint is never contacted).
$userSettings = Join-Path $appDir 'appsettings.json'
[IO.File]::WriteAllText($userSettings,
    '{ "VideoCreator": { "Llm": { "Provider": "azure-openai", "Endpoint": "https://verify.invalid/openai/v1" } } }',
    [Text.UTF8Encoding]::new($false))
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$base = "http://127.0.0.1:$port"
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute = $false
$start.WorkingDirectory = $cwd
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Arguments = "--VideoCreator:Port=$port --VideoCreator:OpenBrowser=false --VideoCreator:Asr:Provider=none"
foreach ($key in @($start.EnvironmentVariables.Keys)) {
    if ($key.StartsWith('VideoCreator__', [StringComparison]::OrdinalIgnoreCase)) { $start.EnvironmentVariables.Remove($key) }
}
$start.EnvironmentVariables['VideoCreator__DataDir'] = Join-Path $temp 'data'
$start.EnvironmentVariables['VideoCreator__ProjectsDir'] = Join-Path $temp 'projects'
$start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $temp 'extracted'
$start.EnvironmentVariables.Remove('DOTNET_ROOT')
$start.EnvironmentVariables['PATH'] = if ($onMac) { '/usr/bin:/bin:/usr/sbin:/sbin' } else { "$env:SystemRoot\System32;$env:SystemRoot" }
$process = $null
try {
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $ready = $false
    for ($i = 0; $i -lt 120 -and -not $ready; $i++) {
        if ($process.HasExited) { throw "The executable exited ($($process.ExitCode)): $($stdout.Result) $($stderr.Result)" }
        try { $ready = (Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'The executable did not become ready within 60 seconds.' }
    $health = Invoke-RestMethod "$base/api/health"
    $expectedPresets = @(Get-ChildItem -Path (Join-Path $PSScriptRoot '..\presets') -Filter 'P*.json').Count
    if ($health.presets -ne $expectedPresets -or $health.missing_filters.Count -ne 0 -or -not $health.faces_model) {
        throw "Bundled dependencies failed health checks: $($health | ConvertTo-Json -Depth 5)"
    }
    if ($health.transmission.llm_destination -ne 'https://verify.invalid') {
        throw "appsettings.json next to the executable was not applied: $($health.transmission | ConvertTo-Json -Compress)"
    }
    $root = (Get-ChildItem (Join-Path $temp 'extracted') -Recurse -Filter 'VideoCreator.App.dll' -File | Select-Object -First 1).DirectoryName
    if (-not $root) { throw 'The bundled application was not extracted.' }
    $bundled = Get-Content (Join-Path $root 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($RequireNeutralSettings -and ($bundled.VideoCreator.Asr.Endpoint -or $bundled.VideoCreator.Llm.Endpoint)) {
        throw 'The bundled appsettings.json contains ASR/LLM endpoints; publish with -NeutralSettings.'
    }
    $www = Join-Path $root 'wwwroot'
    foreach ($file in Get-ChildItem $www -Recurse -File) {
        $relative = $file.FullName.Substring($www.Length).Replace('\', '/')
        $asset = Invoke-WebRequest "$base$relative" -UseBasicParsing
        if ($asset.StatusCode -ne 200 -or $asset.RawContentLength -ne $file.Length) { throw "Static asset failed: $relative" }
    }
    if (-not $onMac) {
        $runtimeModules = @($process.Modules | Where-Object { $_.ModuleName -like '*coreclr*' })
        if (@($runtimeModules | Where-Object { -not $_.FileName.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            throw 'The executable loaded an external .NET runtime.'
        }
    }
    $natives = if ($onMac) { 'libe_sqlite3.dylib', 'libonnxruntime.dylib' } else { 'e_sqlite3.dll', 'onnxruntime.dll', 'DirectML.dll' }
    $ffmpegFile = 'tools/ffmpeg/bin/ffmpeg' + $(if ($onMac) { '' } else { '.exe' })
    $ffmpeg = Join-Path $root $ffmpegFile
    foreach ($file in @($natives) + @($ffmpegFile,
        'tools/models/ultraface.onnx', 'tools/models/realesr-animevideov3.onnx',
        'tools/fonts/MPLUS1p-ExtraBold.ttf', 'tools/fonts/NotoSansCJKsc-Bold.otf',
        'tools/fonts/NotoSansCJKkr-Bold.otf', 'tools/models/UltraFace-LICENSE.txt',
        'tools/models/Real-ESRGAN-LICENSE.txt', 'tools/ffmpeg/LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Missing bundled file: $file" }
    }
    # Render text with the extracted font and libass, then register the output through the real API/ffprobe/SQLite.
    [IO.File]::WriteAllText((Join-Path $cwd 'test.srt'), "1`r`n00:00:00,000 --> 00:00:11,000`r`nSingle file subtitle test`r`n")
    $fonts = (Join-Path $root 'tools/fonts').Replace('\', '/').Replace(':', '\:')
    $video = Join-Path $cwd 'test.mp4'
    Push-Location $cwd
    try {
        & $ffmpeg -hide_banner -loglevel error -y `
            -f lavfi -i 'color=c=blue:s=640x360:r=10:d=11' -f lavfi -i 'sine=frequency=440:duration=11' `
            -vf "subtitles=test.srt:fontsdir='$fonts':force_style='FontName=M PLUS 1p ExtraBold'" `
            -c:v libx264 -preset ultrafast -c:a aac -shortest $video
        if ($LASTEXITCODE -ne 0) { throw "Bundled subtitle rendering failed ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
    $headers = @{ 'X-VideoCreator' = '1' }
    $project = Invoke-RestMethod "$base/api/projects" -Method Post -Headers $headers -ContentType 'application/json' `
        -Body '{"name":"Single file verification","language":"en-US"}'
    $asset = Invoke-RestMethod "$base/api/projects/$($project.id)/assets" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body (@{ path = $video } | ConvertTo-Json -Compress)
    if (-not $asset.id -or $asset.media.width -ne 640 -or $asset.media.height -ne 360 -or $asset.media.duration -lt 10) {
        throw "Video registration failed: $($asset | ConvertTo-Json -Depth 5)"
    }
    # Deletion goes through the platform Recycle Bin / Trash, not a permanent delete.
    $deleted = Invoke-RestMethod "$base/api/projects/$($project.id)" -Method Delete -Headers $headers
    if ($deleted.folder_path -and (Test-Path -LiteralPath $deleted.folder_path)) { throw "The project folder was not removed: $($deleted.folder_path)" }
    $beside = @(Get-ChildItem -LiteralPath $appDir -Force | Where-Object { $_.Name -ne 'appsettings.json' })
    if ($beside.Count -ne 1) { throw "Unexpected files next to the executable: $($beside.Name -join ', ')" }
    Write-Host "PASS: single file; isolated startup; bundled runtime, UI, $expectedPresets presets, models, fonts; user settings beside the executable;"
    Write-Host '      FFmpeg/libass render; API/ffprobe/SQLite; project deletion to Recycle Bin/Trash.'
    $health | ConvertTo-Json -Depth 5
}
finally {
    if ($process) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.WaitForExit()
        $process.Dispose()
    }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
