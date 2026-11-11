# Documentation verification and maintenance

[App README](../README.md) / [Guide index](README.md) / [Technical reference](technical-reference.md) / [Documentation brief](documentation-brief.md)

## Primary sources and verification scope

This guide investigated the working tree on 2026-11-08, preserving application changes already present and grounding explanations in implementation, settings, and tests. It includes uncommitted changes, so it is not a specification guarantee for a particular release tag.

| Subject | Verification |
|---|---|
| Startup/preparation | [run.cmd](../../../run.cmd), [setup.ps1](../../../scripts/setup.ps1), [AppOptions](../../../src/VideoCreator.App/AppOptions.cs) |
| Screens/input/operations | [app.js](../../../src/VideoCreator.App/wwwroot/js/app.js), real browser |
| API/approval/conditions | [Api.cs](../../../src/VideoCreator.App/Api.cs), [JobQueue](../../../src/VideoCreator.App/JobQueue.cs), [HTTP examples](../../../src/VideoCreator.App/exports.http) |
| Models/processing/output | [Models](../../../src/VideoCreator.Core/Models/Models.cs), [PipelineRunner](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs), Renderer, QaEngine |
| Azure prerequisites | Microsoft Learn [Fast Transcription](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create), implementation connection/authentication code |
| Screen SVGs | Actual app in Edge; PNG embedded in SVG, distinguished from schematics |
| Documentation/diagram consistency | Automated local links, heading anchors, XML, embedded images, text bounds, browser rendering |

### Execution checks performed

- Normal Release output was locked by a running process; built to **independent artifacts output** without stopping it. Success, zero warnings/errors.
- Launched dedicated DataDir/ProjectsDir/port; checked health/required filters.
- Synthetic video + cached transcript: actual UI rights → P4 generation → review → direct final export.
- All final hard QA results passed.
- Eight output keys `mp4` / `srt` / `ass` / `editplan` / `metadata` / `thumb1` / `thumb2` / `thumb3`; nonempty files retrievable through APIs.
- ffprobe: H.264, 1080×1920, AAC, 48 kHz, stereo. P4 27.20 seconds, −14.0 LUFS, −12.5 dBTP (sine-wave fixture, not speech-quality evaluation).
- Generated two series candidates from the same source; checked series structure/screens.
- All guide SVGs/local links checked with [validator](../../../users-guide/tools/validate.mjs).

Final original-guide verification: **9 Markdown files (including root README and [brief](documentation-brief.md)), 211 local links/anchors, 20 SVGs (5 schematics, 15 actual screens)**. XML, embedded PNGs, diagram text bounds, Edge rendering, helper syntax, and whitespace checks all passed. Final capture also checked SRT time syntax, ASS Events, plan Canvas/Timeline, PNG thumbnails, MP4 codec/resolution/audio/P4 duration.

**Not performed:** sending footage to real Azure/Copilot, real-speech recognition accuracy, every P1–P10 generation, every OS dialog/storage move/deletion/batch-approval operation, full repository tests, social posting, independent novice usability testing. These feature explanations use source/existing tests, distinct from this execution evidence.

Passing loudness QA on synthetic data does not prove speech/content correspondence. Check your first real authorized source yourself.

## Export-artifact UI regression (added 2026-11-11)

- [ExportArtifactTests](../../../tests/VideoCreator.Tests/ExportArtifactTests.cs): real API checks for absolute paths, missing files/formats, saved posting text, 65,536-character previews, full retrieval, video Range, edit invalidation.
- [verify-export-artifacts.mjs](../../../ui/verify-export-artifacts.mjs): Edge playback/seeking of actual synthetic H.264, per-video paths/copy targets, original download names, read-only posting text, image/text previews, automatic completion display, failures/retry, narrow screen, navigation races. APIs are fixtures; clipboard uses a test implementation without overwriting user contents. No actual Explorer launch/social posting.
- [verify-multi-export.mjs](../../../ui/verify-multi-export.mjs): approval/batch-export regression.

Launch an isolated app, verify health, then run (match the port):

```powershell
dotnet test --project tests\VideoCreator.Tests --filter-class VideoCreator.Tests.ExportArtifactTests VideoCreator.Tests.ApprovalExportTests VideoCreator.Tests.PostCopyTests
node ui\verify-export-artifacts.mjs http://127.0.0.1:18788
node ui\verify-multi-export.mjs http://127.0.0.1:18788
```

## Recreating screenshots

### 1. Prerequisites

.NET SDK 10, FFmpeg/fonts/models, Node.js, installed OS Microsoft Edge. End users need no Node.js; **developers recreating screens do**. Use existing Playwright dependency in [ui/package.json](../../../ui/package.json); run `npm ci` in `ui` if absent. No extra browser download.

Run PowerShell from repository root; replace example paths with your workspace paths.

### 2. Prepare isolated fixtures

[seed.cs](../../../users-guide/tools/seed.cs) rejects folders with existing data. Specify a new empty directory.

```powershell
dotnet run --file users-guide\tools\seed.cs -- C:\Temp\VideoCreatorGuideCapture
```

Creates a 260-second test-pattern/sine-wave video, project/asset DB records, and [talk_transcript.json](../../../tests/VideoCreator.Tests/Fixtures/talk_transcript.json) repeated twice with 130-second offset. Subtitles are **not recognition of the sine wave**. The first 130-second fixture could not satisfy two minimum-length parts after silence removal, so it was extended.

Fixture keys match the current ASR cache and empty dictionary. Update seed when cache implementation/dictionary/ASR Name changes. Do not reuse verification endpoints in production.

### 3. Start on a dedicated port

In another fresh PowerShell, without real-use overrides such as extra Dictionary variables; never point at a running app's/real project's DataDir:

```powershell
$env:VideoCreator__DataDir = 'C:\Temp\VideoCreatorGuideCapture\data'
$env:VideoCreator__ProjectsDir = 'C:\Temp\VideoCreatorGuideCapture\projects'
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
$env:VideoCreator__Asr__Provider = 'azure'
$env:VideoCreator__Asr__Endpoint = 'http://127.0.0.1:1/'
dotnet run --project src\VideoCreator.App -c Release
```

Choose another port if busy and update all URLs. LLM disabled; ASR reads fixtures. Even a cache mismatch fails at unused loopback rather than an external service.

If running binaries conflict, build/launch independent output:

```powershell
dotnet build src\VideoCreator.App\VideoCreator.App.csproj -c Release `
  --artifacts-path C:\Temp\VideoCreatorGuideBuild
dotnet C:\Temp\VideoCreatorGuideBuild\bin\VideoCreator.App\release\VideoCreator.App.dll `
  --contentRoot "$PWD\src\VideoCreator.App"
```

Use the same PowerShell with the above environment variables.

### 4. Capture and validate

After confirming readiness, in another terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:18765/api/health
node users-guide\tools\capture.mjs http://127.0.0.1:18765
node users-guide\tools\validate.mjs
git diff --check -- README.md users-guide
```

[capture.mjs](../../../users-guide/tools/capture.mjs) checks dedicated `guide-demo`-only environment, no LLM, loopback ASR before operating. It leaves generated/exported data and updates screen SVGs in original [images](../../../users-guide/images). Masks CLI paths potentially containing personal names. After capture, Ctrl+C in launch terminal; clean only the dedicated working directory when no longer needed.

## Update checklist

1. Changed UI labels/defaults/conditions: update [screens](screens.md) and captures.
2. Changed settings/API records/errors/states: update [configuration/API](configuration-api.md) and examples.
3. Changed outputs/QA/cache/timelines: update [technical reference](technical-reference.md), seed/capture.
4. Changed first-use permissions/costs/destinations: update [quickstart](quickstart.md), [README](../README.md).
5. Mechanically validate SVGs and visually inspect primary diagrams. Link validation proves neither external availability nor explanation truth.
6. Ask an independent nondeveloper first-time user to export using only the guide; record confusing fields/failures. This third-party evaluation has not been performed.
