# Quickstart: make your first video

[↑ Documentation map](../README.md) | [日本語](../../../users-guide/quickstart.md) | **English** | [简体中文](../../zh-CN/users-guide/quickstart.md) | [한국어](../../ko/users-guide/quickstart.md)


[App README](../README.md) / [Guide index](README.md) / [Screens](screens.md) / [Troubleshooting](troubleshooting.md) / [Configuration/API](configuration-api.md)

**Goal:** make P4 candidates from one speech video, review content, and save a 1080×1920 MP4 and subtitles. Processing time depends on footage, PC, and services; no fixed completion time is guaranteed.

## 1. Prepare your PC and footage

- Windows 10/11 (x64 / ARM64), Internet. Screens verified in Microsoft Edge.
- Download/extract the repository. Without Git, use GitHub “Code → Download ZIP”; open the extracted folder, not the ZIP.
- Prepare **one video you have permission to use**, including music, faces, logos. Listed formats: MP4 / MOV / MKV / WebM, 10 seconds–3 hours. Start with a few minutes of speech. P4 requires 25–35 seconds of main content; a 10-second input does not guarantee success.
- Check free space. File selection/drag-drop needs the source size in both temporary and destination storage. On one drive allow about twice the source size plus proxies, cache, previews, exports.
- **Full-path input** avoids copying, but moving/deleting the original makes the project unusable.

## 2. Prepare your cloud settings

Automatic subtitles use Azure Speech Fast Transcription. Ask your Azure administrator to confirm:

| Purpose | Information/permissions |
|---|---|
| Required: transcription | Compatible resource, custom-subdomain endpoint `https://<your-resource>.cognitiveservices.azure.com/`, supported region, user's `Cognitive Services User` role |
| Optional: Foundry selection | Compatible model deployment name, `https://<your-resource>.cognitiveservices.azure.com/openai/v1`, `Cognitive Services OpenAI User` |
| Optional: Copilot selection | Copilot entitlement and installed, signed-in GitHub Copilot CLI |

Azure is metered. Configure **the deployment name you created**, not a model name. Setup creates neither resources nor deployments.

Before launch, edit the `VideoCreator` section in [appsettings.json](../../../src/VideoCreator.App/appsettings.json). This is a section example, not a replacement for the entire file.

```json
{
  "Asr": {
    "Provider": "azure",
    "Endpoint": "https://<your-resource>.cognitiveservices.azure.com/",
    "Locale": "ja-JP"
  },
  "Llm": {
    "Provider": "none"
  }
}
```

Starting with `Llm.Provider=none` is fine: Speech makes subtitles; volume peaks select candidates. Configure Foundry or Copilot for content-based selection.

**Warning:** shipped settings contain example endpoints. You may not have access to those resources. Do not store keys/tokens in settings. Check your organization's transmission/data-use policy.

Official references: [Fast Transcription](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create) / [Speech Entra ID authentication](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-configure-azure-ad-auth) / [Regions](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions).

## 3. Launch and check readiness

Double-click root [run.cmd](../../../run.cmd), or run from the repository folder:

```powershell
.\run.cmd
```

First run installs missing .NET SDK 10, FFmpeg, Japanese fonts, models, and Azure CLI. Administrator/sign-in (`az login`) prompts may appear. To install Copilot CLI too, use `.\run.cmd --setup --with-copilot`.

- Every launch builds current sources; failed builds do not launch old artifacts.
- When ready, the installed/configured Windows default browser opens <http://127.0.0.1:8765/> (or configured port). No browser installation or new profiles. If opening fails, use the warning's URL manually. `--check` / `--no-start` launch neither app nor browser.
- Keep the launch terminal open; closing it stops the app.
- `FAIL` means missing required local components. `WARN`, particularly Azure warnings, may mean uncached transcription is unavailable. “Started” does not mean “subtitle generation ready.”
- `.\run.cmd --check` installs/saves nothing, but **sends minimal Speech/OpenAI requests to configured endpoints, potentially causing traffic and small charges**.
- If unable to assign missing roles, send the displayed information to an administrator. Do not use `--assign-roles` casually.

## 4. Add footage and choose P4

1. Enter Project name; click **New project**.
2. **Choose file** or drag/drop footage.
3. Check name, duration, resolution, audio; confirm rights for each asset.
4. Choose **P4 Clip (long video → 30 seconds)**; a blue border indicates selection.
5. Series off; **Standard** narrowing. BGM/openings are unnecessary initially.
6. Read destinations/cost estimates in Settings or at the bottom, then **Create 3 candidates**.

![Assets, patterns, generation in the real app](../../../users-guide/images/screen-project.svg)

This screen has three columns. Scroll within each to reveal lower controls. The six-stage indicator shows progress; it is not a set of navigation tabs.

## 5. Review and revise candidates

Generation shows overall/current-task progress. Play each candidate and check:

- Does it preserve the original meaning and make sense independently?
- Subtitle spelling, proper nouns, numbers, readability.
- Face/screen framing, volume, speech boundaries.
- QA failures, warnings, not-run checks. **Not run does not mean passed.**

Open **Review/edit**. After text changes, move focus out of the field to apply, then **Update preview** and review again. Correct transcription errors before manual subtitle edits: transcript corrections regenerate subtitles.

![Real review screen](../../../users-guide/images/screen-review.svg)

## 6. Export and save

Choose either:

- **Export one directly:** in Review, **Approve this content and export (1080×1920)** (or **Export only this video now (1080×1920)** with multiple videos). Approves current content before exporting. Hard QA and required AI-text approvals must also pass.
- **Review multiple videos individually, export together:** **Approve and next video**, then on the last **Approve and proceed to export** → **Export selected videos**. **Approve this content and return to candidates** lets you check remaining work in the candidates' **Review progress**. Editing requires reapproval.

In the export list, play/seek each completed final file. Inspect its absolute path; **Copy path** or **Open folder** opens storage. Individual completion screens offer the same operations and final QA.

![Real export-completion screen](../../../users-guide/images/screen-export.svg)

| File | Purpose |
|---|---|
| `.mp4` | Publishable video with burned-in subtitles; watch to the end before posting |
| `.srt` | Timestamped subtitles for compatible platforms/editors |
| `.ass` | Subtitle rendering data including fonts, placement, decoration |
| `.editplan.json` | Cuts/captions/audio/insertions; development/reproduction record |
| Three thumbnails | Posting-image candidates; choose yourself |
| `.metadata.md` | Proposed title/description/hashtags; fact-check and manually paste |
| `.post.json` | Same posting text as completion screen: YouTube description, Viva Engage post, chapters; also appended to `.metadata.md` for easy copying |

**Posting text (copy/paste)** in export/completion displays saved YouTube descriptions, Viva Engage posts, and `00:00 Chapter title` chapters, each with **Copy**. If missing, **Create posting text**. Expand **Other artifacts/previews** to inspect subtitles, metadata, edit plans, thumbnails, and open/save individual files.

- Chapter times are actual exported-video times, including openings/cards. Omitted unless YouTube rules are satisfied: start 00:00, at least three, each ≥10 seconds. Vertical videos ≤3 minutes may be Shorts, without player-bar chapters.
- AI uses transcripts; unsupported numbers, katakana/Latin names, URLs, and masked words are automatically removed (kanji proper nouns cannot all be detected). Reasons appear on screen. No-LLM mode uses templates.
- **Create another proposal** changes the angle. The display is read-only saved text; make revisions after copying into the destination. If Copy fails, select the displayed text and Ctrl+C.

Showing SRT alongside burned-in subtitles may double captions; choose the platform's display behavior. The app does not post/publish or automatically populate series URLs.

Stop with Ctrl+C in the launch terminal. Reopen a project next time to resume its last project-settings, generation, candidates, review, or export stage; missing references fall back to a recoverable earlier stage. See [backup](troubleshooting.md#storage-and-backups).
---

[↑ Documentation map](../README.md) | [日本語](../../../users-guide/quickstart.md) | **English** | [简体中文](../../zh-CN/users-guide/quickstart.md) | [한국어](../../ko/users-guide/quickstart.md)
