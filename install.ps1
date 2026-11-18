<#
.SYNOPSIS
  Sets up the current VideoCreator checkout and adds a Start Menu shortcut.

.DESCRIPTION
  Installs missing prerequisites with scripts/setup.ps1, builds the Release app,
  and creates a per-user shortcut to run.cmd. Safe to re-run.
#>
param(
    [switch]$SkipAzure,
    [switch]$WithCopilot,
    [switch]$NonInteractive,
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$setup = Join-Path $root 'scripts\setup.ps1'

$setupHost = Get-Command pwsh.exe -ErrorAction SilentlyContinue
if (-not $setupHost) { $setupHost = Get-Command powershell.exe -ErrorAction SilentlyContinue }
if (-not $setupHost) { throw 'PowerShell was not found.' }

$setupArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $setup)
if ($SkipAzure) { $setupArgs += '-SkipAzure' }
if ($WithCopilot) { $setupArgs += '-WithCopilot' }
if ($NonInteractive) { $setupArgs += '-NonInteractive' }

Write-Host 'Setting up VideoCreator prerequisites...'
& $setupHost.Source @setupArgs
if ($LASTEXITCODE -ne 0) { throw "Prerequisite setup failed (exit $LASTEXITCODE)." }

$setupEnvironment = Join-Path $root 'tools\setup-env.cmd'
if (-not (Test-Path -LiteralPath $setupEnvironment -PathType Leaf)) {
    throw "Setup did not create $setupEnvironment."
}

# Apply the generated environment to this process so a per-user .NET install is usable below.
foreach ($line in Get-Content -LiteralPath $setupEnvironment) {
    if ($line -match '^set "([^=]+)=(.*)"$') {
        $name = $Matches[1]
        $value = $Matches[2].Replace('%%', '%')
        $value = $value.Replace('%PATH%', $env:PATH)
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}

Write-Host 'Building VideoCreator (Release)...'
& dotnet build (Join-Path $root 'src\VideoCreator.App') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed (exit $LASTEXITCODE)." }

if (-not $NoShortcut) {
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if (-not $programs) { throw 'Could not resolve the current user Start Menu Programs folder.' }
    $menu = Join-Path $programs 'VideoCreator'
    New-Item -ItemType Directory -Path $menu -Force | Out-Null
    $shortcutPath = Join-Path $menu 'VideoCreator.lnk'

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $root 'run.cmd'
        $shortcut.WorkingDirectory = $root
        $shortcut.Description = 'Start VideoCreator'
        $shortcut.Save()
    }
    finally {
        if ($shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
    Write-Host "Start Menu shortcut created: $shortcutPath"
}

Write-Host 'VideoCreator installation completed.'
