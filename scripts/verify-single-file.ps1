<#
.SYNOPSIS
  Verifies a published EXE outside the repository without using cloud services.
#>
param([Parameter(Mandatory = $true)][string]$ExePath)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ('vc-single-file-' + [Guid]::NewGuid().ToString('N'))
$appDir = Join-Path $temp 'app'
$cwd = Join-Path $temp 'unrelated'
New-Item -ItemType Directory -Path $appDir, $cwd | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $appDir 'VideoCreator.App.exe')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$base = "http://127.0.0.1:$port"
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $appDir 'VideoCreator.App.exe'))
$start.UseShellExecute = $false
$start.WorkingDirectory = $cwd
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Arguments = "--VideoCreator:Port=$port --VideoCreator:OpenBrowser=false --VideoCreator:LocalOnly=true --VideoCreator:Asr:Provider=none"
foreach ($key in @($start.EnvironmentVariables.Keys)) {
    if ($key.StartsWith('VideoCreator__', [StringComparison]::OrdinalIgnoreCase)) { $start.EnvironmentVariables.Remove($key) }
}
$start.EnvironmentVariables['VideoCreator__DataDir'] = Join-Path $temp 'data'
$start.EnvironmentVariables['VideoCreator__ProjectsDir'] = Join-Path $temp 'projects'
$start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $temp 'extracted'
$start.EnvironmentVariables['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
$process = $null
try {
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $ready = $false
    for ($i = 0; $i -lt 120; $i++) {
        if ($process.HasExited) { throw "EXE exited ($($process.ExitCode)): $($stdout.Result) $($stderr.Result)" }
        try {
            $response = Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch {
            if ($i -eq 119) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) { throw 'EXE did not become ready.' }
    $health = Invoke-RestMethod "$base/api/health"
    if ($health.presets -ne 10 -or $health.missing_filters.Count -ne 0 -or -not $health.faces_model) {
        throw "Bundled dependencies failed health checks: $($health | ConvertTo-Json -Depth 5)"
    }
    $root = (Get-ChildItem (Join-Path $temp 'extracted') -Recurse -Filter 'VideoCreator.App.dll' -File | Select-Object -First 1).DirectoryName
    if (-not $root) { throw 'The bundled application was not extracted.' }
    foreach ($file in Get-ChildItem (Join-Path $root 'wwwroot') -Recurse -File) {
        $relative = $file.FullName.Substring((Join-Path $root 'wwwroot').Length).Replace('\', '/')
        $asset = Invoke-WebRequest "$base$relative" -UseBasicParsing
        if ($asset.StatusCode -ne 200 -or $asset.RawContentLength -ne $file.Length) {
            throw "Static asset failed: $relative"
        }
    }
    $runtimeModules = @($process.Modules | Where-Object { $_.ModuleName -like '*coreclr*' })
    if ($runtimeModules.Count -gt 0 -and @($runtimeModules | Where-Object { -not $_.FileName.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw 'The EXE loaded an external .NET runtime.'
    }
    foreach ($file in 'e_sqlite3.dll', 'onnxruntime.dll', 'DirectML.dll',
        'tools\models\ultraface.onnx', 'tools\models\realesr-animevideov3.onnx',
        'tools\fonts\MPLUS1p-ExtraBold.ttf', 'tools\fonts\NotoSansCJKsc-Bold.otf',
        'tools\fonts\NotoSansCJKkr-Bold.otf', 'tools\models\UltraFace-LICENSE.txt',
        'tools\models\Real-ESRGAN-LICENSE.txt', 'THIRD-PARTY-NOTICES.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Missing bundled file: $file" }
    }
    # Render text with the extracted font and libass, then register the output through the real API/ffprobe/SQLite.
    $srt = Join-Path $cwd 'test.srt'
    [IO.File]::WriteAllText($srt, "1`r`n00:00:00,000 --> 00:00:11,000`r`nSingle EXE subtitle test`r`n")
    $fonts = (Join-Path $root 'tools\fonts').Replace('\', '/').Replace(':', '\:')
    $video = Join-Path $cwd 'test.mp4'
    Push-Location $cwd
    try {
        & (Join-Path $root 'tools\ffmpeg\bin\ffmpeg.exe') -hide_banner -loglevel error -y `
            -f lavfi -i 'color=c=blue:s=640x360:r=10:d=11' -f lavfi -i 'sine=frequency=440:duration=11' `
            -vf "subtitles=test.srt:fontsdir='$fonts':force_style='FontName=M PLUS 1p ExtraBold'" `
            -c:v libx264 -preset ultrafast -c:a aac -shortest $video
        if ($LASTEXITCODE -ne 0) { throw "Bundled subtitle rendering failed ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
    $headers = @{ 'X-VideoCreator' = '1' }
    $project = Invoke-RestMethod "$base/api/projects" -Method Post -Headers $headers -ContentType 'application/json' `
        -Body '{"name":"Single EXE verification","language":"en-US"}'
    $asset = Invoke-RestMethod "$base/api/projects/$($project.id)/assets" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body (@{ path = $video } | ConvertTo-Json -Compress)
    if (-not $asset.id -or $asset.media.width -ne 640 -or $asset.media.height -ne 360 -or $asset.media.duration -lt 10) {
        throw "Video registration failed: $($asset | ConvertTo-Json -Depth 5)"
    }
    if (@(Get-ChildItem -LiteralPath $appDir -Force).Count -ne 1) { throw 'Unexpected files next to the EXE.' }
    Write-Host "PASS: single EXE; isolated startup; bundled runtime, UI, 10 presets, models, fonts; FFmpeg/libass render; API/ffprobe/SQLite."
    $health | ConvertTo-Json -Depth 5
}
finally {
    if ($process) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.WaitForExit()
        $process.Dispose()
    }
    Remove-Item -LiteralPath $temp -Recurse -Force
}
