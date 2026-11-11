# VideoCreator implementation and verification report

- Target: [PRD.md](PRD.md), VC-PRD-001 v1.2.3; historical results and current rechecks are distinguished.
- Date: 2026-11-07; recheck 2026-11-08, AT-13–15 2026-11-09.
- Implementation: C#/.NET 10, ASP.NET Core Minimal API local web app, Fluent 2 Web Components v3.
- Environment: Windows 11 ARM64, FFmpeg N-128532 BtbN win-arm64 GPL, Azure Japan East.

## 1. Approach

Before integrating each PRD feature, **small standalone programs in spikes/** measured its thresholds. Afterwards, 67 unit/integration tests and real Edge + Azure E2E (`ui/e2e.mjs`, nine checks; `ui/e2e-v11.mjs`, 29) verified the implementation.

| Spike | Verification | Result | Discovery → design |
|---|---|---|---|
| [01-ingest](../../../spikes/01-ingest.cs) | ffprobe, VFR, rotation, invalid files, CFR proxies | All pass | MKV metadata does not reveal VFR → **detect it from packet timestamp differences**. |
| [02-signals](../../../spikes/02-signals.cs) | Shot boundaries (scdet), 100 ms loudness, silence, peaks | All pass, within ±0.1 seconds of ground truth | Standard FFmpeg scdet is sufficient instead of PySceneDetect, the PRD D-4 alternative. |
| [03-render](../../../spikes/03-render.cs) | ASS, blurred backgrounds, punch-ins, loudness, determinism, portrait sources | All pass | (1) cropdetect falsely passes → **measure text bounding boxes directly from raw pixels**. (2) One-pass loudnorm yields −17.3 LUFS; two-pass linear silently falls back to dynamic and yields −15.1 → **measure after export and loop audio-only corrections**. |
| [04-asr](../../../spikes/04-asr.cs) | Keyless Fast Transcription, word timings, diarization | CER 11.5%, speaker purity 97–99%, 125 seconds in 3.9 seconds | Numeral spelling, e.g. 一万→1万, dominates differences → **compare kanji and Arabic numerals by value**. |
| [05-llm-select](../../../spikes/05-llm-select.cs) | Microsoft Foundry Structured Outputs | Four candidates, all checks pass | Strict mode does not support `maxLength`/`minimum`/`maxItems`, per Microsoft Learn → **enforce constraints in code**. |
| [06-copilot-sdk](../../../spikes/06-copilot-sdk.cs) | GitHub Copilot SDK with OS-installed CLI | Pass | Agent sends intermediate and corrected JSON messages, with duration/interval-count violations → **extract latest complete JSON and apply the same validator**. |
| [07-make-long](../../../spikes/07-make-long.cs) | Generate a 64-minute Japanese video from an LLM script and TTS | 1,252 lines, 64 minutes | Used for performance and long-form splitting tests. |

These sources are synthetic FFmpeg patterns + Azure TTS to avoid rights problems. Only §2.2 uses user-designated, explicitly transmission-authorized real footage. §2.3 uses fixed transcripts/synthetic FFmpeg media, not Azure.

## 2. Acceptance tests (PRD §14.1)

> **Note, PRD 1.2.3:** the following results use conditions from version 1.1 or earlier. Revised AT-01/AT-03 verdicts are in §2.1, AT-10 in §2.2, and AT-13–AT-15/latest quality gates in §2.3. See §17 verification for AT-16/AT-17 added in PRD 1.2.4. Partial synthetic/mock verification is not equivalent to full acceptance on the designated corpus and actual cloud.

| ID | Result | Evidence |
|---|---|---|
| AT-01 | ✅*1 | Three P4 candidates from a 64-minute Japanese two-speaker video in **193 seconds**, target ≤25 minutes. All pass hard QA, with four score axes and reasons. |
| AT-02 | ✅ | E2E export: ffprobe **1080×1920, 30.90 seconds**, loudnorm **−14.0 LUFS/−1.5 dBTP**. Integration test `Final_render_meets_platform_spec_and_passes_hard_qa`. |
| AT-03 | ✅ | Render the same plan twice; video-stream SHA-256 matches (`Rendering_is_deterministic`, spike 03). |
| AT-04 | ✅ | Move mid-sentence intervals to sentence boundaries and pass H-06 (`Solver_snaps_mid_sentence_edges_and_never_cuts_words`). |
| AT-05 | ✅ | H-07 rejects “5 million yen,” absent from transcript (`UngroundedClaims_flags_invented_numbers`, `Qa_fails_ungrounded_and_unapproved_telops`). |
| AT-06 | ✅ | Captions ≤14 characters, no initial particles or orphan lines (`Captions_respect_width…`, `Caption_lines_are_balanced_without_orphans`). |
| AT-07 | ✅ | Three LLM schema violations → signal-based candidates and specified message (`Selection_falls_back_to_signals_after_retries`). |
| AT-08 | ✅ | Actual API returns 409 `E-RIGHTS-REQUIRED` without rights confirmation. |
| AT-09 | ✅ | Editing a telop to 22 characters displays S-02 warning in **39–47 ms**, E2E including UI round trip. |
| AT-10 | ⚠ Historical v1.0 result; v1.1 sends transcription to Azure even in “local only”; PRD 1.2 revised conditions; current verification in §2.2 | Sample app/FFmpeg-child TCP connections every 100 ms during local-only job: **zero external**. Positive-control Azure mode detects `20.191.161.227:443` with the same method. |
| AT-11 | ✅ | Portrait sources pass without blur/black bars, source comparison PSNR 45 dB (`Vertical_source_passes_through`). |
| AT-12 | ✅ | Judge safe areas by **measuring rendered output**; deliberately overflowing settings fail (`SafeZone_check_detects_violations`). |

*1 The PRD's TC-JA-60 in-house interview did not exist, so a 64-minute synthetic video using an LLM-written script read by TTS was substituted.
*2 This is OS TCP-table sampling, not packet capture.

### 2.1 PRD 1.2.1 revalidation

On 2026-11-08, reinvestigated the concerns raised in the v1.2 revision and implemented/verified the following.

Before the additional §2.2 telemetry correction, `dotnet test --project .\tests\VideoCreator.Tests` passed **191/191, zero skipped, in 4:04**. Afterwards, the relevant `PrdVerificationTests` passed **4/4**, and actual generation was checked in §2.2. Existing CS8321 (V11Tests) and CS8604 (V12Tests) warnings remain. Syntax checks of three JavaScript files and `git diff --check` also passed. These are implementation regressions, not evidence for uncalibrated quality gates.

| Target | Result | Reproducible evidence / conditions |
|---|---|---|
| UI mode labels/destinations | Pass | Header/settings renamed “No-LLM mode.” Before Generate, show ASR origin and Entra ID authentication transmission; refresh on setting changes. [verify-prd.mjs](../../../ui/verify-prd.mjs), real Edge, **12/12**. Only candidate responses use fixtures; no cloud generation. |
| FR-TEL-02 extension limit | Boundary behavior passes; quality uncalibrated | Limit to 0.5 seconds, next caption, and clip end. [PrdVerificationTests](../../../tests/VideoCreator.Tests/PrdVerificationTests.cs) checks long silence/short clips preserve text and warn S-01 for remaining excessive speed. No Japanese-reader readability/synchronization assessment. |
| AT-01 | Partial; acceptance not passed | [RenderingTests](../../../tests/VideoCreator.Tests/RenderingTests.cs), real FFmpeg, checks preview H-02–H-04 `stage=final,status=not_run`, performed plan QA `stage=plan,plan_hash`. Edge confirms not-run has no success coloring. H-11 done in §2.3, but designated TC-JA-60 interview acceptance remains incomplete by user instruction. |
| AT-03/NFR-REL-04 | Pass for synthetic candidate/current environment | Render twice; **all decoded frames** from `ffmpeg -map 0:v -f framemd5 -` match, as do reproduction conditions (1)–(7). `Final_export_creates_exactly_one_render_record` verifies SQLite-persisted `reproducibility`/`plan_hash`. Store file paths as `{path,sha256}` arrays to avoid JSON dictionary-key conversion damaging them. |
| AT-10 | Pass for this configuration, §2.2 | In addition to mocks, complete authorized real-video P4 generation in both modes. No-LLM: ASR POST one, LLM zero; normal: ASR one, LLM seven. Verify pre-generation destinations and proxy bypass observation; scope/raw records/limits in §2.2. |
| AT-13–AT-15 | Pass, §2.3 | Specified counterexamples; real FFmpeg 10-second rejection/27-second single candidate; real API final-QA invalidation and rendering races. Edge fixtures separately verify UI, not quality gates. |
| Initial targets 95%/90%/CER +0.5 percentage points | Unverified | No designated-corpus job logs/initial candidate QA/ten human references. Do not substitute synthetic footage or unit-test pass rates. Definitions, calibration, held-out reassessment in [PRD §15.6](PRD.md#156-initial-values-and-gates). |

[RenderProvenance.cs](../../../src/VideoCreator.Core/Rendering/RenderProvenance.cs) records reproduction conditions. The subtitle runtime exposes libass API/source and FriBidi/HarfBuzz identifiers, but not FreeType's version. Rather than guessing, fix embedded libraries through the statically linked FFmpeg executable's SHA-256. NFR-REL-04 explicitly distinguishes this.

At startup, convert legacy preview results such as “540×960, H-02 passes” to production checks marked `not_run`. This migration is idempotent and does not rewrite final results or unknown `plan_hash` values.

PowerShell:

```powershell
dotnet test --project .\tests\VideoCreator.Tests
node --check .\src\VideoCreator.App\wwwroot\js\app.js
node --check .\ui\e2e.mjs
node --check .\ui\verify-prd.mjs
```

For UI verification, use a separate PowerShell and isolated data, then run from another terminal. `example.invalid` tests destination display/LLM prohibition, not an actual cloud destination:

```powershell
$env:VideoCreator__DataDir = Join-Path $env:TEMP 'vc-prd-ui-data'
$env:VideoCreator__ProjectsDir = Join-Path $env:TEMP 'vc-prd-ui-projects'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__Port = '18765'
$env:VideoCreator__LocalOnly = 'true'
$env:VideoCreator__Asr__Endpoint = 'https://speech.example.invalid/'
dotnet run --project .\src\VideoCreator.App --no-build --no-launch-profile
# Run in another terminal; afterwards stop the server above with Ctrl+C.
node .\ui\verify-prd.mjs http://127.0.0.1:18765
```

**Shipping has not passed.** AT-10 and AT-13–AT-15 are complete, but neither the whole PRD nor M2/M3 is considered complete until designated-source AT-01, human evaluation, and quality gates are satisfied. AT-01 is held by user instruction. Unrelated changes were preserved; no commit was made.

### 2.2 AT-10 actual traffic

On 2026-11-08, with explicit transmission permission, **completed P4 generation in both modes on the entire designated MOV**. AT-10 passes for the Azure CLI authentication configuration tested.

- Input `20260615_024326000_iOS.MOV`: 1081.42 seconds, about18:01, 6,494,572,403 bytes, 3840×2160 HEVC/AAC48kHz stereo.
- SHA-256 `fe9c763fdf6fe7be4f368bfba926a1f3f2407ceaf044ec7d055e0869e65c1a9f`; registration results match in both projects.
- Separate projects/data/ASR caches by mode. Reuse only locally generated `proxy.mp4`/`audio16k.flac`, never transcripts; call ASR anew for each mode. Each multipart POST:36,502,012bytes.
- P4/balanced, no BGM/SFX/reactions/opening/closing. Model `gpt-chat-latest` only in normal mode. Final export excluded.

| Record | No-LLM | Normal positive control |
|---|---|---|
| Job |job_79d02f446d|job_9c43b77c99|
| Start→ready, UTC |15:06:47→15:13:35|15:09:24→15:24:53|
| Candidates/previews |All three generated, signal-based|All three generated, LLM|
| ASR POST |One, HTTP200|One, HTTP200|
| LLM POST |**Zero**|**Seven, all HTTP200**|
| Forbidden attempts/blocks/proxy errors |Zero|Zero|
| Direct external app/child TCP observed |Zero|Zero|
| .NET socket ConnectStart |One, dedicated proxy only|Eight, dedicated proxy only|
| Pre-generation destination |Verified real Edge/settings JSON|Verified real Edge/settings JSON|

**Observation and failures**

ASR and LLM share an Azure host, so IP/hostname alone cannot distinguish them. A dedicated loopback HTTPS proxy per mode temporarily terminated TLS, distinguishing `POST /speechtotext/transcriptions:transcribe` from `POST /openai/v1/chat/completions`. Logs contain only time/destination/method/path without query/response status/byte count. No authentication headers, audio, transcript, or LLM-response bodies were saved. Upstream TLS certificate checks remained enabled. No video frames were transmitted.

The first no-LLM run's Azure CLI authentication child attempted telemetry to `dc.services.visualstudio.com`. This **failed AT-10 even though the proxy blocked it**, and is excluded from passing evidence. [Runtime.cs](../../../src/VideoCreator.App/Runtime.cs) now sets `AZURE_CORE_COLLECT_TELEMETRY=false` in the app's own process environment, inherited by authentication children. The user's global Azure CLI configuration is untouched. After four relevant tests passed, both modes were rerun for the table above.

Also monitor .NET socket events and IPv4/IPv6 TCP tables for the app/descendants. Verify FFmpeg detection and observation through app exit. No-LLM/normal sample counts:11,439/27,172. Configured20ms interval, actual maximum484/500ms. **Sampling alone cannot exclude every brief native connection**; this is not full-OS packet capture or proof for all authentication methods. Proxy/.NET events are primary. Cached CLI tokens meant no Entra ID HTTP requests appeared.

Raw evidence remains in the verification session's `files\at10`: `at10-summary.json` has verdicts, job IDs, input hash, conditions, and evidence SHA-256; `validate-evidence.py` performs machine judgment; per-mode JSONL retains traffic/socket records; `preflight.json`/screenshots show destinations; `result.json` records completion. Initial failed telemetry evidence is retained too. Private footage/transcripts/screenshots were not copied into the repository. Apps/monitor/proxies stopped; temporary CurrentUser Root certificate and private-key file deletion verified.

This covers **one source, P4, this Azure CLI authentication route**. It does not substitute an18-minute video for TC-JA-60 or ten human references, and at that time did not change AT-01/H-11/AT-13–AT-15/quality-gate verdicts.

### 2.3 AT-13–15 implementation/verification

On 2026-11-09, implemented the specified counterexamples, error wording, candidate counts, and stale-result responses. **AT-13–AT-15 pass.** AT-01 remains incomplete as requested. Quality gates lack the mandatory evidence listed below and have not passed; the overall task cannot be reported as complete through quality-gate verification.

| Target | Evidence |
|---|---|
| AT-13 | [AcceptanceGroundingTests](../../../tests/VideoCreator.Tests/AcceptanceGroundingTests.cs), four fixed counterexamples: H-11 rejects company A/B amount confusion, wrong P7 speaker attribution, and a product found only in the dictionary. Changing “did not sell” to “sold” warns S-09. Simultaneously verify H-07 misses these association mismatches. Also test correct amounts/speakers, full-width/kanji numerals, signs/decimals, discontinuous/out-of-order/out-of-range word IDs, and reaction exemptions. |
| AT-14 | [AcceptanceGenerationTests](../../../tests/VideoCreator.Tests/AcceptanceGenerationTests.cs) uses real FFmpeg synthetic 10-/27-second media and fixed ASR. Reject ten seconds before ASR; generate **exactly one real preview** for 27 seconds and record shortage reason. [ApprovalExportTests](../../../tests/VideoCreator.Tests/ApprovalExportTests.cs) verifies API422 `E-SOL-INFEASIBLE`, exact message “Source is shorter than P4's minimum duration (25 seconds),” and no added Job. |
| AT-15 | [ApprovalExportTests](../../../tests/VideoCreator.Tests/ApprovalExportTests.cs) first verifies real Kestrel/FFmpeg production output, all hard checks passing, and successful retrieval. Change money text: hash changes, approval/QA/final results invalidate, H-11 fails; single/bulk exports, sidecars/media delivery/open-folder operations return409. Public edits during rendering reject without mutation; direct-DB race produces `E-PLAN-STALE`, no saved Render, quarantined output. |
| UI | [verify-acceptance.mjs](../../../ui/verify-acceptance.mjs), actual Edge, **11/11**: original speech/speaker/source times, evidence-ID rebinding, stale display, export blocked on H-11, no pre-edit preview shown as current QA success, no stale-output links, reapproval after correction, approval before export, one candidate and explanation. **API response fixtures**; backend verdicts come from the separate tests above. |

History:

- Initial related tests: **22/22 pass, zero skipped**.
- Full regression including cross-cutting changes: **208/208 pass, zero failures/skips, 2:35**.
- Afterwards additionally protect/verify invalidation of other projects sharing ASR caches, and the latest valid state after correct re-export. Latest `ApprovalExportTests`: **7/7, zero failures/skips, 1:20**. Distinguish the208-test full run as preceding this last addition.
- JavaScript syntax passes; existing CS8321/CS8604 warnings unchanged. Stop UI helper and verify no listener on18766.

Scope/compatibility:

- [TelopGrounding](../../../src/VideoCreator.Core/Planning/TelopGrounding.cs) is shared by generation/edit QA. Evidence is contiguous in-range words, contiguous number/unit/subject expressions, and speaker. Normalize numeral spelling without collapsing decimals/signs. Dictionaries alone are not evidence. Remove invalid generated items, recording why.
- This is conservative phrase matching, not guaranteed detection of all semantic changes/negation/names. It does not identify every kanji proper noun absent from dictionaries. Resolve false rejection of legitimate paraphrases through source-like wording and rebinding evidence; approval never bypasses H-11.
- After generation, check actual duration and post-solver overlap. Treat overlapping teaser/body intervals as sets, preventing duplicate-source padding.
- [Store.Atomic](../../../src/VideoCreator.Core/Storage/Store.cs) protects approval/checking/queue reservation/final save. Save production files per render ID without overwriting old outputs. Quarantine stale artifacts in `rejected`, never distribute them.
- Keep old finals, but results without a current-hash H-11 pass require reapproval/rechecking/re-export. Old previews remain viewable with warnings; never rewrite old QA as proof for the new plan.
- Transcript correction invalidates related hashes, including other projects sharing the same source-hash cache; reject while any affected plan exports. Do not label the latest valid final stale merely because old output remains.
- Move camera/chapter checks to S-13/S-14, distinct from S-09/S-10. Do not restore an external detector separately removed during shared work; remove only remaining pipeline references, preserving local laughter detection.

#### Quality gate verdict

One authorized real source (§2.2) and synthetic fixtures do not supply these mandatory inputs. **Rates, confidence intervals, and p-values are unmeasured.** Never fill unknown denominators with zero, substitute test pass rates, or relax thresholds.

| ID | Current | Missing |
|---|---|---|
| QG-01 | Not passed: designated corpus not run | Required TC-JA-60/P4/P7, TC-JA-LEC/P6, TC-JA-GAME/P4/P1/P3, TC-COOK/P8/P9, TC-VERT, TC-EDGE and complete records: output hashes/QA, started/ready counts, jobs targeting three candidates, initial unedited-candidate QA. Safety tests prove implementation(a), not whole-corpus measurement. |
| QG-02 | Not passed: sample/independent ratings missing | Authorized ≥20 sources' P4/B1/B2 outputs, raw all-pair votes from ≥5 uninvolved judges, presentation-order/side seeds, stimulus hashes. Source-cluster bootstrap10,000 and mixed-effects logistic sensitivity analysis required. Neither AI votes nor repeated identical footage substitute. |
| QG-03 | Not passed: reference labels missing | Fixed conversation-first Mr.HiSum200 subset/selection seed, top10% replay reference intervals, P4 selections. Require1,000 same-duration random samples/video and10,000 one-sided paired permutations. |
| QG-04 | Not passed: human references missing | Ten human transcripts, ASR/caption① from identical extracts; same NFKC/whitespace-punctuation removal and Levenshtein CER. Source bootstrap10,000,seed20261108,95%CI of difference; upper +0.5percentage-point criterion unmeasured. |

Holding AT-01 does not authorize omitting TC-JA-60 from QG-01 or passing gates. Without these inputs, gates cannot be declared complete. Shipping remains not passed.

Rerun PowerShell;do not build and run UI server concurrently;stop server afterwards:

```powershell
dotnet test --project .\tests\VideoCreator.Tests
dotnet test --project .\tests\VideoCreator.Tests --filter-class 'VideoCreator.Tests.ApprovalExportTests'
node --check .\src\VideoCreator.App\wwwroot\js\app.js
```

UI dedicated server,other terminal `node .\ui\verify-acceptance.mjs http://127.0.0.1:18766`:

```powershell
$env:VideoCreator__DataDir = Join-Path $env:TEMP 'vc-acceptance-ui-data'
$env:VideoCreator__ProjectsDir = Join-Path $env:TEMP 'vc-acceptance-ui-projects'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__Port = '18766'
$env:VideoCreator__LocalOnly = 'true'
$env:VideoCreator__Asr__Provider = 'none'
$env:VideoCreator__Llm__Provider = 'none'
dotnet run --project .\src\VideoCreator.App --no-build --no-launch-profile
```

## 3. Nonfunctional measurements

| ID | Target | Measured |
|---|---|---|
| NFR-PERF-01 |60-minute input→candidates, p50≤15minutes|**193seconds**: ingest28,ASR54,analysis19,AI selection5,preview84.|
| NFR-PERF-02 |Regenerate changed pattern≤2minutes|**112seconds**; cached ASR/signals, most time in previews.|
| NFR-PERF-03 |30-second export≤60seconds|30-second export11.8seconds,spike03;app57-second export40seconds including two-pass volume/QA.|
| NFR-COST-01 |Pre-generation estimate|¥65.6 estimate→¥65.1 actual for64minutes; Azure Retail Prices API,JPY/japaneast.|
| NFR-COST-03 |AI input≤50% of full text|**46%**,34.2k→18.8k input tokens;tradeoff§6.|
| NFR-REL-02 |Resume|Restart requeues unfinished jobs; cache each stage's artifacts.|
| NFR-REL-04 |Determinism|See AT-03.|
| NFR-SEC-01/05/06 |Loopback/path/injection safety|127.0.0.1 only;other Host400;POST without custom header403;writes confined to project;FFmpeg argument arrays;ASS escaping, tested.|
| NFR-SEC-02 |Keyless|`DefaultAzureCredential`,resource local authentication disabled.|
| NFR-MNT-02 |Filter verification|`/api/health` reports missing required filters;none missing here.|

## 4. E2E: real browser/Azure

[ui/e2e.mjs](../../../ui/e2e.mjs) drives the app with OS-installed Microsoft Edge, headless. Latest **9/9 pass,91seconds**.

- UX-01: select file→rights→pattern→generate→candidate, **five clicks**.
- Every candidate has scores/reasons; previews pass hard QA.
- Export passes all nine H-01–H-09 checks and produces MP4/SRT/EditPlan/ASS/three thumbnails/metadata suggestions.

Historical capture paths in the source report: `docs/screenshots/02-assets-patterns.png`, `03-progress.png`, `04-candidates.png`, `05-review.png`, `06-export.png` (not available as tracked assets here).

## 5. E2E defects corrected

| Discovery | Cause | Prevention |
|---|---|---|
| Rights text invisible |Fluent v3 `fluent-checkbox`/`fluent-switch` lack label slots|Use `fluent-field` label/input slots.|
| Development exception/stack trace returned |`launchSettings.json` forces Development|Remove it;add JSON exception handler.|
| Candidate3 telop outside screen,detected H-05 |13-character sentence used as unwrapped emphasis|Limit emphasis to ten characters;autoshrink all text within safe width;regression test.|
| Orphan final line,「…いっぱい」/「ですよ」 |Greedy line wrapping|Balance line widths with dynamic programming;regression test.|
| Overlapping emphasis |Nearby events share same line|Resolve in time order;regression test.|
| Workflow steps clipped top/bottom |Grid row containing scroll container has zero minimum height|Fix layout.|

## 6. Decisions/tradeoffs

1. **Long-input narrowing, NFR-COST-03, versus quality: selectable in-app since v1.1.** Project generation settings offer Economy/Standard/Full, immediately displaying AI transmission amount/cost. Measurements on64-minute `spikes/media/long.mp4`,actual Azure;ASR cached,so time begins at AI selection:

   | Scope | Sent | Input tokens | Actual cost | Top score | Time |
   |---|---|---|---|---|---|
   | Economy |~31%,2ranges|12,010|¥62.72|9.06|66s|
   | Standard |~46%,3ranges|18,710|¥63.97|8.52|85s|
   | Full |100%|34,224|¥65.09|9.40|103s|

   Most cost is ASR,¥57/hour,so difference is about¥2–3/hour. Synthetic audio has little loudness variation; narrowing success/failure differs for real footage. Choose Full to reduce missed highlights.
2. **Subtitle display speed,S-01:** verbatim fast speech can exceed six characters/second,warning only. The source's historical record says FR-TEL-02 third-stage LLM shortening was implemented. The later verbatim policy in§7/§17 supersedes that behavior. v1.2.1 enforces≤0.5-second extension/clip end;readability calibration§2.1.

## 7. Unimplemented features and limitations: explicit inventory

Version1.1 implemented every previously unimplemented v1.0 item. Its addendum also completed the following formerly unverified/unimplemented items:

| Item | Implementation/verification |
|---|---|
| P8 real-footage speedup |Five-minute public-domain NASA `spikes/media/rollout.webm` with speech at known times. Compress110seconds into9.5,11.6×;two speech sections stay1×;all hard QA passes. **Azure Speech retranscription recovers100% of retained speech characters**, independent verification.|
| Crossfade,FR-SWT-05 |Only scene transitions jumping>3source seconds;P6 default0.3seconds,`scene_fade_sec`. Audio hard cuts;extend previous video by overlap so duration unchanged. Actual frame colors verify before→blend→after,and no blending for continuous sections.|
| Semantic loop,FR-SOL-06 |LLM scores five endings against opening sentence,selects highest;applied to all3/3E2E candidates.|
| Verbatim captions,FR-TEL-02 |No AI shortening in generation/repair. Keep fillers/endings/backchannels/repetitions;change timing/boundaries/merging only. Retain unresolved S-01 warnings;decorative/card AI edits separately require approval.|
| Transcript editing,FR-ASR-06 |Review “Correct transcript” changes words,not timestamps;rebuilds captions and saves corrections in cache;E2E verified.|
| Laughter,FR-SIG-04 |Among loud nonword sounds,only3–8Hz pulses,like laughter. Exclude steady applause-like noise/single impacts;synthetic-audio tests.|
| Speaker-camera mapping,FR-SWT-01 |Assign relatively loudest speaker per camera microphone. Shared microphones that cannot distinguish warn/fall back master=speaker1/cameraB=speaker2;manual override;synthetic tests.|

Remaining:

| Scope | Status |
|---|---|
| Fully offline transcription |None;mandatory subtitles always use Azure Speech,transmitting audio.|
| Laughter accuracy on real audio |Synthetic behavior checked;no accuracy evaluation on recorded laughter/applause because evaluation data unavailable.|
| Speaker-camera mapping |Separate microphones only;face-position assignment unimplemented.|
| Humor of onomatopoeia/reactions |Rule-based effects/approval-gated AI reactions;user judges humor.|
| Loop SSIM |LLM evaluation only;PRD allows “or.”|
| QG-01–QG-04,real-material quality |Not passed. Planned future user evaluation is not completion;required data/calibration inPRD§15.6.|

## 8. Azure resources

| Resource | Value |
|---|---|
| Resource group |`rg-videocreator-jpe`,Japan East|
| AI Services |`videocreator-ai-40800`,S0,custom domain,local authentication disabled|
| Deployment |`gpt-chat-latest`,2026-10-07Preview,GlobalStandard100KTPM,automatic upgrade to new default versions|
| Roles |User assigned `Cognitive Services User`/`Cognitive Services OpenAI User`|
| Budget |`videocreator-monthly`,100,000/month billing currency;email at actual80%/forecast100%|

When no longer needed,delete with `az group delete -n rg-videocreator-jpe`.

## 9. Series with a specified count or duration

Divide the entire long source by **count,2–20parts**, or target duration,1–60minutes/part,never both. Label first/last,add middle for three,number episodes for≥4;apply the same editing as existing patterns. Applies to speech-led P1–P4/P6/P7/P9/P10. Implementation:[Series.cs](../../../src/VideoCreator.Core/Planning/Series.cs);verification:[SeriesTests.cs](../../../tests/VideoCreator.Tests/SeriesTests.cs),[spikes/13-series.cs](../../../spikes/13-series.cs).

| Stage | Behavior |
|---|---|
| Outline |AI Structured Outputs groups a line-numbered transcript by topic,returning split ease—natural preceding end and interest in continuation—highlight lines and terms. Code validates line indices/highlights/headings/emphasis,discards out-of-range values,and permits heading numbers/names only from transcript. Without AI/on failure,use TextTiling,Hearst1997,content-word character bigram cohesion plus transition phrases.|
| Count |Exact requested count;reject counts giving<1edited minute/part and show maximum. Duration mode N=edited duration/target,rounded;error if<2. UI immediately estimates per-part duration;with transcript,use same post-silence-removal calculation as splitting,otherwise explicitly label original-duration estimate.|
| Boundaries |Candidates only at sentence boundaries. Dynamic programming minimizes squared deviations from equal durations minus boundary quality:lexical shifts,pauses,transition phrases,speaker changes,AI ease. Tolerance±25%;expand to±40%with notice if needed. If boundary is neither AI chapter nor transition phrase,as when parts exceed topics,warn which parts/time. Closure/continuation/laughter/fades/missing punctuation cues:§14.|
| Structure |Begin each part with its own highlight sentence,cold open. Parts after first recap preceding part's last1–3sentences. Nonlast parts end with next part's teaser marked continuation. Body uses silence jump cuts,punch-ins,chapter headings,and normal-pattern captions/emphasis.|
| Coverage |Bodies do not overlap;every word belongs to exactly one body. Recaps/previews intentionally repeat.|
| Long-form rendering |(1) Open reordered teaser/recap/preview clips as separate accurate`-ss`inputs per forward-moving run;sharing one input buffers later clip frames while waiting for an earlier-played clip. (2) Hundreds of cuts exceed Windows32,767-character command limit;pass graph via`-/filter_complex`file. (3) Branch many clips sharing input with`split`/`asplit`,~25%faster. (4) Compose previews directly at half resolution,ASSPlayRes preserving placement. (5) Measure safe areas on widest/evenly sampled telops,max160.|
| QA |H-01 uses per-part range,±30%equal duration. H-07 checks whole transcript because series names can refer to another part;part labels such as episode2 are not factual claims. Historical S-10 checks YouTube chapters:start00:00,≥3,each≥10seconds,YouTube Help“Video chapters”;subsequently S-14. S-11 checks≤60minutes,TikTok upload maximum.|
| Metadata |Add first/last labels to titles;merge chapters<10seconds backward to satisfy conditions;provide series-part link fields. No#Shorts for>3minutes,even portrait,as YouTube does not classify it as a Short.|

### Verification: source/Azure, `dotnet run spikes/13-series.cs`

Use the first30minutes of the64-minute LLM-script/TTS test video. Each topic begins“Next segment…”;these sentence positions supply **ground-truth topic boundaries**,19intervals.

| Item | Result |
|---|---|
| Two15-minute parts |Boundary **15:22**,exact topic start,0.0seconds error. Clock15:00 is22.8seconds from nearest topic and cuts midway through「今日は」.|
| AI outline |19chapters versus19true;**Pk0.000/WindowDiff0.000**.|
| No AI,lexical shift/pause/transition phrase |16:45,exact boundary;Pk0.115.|
| No AI or transition phrases,lexical shift/pause only |16:51,six-second error,Pk0.384. Honest limitation:without AI,accuracy falls when source lacks explicit transitions.|
| Coverage |All5,563words exactly once in bodies.|
| ffprobe |First13:35/last13:31,26.9edited minutes equally split;silence removal saves~3minutes;1080×1920.|
| ebur128,independent of rendering loudnorm |Both−14LUFS;TruePeak−1.4/−1.5dBTP.|
| QA |All hard checks pass;S-10 nine/ten chapters;S-11 passes.|
| Independent final-video AzureSpeech retranscription |Body character-bigram recall**98.4%/98.5%**,including ASR variability;cold open/recap/next preview audible at planned positions,100%each.|
| Accurate seeking |Unit test confirms new seek-input versus previous single-input paths produce **bit-identical video**.|
| WindowsARM64 time |Generation10minutes,ASR/outline/faces/two previews;~11minutes per nearly15-minute1080×1920export including two-pass volume/QA,measured before split optimization.|

#### Three parts: `dotnet run spikes/13-series.cs spikes/media/long30.mp4 parts=3`

| Item | Result |
|---|---|
| Count |Exactly**three**,first/middle/last.|
| Boundaries |**10:33/20:06**,both exact ground truth,0.0seconds. Clock10:00/20:00 cuts midsentence/midword「バ」.|
| UI estimate versus ffprobe |Estimated8:57;measured9:05/8:54/9:22,+2%/−1%/+5%.|
| ebur128 |All−14LUFS;TruePeak−1.4to−1.5dBTP.|
| Retranscription |Body recall98.1%/98.5%/98.2%;all cold opens/recaps/next previews audible at planned positions.|
| QA |All hard checks pass;S-10 six/seven chapters;S-11 passes.|
| Invalid requests |Both count/duration→422;excessive count→422,UI explains maximum.|
| Time |Generation5minutes,cached transcript;each~9-minute part exports in4.3–4.9minutes after split optimization.|

## 10. Openings, closings, and transition reaction cards

Insert a channel's opening/closing videos and 2–3-second reaction cards (default 2.5 seconds) that appear to pause the video. Implementation: [Packaging.cs](../../../src/VideoCreator.Core/Planning/Packaging.cs) (mapping, registration, placement), [Renderer.cs](../../../src/VideoCreator.Core/Rendering/Renderer.cs); verification: [PackagingTests.cs](../../../tests/VideoCreator.Tests/PackagingTests.cs).

Choose how P3's AI reactions appear using generation option `tsukkomi_style`: `telop` (a red-backed overlay without stopping the video), `card` (a pause card immediately after the associated statement), or `auto` (default). For `auto`, [TsukkomiPlacement](../../../src/VideoCreator.Core/Planning/Packaging.cs) decides individually. A card is allowed only where pausing interrupts nothing: (1) at least three seconds from either end of the main body; (2) the associated caption ends in a complete statement or question (`SplitCues.Closure` ≥ 0.6; do not interrupt a statement ending in “but…” or “this…”); (3) the next caption is not a response/continuation such as “Exactly” or “therefore” (`SplitCues.Continuation` ≤ −0.6); (4) at least eight seconds from transition cards, the opening, and other reaction cards, matching the transition-card interval; (5) total card time ≤10% of the body. Rank candidates satisfying (2)/(3) by “closure + pause before the next statement (capped at one second),” then apply (4)/(5). Items not selected become overlay text rather than being discarded. Candidate notes retain each decision's reason. Tests: rule-specific counterexamples in [PackagingTests.cs](../../../tests/VideoCreator.Tests/PackagingTests.cs), generation/approval gates for all three modes in [V11Tests.cs](../../../tests/VideoCreator.Tests/V11Tests.cs), and persistence in [ProjectGenerationSettingsTests.cs](../../../tests/VideoCreator.Tests/ProjectGenerationSettingsTests.cs).

If a `card`/`auto` card falls inside a clip, split there while preserving the original video/audio and starting positions for camera/screen tracking and PiP. Pause source audio during the card and briefly fade independently synthesized `upbeat` BGM in/out, independently of body BGM selection. Mute body BGM during the card to avoid doubling. If an image is registered, pop the text over it; otherwise use an original full-screen background panel with an inset yellow frame. Edit/approve in the review screen's card section. Existing saved overlays/card audio are not converted automatically: regenerate to receive the new presentation. Old one-second registered settings become two seconds for new generation.

Additional real-FFmpeg tests with/without an image measure the extra 2.5 seconds, a still image during the card, equality of resumed frames to output without inserts, text pixels, dedicated BGM, and source-audio stop/resume. Automated tests also cover splitting at changed speed, separate audio, PiP, and horizontal/vertical tracking. The specified YouTube reference interval could not be viewed directly; this implementation follows the user's description of its structure and does not copy source imagery/music.

| Stage | Details |
|---|---|
| Registration | Register a full folder/file path at “3. Generate” (shared by all projects; rights confirmation required). Detect Opening/Intro/オープニング and Closing/Ending/Outro/エンディング in filenames; explain unidentified videos instead of guessing. PNG/JPG are card images. Store resolution/duration measured by ffprobe and integrated loudness by ebur128. |
| Version selection | If both 9:16 and 16:9 exist, automatically select the one closest to 1080×1920 output. Fit differently shaped media with the same blurred background as the body. |
| Placement | Opening goes **after the cold open**, the usual Japanese variety-video sequence (at the beginning for patterns without a cold open). Closing goes last. In shorts, place cards at body joins jumping over three seconds in source time (same definition as FR-SWT-05; a card replaces the crossfade). In series, use chapter starts. If no join lies within four seconds of a chapter start, split the clip there to create one, allowing a card at a topic change even in continuous speech. At least eight seconds between cards, three seconds from body ends, at most three cards in a short. |
| Cards | Freeze/darken the preceding scene and show the registered image **full screen**. `Renderer.CardCovers` scales/crops to fill when the absolute logarithmic aspect-ratio difference is <0.1; otherwise fit the whole image over a blurred copy. Transparent regions reveal the frozen scene. Without an image, cards with dedicated BGM use an original full-screen background panel with inset yellow border. Pop bold yellow text over it (libass, automatic shrinking); play dedicated BGM and “hit” SFX. With AI, regardless of image presence, propose a ≤14-character **viewer reaction** from preceding/following captions: a quip (“What on earth!”), praise (“Looking good!”), empathy, surprise, or anticipation. Do not copy the speaker verbatim, duplicate another card/reaction, use prohibited terms, or invent numbers/names absent from captions. Reaction slang such as “cool,” “seriously,” or “wild” is not treated as a proper noun. **Export is blocked until approval.** Without AI, or when its text is unusable, use harmless templates such as “On to the next!” Review allows editing, approval, and individual enable/disable. Regenerate older candidates whose image cards were created with empty text to populate it. |
| Timing | Create telops, SFX, and chapters in body time. Centralize insert-duration shifts in `InsertMap`, used by ASS/SRT, SFX, BGM muting, chapters, thumbnails, and QA face detection. Split telops spanning inserts so captions do not appear over cards/openings. |
| Audio | First measure the **body mix and match opening/closing loudness**, then normalize the whole file to −14 LUFS. Measuring exposed that matching an absolute −14 before normalization made the opening 12 LU louder than the quiet body; this was corrected. Stop BGM during opening/closing and fade it out at the closing's start. |
| Frame precision | Rounding each clip separately, plus concat taking the longer audio/video duration, added about 17 ms per clip (a 25.95-second plan became 26.10; hundreds of series cuts drifted seconds). Determine frame counts from **cumulative plan time**, and align audio by sample counts (48 kHz/30 fps = 1,600 samples/frame). Reassign timestamps after resampling to remove the ~0.1-second next-section delay caused by AAC's initial −0.011-second priming. |
| QA | H-01 applies pattern duration to the **body**, and the Shorts 180-second ceiling to the **whole file**; verify file duration against the plan. Include card text in H-07/H-08. H-09 checks insert rights. S-03 warns if a short begins with an opening, since viewers may leave in those first seconds. S-06 excludes still periods inside cards/openings/closings. S-12 warns when a source is smaller than output and enlarged. |

### Verification

- Automated [PackagingTests.cs](../../../tests/VideoCreator.Tests/PackagingTests.cs): mapping/splitting, filename identification/version selection, placement (after cold open, jumps, chapters, splitting chapters without joins), QA, retaining edits, and ASS/SRT times. Real FFmpeg exports measure ffprobe duration = plan (±one frame), scdet transition times = plan (±0.05 seconds), yellow text pixels at card times, opening/closing colors, opening-to-body loudness difference <4 LU, and all hard-QA passes.
- Real assets (`C:\Work\HubRadio\OpeningClosing`, 720p/AAC 96 kHz/12.1 seconds, video 12.0 seconds): automatic 9:16 selection; −14.0 LUFS/−1.4 dBTP; transitions 2.767/14.867/39.033 seconds versus planned 2.76/14.86/39.05; S-12 warns about 1.5× enlargement.
- Real recording (four-minute iPhone 4K extract, actual Azure transcription): all three short candidates include opening/closing, after the cold open when present; otherwise S-03 warns. For a two-part series, split at the second part's chapter start, 16 seconds from a join, and add a card. Edit text on screen, export, and pass all hard QA (H-03 −14.5 LUFS; H-04 −1.0 dBTP).

Limitations: YouTube reference videos were not downloaded because of their terms. Their structure (cold open → opening, brief reaction inserts at topic changes) is implemented as a genre convention. Register openings/closings exported at 1080×1920 to avoid enlargement blur.

### Improving small opening/closing videos

When registered openings/closings are smaller than 1080×1920 (sample: 720×1280), create an enhanced version once during registration and use it for exports. Implementation: [SuperResolution.cs](../../../src/VideoCreator.Core/Media/SuperResolution.cs), `BrandKitEnhancer` in [Packaging.cs](../../../src/VideoCreator.Core/Planning/Packaging.cs), [BrandKitWorker.cs](../../../src/VideoCreator.App/BrandKitWorker.cs). Verification: [UpscaleTests.cs](../../../tests/VideoCreator.Tests/UpscaleTests.cs), [spikes/14-upscale.cs](../../../spikes/14-upscale.cs).

**The method was selected by measurement.** For a standard ground-truth super-resolution evaluation, reduce the original 720×1280 to two-thirds, enlarge 1.5× by each method, then compare with the original. Use FFmpeg libvmaf's **NEG model**, which does not reward sharpening, XPSNR, and SSIM with matched encoding settings.

| Method (opening) | VMAF-NEG | XPSNR-Y | SSIM | Decision |
|---|---|---|---|---|
| Lanczos (previous export-time enlargement) | 92.77 | 44.29 | 0.99792 | Baseline |
| Lanczos + CAS 0.4 sharpening | 94.81 | 39.86 | 0.99575 | VMAF rises but XPSNR falls 4.4 dB: less faithful |
| spline36 | 92.33 | 44.17 | 0.99792 | No improvement |
| libplacebo ewa_lanczossharp (GPU) | Ordinary VMAF 93.40, versus Lanczos 93.51 | — | 0.99686 (Lanczos 0.99819) | No improvement |
| Real-ESRGAN animevideov3 alone | 86.53 | 24.50 | 0.99275 | Sharper letters, but changes pastel-background brightness/gradients into blotches, confirmed in difference images |
| **Lanczos + high-frequency component of (model − Lanczos) ×0.4** (selected) | **93.88** | **44.70** | **0.99794** | All three metrics at least baseline |

The same settings on the closing yielded VMAF-NEG +1.11, XPSNR +0.71 dB, SSIM +0.00007, confirming settings were not fitted to one video.

| Stage | Details |
|---|---|
| Mechanism | Generate chroma/low frequencies (color, brightness, gradients) from original YUV using Lanczos. Add only luminance detail at 0.4×, from the high-frequency difference (Gaussian σ0.7) between the 4× model result downsampled by area averaging and Lanczos. An RGB round trip alone lost 4 dB XPSNR, so colors bypass the model. |
| Per-asset adoption | Repeat that comparison on the middle four seconds of the registered video itself. Adopt only if more faithful than Lanczos: VMAF-NEG does not fall and XPSNR loss ≤0.1 dB. Otherwise keep the original and display the reason. |
| Execution | Run in the background after registration with visible progress. ONNX Runtime DirectML GPU inference: 0.13 seconds per 720×1280 frame, CPU 6.3 seconds. Fall back to CPU without a DirectX 12 GPU. Store x264 CRF 10, effectively lossless, with unchanged audio and every original frame. Cache by filename/size/modification time for immediate repeated registration. If the cache disappears, export from the original. |
| Display/QA | Example: “→ Enhanced to 1080×1920 (GPU, 89 s; measured for this video: VMAF +1.1, XPSNR +0.35 dB).” S-12 passes for enhanced material, warns for unenhanced enlargement. |
| Dependency | Replace `Microsoft.ML.OnnxRuntime` with `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4, including CPU. All existing tests, including face detection, pass. get-tools.ps1 downloads the model from Hugging Face and verifies SHA-256 (converted from Real-ESRGAN v0.2.5.0, BSD 3-Clause). |

Measurement uncovered and corrected: (1) `progress?.Report(++n)` skips `++n` when progress is null, falsely reporting a successful run as zero frames; (2) FFmpeg reads parent stdin as keyboard input, ending decoding immediately—use `-nostdin`; (3) copying 44 million inference elements and pixel-by-pixel conversion took 0.69 seconds/frame; direct destination arrays and row parallelism reduced this to 0.28 seconds, with bit-identical output.

Actual registration through the app for `C:\Work\HubRadio\OpeningClosing`: opening adopted after 89 seconds (VMAF-NEG +1.1, XPSNR +0.35 dB); closing after 98 seconds (+1.3, +0.84 dB). Real-recording generation/export used enhanced 1080×1920 versions, passed all hard QA and S-12.

Limitations: enlargement cannot restore lost detail; VMAF gains around +1 are modest. Best quality comes from re-exporting original production data at 1080×1920. Card images are excluded because they are reduced and overlaid in part of the screen.

## 11. Automatic multi-source synchronization and screen explanations (P10: screen recording + presenter inset)

Automatically align multiple cameras/PC screen recordings of the same event with **less than one frame** error, classify screen versus presenter, and switch between “screen + circular presenter inset” and “full-screen presenter.” Implementation: [AudioSync.cs](../../../src/VideoCreator.Core/Audio/AudioSync.cs), [ScreenActivity.cs](../../../src/VideoCreator.Core/Media/ScreenActivity.cs), [ScreenPlanner.cs](../../../src/VideoCreator.Core/Planning/ScreenPlanner.cs), `AppendScreen` in [Renderer.cs](../../../src/VideoCreator.Core/Rendering/Renderer.cs), [P10.json](../../../presets/P10.json). Verification: [MultiSourceTests.cs](../../../tests/VideoCreator.Tests/MultiSourceTests.cs).

### Synchronization (shared by P7 and P10)

Previously one GCC-PHAT measurement used the first 90 seconds and a ±20-second search. Testing known offsets exposed two **silent errors**:

| Material | Ground truth | Previous | Current |
|---|---|---|---|
| Master audio starts 0.28 s after video (MP4/AAC, common with OBS/phones) | +2.000 s | **+2.279 s: eight-frame error, yet “reliable”** | +2.0002 s |
| Camera recording starts 35 s late | −35.000 s | **Camera discarded** as unmatched | −34.9999 s |
| Camera clock 200 ppm fast (150 s) | +1.5 s, −200 ppm | Offset moves within window; cannot decide | +1.4999 s, −209 ppm; 1.5 ms edge error |
| 20 minutes, 60 ppm (typical camera) | 72 ms end drift | No correction | −62.5 ppm; 3.1 ms end error |
| Real two-person dialogue; band-limited/noisy/quiet camera | +4.217 s | — | +4.2168 s |
| Unrelated audio | — | Reject | Reject |

| Stage | Method |
|---|---|
| Time reference | Extract PCM **according to timestamps** with `aresample=async=1:first_pts=0`, padding late-starting audio and internal gaps with silence. Sample i = file time i/rate, matching trim/atrim. Use the same approach for 16 kHz transcription audio, preventing caption/cut drift even for one source. |
| Coarse search | GCC-PHAT correlation of the full-length logarithmic energy envelope at 50 Hz, calculated by streaming rather than storing all PCM. Search the entire source duration, including a short source starting in the latter half of a long master. |
| Fine search | GCC-PHAT over up to 24 ten-second windows, 8 kHz, across the full overlap; parabolic peak interpolation gives sub-sample precision. |
| Clock drift | Fit a Theil–Sen line to window offsets, refit using windows within 8 ms. Treat slopes totaling <2 ms over the overlap as zero. |
| Reliability | Do not judge peak height alone: drift blurred peaks to heights 14–70 while positions stayed within 2 ms. Accept when **≥3 independent windows lie on one line and at least half agree**. Peaks from unrelated audio do not form a line. |
| Long clips | Split drifting-source clips into lengths remaining within half a frame, realigning each section to the master—every 4.6 minutes at 60 ppm. |
| Sources without audio | Use `creation_time` when both files have it; warn accuracy is roughly ±1 second. Otherwise assume simultaneous starts and warn, as before. Manual “Offset (seconds)” always overrides. |
| Display | Source row: “Audio synchronization: −7300.0 ms; 5/5 intervals agree (residual 0 ms).” With drift: “Correcting N ms/hour” (FR-ING-05). |

**Independent verification:** measure the same files using ordinary SciPy `scipy.signal.correlate`, independently of this implementation. Do not use timeline filters; explicitly add ffprobe stream-start times. Results all agree (+2.0000/−35.0000/−7.3000 seconds; 20-minute drift −62.4 ppm versus implementation −62.5). BBC audio-offset-finder was unavailable in the environment's configured package feed and was not used.

### Screen recording and presenter (P10)

| Stage | Method |
|---|---|
| Role classification | Compare grayscale 2 fps/640×360 proxies for each source: proportion of almost unchanged half-second intervals plus face-detection rate over six four-second samples. Screen = quietest with ≥30% still intervals, penalizing faces; presenter = most faces among the rest. An explicit “Screen (PC recording)” role overrides. Show classification/evidence, e.g. “presenter.mp4: 1% still, 100% faces; screen.mp4: 82% still, 0% faces.” |
| Screen motion | Count pixels differing from **both previous and two-frames-back images**, excluding blinking cursors/reverting highlights. Threshold 12 pixels detects a typed character (measured 19–50), versus 0–2 on a still screen. Store change location (bounding box of 8×8 blocks), plus a 16×9 detail/luminance-gradient map every two seconds. |
| Switching | Every 0.25 seconds, choose screen when it changes between 0.6 seconds before and 1.0 after, or speech refers to screen (“here,” “click,” “button,” etc.). After four seconds without either, choose full-screen presenter. First two seconds show presenter unless the screen changes. Absorb intervals shorter than two seconds into neighbors; snap transitions to nearby jump cuts, allowing up to two seconds when extending screen visibility. |
| Zoom | In 9:16, shrinking the full screen to width 1080 makes text unreadable. Crop for the screen band using the 75th-percentile change-area size with 1.3× margin. Limit enlargement to 1.5× to avoid text blur. Pan by tracking change centers with one-second smoothing/speed limits. Show the whole screen for broad changes such as slide transitions. |
| Layout | Put the screen band **between** the top heading and lower emphasis/captions, preventing text overlays from hiding screen content. Fill remaining area with a blurred/darkened screen copy. Circular white-bordered inset height ≤42% of band. Compute its geq mask once, then loop rather than recalculating each frame. |
| Inset position | For each corner, invert zoom/pan back to source coordinates and minimize (1) overlap with change regions plus (2) detail density (text/UI). Do not hide slide titles or the line being typed. Crop presenter to a head-and-shoulders square centered on median face location. |
| QA | Include inset sources in H-09 rights confirmation so material visible only inside the inset is not overlooked. |

### Verification

- **Measure output of synthetic media with embedded ground truth** (`Screen_and_presenter_are_synced_switched_and_composited`): presenter/master has white flashes and beeps at known times; screen starts 2.5 seconds late, audio another 0.3 seconds late, with upper-right typing and magenta markers simultaneous with beeps. Decode export and verify (1) beep starts at plan ±20 ms; (2) each beep's flash in the visible full-screen/inset region is **within one frame**; (3) screen markers also within one frame; (4) screen intervals have a blue presenter inset and gray screen band; presenter intervals are blue.
- Unit tests: late audio, 35-second late start, 200 ppm drift, robust line fitting, drift clip realignment, detecting tiny typing but not blinking cursors, avoiding slide text, a band not overlapping telops, and inset/audio inputs in series with seeking.
- **Full real-app run** via API, no explicit roles, actual Azure transcription/AI selection: presenter face photograph with head wobble/sensor noise and real conversational audio; screen starts 7.3 seconds late with audio delayed 0.2 seconds, showing Japanese editor input → slide → terminal commands. Correct roles; −7.300-second sync, 5/5 windows, zero residual. Screen intervals begin 0.8 seconds before editor typing, 2.6 before slide (snapped to an existing cut), and exactly at terminal input. Terminal inset lies opposite the input line; slide title stays visible. All three candidates pass hard QA.
- Bugs discovered/fixed: (1) a character changes only 19–50 pixels at 360p, below the original 92-pixel threshold—use 12 plus blink exclusion; (2) switching 1.3 seconds before a cut created short fragments—widen snapping distance when extending screen; (3) slide changes lacked location data and inset hid titles—add detail map; (4) fix latent `audio:`-prefixed input-resolution failure for alternate-camera audio in series, affecting P7.

Limitations: importance is inferred from motion/detail, not semantics such as which button is being explained. Without screen audio or creation time, simultaneous starts are assumed; specify “Offset (seconds).” Layout targets portrait 9:16.

## 12. Variety presentation (P1/P3), masking, and thumbnail candidates

The basis is measurement of two Japanese variety videos, about 14 minutes each: a TV clip and a YouTuber project. Windows OCR processed every two seconds across the entire videos and 4 fps at high/low-engagement intervals; character height was calibrated using M PLUS 1p (OCR character height ≈0.63×Fontsize). Measurements were correlated with heatmaps/comment timestamps. Liveliness came **not from faster cuts** (ASL 3.2–3.7 seconds), but (1) persistent information, (2) large text-size differences, information:speech:punchline ≈1:3:5, and (3) masking/editor reactions.

| Item | Change (only presets with `telop.look = "variety"`; P1/P3 version 1.1.0) |
|---|---|
| Captions | 124 px, formerly 80; condensed ScaleX 85%, outline 10; **ten characters/line**, formerly 14. Black blurred halo beyond outline, using a separate transparent-text/thick-outline/`\blur10` layer. White text remains readable on white sets. |
| Red emphasis words | Color AI's verbatim transcript-extracted emphasis terms red inside captions, using valid emphasis-telop text; at least two characters, longest terms first. Escape each character before inserting color tags. |
| Punchlines | Emphasis: yellow 210 px/black outline 14/shadow 4. Reactions: white 180 px on red backing, BorderStyle 3. Onomatopoeia: 160 px. **Only one at a time:** a later item cuts off a prior one shown ≥0.8 seconds, otherwise suppress the later. Simultaneous priority: emphasis > reaction > onomatopoeia. |
| Headings | Already persistent from after hook through end; change appearance only to a “Q. bar,” light-blue strip/navy 56 px text. |
| Metrics | Centralize font size, outline, ScaleX, and vertical bands in `AssWriter.Metrics`/`AssWriter.Band`; rendering, automatic shrink, and face-overlap S-05 use identical values. Standard values unchanged. |
| Masking | Generation accepts up to 20 masked terms, ≤30 characters each. Match case-insensitively with NFKC full/half-width normalization, **across ASR word boundaries**. Mask transcript before generating telops so caption splits cannot leak terms. Mask AI hooks/headings/emphasis/reactions/cards/series chapter names with ●. Silence dialogue over matching word intervals and overlay 1 kHz beeps only there, before BGM ducking. |
| H-10 (new hard check) | Shown only when terms exist. (1) No term remains in enabled telops/cards, including manual edits; (2) **independently recalculate intervals from the original transcript** and verify all are beep-covered. Either failure blocks export. |
| Thumbnail candidates | Sample punchlines (emphasis, approved reactions, onomatopoeia, counters) after full appearance, start+0.45 seconds. Detect faces in exported frames and select two with **largest faces**, ≥2 seconds apart, plus the original opening frame. Without face model, chronological selection. Variety saturation ×1.6; reference thumbnails were ×1.7–1.8 body saturation. |

### Verification

- **Telop frequency/face avoidance:** display emphasis at every occurrence rather than only the first, filling gaps with short verbatim phrases at roughly four-second intervals. Caption generation/SRT unchanged. New plans store source-coordinate face analysis in `telop_faces`; `TelopLayout` repositions at render time, reevaluating after review edits to text/time/enabled state. Account for face tracking, fit, blurred background, zoom, screen/insets, cuts, and crossfades. Protect movement envelopes between face samples plus 32 px; also avoid screen-recording bands. Without a safe position or with missing analysis, suppress only the decorative effect and record why. Old plans without `telop_faces` retain previous placement; regenerate to apply. Two-fps detection can miss faces/rapid motion. S-05 independently redetects exported video, checking rectangle intersections near start/middle/end, including headings/SFX. `TelopLayoutTests` compare frequency on identical 30-second input, coordinate conversion/missing data/motion/cut boundaries, and actual standard/variety libass pixels versus detected faces.
- **Other presets unchanged:** compare prechange git-worktree versus postchange EditPlan telop/audio and ASS bytes for all ten presets. Only P1/P3 differ; P2/P4–P10 match.
- **Metrics match libass:** render captions/emphasis/reactions at 1080×1920 and ensure pixel bounding boxes fit `Band` and safe zone. Geometrically check simultaneous bands (caption/punchline, counter/punchline) do not overlap. This found a 1.2 px backing/caption overlap; move punchline band up ten px.
- **Characters/line:** proposed eight-character limit made 11/28 P3 captions exceed six CPS, versus old 6/18. Ten characters gives six P3/seven P1 violations, comparable to previous behavior. Ten fits 124 px/ScaleX85 without shrink.
- **Readability, independent Windows OCR:** overlay actual P3 output on a white studio video, reduce to Shorts-list width 270 px, OCR it. Caption heights 49–51→76–81 px, emphasis 61→129. Previous OCR misread 「1万円なんで」; new appearance read correctly.
- **Beep:** mask 2.0–3.0 seconds in 440 Hz dialogue. Goertzel measurement of decoded PCM shows 440 Hz inside >40 dB lower than outside, and 1 kHz inside >40 dB higher.
- **End-to-end masking:** create a plan from real transcription where `1万円` spans words, specifying full-width `１万円`. Verify absent from telops/SRT/ASS/AI hooks; H-10 passes, manually restoring the text fails, removing beep fails.
- Real app: API rejects 21 terms/31 characters with 422. Playwright/Edge confirms generation input persists and restores on reload.

Limitations: only AI-selected emphasis terms turn red; grammatical subjects are not parsed. Halo is always present regardless of background brightness, unobtrusive on dark backgrounds. Reference measurements are long landscape videos; portrait Shorts conversion uses screen-width ratios.

## 13. Evaluating video-analysis services (Video Indexer / Content Understanding)

**Question:** does Azure AI Video Indexer (VI) or Azure Content Understanding (CU, GA `2025-11-01`) improve analysis accuracy? **Answer:** only **laughter/applause detection** improved, so only that VI function was implemented, off by default. Transcription, shots, and semantic video descriptions were no better, or improvement could not be demonstrated.

### Method: establish ground truth independently of the services

| Evaluation set | Ground truth construction | Measurements |
|---|---|---|
| `bench.mp4`, 515 s, historical `spikes/15a-make-ai-bench.cs` (unavailable in this checkout) | Crop real single-fixed-camera Hub Radio recording wide/left/right, joining at **known times**: 44 boundaries, 41 hard cuts including 11 same-composition jump cuts, two dissolves, one fade to black. Burn in eight known-time texts. Audio: **34 real Google FLEURS ja_jp test recordings with human transcripts**, interleaved with **real ESC-50 recordings**, five laughs/four applause/six confusable sounds. | CER, speech onset, shot F1 ±0.5 s, laughter/applause, screen text |
| `events.mp4`, 1,475 s, historical `spikes/15b-make-event-bench.cs` (unavailable) | Insert **all 40 laughs and 40 applause** from ESC-50 plus 40 coughs/knocks/dogs/babies/typing/footsteps between 120 FLEURS utterances. | Laughter/applause, larger sample to check chance effects |
| Real 18-minute Hub Radio recording | No ground truth | Agreement across three methods, CU description content, time/cost |

Feed **the same file** to all three; save raw VI Index JSON, CU `result.contents`, and current transcript/signals; score with historical `spikes/15-video-analysis.cs` (unavailable). Normalize CER using NFKC, punctuation removal, and kanji/Arabic numerals (“一万”/“1万”→10000). VI: Advanced audio+video, ja-JP. CU: segmented analyzer based on `prebuilt-video`, gpt-4.1, with Japanese summary, screen text, nonspeech sounds, and highlight-score fields.

One ground-truth error was corrected: FLEURS silence contains ~−45 dB noise; defining speech as within 30 dB of peak placed onsets up to 2.5 seconds early, exposed by all methods' median errors of 1.4 seconds. Rebuild only ground truth using noise floor+12 dB; audio hashes confirm unchanged media. Median errors became 0.08–0.13 seconds.

### Results: bench.mp4

| Item | Current | Video Indexer | Content Understanding |
|---|---|---|---|
| Transcription CER | **4.74%** | 9.74% | **4.34%** |
| Word timings | Yes | **No**, phrase only | Yes |
| Speech-onset error, median/90th percentile | 0.12/0.36 s | 0.08/0.33 s | 0.13/12.7 s, omissions at segment joins |
| Shot F1 ±0.5 s | 83%, P100% | 84%, P100% | 85%, P97% |
| Same-composition jump cuts | 0/11 | 0/11 | 0/11 |
| Dissolve/fade ±1 s | 0/3 | 1/3 | 2/3 |
| Laughter, five events | P29% R80% | P75% R60% | P50% R20%, LLM inference |
| Applause, four events | Not supported | **4/4** | 0/4 |
| Screen text, eight items | Not supported | 7/8 | 7/8 |

### Laughter/applause: events.mp4, 120 events

| Method | Laugh recall | Laugh precision | False laughs among 80 nonlaugh events | Applause F1 |
|---|---|---|---|---|
| Current actual behavior | 23%, 9/40 | 60% | 4 | Not supported |
| Current classifier over all intervals | 68%, 27/40 | 39% | **21**, including dogs 11/applause 9/knocks 5 | Not supported |
| **VI app route: audio-only 16 kHz FLAC** | **68%, 27/40** | **82%** | **4**, babies crying | **79%, P100%** |

- Low current recall came from `LaughDetector.DetectAsync` evaluating **only the first 60 intervals**, the first 310/1,475 seconds here, not from its classifier. Removing the cap matches VI recall but more than doubles false detections, 42 versus six, adding laughter telops to dogs/applause. Do not change the cap here because it changes default behavior and raises false positives.
- VI versus actual-current recall difference: Fisher exact p=1.0×10⁻⁴. MP4 upload and app audio-only route gave similar accuracy: laughter F1 75%/74%, applause 81%/79%.

### Real recording: 18 minutes

- Transcription: CU/current differ 2.6%, both Azure Speech family; VI/current 16.1%.
- Shots: fixed camera, truth zero; current/VI zero, **CU ten false positives**.
- Laughs: current five, VI four, one shared. Cannot judge without ground truth. Audio extracts around detections and a marking sheet are available for listening, under remaining verification below.
- CU descriptions: all 19 segments say “two men sitting in chairs discussing…” plus conversation summaries; highlight scores nearly flat at 6–8. They add no new evidence beyond the full transcript already read by current AI selection.

### Cost/time: Azure Retail Prices API, JPY, japaneast, 2026-11-08

| Service | Unit price | 18-minute recording | Processing time |
|---|---|---|---|
| Current Fast Transcription | ¥56.96/hour | ¥17 | 20 s transcription |
| VI Advanced Audio, adopted route | ¥6.33/minute | ¥114 | About 9–11 min, parallel with AI selection |
| VI Advanced audio+video | ¥23.73/minute | ¥427 | About 9 min |
| CU: extraction ¥158/hour + contextualization ¥0.16/1K tokens + gpt-4.1 | About ¥550/hour | About ¥165 | About 13 min |

With gpt-4.1 at 50K TPM, CU's **entire job failed** with 429 even for eight minutes; 300K TPM succeeded. A ¥10,000 monthly budget buys about 26 hours of new-video VI laughter/applause detection. Cached results avoid charges on regeneration.

### Decision and implementation

| Analysis | Decision | Reason |
|---|---|---|
| Laughter/applause | **Adopt optional VI, off by default** | One-seventh false detections; also applause. False detections directly become on-screen laugh telops/reaction shots. |
| Transcription | Keep current | VI roughly doubles CER and lacks word times. CU comparable, six characters out of 1,520 difference, and same word-timing family. |
| Shot boundaries | Keep current | Difference one/two events; all miss 0/11 jump cuts; CU false positives on real recording. |
| Screen OCR | Do not adopt | Good 7/8, but no current app use. |
| CU semantic description | Do not adopt | No extra interview information beyond transcript, flat highlight scores, ~10× cost/~40× time. Without human highlight truth, cannot claim improvement. |

Historical implementation: `src/VideoCreator.Core/Audio/VideoIndexerAudioEvents.cs` (no longer available): keyless ARM `generateAccessToken`, AdvancedAudio/ja-JP/private upload → wait → read `audioEffects` → **delete upload**. When `VideoIndexer.Enabled=true`, [PipelineRunner](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs) started immediately after signal analysis, waited in parallel with AI selection, and consumed results just before edit planning. Per-source `audio_events_video-indexer.json` cache. Failure warned/fell back to existing detection, not stopping generation. UI displayed destination and estimated/actual costs. Historical infrastructure `infra/video-analysis.bicep` (unavailable) included VI account/storage with shared keys disabled and default-deny except trusted Azure services/user-assigned managed identity/CU test model deployments. Historical tests: `tests/VideoCreator.Tests/AudioEventTests.cs` (unavailable). These references document the original evaluation, not currently available functionality.

### Remaining verification and limitations

- **Real-recording laugh correctness:** no ground truth. Follow `spikes/15-video-analysis.cs` to create ±1.5-second detection extracts and mark “laughter/not laughter.” Human labels permit precision measurement.
- ESC-50 isolated laughter differs from conversational laughing-while-speaking/quiet chuckles. VI returns applause as `CrowdReactions`, not distinct from cheers. No feature yet uses applause, such as applause telops.
- Semantic description might help visually driven games/sports/demonstrations. Only interviews were tested; no decision until measured against human highlights, such as PRD §14.2 Mr. HiSum.
- Organization policy changes storage `publicNetworkAccess` to `Disabled`; VI access still succeeds with default-deny/trusted-Azure-service access.

## 14. Aligning part boundaries with discourse breaks: SplitCues

**Problem:** the actual 18-minute Hub Radio Ep.014 two-part output ended part one “…I think you should definitely go somewhere, and one more thing, this is a little…” and began part two “a rather interesting impression, I thought….” It cut mid-thought.

**Cause from raw data:** candidates were sentence boundaries, but Azure Fast Transcription merged “…I think” and “and one more thing…” without punctuation. Word timings show a **0.36-second pause** after “I think,” the real break. AI outlining gave the next “a rather interesting…” line split ease nine. Boundary scoring ignored whether the preceding sentence was complete, so accepted it. The fixed camera produced zero shot boundaries, offering no clue.

### Research: YouTube/TikTok splitting

| Source | Finding | Applied |
|---|---|---|
| Creator YouTube chapters, YTSeg/EACL2024, and §9 | Chapters mark topic boundaries just after the previous point is completed. | Closure detection below |
| TikTok Part1/Part2 guidance | End a part with a completed question/setup, answer next part; **never midway through the setup**. | Questions count as closure; strong interruption penalty |
| J/L cuts, shifted audio/video | Editors wait until laughter/reactions finish; fade/scene change signals a break. | Bonus just after laughter; penalty inside; fade/shot bonus |
| AutoShot research: ~30% short-video transitions gradual; PySceneDetect F1<0.6 | Frame-difference scdet misses dissolves/fades, also §13 0/3. | Detect fades with `blackdetect`; dissolves excluded below |

### Mechanism: [SplitCues.cs](../../../src/VideoCreator.Core/Planning/SplitCues.cs), series only

| Cue | Decision | Boundary score |
|---|---|---|
| Restore missing sentence ends, `Resegment` | Split within a sentence between words when the left ends strongly, e.g. Japanese polite declarative endings, and a ≥0.3-second pause follows. Change only sentence boundaries, never words; every word belongs to exactly one sentence. Do nothing if sentence text does not match concatenated words. | More boundary candidates |
| Prior ending, `Closure` | Complete statement +1, question +0.6, trailing “…but.” zero; truncation at comma/conjunction (“but/and/because…”), demonstrative/filler (“this/a little/um…”) −1. | +0.3×closure; **interruption −2.0**, larger than max AI ease bonus1.6 |
| Next beginning, `Continuation` | Back-references such as “and,” “therefore,” “then,” “right,” skipping introductory “yes/well”: −1; demonstrative −0.6. | ×0.8, weaker because conversational “therefore” can be mere preamble |
| Summing-up words | Ends with “so,” “that is all,” “thank you.” | +0.4 |
| Laughter | Ends just before boundary / crosses boundary | +0.3 / −0.5 |
| Video | Fade, `blackdetect=d=0.2:pix_th=0.10`, same decode as scdet / shot boundary | +0.8 / +0.4 |

The AI-outline prompt (`series-1.1.0`) explicitly allows high split ease only when the previous line ends completely or as a question and the next line starts independently. If no complete boundary exists within duration tolerance, warn which part ends mid-thought. Explanations show evidence such as “complete sentence ending,” “video fade,” “just after laughter.” Renaming cache to `signals.v2.json` automatically reanalyzes fades next generation, about two extra seconds for 18 minutes.

### Verification: [spikes/16-split-cues.cs](../../../spikes/16-split-cues.cs), [spikes/16-split-cues-check.py](../../../spikes/16-split-cues-check.py)

| Item | Before | After |
|---|---|---|
| Ep.014, two parts, AI, reproducing actual failure | 8:32 “…this is a little｜a little interesting…” **interrupted** | **8:30.8 “…I think you should go somewhere｜and one more thing…”** |
| Ep.014, two parts, no AI | 9:26 “…because I'm doing it.｜And, completely…” continuation | 8:30.8, same as above |
| Ep.014, four parts, no AI | Second boundary “…somehow, this.｜do.” interrupted | All three complete/independent; second8:30.8 |
| Regression, 30-minute synthetic talk, truth “Next segment…” | 16:45.9 correct | 16:45.9 correct, with/without AI |
| Closure agreement with janome/IPADIC, all313 Ep.014 boundaries both could classify | — | **97.0%,224/231**; seven disagreements conversationally complete ellipses such as “Absolutely,” “Exactly.” |
| Sentence before boundary, independent janome | Two-part AI ends on adverb “a little,” incomplete | Auxiliary “ます” base form, complete |
| Synthetic52s, two cuts/two dissolves/two fades at known times | scdet fades0/2 | **scdet+blackdetect fades2/2**, zero false positives |
| [SplitCuesTests.cs](../../../tests/VideoCreator.Tests/SplitCuesTests.cs) | Two split tests fail when cues disabled, proving effect | All238tests pass |

For listening comparisons, export ten seconds either side of each boundary to `spikes/out/split-cues/cut_<method>_<before|after>_<n>.wav`.

### Azure use: ¥100,000/month budget

No additional resources. The failure was linguistic—missing sentence boundaries and scoring ignoring them. §13 found VI/CU shots comparable to scdet, F1 83–85%; fixed-camera interviews offer no visual boundary evidence. Existing AI outlining is reused with improved prompts at unchanged cost.

### Limitations

- Dissolves undetected, synthetic0/2. Edited sources with many gradual transitions need a trained model such as TransNetV2, not implemented.
- One synthetic hard cut between similar-brightness colors scored7.1, below existing10 threshold. Keep threshold evaluated in §13.
- Weights manually chosen because no Japanese conversational split-ground-truth data. Closure checked against morphology; splits against actual failure/synthetic truth. Viewer desire to continue was not measured.
- Laughter uses existing classifier; low §13 precision motivates small weight.

## 15. Choose only scene/topic boundaries: scene-first splitting

Through §14, equal duration came first; without a topic break in range, select a linguistically convenient point inside it. Example: splitting a ten-minute source containing seven minutes Azure then three Copilot around the five-minute midpoint. Three changes:

1. **Scene first**, `SceneStrength`/`Split` in [Series.cs](../../../src/VideoCreator.Core/Planning/Series.cs): establish scene boundaries solely from topic-change evidence—vocabulary shift, transition phrases, AI chapter starts, fades—then split using **only those boundaries first**. Permit 30–170% of target duration, minimum60seconds/part; notify when deviation exceeds ±25%. Pauses, closure, laughter, shot cutting score *where* to cut but are not scene evidence. Only if too few boundaries exist, fall back to length-first and warn “Splitting within a topic.”
2. **Semantic topics**, [TopicEmbedding.cs](../../../src/VideoCreator.Core/Planning/TopicEmbedding.cs): vectorize sentences with existing Microsoft Foundry `text-embedding-3-large`. Mix TextTiling valleys of mean vectors in adjacent30/60/90-second windows with lexical valleys at0.7:0.3. If `Llm.EmbeddingDeployment` empty/offline, lexical only. Vocabulary alone creates false valleys through paraphrase/ASR variability within one topic or hides real changes through shared terms. Cache vectors at `cache/<source>/topic_embed.json`, under ¥1/18minutes. Only existing Microsoft Foundry destination; never for `provider=none`/`LocalOnly`.
3. **Multiwindow lexical valleys:** replace single45-second window, prone to chance dips from sparse words, with30/60/90-second average.

### Verification

| Method | Result |
|---|---|
| Ground-truth synthetic talk, [TopicFidelityTests.cs](../../../tests/VideoCreator.Tests/TopicFidelityTests.cs), no topic announcements, shared terms, **complete sentence/long pause/laughter/“so” trap at clock midpoint**;5/5,6/4,4/6,7/3,3/7,and two three-part cases | Before, `SceneFirst=false`,3/7 cuts the midpoint trap, reproduced and fixed in test. After, **all seven within ±one sentence of true topic start**. Lexical-only fails this synthetic case at chance valleys; semantics required. Tests use substitute semantic vectors; real model checked below. |
| Ten minutes of one topic | Warns no break, explicitly within-topic |
| Real Ep.014,18minutes: independent gpt-4.1 **blinded to condition** rates ±90seconds around boundaries for same-topic0–10, [18-scene-check.cs](../../../spikes/18-scene-check.cs) | Across54locations, only **11%** clearly different≤2, **63%** same≥7; random cuts interrupt discussion nine times in ten. Two parts8:31 unchanged,2–3. Three: old6:35=**7**,13:04=2→new7:09=**1**,12:59=2. Four: all three new boundaries2. **All new boundaries separate topics.** |
| Full tests | 247/247 pass |

Cosine similarity over ±90seconds alone could not distinguish topic changes within the same technical domain, cos0.81–0.88. Embeddings therefore help propose valleys; verification uses another AI's blind judgment.

### Limitations

- Boundaries derive from vocabulary/semantics/AI topics. Near-monothematic videos offer only small differences, with warning.
- Durations can diverge substantially, e.g. seven/three minutes. For balance, choose count/duration compatible with topic boundaries.
- Only one judging AI; agreement with humans unmeasured. Public datasets such as YTSeg lack Japanese.

### 15.1 Does “send the entire transcript to AI and request timestamped chapters” work, including nontechnical topics?

Conclusion: **yes, but asking for “chapters” oversegments; request only main-topic changes.** The app already used full-transcript AI [OutlineAsync](../../../src/VideoCreator.Core/Planning/Series.cs), without distinguishing small headings from major changes. Ask AI to mark `major` for a new main topic versus `false` for a subheading; do not count subheadings as scenes. Prompt explicitly includes casual conversation, daily life, hobbies, work, not only technology.

[19-topic-methods.cs](../../../spikes/19-topic-methods.cs) joins separately gpt-4.1-written colloquial dialogues with known starts into three38–48-minute videos, six changes each. **Mostly nontechnical:** cooking, babies crying at night, professional baseball, household finances, Kyoto travel, sleep, movies, summer festivals, matchmaking, cats, changing jobs, pensions, hiking, moving, Korean dramas, dieting/exercise as adjacent related topics, side work, neighborhood associations, soccer. Technology: one Azure/Copilot topic. Evaluator does not see truth.

| Topic-start detection, match within ±3 sentences | Precision | Recall | F1 |
|---|---|---|---|
| Lexical only, offline | 30% | 83% | 44% |
| Lexical + semantic embeddings | 77% | 94% | 85% |
| AI chapters, line numbers, all small chapters | 23% | 100% | 38% |
| AI chapters, timestamps, same | 23% | 100% | 38% |
| **AI chapters, line numbers, major only** | **100%** | 94% | **97%** |
| AI chapters, timestamps, main-topic changes only | 100% | 100% | 100% |

- Position: over90% of AI chapters **exactly match** true sentence, zero-sentence error. Timestamp mm:ss differs from nearest sentence start by only0.3–0.5seconds on average. Both work. App retains line numbers, directly tied to transcript sentences without rounding/hallucinated times.
- Actual splitting,2/3/4parts×3videos=18boundaries: clock halves **11%**, lexical length-first72%, lexical scene-first72%, lexical+semantic scene-first83%, AI-outline scene-first **100%**, AI+semantic **100%**.
- Real Ep.014 new boundaries: blind gpt-4.1 four-part ratings2/4/2,three-part2/1; only7% of random locations clearly different.
- Limitations: synthetic LLM-written/LLM-judged dialogues may be easier than noisy ASR/tangential real conversations. No nontechnical real recordings or human agreement measured. Without AI, lexical only,F1 44%, raises within-topic risk and warns.

## 16. Keep multiple patterns (Jobs) per project and restart from earlier stages

`Project 1 --- n Job`: each generation adds a Job/pattern without deleting previous ones. Candidates/plans/exports belong to their Job; files live in `render_id` directories, avoiding cross-pattern overwrites.

- List: `GET /api/projects/{id}/patterns`, name/settings/status/candidate/approval/export counts/processing state; shown as “Created patterns” on source/pattern screen.
- Restart: “Create again from these settings” loads settings, edits then generation create a new Job with `derived_from_job_id`. Completed source/pattern/generate/candidate/review workflow steps link back.
- Delete individually: `DELETE /api/jobs/{jobId}`, `JobDeletion`, deletes only that Job's candidates/plans/all preview/final renders, `exports/{render}` via recycle bin, previews, `rejected/{render}`, QA directories. Keep sources/rights/BGM/otherJobs/cache/project folder.
- Safe behavior: generation/export/preview rebuild returns409 `E-JOB-BUSY`. If files cannot be deleted, DB unchanged, `E-FILE-IN-USE`. If saved workflow points to deleted Job, return to source/pattern screen.
- Verification: [PatternTests.cs](../../../tests/VideoCreator.Tests/PatternTests.cs): delete one while preserving other/source/files, reject busy, unchanged on file failure. Real Edge/Playwright checks list/settings inheritance/delete confirmation/step links.
- Limitation: changing source roles/offset/transcript affects existing plans, but does not reselect their candidates. Use “Create again from these settings” for a new pattern.

### 16.1 Reuse previous analysis or restart from the beginning

Each generation chooses “Previous analysis results (from other patterns),” `JobOptions.ReuseAnalysis`, JSON `reuse_analysis`, default reuse. Choice is not saved; every time starts with reuse. Choose redo after app updates change analysis, for example.

| Item | Reuse, default | Redo |
|---|---|---|
| Analysis proxy/16kHz audio/transcript/shots/volume/silence/laughter/faces/screen classification/topic embeddings/BGM tempo | Use `DataDir/cache/<sha256[..16]>/` if present | Recreate each once in this generation and overwrite for subsequent reuse |
| Camera auto-sync | Reuse measured result | Measure again; keep manual offset |
| Speaker assignment | Preserve | Preserve |
| Review-corrected transcript | Preserve | Replace with new transcript; UI warns |
| AI highlight selection/plan/preview | Every time | Every time |

- Cost: estimate `GET /api/projects/{id}/estimate`, `scopes`/`analysis.redo_scopes`, and Job actual `cost` count reused ASR as zero minutes. UI shows extra redo cost and prior `analysis.updated_at`.
- “Create again from these settings” and candidate regeneration `/api/jobs/{id}/regenerate` use reuse, not inherited redo.
- Source-hash cache shared across projects. Redone results are reused by later generations in those projects. Atomic temporary-file replacement prevents concurrent preview rebuilds reading partial writes.
- [AnalysisReuseTests.cs](../../../tests/VideoCreator.Tests/AnalysisReuseTests.cs): first ASR once; second reuse no new call/no modified cache timestamp/zero cost; redo another ASR call and rebuilt proxy/signals/transcript.

## 17. Recommended QA fixes, “Automatically repair with Copilot,” and timestamped right-hand panel

Review/edit “Automatic checks” is an independent rightmost panel, below preview/telops at width≤1240px, single-column≤900px. Findings explain what/why/how to fix using **finished-video time**.

- Per-check “Approve,” “Revoke approval,” and “Approve all” record review of completed results, including passes. Exclude not-run; bulk applies all unapproved completed checks. Keep verdict/advice/findings: approval neither waives hard failure nor approves telops/cards/candidates. Display approval time/count.
  - `PUT /api/editplans/{id}/qa/approval`: `plan_hash`, `checks: [{check_id, result_hash}]`, `approved`. Save all atomically only if viewed plan/result hashes match. Empty/duplicate/unknown/not-run422;oldplan/result409;no partial save.
  - `EditPlan.qa_approvals` stores time/reviewer/plan/result hashes. Reevaluate after retrieval/edit/repair/undo/preview updates, returning only matching records as `QaResult.review_approval`. Editing invalidates all; external-condition changes invalidate affected checks. Approval records are excluded from `EditPlanFingerprint`, preserving candidate approval/preview/export validity. Result hash excludes display-only advice such as repair availability.
  - If a valid current-plan preview exists, show measured H-05 safe area and production-only not-run checks. H-05 can be approved individually/bulk; changed remeasurement requires reapproval. Never mix pre-edit preview metrics.
- Locations: `QaResult.items`, `QaItem` target id/type `telop/insert/clip/asset/audio`, message, final `start`/`end`. Telop times are body time, shifted through `InsertMap.ToFinal` for openings/cards/closings, [QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs). Panel timeline lists findings; clicking seeks video/highlights corresponding telop/transition. Playback synchronizes timeline playhead/current findings/telop row.
- Recommendations: `QaResult.advice`, `QaAdvice` text and `rule`/`ai`/`manual`/`none`. Per-check table: [QaAdvisor.cs](../../../src/VideoCreator.Core/Qa/QaAdvisor.cs).
- Repairability preassessment, `QaRepair.Assess`, attached to get/save/repair/undo responses: for each finding predict repair without executing, add `QaItem.repair` rule/ai/none and reason `QaItem.note`. S-01/S-02/S-08 captions run the same repair candidates through word-level `LocalOk`; any passing→rule. H-10/H-11 validate rule changes through full QA. AI only when LLM configured. Each check returns `advice.fixable` count and `advice.remaining`, e.g. “N unrepairable findings remain warnings (counts by reason).” Zero fixable→`advice.repair=none`. S-01 derives rates from word times: “Speech X chars/s; even including available surrounding time, Y chars/s (limit Z),” explaining why maximum verbatim duration still exceeds threshold.
  - UI shows “Repair this check (N findings)” only if≥1 fixable, fixable findings first; fold others with reasons under “N cannot be repaired automatically.” Unrepairable checks show that explicitly, no repair button. Top button shows item/finding counts beneath and disables at zero.
  - Real data,12plans/520–570captions each/P3-P4/noLLM, predicted versus actual: S-02 66/66,S-08 3/3,H-11 9/9,S-01 346/359,96%, since earlier repairs may consume later available time. One unpredicted repair. Plan retrieval≤0.52seconds. Previously S-02 fixed zero in plans with46/23findings; now66/76.
- Automatic repair: `POST /api/editplans/{id}/qa/repair`, filter `check_ids` for per-check button. [QaRepair.cs](../../../src/VideoCreator.Core/Qa/QaRepair.cs) follows:
  1. Rules, lightest candidates first, rerun QA and accept first passing: mask H-10; rebind H-11 evidence, matching shortest contiguous in-range phrase by speaker/names/numbers even when sentence grouping includes another speaker, preferring nearby displayed time; extend S-01 to two frames before next caption,≤0.5seconds after last word,≤0.25seconds before first. Borrow preceding caption's trailing display time only if still readable. Move initial particles back for S-08. Restructure S-01/S-02/S-08 by merging neighbors, moving boundary words, or splitting one at word boundaries (FR-TEL-02; prefer pauses/phrase breaks, then balanced lengths). New second-half id starts at its first word. Apply duration extension also after merge/move/split. Never cross speaker change/cut/pause>0.6seconds. Use full-width spaces at former pauses/punctuation. Never change caption wording.
  2. AI, configured Microsoft Foundry or GitHub Copilot: fix unspoken terms/evidence/negation in decorative telops/cards,H-07/H-11/S-09,and overlong emphasis,S-02. Exclude captions. Hard checks first, then time order,max200/click,batches25,four concurrent. Keep successful batches if another fails; another click handles remainder. Send telop text and neighboring transcript; destination shown below button.
  3. Rerun `QaEngine.PlanChecks` after every change, keep only when targeted finding disappears, no other check worsens, no new findings. Exception: rewritten AI text may become unapproved H-08 for that target. Compare S-01/S-02/S-08 by `SourceWordIds`, not caption ids; merge/split may change ids, but targeted words must leave findings without new implicated words. Before full QA, lightweight same-criteria `LocalOk` checks changed caption and neighbors. Return rejected repairs with reasons; UI always shows reason counts.
- Caption versus decorative-text policy:
  - Preserve spoken words. Remove generation-time AI shortening; no paraphrase/summary/word omission/caption deletion, including fillers/endings/backchannels/repetition. Exceptions only explicit masking and presentation punctuation/spacing.
  - Before accepting repairs, verify all displayed wording/order and `SourceWordIds` order unchanged. Leave S-01/S-02/S-08 warnings when timing/boundaries/merging cannot fix them, returning reasons. S-01 and caption-only S-02 advice is `rule`.
  - If emphasis duplicates simultaneous caption wording, omit the extra emphasis, not captions. Preserve full caption/SRT while avoiding double display.
  - Decorative telops/cards prioritize appearance/readability. Validate emphasis/headings against evidence then insert unapproved. Reactions/cards must pass existing `TsukkomiService.ValidateText`, also unapproved.
- S-08 false-positive correction: evaluate the **displayed caption's** initial particle. Do not flag hidden original words after filler removal/editing (“but｜Copilot…”), genuine sentence-opening conjunctions after transcript punctuation (`JapaneseText.CanStartSentence`), or following another speaker.
- Undo: save prior plan as `qarepair` for every repair. `POST /api/editplans/{id}/qa/repair/{repairId}/undo` restores exactly, including hash and pre-repair approval/export validity. Editing after repair409 `E-PLAN-STALE`;second undo404. Delete with pattern/project.
- UI: per-check result appears in clicked card, or panel top if all findings vanish; scroll there. Always show fixed/unfixed counts and four most common reasons, descending; fold full list under “Items not repaired.”
- Verification:
  - [QaRepairTests.cs](../../../tests/VideoCreator.Tests/QaRepairTests.cs): final-video times; timing/merge/word-move/two-way split; full wording/word-id preservation; never delete fillers/endings/short repetitions; never send captions for AI; decorative-only AI shortening and approval; S-08false positives; speaker/cut boundaries; reject worsening other checks; cap/batching; noLLM; preassessment impossibleS-01rate/none, prediction agreement, noLLMH-11rule.
  - [V12Tests.cs](../../../tests/VideoCreator.Tests/V12Tests.cs): generated captions retain all source words regardless of LLM, unresolvable S-01 remains.
  - PRD AT-16,1.2.4: [AcceptanceQaRepairTests.cs](../../../tests/VideoCreator.Tests/AcceptanceQaRepairTests.cs), `Repair_promise_matches_result_and_can_be_undone`, via UI APIs GETplan→POSTrepair→POSTundo. PredictedS-02rule/one and S-01none/zero/15.0chars-per-second reason match actual. UnfixableS-01 leaveshash unchanged/no undo. S-02split preserves text/idsequence;undo restores originalhash.
  - PRD AT-17: `Caption_words_are_never_shortened_even_when_ai_could_clear_the_warning`, `Short_repetitions_and_fillers_are_not_deleted`, `Presentation_telop_can_be_shortened_by_ai_while_captions_stay_verbatim`. Even mockLLM suggesting shortening/deletion never receives/changes captions; only decorative text shortened unapproved.
  - Current-policy regression:80related QA-repair/generation/layout/PRD-boundary/existing-telop tests pass;JSsyntax/whitespace pass.
  - Earlier real-data/browser tests allowed caption deletion under old policy. Their AI-deletion counts are not current-policy repair results.
- Limitations: fast speech may retain warnings despite timing/boundary edits. This change does not automatically restore/regenerate saved captions. Old-policy deletions can be undone if that repair's undo remains valid; generation-shortened captions require rebuilding from transcript.

## 18. Review each video in a pattern, then export together

A Job/pattern can produce multiple candidates/series parts. Step5review/edit works per video; step6exports approved videos together. Reuse server mechanisms—approval bound to plan hash, `POST /api/jobs/{id}/exports` prechecks all and starts none if any fails—adding UI navigation only.

- Review order: below heading, previous/next/selector with every video's status. Main button “Approve and go to next video (name)” selects next unapproved, wrapping to start but excluding current. Reload candidates after approval, so approval elsewhere never sends users back to already-approved videos. If all others approved, “Approve and continue to export.” Do not duplicate approval if current content already approved. Disable all approval buttons on hard failure. Single-video behavior remains “Approve this content and export.”
- Progress: candidate/series/export lists show “Approved n/N · Exported m/N” and each status: unreviewed/approved/reapproval required/exporting/exported. Review step links to first unapproved;export step to export list.
- Export list `#/job/{id}/exports`: initially select approved not-yet-exported videos, reuse candidate bulk-export API. Save workflow using `PUT /api/projects/{id}/workflow-state`, `step:"exports"`, required `job_id`, only ready/done, forbid `edit_plan_id`/`render_id`;resume here next time. Completion links back to candidates to review others, or export list.
- Route switching: `router` sequence `routeGuard` prevents delayed old-screen responses overwriting newer screens.
- Verification:
  - [ProjectWorkflowResumeTests.cs](../../../tests/VideoCreator.Tests/ProjectWorkflowResumeTests.cs): save/restore exports state,422withedit_plan_id;all293tests pass.
  - [ui/verify-multi-export.mjs](../../../ui/verify-multi-export.mjs),Edge/Playwright with mock APIs,23checks/AT-18:three-video progress/switching,approve-next targeting only the viewed video,no redundant approval,last-video navigation to export list,step links,initial selection restricted to approved/unexported videos,one bulk request,navigation after concurrent external approval,and bulk rejection starting nothing while showing per-video failures.
  - Existing [verify-acceptance.mjs](../../../ui/verify-acceptance.mjs),11checks including single-candidate approval before export,and [verify-qa-approval.mjs](../../../ui/verify-qa-approval.mjs),17checks,pass. Added the missing workflow-state mock that had stopped acceptance testing after workflow persistence was introduced.
- Limitations: exports process one at a time in rank/part order, not parallel rendering. Export-list selection not persisted.
- Unified errors,PRD1.2.6/Q-10decision(b): for each candidate, same check order as single `POST /api/editplans/{id}/render`: unapproved→changed after approval→hard plan failure→export busy. Each detail entry has `code`;hard failures include check results,busy includes `render_id` in itemdetail. Response uses earliest applicable priority across candidates:404 `E-NOT-FOUND`→409 `E-NOT-APPROVED`→409 `E-PLAN-STALE`→422 `E-QA-HARDFAIL`→409 `E-EXPORT-BUSY`. One-item bulk matches single. Previously busy/hardfail incorrectly409NOT-APPROVED. Export list displays failedchecks below video. [ApprovalExportTests.cs](../../../tests/VideoCreator.Tests/ApprovalExportTests.cs) compares single/bulk hardfail/busy and mixed-reason overall/per-itemcodes.

## 19. Multiple BGM tracks, scene-based selection, and automatic speech-friendly volume

Request: register multiple music files, select automatically for scenes, and adjust volume so speech remains clear, with review-screen adjustments before export.

### Mechanism

| Stage | Implementation | Rationale |
|---|---|---|
| Registration measurements | [BgmAnalyzer.cs](../../../src/VideoCreator.Core/Audio/BgmAnalyzer.cs):use FFmpeg ebur128M,400ms,and S,3seconds,at100msintervals to calculate BS.1770-gated integrated loudness,−70LUFS/−10LU,and EBU Tech3342LRA. Exclude head/tail regions>20LUbelow average to identify usable span;retain a three-second loudness curve. Normalize16kHzPCMto−20dBFSRMSand measure onsets with log-spectral-flux peaks,Dixon2006,absolute threshold40. Amplitude-weighted spectral centroid estimates brightness. Beatless tracks have unknown tempo. | Mastering levels can differ>10dB:normalize by loudness,not fixed attenuation. Sustained pads have flux<25;percussion/onsets100–1000,[spikes/21-onset-calibration.cs](../../../spikes/21-onset-calibration.cs). |
| Track energy | `BgmAnalyzer.Energy`:tempo70→150BPM,weight0.4;onsets1→7/second,0.35;spectral centroid150→3000Hz,0.25. | Acoustic arousal/energy is easier to estimate than happy/sad mood. Match energy only;users choose emotional mood. |
| Scenes | [BgmPlanner.Scenes](../../../src/VideoCreator.Core/Planning/BgmPlanner.cs):role switches/cards/chapters are strong boundaries;crossfades/source jumps≥3seconds are weak. Minimum scene length20seconds. | Avoid distracting frequent music changes. |
| Scene energy | Speech rate3→9characters/second,weight0.45;voice−6→+6dB,0.35;laughter0→6/minute,0.2. Add relative interscene z-score difference at one-quarter weight. | Japanese conversation is typically5–7characters/second. Low relative weighting avoids switching tracks just because a short scene is slightly livelier. |
| Selection | `BgmPlanner.Assign`:Viterbi over scenes×tracks,cost=energy difference×scene duration+0.15per switch. Fix manually selected scenes and reoptimize before/after them. | Display measured-value reasons for every scene. |
| Playback | `Renderer.AppendCueBus`:repeat each track's usable span with`aloop`. Continuing the same track keeps playback position across scenes,with seamless0.3-second linear fades. Returning tracks resume where they stopped. Different tracks use a two-second equal-power crossfade. At BGM-stopping inserts,opening/music cards,switch inside the insert. | |
| Volume | Without speech:set voice loudness+`bgm_gap_lu`,default−8LU. During speech:voice−BGM≥`bgm_speech_ld`,default14LU,minimum10LU. Derive speech intervals from ASRword times,joining gaps<0.8seconds. Reduction=gap+target−relative local voice level+the track's local excess over its average,the latter capped10dB. Begin0.25seconds early and release over0.6seconds. Multiply by a1kHzgain-waveform WAVusing`amultiply`. | Torcoli etal.,JAES67(12),2019,recommend commentary/music difference≥10LU;nonexperts prefer≈4LUmore than experts,so default14LU. Measure edited voice with`ebur128`at export,separately from planning estimates. |
| Review | Per-scene controls:track,reset to automatic,no BGM,and−12…+6dBvolume. Global controls:speech gap and non-speech level. Display music/ducking bands and playback position. | `PATCH /api/editplans/{id}`operations`set_bgm`,`set_bgm_cue`,`set_bgm_trim`,`set_bgm_level`enter edit history. Recalculate reduction after every change. |
| Measurement,S-15 | For preview/final,create voice/BGMstems with identical export processing. From`ebur128@dlg`/`ebur128@mus`400msblocks,derive integrated speech-time difference,bottom10%blocks,and non-speech BGMloudness. | Independent meter measures actual rendered sound. |

Per-pattern values:P1/P8use−6LUwithout speech;music-led P5uses0LUand10LUgap;explanation P6uses−14LUand18LUgap;others−8LUand14LU. Old plans without scene information retain one track plus`sidechaincompress`;adjust intervals/regenerate for the new method. P5beat synchronization cuts to one track's beats,so tracks cannot change,although volume can.

### Verification

- [BgmSceneTests.cs](../../../tests/VideoCreator.Tests/BgmSceneTests.cs),14tests using actual FFmpeg:calculated integrated loudness agrees with ebur128/loudnorm within±0.3LU;energy ordering chill<upbeat<hype;exclude initial three-second silence/fades;word-to-speech intervals including joined gaps/speed/clip position;minimum20-second scenes;Viterbi energy matching/no short-scene switching/fixed manual choices;ducking based on voice level/music peaks;gain lookahead/mute/fades;crossfade/loop positions;exported BGMducks before the first word,reaches planned depth,and releases over0.6seconds,measured against identical unducked music;measured speech gap meets target and agrees within±1.5LUwith independent mixed-audio-minus-voice power estimation;no audio gaps at scene changes;review operations/regeneration inheritance.
- Actual audio,[spikes/20-bgm-scene.cs](../../../spikes/20-bgm-scene.cs):three sections,total93seconds,from a30-minute recording,P3. Speech-time difference14.3LU,target14,unducked8.1;non-speech−8.0LU,target−8. Planning estimated voice−17.0LUFSversus measured−17.5. Azure AI Speech retranscription of the mix has94.6%character-bigram recall relative to voice-only transcription,versus96.1%without ducking. ASRdoes not break at8LU,so this checks only minimum intelligibility;the ebur128difference demonstrates ducking's effect.
- Actual app E2E,[ui/verify-bgm.mjs](../../../ui/verify-bgm.mjs),Edge:register two tracks together,excluding two seconds' initial silence;quiet pad energy0.01,fast drums0.84. Generate with“Automatically select for scenes”:preview S-15is18.5LU,P6target18. Change to drums and24LUgap in review:all reductions deepen6dB,and preview measures24.4LU. Target voice gap remains despite2.4LUtrack-level difference.
- Fixed registration:previously BGMused video probing requiring a video stream,so MP3without cover images failed500. Switched to audio-specific`MediaProbe.ProbeAudioAsync`.

### Limitations

- Energy weights/scales are explainable measured-value rules,not evaluated on labeled data. No happy/sad mood matching;choose in review.
- Bundled BtbN latest master FFmpeg N-128532 arm64 AAC encoder stalls at192kb/s when audio begins after complete silence;not at128kb/s. Production uses192kb/s. Consider pinning `scripts/get-tools.ps1` to release version;outside this feature change.

## 20. Multilingual UI, documentation, and videos

- UI languages are Japanese, English, Simplified Chinese, and Korean. Display language and video language are separate. Translations mirroring the originals belong in `locales/en`, `locales/zh-CN`, and `locales/ko`. Historical Japanese screenshots show the actual UI at capture time, not evidence of the translated interface.
- Project `language` is `ja-JP` (default), `en-US`, `zh-CN`, or `ko-KR`. Creation/update APIs validate it and reject changes during generation/export. `Runtime.AsrForLanguage` propagates it to Speech `locales`. Transcription/laughter caches are language-specific; QA, reediting, and posting text for old plans use transcripts in the plan's language.
- Subtitles remain verbatim. Restore spaces between ASR words for English/Korean; concatenate characters for Japanese/Chinese. `CaptionText` performs language-specific splitting, line breaking, and normalization. QA repair uses the same joining to preserve words/IDs/timing. Masking across multiple words also checks spaces.
- Specify video language in prompts for selection, series outlining, decorative text, cards, AI QA repair, and posting text. Stock cards, effects, recaps/previews, and posting templates also use video language. AI decorative changes remain unapproved and require human review.
- Save creation language in `EditPlan.language`. Japanese/English use M PLUS 1p; Chinese Noto Sans CJK SC; Korean Noto Sans CJK KR. `scripts/get-tools.ps1` downloads OFL fonts/licenses. Explicitly fail export if required CJK fonts are missing, rather than relying on OS fallback.
- [LocalizationTests.cs](../../../tests/VideoCreator.Tests/LocalizationTests.cs) verifies API persistence/invalid values, Speech request locale, language caches, word/space/masking preservation, AI prompts, ASS/SRT, actual libass rendering/missing glyphs, measured safe-zone pixels for long English/Chinese/Korean captions, and MP4 previews. Non-Japanese width estimation includes wide Latin glyphs; rendering and face-avoidance layout share the same font-fitting logic. Check document structure/completeness/links with `node scripts/verify-localized-docs.mjs`.
- Limitations: automated tests use synthetic video/audio and fixed responses. They do not guarantee Speech accuracy on real English/Chinese/Korean speech, natural AI wording, or native-speaker translation review. Automatic subtitle translation and automatic detection of mixed speech languages are out of scope.
