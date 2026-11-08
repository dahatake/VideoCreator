<#
.SYNOPSIS
  Installs and configures everything VideoCreator needs on Windows 10/11 (x64 / ARM64). Safe to re-run.

.DESCRIPTION
  Runs on Windows PowerShell 5.1 (built into Windows), so nothing has to be installed first.
    1. .NET SDK 10 (must satisfy global.json)  winget -> fallback: official dotnet-install.ps1 (per user, no admin)
    2. FFmpeg, Japanese fonts, face model       scripts/get-tools.ps1 -> tools/
    3. Azure CLI (sign-in for keyless Azure)    winget -> fallback: official MSI
    4. Azure sign-in + live probe of the configured Speech / OpenAI endpoints with a real token
       (HTTP 403 = missing role -> prints, or with consent runs, the role assignment)
    5. GitHub Copilot CLI (optional, -WithCopilot)
  Writes tools/setup-env.cmd (PATH for run.cmd) and tools/setup-state.json.
  Exit code: 0 = app can start (Azure problems are warnings), 1 = a required local component is missing.
#>
param(
    [switch]$CheckOnly,      # report only, install nothing
    [switch]$NonInteractive, # never prompt / never open a sign-in window
    [switch]$SkipAzure,      # do not install Azure CLI or probe Azure
    [switch]$WithCopilot,    # also install GitHub Copilot CLI
    [switch]$AssignRoles     # assign missing Azure roles without asking
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $root 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null
$interactive = -not $NonInteractive -and [Environment]::UserInteractive
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Item, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Item = $Item; Status = $Status; Detail = $Detail })
    $color = @{ OK = 'Green'; INSTALLED = 'Cyan'; WARN = 'Yellow'; FAIL = 'Red'; SKIP = 'DarkGray' }[$Status]
    Write-Host ("  [{0}] {1}: {2}" -f $Status, $Item, $Detail) -ForegroundColor $color
}
function Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor White }

function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $seen = @{}; $parts = @()
    foreach ($p in (($env:Path, $machine, $user) -join ';').Split(';')) {
        $t = $p.Trim().TrimEnd('\')
        if ($t -and -not $seen.ContainsKey($t.ToLowerInvariant())) { $seen[$t.ToLowerInvariant()] = 1; $parts += $p.Trim() }
    }
    $env:Path = $parts -join ';'
}

# Native OS architecture (the process itself may be emulated on ARM64).
$nativeArch = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE
$arch = if ($nativeArch -eq 'ARM64') { 'arm64' } else { 'x64' }
$winget = Get-Command winget.exe -ErrorAction SilentlyContinue

function Install-WithWinget([string]$Id) {
    if (-not $winget) { return $false }
    Write-Host "  winget install $Id ..."
    & $winget.Source install --id $Id --exact --source winget --silent --disable-interactivity --accept-package-agreements --accept-source-agreements | Out-Host
    $code = $LASTEXITCODE
    Update-SessionPath
    # 0x8A15002B (no applicable upgrade) / 0x8A150061 (already installed) mean the package is present.
    return ($code -eq 0 -or $code -eq -1978335189 -or $code -eq -1978335135)
}

function Get-Sdk10 {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $null }
    $ErrorActionPreference = 'Continue'  # PS 5.1 turns redirected native stderr into a terminating error under 'Stop'
    $sdks = & $dotnet.Source --list-sdks 2>$null
    $v = $sdks | ForEach-Object { ($_ -split ' ')[0] } | Where-Object { $_ -match '^10\.0\.(\d+)' -and [int]$Matches[1] -ge 100 } | Select-Object -Last 1
    if ($v) { return [pscustomobject]@{ Version = $v; Path = $dotnet.Source } }
    return $null
}

$extraPath = New-Object System.Collections.Generic.List[string]
$dotnetRoot = $null
Update-SessionPath
Write-Host "VideoCreator setup  (Windows $([Environment]::OSVersion.Version), $arch, PowerShell $($PSVersionTable.PSVersion), winget: $([bool]$winget))"

# ---------------------------------------------------------------- 1. .NET SDK 10
Step '.NET SDK 10'
$sdk = Get-Sdk10
if (-not $sdk) {
    $userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) { $env:Path = "$userDotnet;$env:Path"; $sdk = Get-Sdk10 }
}
if ($sdk) { Add-Result '.NET SDK' OK "$($sdk.Version) ($($sdk.Path))" }
elseif ($CheckOnly) { Add-Result '.NET SDK' FAIL 'SDK 10.0.100+ not found (run: run.cmd --setup)' }
else {
    $null = Install-WithWinget "Microsoft.DotNet.SDK.10"
    $sdk = Get-Sdk10
    if (-not $sdk) {
        Write-Host '  Falling back to dotnet-install.ps1 (per-user, no admin) ...'
        $userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
        $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
        & $installer -Channel 10.0 -Architecture $arch -InstallDir $userDotnet -NoPath | Out-Host
        $env:Path = "$userDotnet;$env:Path"
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        if (-not (($userPath -split ';') -contains $userDotnet)) {
            [Environment]::SetEnvironmentVariable('Path', (@($userDotnet, $userPath) | Where-Object { $_ }) -join ';', 'User')
        }
        [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $userDotnet, 'User')
        $dotnetRoot = $userDotnet
        $sdk = Get-Sdk10
    }
    if ($sdk) { Add-Result '.NET SDK' INSTALLED "$($sdk.Version) ($($sdk.Path))" } else { Add-Result '.NET SDK' FAIL 'installation failed' }
}
if ($sdk) {
    $dir = Split-Path $sdk.Path -Parent
    $extraPath.Add($dir)
    if (-not $dotnetRoot -and $dir -like "$env:LOCALAPPDATA*") { $dotnetRoot = $dir }
}

# ---------------------------------------------------------------- 2. FFmpeg / fonts / face model
Step 'FFmpeg, fonts, face model (tools\)'
$needed = 'ffmpeg\bin\ffmpeg.exe', 'ffmpeg\bin\ffprobe.exe', 'fonts\MPLUS1p-Bold.ttf', 'fonts\MPLUS1p-ExtraBold.ttf', 'models\ultraface.onnx', 'models\realesr-animevideov3.onnx'
$missing = @($needed | Where-Object { -not (Test-Path (Join-Path $tools $_)) })
if ($missing.Count -and -not $CheckOnly) {
    try { & (Join-Path $PSScriptRoot 'get-tools.ps1') | Out-Host } catch { Write-Host "  get-tools.ps1 failed: $_" -ForegroundColor Red }
    $fresh = $true
    $missing = @($needed | Where-Object { -not (Test-Path (Join-Path $tools $_)) })
}
if ($missing.Count) { Add-Result 'Media tools' FAIL ("missing: " + ($missing -join ', ')) }
else {
    # Capture all output before reading the exit code: "| Select-Object -First 1" stops the pipeline early and makes $LASTEXITCODE unreliable.
    $ErrorActionPreference = 'Continue'
    $ffOut = @(& (Join-Path $tools 'ffmpeg\bin\ffmpeg.exe') -hide_banner -version 2>$null)
    $ffCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $ffv = $ffOut | Select-Object -First 1
    if ($ffCode -ne 0 -or -not $ffv) { Add-Result 'FFmpeg' FAIL "ffmpeg.exe does not run (exit $ffCode)" }
    else { Add-Result 'FFmpeg' $(if ($fresh) { 'INSTALLED' } else { 'OK' }) ($ffv -replace '^ffmpeg version (\S+).*', '$1') }
    Add-Result 'Fonts + face model' $(if ($fresh) { 'INSTALLED' } else { 'OK' }) 'M PLUS 1p, ultraface.onnx, realesr-animevideov3.onnx'
}

# ---------------------------------------------------------------- 3/4. Azure
$config = Get-Content (Join-Path $root 'src\VideoCreator.App\appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$vc = $config.VideoCreator
function Get-Setting([string]$EnvName, $Default) { $e = [Environment]::GetEnvironmentVariable($EnvName); if ($e) { $e } else { $Default } }
$asrEndpoint = Get-Setting 'VideoCreator__Asr__Endpoint' $vc.Asr.Endpoint
$asrProvider = Get-Setting 'VideoCreator__Asr__Provider' $vc.Asr.Provider
$llmEndpoint = Get-Setting 'VideoCreator__Llm__Endpoint' $vc.Llm.Endpoint
$llmProvider = Get-Setting 'VideoCreator__Llm__Provider' $vc.Llm.Provider
$deployment = Get-Setting 'VideoCreator__Llm__Deployment' $vc.Llm.Deployment
$localOnly = (Get-Setting 'VideoCreator__LocalOnly' "$($vc.LocalOnly)") -eq 'true'

$useAsr = $asrProvider -eq 'azure' -and $asrEndpoint
$useLlm = $llmProvider -eq 'azure-openai' -and $llmEndpoint -and -not $localOnly
$azCmd = $null

function Invoke-Az([string[]]$AzArgs) {
    $ErrorActionPreference = 'Continue'  # "az account show" when signed out writes to stderr; that is an answer, not a failure
    $out = & $azCmd @AzArgs 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($out -join "`n").Trim()
}

function New-SilentWav {
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
    $data = 32000  # 1 s, 16 kHz, 16-bit mono
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $data)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]16000); $w.Write([int]32000); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]$data); $w.Write((New-Object byte[] $data)); $w.Flush()
    return $ms.ToArray()
}

function Send-Probe([string]$Kind, [string]$Token) {
    Add-Type -AssemblyName System.Net.Http
    $http = New-Object System.Net.Http.HttpClient
    $http.Timeout = [TimeSpan]::FromSeconds(60)
    $http.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    try {
        if ($Kind -eq 'speech') {
            # Same call the app makes (AzureFastTranscriber, api-version 2025-10-15) with 1 s of silence.
            $form = New-Object System.Net.Http.MultipartFormDataContent
            $audio = New-Object System.Net.Http.ByteArrayContent(, (New-SilentWav))
            $audio.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('audio/wav')
            $form.Add($audio, 'audio', 'probe.wav')
            $form.Add((New-Object System.Net.Http.StringContent('{"locales":["ja-JP"]}', [Text.Encoding]::UTF8, 'application/json')), 'definition')
            $uri = $asrEndpoint.TrimEnd('/') + '/speechtotext/transcriptions:transcribe?api-version=2025-10-15'
            $resp = $http.PostAsync($uri, $form).GetAwaiter().GetResult()
        } else {
            $body = @{ model = $deployment; messages = @(@{ role = 'user'; content = 'Reply with OK.' }); max_completion_tokens = 64 } | ConvertTo-Json -Depth 5 -Compress
            $content = New-Object System.Net.Http.StringContent($body, [Text.Encoding]::UTF8, 'application/json')
            $resp = $http.PostAsync($llmEndpoint.TrimEnd('/') + '/chat/completions', $content).GetAwaiter().GetResult()
        }
        $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() -replace '\s+', ' '
        return [pscustomobject]@{ Code = [int]$resp.StatusCode; Body = $(if ($text.Length -gt 200) { $text.Substring(0, 200) } else { $text }) }
    } catch {
        return [pscustomobject]@{ Code = 0; Body = $_.Exception.GetBaseException().Message }
    } finally { $http.Dispose() }
}

if ($SkipAzure) { Add-Result 'Azure' SKIP '-SkipAzure' }
elseif (-not $useAsr -and -not $useLlm) { Add-Result 'Azure' SKIP 'no Azure endpoint configured in appsettings.json' }
else {
    Step 'Azure CLI and sign-in'
    $azCmd = (Get-Command az.cmd -ErrorAction SilentlyContinue).Source
    if (-not $azCmd -and -not $CheckOnly) {
        $null = Install-WithWinget 'Microsoft.AzureCLI'
        $azCmd = (Get-Command az.cmd -ErrorAction SilentlyContinue).Source
        if (-not $azCmd) {
            Write-Host '  Falling back to the Azure CLI MSI (needs admin) ...'
            $msi = Join-Path $env:TEMP 'azure-cli.msi'
            Invoke-WebRequest 'https://aka.ms/installazurecliwindowsx64' -OutFile $msi -UseBasicParsing
            $p = Start-Process msiexec.exe -ArgumentList '/i', "`"$msi`"", '/quiet', '/norestart' -Wait -PassThru -Verb RunAs
            Update-SessionPath
            $azCmd = (Get-Command az.cmd -ErrorAction SilentlyContinue).Source
            if (-not $azCmd -and (Test-Path "$env:ProgramFiles\Microsoft SDKs\Azure\CLI2\wbin\az.cmd")) { $azCmd = "$env:ProgramFiles\Microsoft SDKs\Azure\CLI2\wbin\az.cmd" }
        }
        if ($azCmd) { $azFresh = $true }
    }
    if (-not $azCmd) { Add-Result 'Azure CLI' $(if ($CheckOnly) { 'WARN' } else { 'FAIL' }) 'not installed: subtitles (Azure Speech) will not work' }
    else {
        $extraPath.Add((Split-Path $azCmd -Parent))
        # Fetch JSON and filter here: Windows PowerShell 5.1 mangles quoted JMESPath arguments to az.cmd.
        $azVer = Invoke-Az @('version', '-o', 'json')
        Add-Result 'Azure CLI' $(if ($azFresh) { 'INSTALLED' } else { 'OK' }) $(if ($azVer) { ($azVer | ConvertFrom-Json).'azure-cli' } else { '' })

        $acct = Invoke-Az @('account', 'show', '-o', 'json')
        if (-not $acct -and $interactive -and -not $CheckOnly) {
            Write-Host '  Not signed in. Opening Azure sign-in (az login) ...'
            # Inherit the console directly: az may ask which subscription to use, and a pipe would hide that prompt.
            Start-Process -FilePath $azCmd -ArgumentList 'login', '--only-show-errors' -NoNewWindow -Wait
            $acct = Invoke-Az @('account', 'show', '-o', 'json')
        }
        if (-not $acct) { Add-Result 'Azure sign-in' WARN 'not signed in: run "az login", then "run.cmd --check"' }
        else {
            $a = $acct | ConvertFrom-Json
            Add-Result 'Azure sign-in' OK "$($a.user.name) (subscription: $($a.name))"

            Step 'Azure endpoints (live probe with your token)'
            $token = Invoke-Az @('account', 'get-access-token', '--resource', 'https://cognitiveservices.azure.com', '--query', 'accessToken', '-o', 'tsv')
            $probes = @()
            if ($useAsr) { $probes += [pscustomobject]@{ Kind = 'speech'; Item = 'Azure Speech (subtitles)'; Endpoint = $asrEndpoint; Role = 'Cognitive Services User' } }
            if ($useLlm) { $probes += [pscustomobject]@{ Kind = 'openai'; Item = "Azure OpenAI ($deployment)"; Endpoint = $llmEndpoint; Role = 'Cognitive Services OpenAI User' } }
            $denied = @()
            foreach ($pr in $probes) {
                if (-not $token) { Add-Result $pr.Item WARN 'could not get a token for cognitiveservices.azure.com'; continue }
                $r = Send-Probe $pr.Kind $token
                switch ($r.Code) {
                    200 { Add-Result $pr.Item OK "HTTP 200 from $(([Uri]$pr.Endpoint).Host)" }
                    401 { Add-Result $pr.Item WARN "HTTP 401: token rejected. Sign in to the resource's tenant: az login --tenant <tenant>" }
                    403 { Add-Result $pr.Item WARN "HTTP 403: role '$($pr.Role)' missing"; $denied += $pr }
                    404 { Add-Result $pr.Item WARN "HTTP 404: check endpoint / deployment name in appsettings.json ($($r.Body))" }
                    0 { Add-Result $pr.Item WARN "network error: $($r.Body)" }
                    default { Add-Result $pr.Item WARN "HTTP $($r.Code): $($r.Body)" }
                }
            }
            if ($denied.Count) {
                $resId = $null
                $resHost = ([Uri]$denied[0].Endpoint).Host
                $accounts = Invoke-Az @('cognitiveservices', 'account', 'list', '-o', 'json')
                # foreach, not the pipeline: Windows PowerShell 5.1 emits a JSON array from ConvertFrom-Json as ONE object.
                if ($accounts) { foreach ($acc in (ConvertFrom-Json $accounts)) { if ($acc.properties.endpoint -and ([Uri]$acc.properties.endpoint).Host -eq $resHost) { $resId = $acc.id; break } } }
                $oid = Invoke-Az @('ad', 'signed-in-user', 'show', '--query', 'id', '-o', 'tsv')
                $scope = if ($resId) { $resId } else { '<resource id of ' + $resHost + '>' }
                $who = if ($oid) { $oid } else { '<your object id>' }
                $cmds = $denied | ForEach-Object { "az role assignment create --assignee-object-id $who --assignee-principal-type User --role `"$($_.Role)`" --scope $scope" }
                $doAssign = $AssignRoles
                if (-not $doAssign -and $interactive -and -not $CheckOnly -and $resId -and $oid) {
                    $doAssign = (Read-Host '  Assign the missing role(s) to yourself now? (requires Owner / User Access Administrator) [y/N]') -match '^[yY]'
                }
                if ($doAssign -and $resId -and $oid) {
                    foreach ($pr in $denied) {
                        & $azCmd role assignment create --assignee-object-id $oid --assignee-principal-type User --role $pr.Role --scope $resId --only-show-errors | Out-Null
                        if ($LASTEXITCODE -eq 0) { Add-Result "Role: $($pr.Role)" INSTALLED 'assigned (may take up to 5 min to apply; then run.cmd --check)' }
                        else { Add-Result "Role: $($pr.Role)" WARN 'assignment failed (no permission?) - ask the subscription owner' }
                    }
                } else {
                    Write-Host '  Ask the subscription owner to run:' -ForegroundColor Yellow
                    $cmds | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
                }
            }
        }
    }
}

# ---------------------------------------------------------------- 5. GitHub Copilot CLI (optional)
Step 'GitHub Copilot CLI (optional)'
function Find-Copilot {
    # Same lookup order as the app (CopilotSelector.FindInstalledCli): COPILOT_CLI_PATH, winget package, PATH.
    if ($env:COPILOT_CLI_PATH -and (Test-Path $env:COPILOT_CLI_PATH)) { return $env:COPILOT_CLI_PATH }
    $pkgs = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $pkgs) {
        $hit = Get-ChildItem $pkgs -Directory -Filter 'GitHub.Copilot*' | ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter copilot.exe -File } | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return (Get-Command copilot.exe -ErrorAction SilentlyContinue).Source
}
$copilot = Find-Copilot
if (-not $copilot -and $WithCopilot -and -not $CheckOnly) {
    $null = Install-WithWinget 'GitHub.Copilot'
    $copilot = Find-Copilot
    if ($copilot) { Add-Result 'Copilot CLI' INSTALLED "$copilot (sign in once by running: copilot)" } else { Add-Result 'Copilot CLI' WARN 'installation failed (needs winget)' }
} elseif ($copilot) { Add-Result 'Copilot CLI' OK $copilot }
else { Add-Result 'Copilot CLI' SKIP 'not installed (optional; run.cmd --setup --with-copilot)' }
if ($copilot) { $extraPath.Add((Split-Path $copilot -Parent)) }

# ---------------------------------------------------------------- outputs
$failed = @($results | Where-Object Status -eq 'FAIL')
if (-not $CheckOnly) {
    # run.cmd "call"s this so newly installed tools are usable without opening a new window.
    $lines = @('@echo off', 'rem Generated by scripts\setup.ps1 - do not edit.')
    foreach ($d in ($extraPath | Select-Object -Unique)) { $lines += 'set "PATH=' + $d.Replace('%', '%%') + ';%PATH%"' }
    if ($dotnetRoot) { $lines += 'set "DOTNET_ROOT=' + $dotnetRoot + '"' }
    $lines += 'set "DOTNET_NOLOGO=1"'
    Set-Content (Join-Path $tools 'setup-env.cmd') $lines -Encoding ASCII
    if (-not $failed.Count) {
        @{ completed = (Get-Date).ToString('o'); arch = $arch; results = $results } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $tools 'setup-state.json') -Encoding UTF8
    }
}

Write-Host ''
$results | Format-Table Item, Status, Detail -AutoSize -Wrap | Out-Host
if ($failed.Count) { Write-Host 'Setup incomplete: fix the FAIL items above and run again.' -ForegroundColor Red; exit 1 }
if (@($results | Where-Object Status -eq 'WARN').Count) { Write-Host 'The app can start, but see the WARN items above (Azure is needed for subtitles).' -ForegroundColor Yellow }
else { Write-Host 'All set.' -ForegroundColor Green }
exit 0
