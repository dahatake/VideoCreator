#Requires -Version 7
# Generates synthetic media that covers the PRD input edge cases (TC-EDGE, TC-VERT).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ff = Join-Path $root 'tools/ffmpeg/bin/ffmpeg.exe'
$out = Join-Path $PSScriptRoot 'media'
New-Item -ItemType Directory -Force $out | Out-Null
function FF { & $ff -hide_banner -loglevel error -y @args; if ($LASTEXITCODE) { throw "ffmpeg failed: $args" } }

# 16:9 60s with tone bursts at known times (loud events at 20s and 40s, silence 30-33s) and hard cuts every 10s
$a = "sine=f=440:d=60,volume='if(between(t,30,33),0,if(between(t,20,21)+between(t,40,41),1.0,0.15))':eval=frame"
Push-Location $out
FF -f lavfi -i "testsrc2=s=1920x1080:r=30:d=10" -f lavfi -i "smptehdbars=s=1920x1080:r=30:d=10" -f lavfi -i "testsrc=s=1920x1080:r=30:d=10" -f lavfi -i $a `
   -filter_complex "[0:v][1:v][2:v][0:v][1:v][2:v]concat=n=6:v=1:a=0[v]" -map "[v]" -map 3:a '-c:v' libx264 -preset ultrafast '-c:a' aac landscape.mp4
# vertical source (TC-VERT)
FF -f lavfi -i "testsrc2=s=1080x1920:r=30:d=20" -f lavfi -i "sine=f=330:d=20" '-c:v' libx264 -preset ultrafast '-c:a' aac vertical.mp4
# square source
FF -f lavfi -i "testsrc2=s=1080x1080:r=30:d=20" -f lavfi -i "sine=f=330:d=20" '-c:v' libx264 -preset ultrafast '-c:a' aac square.mp4
# no audio
FF -f lavfi -i "testsrc2=s=1280x720:r=30:d=15" '-c:v' libx264 -preset ultrafast noaudio.mp4
# VFR (frames dropped irregularly) in MKV
FF -f lavfi -i "testsrc2=s=1280x720:r=30:d=15" -f lavfi -i "sine=d=15" -vf "select='not(mod(n\,7))+lt(mod(n\,30)\,10)'" -fps_mode vfr '-c:v' libx264 -preset ultrafast '-c:a' aac vfr.mkv
# rotated (phone video: stored 1920x1080, display rotation 90)
FF -f lavfi -i "testsrc2=s=1920x1080:r=30:d=10" -f lavfi -i "sine=d=10" '-c:v' libx264 -preset ultrafast '-c:a' aac rot_tmp.mp4
FF -display_rotation 90 -i rot_tmp.mp4 '-c' copy rotated.mp4
Remove-Item rot_tmp.mp4
# non-video file
Set-Content notvideo.mp4 'this is not a video'
Pop-Location
Get-ChildItem $out | Select-Object Name, Length
