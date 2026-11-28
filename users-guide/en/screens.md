# Screens and components

<!-- language-nav -->
[日本語](../screens.md) | [English](screens.md) | [한국어](../ko/screens.md) | [简体中文](../zh-CN/screens.md)
<!-- /language-nav -->

[Start](start.md) / [Guide index](README.md) / [Detailed overview](overview.md) / [For engineers](technical-reference.md)



[App README](start.md) / [Guide index](README.md) / [First video](quickstart.md) / [QA/errors](troubleshooting.md) / [Configuration/API](configuration-api.md)

Examples captured the 2026-11-08 implementation in Microsoft Edge, viewport 1440×1000; long pages use full-page captures. Controls vary with PC width, pattern, asset count. Scroll **inside each column**.

> **Display language:** app buttons and descriptions use your chosen app language. Native file/folder dialogs follow the operating system/browser language instead. Selected filenames are shown verbatim, not translated.

## Shared controls

| Component | Meaning/action |
|---|---|
| Top-left VideoCreator logo | Project list; does not stop work |
| Top-right transcription/selection | Current providers; availability also depends on connection/permissions |
| Settings | AI selector, destinations, storage, tools |
| Assets → Pattern → Generate → Candidates → Review/edit → Export | Workflow position: blue current, check completed; not navigation tabs |
| Show safe area | Preview guide against posting-UI overlap; does not alter video |
| Messages/badges | Success/warnings/errors/approvals; read text/codes, not just colors |

## 1. Project list

![Project list](../images/screen-home.svg)

| Component | Meaning/caution |
|---|---|
| Project name / New project | Creates work; empty allowed, distinctive names recommended |
| Project card | The name opens the last work position directly (shown as "Continue at"); also created time, asset count, latest generation. Warns when a source video is missing. Most recently worked-on first |
| Delete | Confirmation; managed folder recycled, analysis/edit DB records deleted |

**Deletion is cleanup, not finishing work.** Full-path originals remain; uploaded assets/exports go to Recycle Bin with the project. Restoring only the folder does not restore DB edits. Wait for completion/cancel when busy.

## 2. Assets, pattern, generation

These are **three columns of one project screen**, not separate screens.

![Three-column layout](../images/screen-project.svg)

Top **Rename** opens save/cancel input. Storage path also appears. Renaming does not move folders.

### Left: assets

#### Trim the beginning and end

In the assets column, “Trim beginning / end” sets the main video's body in its own seconds. “Analyze and suggest edit points” shows manual boundaries padded around the first and last timestamped speech (transcript times corrected by the measured sound), plus automatic boundaries supported by both black picture and silence. When unbroken sound (music, applause or noise) runs for 1 second or more before or after the speech, its length is shown too. New transcription can incur charges through the configured speech service; previous analysis is reused.

Copy the manual suggestion to the inputs, review the picture and sound in the preview, adjust boundaries with the playhead, and save. Automatic application removes only continuous black, silent edges, keeping the whole recording when evidence is insufficient. It does not semantically identify greetings, setup conversations, silent demonstrations, conclusions or end screens.

The next generation keeps the entire body rather than extracting highlights: no internal cuts, speed changes, series splitting or additional shorts. Secondary recordings with different start times follow the same body through their sync information; portions they do not cover use only the main video. If the audio source does not cover the body, or the main video changes, revise the boundaries or sync. Clear trimming to restore ordinary generation. Original files and previously generated videos remain unchanged. Separately configured inserts, including closing videos, are outside this source-trimming operation.

![Asset controls](../images/screen-assets.svg)

| Component | Meaning/decision |
|---|---|
| Drop zone / Choose file | Upload/copy PC footage into project |
| Specify full path → Add | Reference absolute path, e.g. `C:\Videos\talk.mp4`; unquoted easiest |
| Asset information | Name/duration/display resolution/fps/VFR/audio/codec |
| Warnings | Rotation/VFR/no audio; visible video does not guarantee subtitles |
| Confirm rights / Confirmed | Personally check copyright/likeness etc.; confirmed checkbox not editable |
| Role (multiple assets) | Master/camera/wide/audio master/screen; affects P7/P10. Only one video can hold each role except Screen: picking a role another video has swaps that video to the role you gave up, with a warning next to your pick. Videos keep the order they were added (A, B…) |
| Speaker (multiple assets) | Camera's speaker 1–8 in transcript appearance order; blank automatic/unset |
| Offset (seconds)/sync | Camera time relative to master audio; inspect evidence/residual/drift before manual changes |

P7 needs ≥2 simultaneous cameras; P10 screen + presenter. Not synchronization of unrelated events. Automatic sync is imperfect; failures/out-of-range warn or fall back to master.

### Center: patterns

![Pattern controls](../images/screen-patterns.svg)

Selected card has blue border. **Opening/tempo/captions/audio** describe policy; seconds are short main-content targets. “Example” links external footage, not guaranteed app output. Scroll to P10.

| ID | Name | Main target | Use |
|---|---|---|---|
| P1 | High-energy challenge | 25–45 s | Challenges/results/reactions |
| P2 | One-shot gag / loop | 15–25 s | Brief punch/loop to start |
| P3 | Variety captions | 25–60 s | Humor/heavy decoration/reactions |
| P4 | Clip (long → 30 s) | 25–35 s | Recommended first; speech highlights |
| P5 | Beat-sync montage | 15–60 s | Cuts to BGM beats |
| P6 | Explanation/lecture (key points) | 30–60 s | Informative explanation |
| P7 | Interview switching | 30–60 s | Simultaneous cameras |
| P8 | Process compression (cooking/DIY) | 20–60 s | Fast-forward low-activity processes |
| P9 | Minimal/ambient | 30–90 s | Little decoration/original sound |
| P10 | Screen explanation (screen/inset) | 30–60 s | PC screen + presenter |
| P11 | Presenter background + slide PiP | 15–60 s | Places slides away from faces; switches to full-screen slides if necessary |
| P13 | News (anchor + visuals) | 30–120 s | Select one story and overlay visuals on the presenter |

For P11, add a presenter as the main asset and one screen recording with role Screen. Open the PiP scene editor, select only authorized scenes and save; proposed scene boundaries are not semantic slide detection. Optional PowerPoint source files (or a ZIP of slide images) replace matched recording frames with high-resolution originals. PowerPoint or LibreOffice converts decks; unmatched demo/video intervals remain the recording. Audio sync must succeed, and speech must overlap the saved scenes. Inspect slide meaning, text readability and face overlap in the preview; changing the main asset resets synchronization and requires new generation. See [P11 details](docs/IMPLEMENTATION.md).

P13 needs presenter footage; visuals are overlaid from a Screen video (with two or more anchors it also works without visuals). Follow the P11 sync and scene-selection steps and choose landscape 16:9 for a layout closer to the reference. Alongside verbatim captions, a headline is shown when supplied, avoiding faces and captions (or omitted if no space is available). When two or more synced anchor videos have the Main, Camera A or Camera B role, the edit automatically becomes a split screen sized to the number of sources (side by side for two, a row for three, stacked on portrait) and the current speaker's box gets a yellow frame. Speakers follow their assignment, otherwise Main, Camera A and Camera B are speakers 1, 2 and 3. While visuals are shown they are overlaid on the current speaker's camera, and an anchor is not shown where their recording has no footage. A fixed breaking-news ticker runs along the bottom: when candidates are built, the topics in the transcript are researched with the web search of the AI connection (Azure OpenAI or GitHub Copilot), and up to 5 items whose source URL actually appears in the search results are shown with source and date. Items without a source, with a future date or that are too long are dropped. In the "Ticker" panel below the telop table you can edit, hide and approve items; export is blocked while unapproved items remain (H-08). Web search may send data outside your organization's compliance boundary, and its cost is not included in the estimate. Connections without web search show no ticker. Review the headline, the correspondence between visuals and speech, and the ticker facts and sources. Studio backgrounds and visuals are not generated automatically.

These are not source durations. Inserts extend totals; series uses other rules. Preset `target.aspect` does not force the output orientation.

### Right: basic generation

![Basic generation controls](../images/screen-generation.svg)

| Component | Meaning/caution |
|---|---|
| Output orientation | Match the input (default; landscape video → landscape 1920×1080, portrait → portrait 1080×1920) / Portrait 9:16 (1080×1920) / Landscape 16:9 (1920×1080). **To make a vertical short from a landscape video, choose “Portrait 9:16”.** Use opening/closing assets that match the orientation |
| Divide into series | Off: highlights; on: whole source into consecutive parts |
| By count | 2–20; balance lengths at topic boundaries |
| By duration | 1–60 min, 0.5 min step; approximate, not identical |
| Split estimate | After assets added; estimates after silence removal when transcript exists |
| Economy | Strong-excitement ranges for long videos; cost/speed first, may miss quiet importance |
| Standard | Transmission/omission balance |
| Full text | Uses all text; may split requests, not necessarily one huge call |
| Cost/amount sent | Internal unit prices/estimates, not billing guarantee; short sources can match across all scopes |
| Destinations | Always above button: audio→ASR, text/signal summary→LLM, authentication; ASR independent of LLM. Cost breakdown expands amounts/costs |
| Create 3 candidates | Requires assets/rights; shows blockers. Up to three results; estimated cost beside button |

Groups: Output orientation, Series, Long-video narrowing, Audio, Opening/closing/transitions, Previous analysis. Click headings to expand/collapse; collapsed headings summarize settings (“BGM Auto / SFX On / Reactions Off”). Expansion persists in this browser. Packaging initially collapsed; series collapses narrowing because full text is sent. Scroll to Created patterns.

Series supports speech-based P1–P4/P6/P7/P9/P10, not P5/P8. Specify one split method. **Each part must retain at least one minute after silence removal.** Teasers/recaps/headings/next previews make total duration differ from main splits.

### Lower right: audio, masking, inserts

![Additional generation controls](../images/screen-generation-extras.svg)

| Component | Meaning/caution |
|---|---|
| BGM | Pattern default / none / automatic per scene (own registered tracks, else three built-ins) / whole-video track. All duck during speech; pattern target, normally ≥14 LU below voice |
| SFX | Pop/boom effects on/off |
| Let AI suggest reactions | P3 only; read/approve before export |
| Reaction presentation | Appears when enabled: Auto default (cards only at speech boundaries, overlays elsewhere; reasons in notes) / overlay / card (freeze 2–3 s, image/text/BGM). In P3 a card is a silent pause (off-white, dark text, no BGM or effect) and an overlay is a red box at the lower right from 0.6 s after the line for 2.4 s |
| Mask words | Japanese/ordinary comma separated; `●` text, beep audio. Does not anonymize source/transcript |
| Own BGM | Batch audio selection, rights registration scoped to project; enables scene auto. Shows loudness/tempo/energy (calm/normal/lively)/usable region excluding leading/trailing silence. Remove unregisters unless plan references it |
| Use app-shared BGM in this project | On by default; includes shared tracks in automatic scene selection and whole-video choices. Off excludes them from future generation, without changing existing plans |
| App-shared BGM | Register rights-cleared tracks for all projects, inspect measurements, or remove unless any plan uses them. Project tracks take precedence when filenames match; a project track can be promoted to shared |
| Insert opening/closing | Available with shared registrations; opening after teaser, closing last |
| Transition cards | Brief still/text/SFX at changes; AI audience reaction/praise text requires approval |
| Register/reregister | Folder video names containing `Opening` / `Closing`, PNG/JPG card images; shared across projects |
| Choose folder… | Windows picker; cancel unchanged; check taskbar if hidden |
| Card duration | 2–3 s, default 2.5; full-screen image/panel + text + dedicated BGM, then source resumes. Card BGM plays even when main BGM none |
| Enhance small videos | Background after registration; measured acceptance, environment-dependent timing |
| Unregister | Removes shared registration; existing candidate plans do not automatically rebuild |
| Create transition images from video | Default Do not create; source or registered Opening/Closing, max 2–6 frames |

Local extraction rejects black/white/similar images, not AI semantic highlights. Affects **newly generated candidates**. Turning off retains files. Shared card images take priority.

### Subtitle/caption proper nouns

At the project bottom register people/places/companies/organizations/products/works. Enter desired spelling, optionally pronunciation and likely ASR variants. Next generation uses phrases as ASR hints and canonical spellings for caption proper-noun matching. Type is organizational only; ASR receives spelling/pronunciation/variants.

Saved transcripts/word timecodes do not change. A changed dictionary triggers new transcription on next candidate generation. Hints do not guarantee accuracy; review audio/subtitles and correct transcripts as necessary. Word times come from recognition.

## 3. Generation progress

![Generating](../images/screen-progress.svg)

| Component | Meaning |
|---|---|
| Overall bar | Pipeline progress, not constant-rate clock |
| Current task bar | Measured when possible; indeterminate otherwise |
| Elapsed/remaining | Elapsed since opening screen; remaining estimated for current measurable task |
| Stages | Proxy → transcription → signals → selection → plan → preview |
| What is happening | Normal descriptions; cache/no AI/pattern can differ |
| Cancel | Stops generation, not an export cancellation |

Leaving the screen does not stop server work. On failure/cancel, read reasons and Return to project to fix conditions.

## 4. Candidates and series

![Candidate comparison](../images/screen-candidates.svg)

| Component | Meaning/caution |
|---|---|
| Number/title/reason | Rank/evidence; AI/signal proposals, not popularity guarantees |
| “n seconds source” / source times | Selected range, not final after cuts/teaser |
| Preview/player | Watch/listen; low-res, not final-quality judgment |
| Hook/self-contained/payoff/emotion | Content assessment; signal fallback need not imply AI understanding |
| Audio energy/overall | Signals/composite; indicative 0–10 |
| Signal-based | AI missing/failed, volume-first fallback |
| QA badges | Plan passes/warnings versus final not-run |
| Adjustment record | Corrections/constraint reasons |
| Review/edit | Open candidate editor |
| Regenerate with instructions | Find alternatives; semantic instructions may not apply without AI |
| Return to assets/patterns | Change pattern/options |

Series replaces rank/scores with first/middle/last or episode order, source main ranges, split reasons, chapters. Change count/duration in project and regenerate to alter boundaries.

![Series settings](../images/screen-series-options.svg)

![Series candidates](../images/screen-series.svg)

### Approve and batch export

| Component | Meaning |
|---|---|
| Approve this content | Record review of **the current plan** |
| Unapproved/approved/modified after approval | Edits require reapproval |
| Revoke/approve again | Update judgment |
| Export checkbox | Select approved/not-exporting candidates |
| Select all approved | Selection aid, not automatic approval |
| Export selected videos | All validated first; any invalid starts none; sequential rank/part order |
| Progress/open exported video | Per-candidate state/completed file |
| Cancel queued | Unstarted only; active completes |

### Review videos individually, export together

With ≥2 videos in one pattern (Job), **Review progress** appears above candidates.

| Component | Meaning |
|---|---|
| Approved n / N · Exported m / N | Approved count includes exporting/exported; completed count |
| Review next unapproved (name) | First unapproved video editor |
| Proceed to export (n approved) | Stage 6 list; primary button if any approved |
| Per-video state | Unreviewed/approved/reapproval/exporting/exported; name opens editor |

Workflow Review/edit links first unreviewed; Export links export list.

## 5. Review/edit

![Review/edit](../images/screen-review.svg)

| Component | Meaning/action |
|---|---|
| Update preview | Rerender after edits; do not judge old preview |
| Duration/main/total/cut/caption counts | Plan data; total includes enabled inserts |
| Captions/transitions | One final-time-ordered table for text/opening/transition/reaction cards/closing. Top switches bulk-enable captions/emphasis/headings/onomatopoeia/reactions/opening/transition cards/closing; reactions include overlays and cards |
| Opening/card rows | Position/duration/text/enabled. Card text ≤20 chars. Freeze full-screen: similar-aspect images fill; others fit over blurred copy |
| Approve all reactions/cards | Bulk approval is at the top left of the captions and transitions panel; each row's approval and visibility controls are in the leftmost column. Read AI text yourself. Manually rewritten card text is recorded as human-authored |
| Cuts (video/speed) | Source/duration/speed/face tracking/screen zoom/inset; not general timeline editing |
| BGM/SFX | Audio/BGM chooses none/per-scene/whole track, speech-to-BGM difference 10–30 LU (10 LU approximate intelligibility floor), nonspeech BGM. Per scene: times, energy/measurements, track (auto/none possible), −12–+6 dB, rationale. Band shows track flow/ducking (height = reduction); blue playback line. Update preview measures actual speech difference (S-15). P5 track locked to beat cuts; volume editable |
| Correct transcript | Words within used range; times unchanged, captions regenerated; manual caption edits may be overwritten |
| Start/end one sentence earlier/later | Short boundaries; source/duration limits may prevent |
| Automatic checks | Left of the captions and transitions panel, or above it on narrower screens. Immediate plan QA, not final loudness/resolution |
| Approve/revoke check review | Save/revoke review record/time |
| Bulk check approval | All performed/unapproved, including pass; excludes not-run |
| Approve and export this content | Single video; approve current then export (size follows the output orientation) |
| Approve only and return | Single video; save approval, candidates |

With ≥2 same-pattern videos, heading controls change:

| Component | Meaning/action |
|---|---|
| ‹ Previous / Video i / N selector / Next › | Switch within pattern; options show review state |
| Approve/next (name) | Approve current; next unapproved, wrapping |
| Approve/proceed to export | When all others approved; Stage 6 |
| Approve/return to candidates | Save, inspect progress |
| Export only this video now | Approve/export without list |

Already-current approvals are not duplicated. Hard failures disable all approval buttons.

Check-review approval records “I reviewed this”; it neither turns fail/warn into pass nor releases export gates. Separate from text/card approval; persisted across reload, invalidated by edits/result changes.

Series boundaries cannot change here. Transcript UI currently replans, potentially producing a series restriction error. Correct series subtitles in caption rows; for underlying transcript correction, use a short workflow and inspect [API restrictions](configuration-api.md#editing-operations).

### Caption table

![Caption table](../images/screen-telops.svg)

| Field/control | Meaning |
|---|---|
| Type | Subtitle=speech, emphasis=key words, hook=opening draw, heading=topic, reaction=commentary, onomatopoeia=sound text, counter=numbers |
| Time | Final-video seconds, including inserts |
| Text | Edit; blur saves. 40-character input cap does not imply readability/safe placement |
| Length | Full-width equivalent (half-width about 0.5); width guide |
| Speed | Full-width chars/second; excessive unreadable |
| Row switch | Individual enable/disable, disabled faint |
| Type switch | Bulk show/hide |
| Approval | Review unapproved AI text; reactions need explicit approval |

Restoring masked terms/numbers/names manually may fail QA. Prioritize readable faithful content, not merely clearing warnings.

## 6. Export

### Export list (multiple videos)

Open via Candidates **Proceed to export** or last video's **Approve/proceed** (`#/job/{id}/exports`). Reopens here after app restart.

| Component | Meaning |
|---|---|
| Review progress | Approved/exported counts, unapproved links |
| Video rows | Name/title/duration/QA/editor/approval/export selection |
| Export checkbox | Initially selects approved not-yet-exported |
| Export selected | Same batch processing; all validated, any failure starts none |
| Progress/open exported | Per-video state/completion |
| Final video/location | Automatically shows final player per completed video; play/seek/safe guides/absolute path/copy/**Open folder** |
| Posting copy | Saved read-only YouTube/Viva Engage/chapters, individual Copy. Missing: **Create posting text**; opening list alone does not generate |
| Other artifacts/previews | Expand per-file paths/Open/Save. Image previews; SRT/ASS/Markdown/JSON text **Preview contents**, first 65,536 chars plus full link |
| Viva Engage image (if configured) | Preview/save a 1000×200 px image separate from the thumbnail. The generation prompt and original post are saved as JSON too. Recreating the post also regenerates the image, which incurs extra charges |

Previous final remains inspectable during re-export. Edit-invalidated results are not shown as current. Missing/play/copy/folder failures show per video; select text + Ctrl+C if copy fails. After moving/deleting files, **Reload artifacts**.

### Completion

![Completion/files](../images/screen-export.svg)

While exporting: composition, loudness, QA, related-file creation. Afterwards:

| Component | Meaning |
|---|---|
| Candidates (review others)/export list | Return to other same-pattern work |
| Hard-pass count | Plan/final results; row count differs from distinct IDs |
| Final video | Review at final quality |
| Location/copy/open folder | Absolute path, copy, select completed file in Explorer; messages on failure |
| File links | Browser-open/save MP4/SRT/ASS/JSON/metadata |
| Thumbnails | Three choices; set on platform yourself |
| QA | ID/state/measurement/criterion/plan/final |
| Posting text (copy/paste) | Shared read-only snippets: YouTube ≤5,000 chars, Viva Engage ≤10,000, chapters `00:00 Title`. Counts, red over limit. Copy; revise at destination. Generate if absent; Create another proposal. Yellow rejected-word/reason warnings; chapter omission reason replaces chapter field |
| Collapsed records | Commands/reproduction data; remove secrets/personal paths before sharing |

## 7. Settings

![Settings](../images/screen-settings.svg)

| Component | Meaning/caution |
|---|---|
| Foundry/Copilot/no LLM | Highlight selection; immediate server change. Close is not Save |
| Unavailable | Missing endpoint/CLI, forced no-LLM startup |
| AI connections | Azure uses **a single Microsoft Foundry connection**, and subtitles (Azure Speech) use the same resource. Choose the authentication method (Microsoft Entra ID / API key) and specify the endpoint and deployment names. GitHub Copilot: sign-in, model, reasoning effort, context. **Test connection** does not save; **Test and save** tests again and persists only on success. As the subtitle test, one second of synthesized silence is sent; if transcription fails, nothing can be saved. Required even in LLM-disabled mode. Keys/tokens use Windows DPAPI and are never returned; unsupported OSes refuse secret storage. **Choose from Azure** lists the resources and deployments available to the signed-in Entra ID identity and fills the boxes (saving still requires a test). Until configured, the top of the screen shows `Transcription: None`. See [Azure access](azure-access.md) |
| Unapplied input (drafts) | Foundry/Copilot and save-location input is auto-saved as a draft in the local DB about 0.5 s after each change and on Close/page exit, and is restored after reopening the dialog or restarting the app, with "Restored unsaved input (time)" and **Discard draft**. Drafts are never used for connections (only settings saved after a passing test and an applied location change are used), and keys/tokens are never put in drafts (only **Test and save** stores them, DPAPI-encrypted). A successful save, forget or location change removes that draft, as does returning to the saved values |
| Dialog width, folder picker | Width is 65% of the window, at least 600px and at most 1140px (never wider than the window minus 32px). Basis: Fluent 2's 600px default dialog width, Bootstrap's 1140px XL width, and WCAG 1.4.8 line length (80 characters, about 40 for CJK); explanatory text is capped at 40em. 1280px window → 832px (65%); 1920px → 1140px cap (59%). The **Choose folder** dialog opens in front of other windows with a purpose-specific title |
| Destinations | Audio/text/authentication; no LLM ≠ no ASR |
| Current storage/count | New-project location, existing count |
| New storage/choose folder | Not DB/cache DataDir |
| Move everything/do not move | Includes root files in current storage / only new projects at new location |
| Review details | Sizes/space/items/problems; no move yet |
| Apply after review | Actually changes; refuses busy/collisions |
| FFmpeg/missing filters/CLI/pattern count | Diagnostics; mask personal CLI paths before sharing |
| Close | Closes dialog |

AI choice and connection settings persist in `DataDir/ai-settings.json` across restart, overriding startup configuration. Removing a saved connection restores its startup configuration. UI storage location settings persist in DB and take precedence. Do not manually move folders back after relocation; see [recovery cautions](troubleshooting.md#storage-and-backups).

![Lower settings: storage/diagnostics, personal paths masked](../images/screen-settings-storage.svg)
---

