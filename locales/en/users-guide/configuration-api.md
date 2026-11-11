# Configuration and API reference

[↑ Documentation map](../README.md) | [日本語](../../../users-guide/configuration-api.md) | **English** | [简体中文](../../zh-CN/users-guide/configuration-api.md) | [한국어](../../ko/users-guide/configuration-api.md)


[App README](../README.md) / [Guide index](README.md) / [Quickstart](quickstart.md) / [Screens](screens.md) / [Troubleshooting](troubleshooting.md) / [Architecture/extensions](technical-reference.md) / [Verification](verification.md)

## Startup configuration

The browser's language selector saves the UI display language independently of each project's video/audio language. Create a project with `POST /api/projects` and `{"name":"Example","language":"en-US"}`, or change it with `PATCH /api/projects/{id}` and `{"language":"ko-KR"}`. Supported values: `ja-JP` (default when omitted), `en-US`, `zh-CN`, `ko-KR`. Unsupported values return 422; changing language during generation/export returns 409. Generate new candidates after changing language: existing edit plans retain their creation language.

Use the `VideoCreator` section of [appsettings.json](../../../src/VideoCreator.App/appsettings.json), environment variables, or command-line arguments. Environment variables use `__`, e.g. `VideoCreator__Asr__Endpoint`. Defaults below come from [AppOptions](../../../src/VideoCreator.App/AppOptions.cs). Replace distributed endpoints with your own.

| Setting | Default / meaning |
|---|---|
| Port / OpenBrowser | `8765` / `true`: loopback port and browser launch |
| DataDir | `%LOCALAPPDATA%\VideoCreator` if unset; DB/cache |
| ProjectsDir | `VideoCreator` inside Windows MyVideos if unset; new projects. UI-saved value takes precedence |
| FfmpegBin / FontsDir / PresetsDir | If unset, searches ancestor repository tools etc. |
| FaceModel / UpscaleModel | If unset, tools UltraFace / Real-ESRGAN |
| LocalOnly | `false`; `true` forces LLM off, not ASR |
| Asr.Provider / Endpoint / Locale | `azure` / empty / `ja-JP`; ASR enabled when azure and endpoint set. For app generation, project `language` overrides Locale. |
| Llm.Provider | `azure-openai`: `azure-openai` / `copilot` / `none` |
| Llm.Endpoint / Deployment | Empty / `gpt-chat-latest`; use your deployment name |
| Llm.CopilotCliPath / CopilotModel | CLI autodetection / default model if unset |
| Llm.SingleCallCharBudget | `12000`; character budget for Standard splitting, not a token limit |
| Dictionary | `[]`; proper nouns for ASR phrase lists and unsupported-token checks |

Example: another port, no AI selection (Speech still needs configuration).

```powershell
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
dotnet run --project src\VideoCreator.App -c Release
```

Variables apply to that PowerShell process and its children; reset in a new terminal. `VideoCreator__Asr__Provider=none` disables ASR for screen inspection, not normal subtitle generation.

## Setting lifetimes

- UI LLM changes affect Runtime; restart restores startup configuration.
- UI project-location changes persist in Store settings and override startup configuration.
- Patterns/generation options persist per project, restore after restart, and are recorded in Job.Options on generation.
- Last workflow stage/job/edit-plan/export is saved in Project.WorkflowState; invalid references fall back.
- BrandKit uses shared Store; BGM is library/project scoped; image creation on/off belongs to Project.
- Presets load at startup; restart after file edits.

## HTTP contract

- Default base URL `http://127.0.0.1:8765`; prefix every route below with `/api`.
- JSON uses `snake_case`; enums are strings. Shared [Json](../../../src/VideoCreator.Core/Json.cs), [Models](../../../src/VideoCreator.Core/Models/Models.cs).
- Writes require `X-VideoCreator: 1`; JSON requires `Content-Type: application/json`.
- GET/HEAD do not need that header. It is a cross-site-write guard, not authentication/API key.
- Normal errors: `{ "error": { "code": "...", "message": "...", "detail": ... } }`. Handle HTTP status and code. Host/header rejections can be plain text.
- Async export `202` means accepted, not finished; poll returned render ID.
- General limit 64 MiB. **Video uploads remove the route-specific limit**, buffer multipart temporarily, then copy. BGM raises HTTP body limit to 2000 MiB (up to 20 tracks), with a separate multipart parser limit. Do not assume other routes are unlimited.

## Routes

[Api.cs](../../../src/VideoCreator.App/Api.cs) is authoritative for handlers/request records. `{id}` identifies each resource.

| Area | Method / route | Behavior |
|---|---|---|
| Diagnostics | GET `/health` | FFmpeg/missing filters/ASR/LLM/models; configuration/detection, not guaranteed cloud connectivity |
| Settings | GET / PUT `/settings` | Read / switch LLM, e.g. `{"llm_provider":"none"}` |
| Storage | GET `/settings/projects-dir` | path, source, configured_path, counts, move progress |
| Storage | POST `/settings/projects-dir/plan` | Preflight `{"path":"D:\\Videos\\VideoCreator","move":true}` |
| Storage | PUT `/settings/projects-dir` | Same body; change/move |
| Patterns | GET `/presets` | Presets loaded at startup |
| Project | GET / POST `/projects` | List / create `{"name":"Project name"}` |
| Project | GET / PATCH / DELETE `/projects/{id}` | Details (`resume_state` includes stage/IDs) / `{"name":"New name"}` / delete |
| Project | PUT `/projects/{id}/generation-settings` | Save pattern/options |
| Project | PUT `/projects/{id}/workflow-state` | Save `project` / `settings` / `job` / `candidates` / `exports` / `review` / `render`; `exports` requires `job_id` and Job `ready` / `done` |
| Asset | POST `/projects/{id}/assets` | `{"path":"C:\\Videos\\talk.mp4"}`; no copy |
| Asset | POST `/projects/{id}/assets/upload` | Copy video from multipart `file` |
| Rights | POST `/projects/{id}/rights` | `{"asset_id":"...","confirmed":true}` |
| Estimate | GET `/projects/{id}/estimate?preset=P4` | Scope-specific transmission/unit-price estimates |
| Asset | PATCH `/assets/{id}` | role, speaker, sync_offset, clear_speaker |
| Transcript | GET / PATCH `/assets/{id}/transcript` | Read / `{"edits":[{"word_id":"...","text":"Correction"}]}` |
| Audio | GET `/bgm?project_id=...` | Library/project tracks; `analysis` (lufs, lra, start, end, bpm, onset_rate, brightness, energy, short_term) |
| Audio | POST `/projects/{id}/bgm` | Multipart file(s), max 20; `rights_confirmed`. Analyze on registration. 201 `{tracks, skipped}`; unreadable / <5-second tracks skipped with reasons |
| Audio | DELETE `/projects/{id}/bgm/{bgmId}` | Unregister and delete file; 409 `E-BGM-IN-USE` when referenced |
| OS | POST `/dialogs/folder` | `{"initial_path":"..."}`; cancel `{"path":null}` |
| Cards | POST `/projects/{id}/scene-cards` | asset_id **or** brand_path (exclusive), count 2–6, default 4 |
| Cards | PUT `/projects/{id}/scene-cards/settings` | `{"make":true}` enables images for future generation |
| Cards | GET `/projects/{id}/scene-cards/{index}` | Extracted image, zero-based |
| BrandKit | GET / PUT / DELETE `/brandkit` | Shared registration; PUT paths, rights_confirmed, card_sec, upscale |
| BrandKit | POST `/brandkit/enhance` | Start enhancement of registered clips |
| Generation | POST `/projects/{id}/jobs` | preset_ids, instruction, options |
| Job | GET `/jobs/{id}` | State, candidate IDs, warnings |
| Job | POST `/jobs/{id}/cancel` | Cancel queued/running generation |
| Job | GET `/jobs/{id}/events` | SSE/EventSource stage/progress |
| Candidates | GET `/jobs/{id}/candidates` | candidate, preview, preview_url, approval, export, final_render_id |
| Approval | PUT `/jobs/{id}/candidates/{candidateId}/approval` | `{"approved":true}`; false revokes |
| Batch | POST `/jobs/{id}/exports` | `{"candidate_ids":["..."]}`; 202 after all validated. Any invalid candidate starts nothing; returns first applicable single-export code (`E-NOT-APPROVED`, `E-PLAN-STALE`, 422 `E-QA-HARDFAIL`, `E-EXPORT-BUSY`) plus per-candidate `code` / `reason` |
| Regeneration | POST `/jobs/{id}/regenerate` | instruction, exclude_candidate_ids; reselect excluding prior candidates |
| Edit | GET / PATCH `/editplans/{id}` | Plan/plan QA; edit with ops |
| QA review | PUT `/editplans/{id}/qa/approval` | `plan_hash`, `checks:[{check_id,result_hash}]`, `approved`; records result review, false revokes |
| Preview | POST `/editplans/{id}/preview` | Rebuild plan preview |
| Preview | GET `/editplans/{id}/preview/progress` | running, progress, step |
| Direct output | POST `/editplans/{id}/render` | Validate plan QA; accept individual export |
| Render | GET `/renders/{id}` | queued / rendering / done / failed / cancelled etc. |
| Render | POST `/renders/{id}/cancel` | Cancel queued only |
| Artifacts | GET `/renders/{id}/artifacts` | Completed export `path` (absolute), `available`, `files:[{key,path,available,preview_type}]`; types `video` / `image` / `text`, unsupported omitted. Recorded outputs only; does not generate posting text |
| Files | GET `/renders/{id}/files/{key}` | Output-key file; video supports Range |
| Preview | GET `/renders/{id}/files/{key}/preview` | Text `{text,truncated}`, first 65,536 characters; unsupported 422, missing 404, stale 409 |
| Post | GET / POST `/renders/{id}/post` | GET saved copy (generates/saves if missing); POST replaces with alternative. UI checks saved-file existence and explicitly invokes generation when absent |
| OS | POST `/renders/{id}/open-folder` | Select completed file in Explorer; missing 404, launch failure 500 `E-OPEN-FOLDER`, non-Windows 501 `E-PLATFORM` |
| Media | GET `/media/{renderId}` | Primarily preview video; Range supported |

See [Api.cs](../../../src/VideoCreator.App/Api.cs) for BGM multipart rights fields. Path APIs/OS dialogs serve a single local user, not general remote automation.

QA review hashes come from plan GET `plan_hash` and each QA `review_hash`. Individual review sends one; bulk sends all performed/unapproved checks. `review_approval` returns status. Empty/duplicate/unknown/not-run checks return 422; changed plans/results 409; no partial saves. This changes neither judgments nor caption/card/candidate approvals.

## Job.Options

| Key | Values/constraints |
|---|---|
| scope | economy / balanced / full (default balanced) |
| bgm | auto / none / scene (automatic per-scene selection from project tracks, otherwise built-ins) / track ID |
| sfx / tsukkomi | bool; AI reactions used by P3 |
| tsukkomi_style | `auto` (default) / `telop` / `card`. telop overlays without pausing; card freezes 2–3 seconds after an utterance, using image/panel, text, dedicated BGM. Auto cards require completed speech, no dependent reply next, ≥8 seconds from main-content edges/other cards/opening, and total cards ≤10% of main duration; otherwise overlays. Other values: 422 |
| parts / part_minutes | 2–20 parts **or** 1–60 minutes per part; mutually exclusive; both null means shorts |
| opening / closing / scene_cards | bool; checks usable shared registrations |
| mask_words | Array of words to mask |

The UI selects one pattern. Even though API preset_ids is an array, the pipeline uses its first item; do not treat it as multipreset comparison in one generation.

## Editing operations

PATCH `/editplans/{id}` with `{"ops":[...]}`. Follow [PlanEditor](../../../src/VideoCreator.Core/Planning/PlanEditor.cs) and API restrictions.

| op | Additional keys |
|---|---|
| replace_text | target (telop ID), text |
| set_enabled | target, enabled |
| set_type_enabled | type (caption/emphasis/headline/sfx/tsukkomi/opening/card/closing), enabled. tsukkomi affects reaction overlays/cards; card affects non-reaction transitions |
| approve / approve_all_tsukkomi | First requires target |
| set_bgm / set_sfx | First target (track ID = whole-video track / none / scene = reselect automatically); second enabled |
| set_bgm_cue | target = scene ID (`bgm1`…), text = track ID / none / auto |
| set_bgm_trim | target = scene ID, value = scene track gain (dB, −12–+6) |
| set_bgm_level | type = `ld` (voice minus BGM while speaking, 10–30 LU) / `gap` (BGM without voice, −30–0 LU relative to voice), value |
| set_insert_enabled | target (insert ID), enabled |
| replace_card_text | target, text (≤20 characters) |
| approve_card / approve_all_cards | First requires target |
| adjust | edge=start/end, delta; UI start+1 = previous sentence, end+1 = next |
| replan | Replan current candidate range |

API rejects series `adjust` / `replan`, and P5 `set_bgm`. Transcript PATCH corrects cached words, not all existing EditPlans. UI subsequently requests replan: observe series restrictions.

## Local API example

Requires speech footage and working ASR settings; may transmit externally/incur charges. Replace the source path.

```powershell
$base = 'http://127.0.0.1:8765/api'
$headers = @{ 'X-VideoCreator' = '1' }
$p = Invoke-RestMethod "$base/projects" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"name":"First API project"}'
$a = Invoke-RestMethod "$base/projects/$($p.id)/assets" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"path":"C:\\Videos\\talk.mp4"}'
Invoke-RestMethod "$base/projects/$($p.id)/rights" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body (@{ asset_id=$a.id; confirmed=$true } | ConvertTo-Json)
$j = Invoke-RestMethod "$base/projects/$($p.id)/jobs" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"preset_ids":["P4"],"options":{"scope":"balanced"}}'
Invoke-RestMethod "$base/jobs/$($j.job_id)"
```

After `status=ready`, fetch candidates and review in the UI. Approval/batch examples: [exports.http](../../../src/VideoCreator.App/exports.http). Progress: [progress.http](../../../src/VideoCreator.App/progress.http); storage: [projects-dir.http](../../../src/VideoCreator.App/projects-dir.http); cards: [scene-cards.http](../../../src/VideoCreator.App/scene-cards.http).
---

[↑ Documentation map](../README.md) | [日本語](../../../users-guide/configuration-api.md) | **English** | [简体中文](../../zh-CN/users-guide/configuration-api.md) | [한국어](../../ko/users-guide/configuration-api.md)
