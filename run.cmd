@echo off
rem VideoCreator launcher.
rem   run.cmd                  first run: install/configure everything, then build and start the app
rem   run.cmd --setup          run the setup again (installs what is missing, re-checks Azure), then start
rem   run.cmd --check          report what is installed / configured, change nothing
rem   options: --with-copilot (also install GitHub Copilot CLI)  --assign-roles (assign missing Azure roles)
rem            --skip-azure  --non-interactive  --no-start
setlocal EnableExtensions
cd /d "%~dp0"

set "DO_SETUP="
set "DO_CHECK="
set "NO_START="
set "PS_ARGS="
:parse
if "%~1"=="" goto :parsed
if /i "%~1"=="--setup"           ( set "DO_SETUP=1" & goto :next )
if /i "%~1"=="--check"           ( set "DO_CHECK=1" & goto :next )
if /i "%~1"=="--no-start"        ( set "NO_START=1" & goto :next )
if /i "%~1"=="--with-copilot"    ( set "PS_ARGS=%PS_ARGS% -WithCopilot" & goto :next )
if /i "%~1"=="--assign-roles"    ( set "PS_ARGS=%PS_ARGS% -AssignRoles" & goto :next )
if /i "%~1"=="--skip-azure"      ( set "PS_ARGS=%PS_ARGS% -SkipAzure" & goto :next )
if /i "%~1"=="--non-interactive" ( set "PS_ARGS=%PS_ARGS% -NonInteractive" & goto :next )
if /i "%~1"=="--help" goto :help
if /i "%~1"=="-h" goto :help
echo [ERROR] Unknown option: %~1
goto :help_fail
:next
shift
goto :parse
:parsed

rem Windows PowerShell 5.1 ships with Windows, so setup needs nothing preinstalled.
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "SETUP=%PS% -NoProfile -ExecutionPolicy Bypass -File scripts\setup.ps1"

if defined DO_CHECK (
  %SETUP% -CheckOnly %PS_ARGS%
  exit /b %errorlevel%
)

if defined DO_SETUP goto :setup
if not exist "tools\setup-state.json" goto :setup
if exist "tools\setup-env.cmd" call "tools\setup-env.cmd"
rem Quick health check: SDK matching global.json, FFmpeg, face model, fonts.
dotnet --version >nul 2>&1 || goto :setup
if not exist "tools\ffmpeg\bin\ffmpeg.exe" goto :setup
if not exist "tools\models\ultraface.onnx" goto :setup
if not exist "tools\fonts\MPLUS1p-ExtraBold.ttf" goto :setup
goto :start

:setup
echo Setting up VideoCreator (installs only what is missing)...
%SETUP% %PS_ARGS%
if errorlevel 1 (
  echo [ERROR] Setup did not complete. Fix the FAIL items above, then run: run.cmd --setup
  if not defined NO_START pause
  exit /b 1
)
call "tools\setup-env.cmd"

:start
if defined NO_START exit /b 0
echo Building VideoCreator from the current source...
dotnet build src\VideoCreator.App -c Release
set "RC=%errorlevel%"
if not "%RC%"=="0" (
  echo [ERROR] Build failed with code %RC%. VideoCreator was not started.
  pause
  exit /b %RC%
)
echo Starting VideoCreator at http://127.0.0.1:8765/  (close this window or press Ctrl+C to stop)
dotnet run --project src\VideoCreator.App -c Release --no-build
set "RC=%errorlevel%"
if not "%RC%"=="0" (
  echo [ERROR] VideoCreator exited with code %RC%. To diagnose: run.cmd --check
  pause
)
exit /b %RC%

:help
echo Usage: run.cmd [--setup ^| --check] [--with-copilot] [--assign-roles] [--skip-azure] [--non-interactive] [--no-start]
exit /b 0
:help_fail
echo Usage: run.cmd [--setup ^| --check] [--with-copilot] [--assign-roles] [--skip-azure] [--non-interactive] [--no-start]
exit /b 2