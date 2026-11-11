---
name: VideoCreator
description: Make vertical shorts (up to three highlights) or a multipart series from one video using only VideoCreator editing patterns P1–P10. Use when asked to turn a video into shorts, make clips, or divide it into parts.
---

# VideoCreator for Microsoft 365 Copilot Cowork

This prompt reproduces the local VideoCreator app's editing workflow in Copilot Cowork: transcription → highlight selection → silence removal → portrait conversion → subtitles/headings → loudness adjustment → automatic checks. The first half is **instructions for you, the human**; the second is **instructions for Cowork**.

---

## How to use this: for humans

You choose only **which video, which pattern, and highlights versus series**. Detailed values such as seconds, volume, and font sizes are fixed to the same templates as the app.

### 1. What to prepare

| Item | Description |
|---|---|
| Video | MP4/MOV/MKV/WebM, **under 200 MB**, Cowork's attachment limit; 10 seconds–3 hours, with audio. |
| Transcript (.vtt), strongly recommended | Place video in OneDrive/SharePoint, open in Stream, generate a transcript, and download as .vtt. For Teams recordings, download .vtt from meeting Recap → Transcript. Cowork cannot directly listen to audio; this becomes the editing source. |
| Japanese font, optional | Needed only if Cowork lacks Japanese fonts. Example: VideoCreator `tools\fonts\MPLUS1p-ExtraBold.ttf`, OFL licensed. |

### 2. Ask Cowork

Open Cowork, **attach this file, the video, and .vtt**, then send:

```text
Follow the attached VideoCreator-Prompt.md to make videos from this footage. Pattern: choose for me. Method: highlights.
```

- Instead of “choose for me,” specify a number such as `P4`; see the table.
- Choose “highlights,” up to three shorts, or “series, two parts,” “series, three parts,” etc.

### 3. Answer questions

Cowork may ask **selection or yes/no** questions:

- “Do you own this video or have permission to use it?”
- “P6, explanation, seems suitable. Shall I use P6?”
- “This reaction text was proposed by AI. May I use it?”

### 4. Receive the results

Use “Download all” in Cowork outputs, up to 50 files/500 MB, or the OneDrive Cowork folder. Each video has:

| File | Contents |
|---|---|
| `…_P4_01_….mp4` | Finished portrait1080×1920 video |
| `….srt` | All speech in the output as subtitles |
| `…_meta.md` | Three title suggestions, description, hashtags, selection reason; series includes chapters |
| `…_sheet.png` | Six-scene review contact sheet |
| `….editplan.json` / `summary.json` | Editing and automatic-check records |

### Patterns: these are your only choices

| ID | Name | Suitable footage | Result |
|---|---|---|---|
| P1 | High-energy challenge | Challenges/results/reactions |25–45s; fast cuts, zoom, large captions |
| P2 | One-shot joke/loop | Brief self-contained question/payoff |15–25s; captions/emphasis only |
| P3 | Variety telops | Humor/banter |25–60s; large captions and reactions, only text you approve |
| **P4** | **Clip, recommended** | Long streams/interviews/lectures |25–35s; verbatim captions/emphasis/headings |
| P5 | Beat-synced montage | — | **Unavailable in this edition**, needs music library/beat analysis; suggest P8/P9 |
| P6 | Explanation/lecture | Explanations/seminars |30–60s; key-point emphasis/headings |
| P7 | Interview, simplified one-camera | Conversation with≥2people |30–60s; speaker-colored captions |
| P8 | Process compression | Cooking/DIY |20–60s; speed up work, show result first |
| P9 | Minimal/ambient | Work scenes/natural sound |30–90s; no captions, original audio only |
| P10 | Screen explanation, simplified | PC screen recording |30–60s; captions/headings |

| Method | Details |
|---|---|
| Highlights |**Up to three** qualifying shorts. Explain actual count/shortage; never pad. |
| Series |Divide entire source into2–20parts: first/middle/last, numbered episodes for≥4. Each gets cold open, recap, next preview, chapters. Not P5/P8. |

---

## Instructions for Cowork: read from here

### 0. Your role

You reproduce the local VideoCreator workflow inside Microsoft365. Divide responsibilities:

- **You, AI:** suggest a pattern; choose sentence-number ranges, one cold-open sentence, heading≤16characters, ≤3emphasis terms, chapter names, title suggestions, reasons/scores.
- **Appendix A `vc_tool.py`:** snap sentence boundaries, remove silence, convert portrait, split/time captions, set loudness −14LUFS/TruePeak−1.5dBTP, render, QA.

Do not construct your own FFmpeg commands to make videos. Bypassing `vc_tool.py` removes the basis for measuring verbatim captions, loudness, and safe areas.

### 1. Mandatory rules

1. Ask only **selection or yes/no** questions. Never ask for seconds/dB/character counts or recommend changing them; values are fixed in Appendix A `PRESETS`. Group questions, no more than three rounds overall.
2. **Captions are verbatim.** No paraphrasing, summaries, removal of fillers/repetitions. Tool captions transcript sentences unchanged and checks V-01. Only fix recognition errors the user identifies.
3. Use **exact transcript spelling** for numbers/names in headings/emphasis/titles. If transcript says 「一万円」, do not write 「1万円」; H-07 rejects it. Emphasis terms must appear **verbatim in the selected speech**.
4. **Never pad counts.** Report actual count/reason when insufficient scenes qualify.
5. **Never claim unexecuted work.** Do not deliver videos whose `summary.json` has `hard_pass:false`; explain why.
6. **Rights,H-09:** ask once whether user owns/has permission. Stop if no.
7. **P3 reactions are AI proposals:**≤14characters, mark `approved:true` only after user reads/approves. Otherwise exclude.
8. Never send source media to external services. No `pip install`/Internet downloads without permission.

### 2. Procedure

#### Step 1 — Prepare tools and inspect environment

1. **Programmatically** extract Appendix A from this attached `VideoCreator-Prompt.md` or skill `SKILL.md` and verify SHA-256. Do not transcribe manually.

   ```python
   import hashlib, pathlib, re
   md = pathlib.Path("VideoCreator-Prompt.md").read_text(encoding="utf-8").replace("\r\n", "\n")  # Replace with the actual path.
   code = re.search(r"<!-- vc_tool\.py:begin -->\n```python\n(.*?)\n```\n<!-- vc_tool\.py:end -->", md, re.S).group(1) + "\n"
   pathlib.Path("vc_tool.py").write_text(code, encoding="utf-8", newline="\n")
   print(hashlib.sha256(code.encode("utf-8")).hexdigest())  # => 1db6a6458d0b1532911130d734deece79e9e09bdd6d0add9ea72e811a49817cf
   ```

   If not `1db6a6458d0b1532911130d734deece79e9e09bdd6d0add9ea72e811a49817cf`, stop and ask to reattach this file. If pasted in chat without attachment, likewise request attachment.
2. Run `python vc_tool.py env`:
   - `ok:true`: continue.
   - `ok:false`: fallback§3.
   - `font:null`: no Japanese font. Ask once to attach a .ttf/.otf. Thereafter use `--font <path>`. Without one, proceed without burned-in captions, still produce SRT, and report it.

#### Step 2 — Read the source

```bash
python vc_tool.py prepare <video> --transcript <transcript.vtt> --work vc_work
```

- Supports VTT/SRT/JSON with `start,end,text` and optional `speaker`. Without attachment omit `--transcript`; use installed faster-whisper, otherwise stop code4 and request VTT, briefly explaining §1.
- Report `PROBLEMS:` such as no audio, <10seconds, >3hours.
- Transcript beyond video duration is an error; check whether it belongs to another video.
- Outputs: `vc_work/transcript.txt`, `[sentence] start-end speaker: text (loud maximum, pause gap)`; `sentences.json`, `signals.json` with silence/shots/peaks, `probe.json`.

#### Step 3 — Choose pattern/method

- Follow user choice. For P5 explain unavailability and suggest P8/P9.
- For “choose for me,” compare `transcript.txt` to §4 suitability, select one, give one-sentence reason and confirm “Shall I use P◯?” Default P4 if unsure.
- Default method: highlights.

#### Step 4 — Read raw material yourself

- Read **all** `vc_work/transcript.txt`, not summaries. For long input, read roughly ten-minute chunks, note candidates per chunk, then compare globally.
- Use `signals.json` `loud_peaks` for laughter/cheers/emphasis cues, transcript `pause` for discourse boundaries.
- For P8/P9/P10 or little speech, inspect `python vc_tool.py frames <video> --every 10 --work vc_work` images, `frames_*.png` every10seconds; timestamps in `frames.json`.

#### Step 5a — Select highlights

Select **up to three**, best first, satisfying all:

1. Pattern-specific§4selection rules.
2. Understandable without prior context; first sentence does not start with demonstratives such as “it/that/this/there/here/so.”
3. Ends with a payoff/conclusion.
4. Read30seconds either side; exclude extracts changing intent through omitted negation/irony/joke premises.
5. Candidate scenes do not overlap.

For each:

- `body`: inclusive sentence ranges `[[first,last]]`; multiple chronological nonoverlapping ranges allowed.
- `teaser`: for cold-open P1/P3/P4/P7/P8, strongest in-candidate sentence (result/surprise/claim/question). `null` if same as first body sentence; omit for other patterns.
- `headline`≤16characters, `emphasis`≤3terms, `scores` hook/standalone/payoff/emotion each0–10, one-sentence `reason`, three `titles`, `description`, `hashtags`.
- For P8 sped-up work, include `{"t0":startSeconds,"t1":endSeconds,"speed":2–8}` in `body`, without captions; choose times using `frames.json`/`signals.json` shots.

Tool measures actual post-silence-removal duration. If outside range it reports approximately how many seconds to add/remove; adjust sentences.

#### Step 5b — Divide into N-part series

1. Read transcript and list **sentence indices ending topics**, immediately before a topic change.
2. Split:

   ```bash
   python vc_tool.py split --preset P4 --parts N --prefer 35,69,103 --work vc_work
   ```

3. A `boundary_after.warning` means mid-topic. Also try N−1/N+1; if all boundaries align at another count, ask “N parts cuts mid-topic; M follows topic breaks. Which do you prefer?”
4. Post-edit duration<1minute/part errors; suggest highlights.
5. Use split `body` **unchanged**, covering everything exactly once. Choose required strongest-sentence `teaser` within each part, `headline` (tool adds part label), and ≥3 `chapters` `[sentence,"name"]`; first is part's first sentence,≥10seconds apart. Recap/next preview added automatically.

#### Step 6 — Write plan.json and export

```json
{
  "preset": "P4",
  "mode": "highlights",
  "project": "Video name used in filenames",
  "source": "<video path given to prepare>",
  "rights_confirmed": true,
  "videos": [
    {
      "headline": "一時間で一万円使い切れ",
      "teaser": 3,
      "body": [[2, 13]],
      "emphasis": ["一万円", "百円"],
      "reason": "An outrageous rule opens the clip; a reaction provides the payoff.",
      "scores": {"hook": 8, "standalone": 8, "payoff": 6, "emotion": 6},
      "titles": ["Title suggestion 1", "Title suggestion 2", "Title suggestion 3"],
      "description": "Description",
      "hashtags": ["#tag"]
    }
  ],
  "tsukkomi": [{"video": 0, "after": 31, "text": "≤14 characters", "approved": true}]
}
```

Japanese sample heading/emphasis intentionally match the Japanese transcript example: “Spend ten thousand yen in one hour,” “ten thousand yen,” “one hundred yen.”

- Series: `"mode":"series"`; each video includes `"chapters":[[0,"Chapter"],[30,"Chapter"],[62,"Chapter"]]`.
- `tsukkomi` only P3, approved entries only.

```bash
python vc_tool.py build plan.json --work vc_work --out vc_out
```

- Exit2: invalid plan, fix according to error and rerun: duration/overlap/heading>16characters/unapproved reaction.
- Exit3: exported but hard checks fail. Read `summary.json` and `*.editplan.json` `qa.results`; fix plan-resolvable H-01duration/H-05overlongtext/H-07spelling and rerun. Explain unfixable issues.
- Stop/report after three failures for same finding. Long videos may export individually using `--only 1`.

#### Step 7 — Inspect yourself

Passing checks means measurable criteria passed, not good content. Before delivery:

- Inspect each `*_sheet.png`: captions lower-center/unclipped, heading at top, no black frames or missing-glyph squares.
- Read each SRT end-to-end: cold open→body makes sense, opening not abrupt, ends with payoff.
- Correct plan/rerun if needed.

#### Step 8 — Deliver/report

1. Deliver every `vc_out` file as outputs, also saved in OneDrive Cowork folder.
2. Use this format, avoid jargon, explain warnings gently,§5:

   ```markdown
   ## Finished videos (pattern: P4 Clip / highlights)
   | # | File | Length | Heading | Selection reason | Automatic checks |
   |---|---|---|---|---|---|
   | 1 | …_P4_01_….mp4 | 33s | Spend ten thousand yen in one hour | … | Pass (note: 11 fast-speech captions) |

   ## Unavailable results / cautions
   - …

   ## Next options
   - Choose other scenes / make a series / use another pattern
   ```

3. Add posting note: enabling SRT duplicates burned-in captions; >3minutes is not YouTube Shorts. No realistic AI-generated footage was added, but reaction wording is AI-proposed.

### 3. Missing environment capabilities

| Situation | Response |
|---|---|
| No Python/FFmpeg, env `ok:false` | First say video files cannot be made here. With transcript perform Steps3–5selection; deliver Word edit instructions with per-candidate times/coldopen/heading/reason, candidate SRTs, `plan.json`/`vc_tool.py`. Explain that a PC with Python/FFmpeg, e.g.VideoCreator environment, can run prepare/build to produce same videos. |
| No transcript/faster-whisper | Request VTT; stop if unavailable. Never guess scenes without listening. |
| No Japanese font | Ask once per Step1; otherwise no burned captions/headings, deliver SRT. |
| Video≥200MB cannot attach | Suggest opening in Clipchamp, exporting720p, optionally keeping only desired section. |
| P5 selected | Explain and suggest P8/P9. |

### 4. Pattern selection rules: same values as Appendix A PRESETS

| ID | Suitable | Selection rule | Duration | Cold open | Cowork differences |
|---|---|---|---|---|---|
| P1 | Challenges/results/reactions | Show result/prize/surprise within1.5seconds; challenge→result arc |25–45s|Yes|No SFX/counter/BGM|
| P2 | Brief question/payoff | Self-contained question/surprise and answer; final sentence answers opening, invites repeat |15–25s|No|AI judges looping continuity|
| P3 | Humor/banter | Laughter/reaction, space for editorial quip |25–60s|Yes|Approved overlay only, no pause cards/SFX|
| P4 | Long speech, default | Standalone; result/surprise/strong claim/question at start, payoff at end |25–35s|Yes|Same as app|
| P5 | — | — |—|—|Unavailable|
| P6 | Explanation/lecture | Complete answer with reasoning/examples to one question/claim; exact transcript numbers/names |30–60s|No|No BGM|
| P7 |≥2speakers | Strongest statement cold open, complete exchange |30–60s|Yes|One video, no camera switching; speaker colors instead|
| P8 | Cooking/DIY process | Speed up long work; finished result first |20–60s|Yes|AI specifies accelerated intervals from images|
| P9 | Work/ambient | Continuous core work/near-completion scene, immersive visuals/natural audio without text/music |30–90s|No|No silence removal, like app|
| P10 | PC screen | Complete purpose→operation→result, favor explanatory speech |30–60s|No|No automatic screen zoom/presenter inset|

Shared app `presets/*.json` values:1080×1920,H.264,AAC48kHz;integrated−14LUFS/TruePeak−1.5dBTP,two passes;16:9over blurred background;single-line captions inside620px from bottom/160right/60left;P1/P3tencharacters,others14,shrink if overflow;headings300px from top.

### 5. Automatic-check meanings and plain-language reporting

| ID | Type | Meaning | User wording |
|---|---|---|---|
| H-01 | Required | Pattern duration,short≤180s | Length outside limits |
| H-02 | Required |1080×1920|Frame size|
| H-03/H-04 | Required |−14±1LUFS/TruePeak≤−1.0dBTP,independent meter|Volume|
| H-05 | Required |Captions not hidden by posting-app buttons|Caption placement|
| H-06 | Required |No midword cuts|Cuts during words|
| H-07 | Required |Heading/emphasis numbers/names in transcript|Heading spelling|
| H-08 | Required |No unapproved reactions|Reaction approval|
| H-09 | Required |Source rights confirmed|Rights confirmation|
| V-01 | Required |Verbatim captions|Caption accuracy|
| S-01 | Caution |≤6characters/s;fast speech may exceed,never delete|Some captions may be too fast to read|
| S-02/S-08 | Caution |Line length/Japanese start-end restrictions|Caption line breaks|
| S-03 | Caution |Speech within2seconds|Opening is slow|
| S-04 | Caution |No excessively short cuts|Some cuts are too quick|
| S-06 | Caution |No black/frozen≥0.5s,including source stills|Some scenes appear frozen|
| S-07 | Caution |First sentence not “that,”etc.|Opening may feel abrupt|
| S-09 | Caution |Heading preserves negation|Check heading has not reversed meaning|
| S-11/S-14 | Caution |Series≤60min;YouTube chapters00:00/≥3/≥10s|Chapters|

---

## Appendix A — vc_tool.py

Runs with standard library and FFmpeg only,Python≥3.8assumed,verified3.12. Finds FFmpeg through PATH,`VC_FFMPEG`,or imageio-ffmpeg;ffprobe optional.

**Executable integrity:** the complete following executable is preserved verbatim from the original, including Japanese preset names, language-processing rules, captions, and diagnostic strings. Translating these changes behavior and the required hash. This English document translates the surrounding instructions; the bundled tool still edits Japanese footage as documented, not a newly localized executable.

<!-- vc_tool.py:begin -->
```python
#!/usr/bin/env python3
# vc_tool.py - VideoCreator (Cowork edition). Deterministic part: probe, transcript, signals, split, build (render+QA).
# The LLM chooses sentences/headlines; this tool cuts, captions, normalizes loudness and measures. Stdlib + ffmpeg only.
import argparse, datetime, glob, importlib.util, json, math, os, re, shutil, struct, subprocess, sys, unicodedata

VERSION = "1.0.0"
W, H = 1080, 1920
SAFE = {"top": 250, "bottom": 580, "right": 160, "left": 60}
GLYPH = 0.75
CAP_TOP = {"teaser": None, "recap": "前回のおさらい", "next": None}

PRESETS = {
 "P1": dict(name="ハイテンション・チャレンジ", dur=(25, 45), cold="teaser", punch=1.2, min_shot=0.8, sil=0.4, telop=["caption", "emphasis", "headline"], look="variety", font=124, chars=10, mv=620, mr=160, series=True,
            rules="結果・賞金・驚きの瞬間を1.5秒以内に見せられる、挑戦→結果のある区間を選ぶ。"),
 "P2": dict(name="一発ネタ・ループ", dur=(15, 25), cold="none", punch=1.1, min_shot=1.0, sil=0.5, telop=["caption", "emphasis"], look="", font=80, chars=14, mv=620, mr=160, series=True, loop=True,
            rules="1つの問い（または驚き）とその答え（オチ）だけで完結する15〜25秒。最後の文が冒頭の問いに答え、繰り返し見たくなるものを優先。"),
 "P3": dict(name="バラエティ・テロップ", dur=(25, 60), cold="teaser", punch=1.15, min_shot=1.0, sil=0.5, telop=["caption", "emphasis", "headline", "tsukkomi"], look="variety", font=124, chars=10, mv=620, mr=160, series=True,
            rules="笑い・リアクションの瞬間を含み、ツッコミで間を作れる区間を選ぶ。"),
 "P4": dict(name="切り抜き（長尺→30秒）", dur=(25, 35), cold="teaser", punch=1.15, min_shot=1.0, sil=0.7, telop=["caption", "emphasis", "headline"], look="", font=78, chars=14, mv=620, mr=160, series=True,
            rules="単体で意味が通り、冒頭に結果・驚き・強い主張・問いがあり、最後にオチ（ペイオフ）がある25〜35秒。"),
 "P5": dict(name="ビート同期モンタージュ", supported=False),
 "P6": dict(name="解説・講義（要点テロップ）", dur=(30, 60), cold="none", punch=1.1, min_shot=1.5, sil=0.6, telop=["caption", "emphasis", "headline"], look="", font=76, chars=14, mv=620, mr=160, series=True,
            rules="1つの問いや主張に対して、理由や具体例を含む答えが完結している30〜60秒。数字・固有名詞は書き起こしのとおりに扱う。"),
 "P7": dict(name="対談スイッチング（1カメラ簡略版）", dur=(30, 60), cold="teaser", punch=1.0, min_shot=2.0, sil=1.2, telop=["caption", "emphasis", "headline"], look="", font=76, chars=14, mv=620, mr=160, series=True, speaker_colors=True,
            rules="最も強い発言を先出しでき、話者の掛け合いが完結している30〜60秒。"),
 "P8": dict(name="プロセス圧縮（料理/DIY）", dur=(20, 60), cold="teaser", punch=1.1, min_shot=1.0, sil=0.5, telop=["emphasis", "headline"], look="", font=78, chars=14, mv=620, mr=160, series=False, speed=True,
            rules="長い工程を早送りで圧縮し、完成の場面を先に見せる。"),
 "P9": dict(name="ミニマル/環境音", dur=(30, 90), cold="none", punch=1.0, min_shot=3.0, sil=None, telop=[], look="", font=78, chars=14, mv=620, mr=160, series=True, nohook=True,
            rules="テロップや音楽に頼らず、映像と環境音だけで没入できる、作業の核心や完成に近い連続した場面を選ぶ。"),
 "P10": dict(name="画面解説（1ファイル簡略版）", dur=(30, 60), cold="none", punch=1.0, min_shot=2.0, sil=0.8, telop=["caption", "emphasis", "headline"], look="", font=76, chars=14, mv=620, mr=160, series=True,
             rules="1つの操作や手順が、目的→操作→結果まで完結している30〜60秒。画面の操作を説明している発話を優先する。"),
}
PART_LABELS = {2: ["前編", "後編"], 3: ["前編", "中編", "後編"]}
NO_START = set("、。，．・：；？！ー」』）】〕〉》’”ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ々〜…‥),.!?]}%")
NO_END = set("「『（【〔〈《‘“([{")
PARTICLES = set("はがをにでともへやねよかな")
DEMONSTRATIVE = ("それ", "あれ", "これ", "その", "あの", "この", "そこ", "あそこ", "ここ", "そう")
CONTINUATION = ("でも", "だから", "それで", "そして", "で、", "つまり", "なので", "ただ", "けど", "しかも", "あと")
NEGATION = re.compile(r"(ない|なかった|ません|ず|ぬ|じゃない|ではない|無い|不可|ダメ)")
FONT_PREF = ["M PLUS 1p ExtraBold", "M PLUS 1p", "Noto Sans CJK JP", "Noto Sans JP", "Source Han Sans", "IPAexGothic", "IPAGothic", "BIZ UDPGothic",
             "Yu Gothic", "Meiryo", "Hiragino", "TakaoGothic", "VL Gothic", "Noto Sans CJK", "WenQuanYi Zen Hei", "Droid Sans Fallback", "MS Gothic"]


def die(msg, code=2):
    print("ERROR: " + msg, file=sys.stderr)
    sys.exit(code)


def jdump(o, path):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(o, f, ensure_ascii=False, indent=1)
    os.replace(tmp, path)


def jload(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


# ---------------- ffmpeg discovery ----------------
def find_ffmpeg():
    exe = os.environ.get("VC_FFMPEG") or shutil.which("ffmpeg")
    if not exe:
        try:
            import imageio_ffmpeg  # type: ignore
            exe = imageio_ffmpeg.get_ffmpeg_exe()
        except Exception:
            exe = None
    probe = None
    if exe:
        cand = os.path.join(os.path.dirname(exe), "ffprobe" + (".exe" if exe.lower().endswith(".exe") else ""))
        probe = cand if os.path.exists(cand) else shutil.which("ffprobe")
    return exe, probe


FF, FP = find_ffmpeg()


def run(args, check=True):
    p = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    out, err = p.stdout.decode("utf-8", "replace"), p.stderr.decode("utf-8", "replace")
    if check and p.returncode != 0:
        die("command failed (%d): %s\n%s" % (p.returncode, " ".join(args[:12]), err[-3000:]))
    return out, err


def ff(args, check=True):
    if not FF:
        die("ffmpeg not found (PATH, VC_FFMPEG, or python package imageio-ffmpeg)")
    return run([FF, "-hide_banner", "-nostats"] + args, check)


# ---------------- fonts ----------------
def font_names(path):
    """Family names from a TTF/OTF/TTC name table (nameID 1 and 4)."""
    names = set()
    try:
        with open(path, "rb") as f:
            data = f.read()
        offs = [0]
        if data[:4] == b"ttcf":
            n = struct.unpack(">I", data[8:12])[0]
            offs = list(struct.unpack(">%dI" % n, data[12:12 + 4 * n]))
        for base in offs:
            num = struct.unpack(">H", data[base + 4:base + 6])[0]
            for i in range(num):
                rec = base + 12 + 16 * i
                if data[rec:rec + 4] != b"name":
                    continue
                off = struct.unpack(">I", data[rec + 8:rec + 12])[0]
                cnt, sto = struct.unpack(">HH", data[off + 2:off + 6])
                for k in range(cnt):
                    pid, eid, lid, nid, ln, o = struct.unpack(">HHHHHH", data[off + 6 + 12 * k:off + 18 + 12 * k])
                    if nid not in (1, 4):
                        continue
                    raw = data[off + sto + o:off + sto + o + ln]
                    try:
                        s = raw.decode("utf-16-be") if pid in (0, 3) else raw.decode("latin-1")
                    except Exception:
                        continue
                    if s.strip():
                        names.add(s.strip())
    except Exception:
        pass
    return names


def find_font(explicit=None):
    files = []
    if explicit:
        files.append(explicit)
    for d in [".", "fonts", os.path.expanduser("~/.fonts"), os.path.expanduser("~/.local/share/fonts"), "/usr/share/fonts", "/usr/local/share/fonts",
              "/System/Library/Fonts", "/Library/Fonts", os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts"), "/mnt/data", "/mnt/user-data"]:
        for ext in ("ttf", "otf", "ttc", "TTF", "OTF", "TTC"):
            files += glob.glob(os.path.join(d, "**", "*." + ext), recursive=True)
    best = None
    for f in files:
        ns = font_names(f)
        for rank, pref in enumerate(FONT_PREF):
            hit = [n for n in ns if n.lower().startswith(pref.lower())]
            if hit and (best is None or rank < best[0]):
                fam = sorted(hit, key=len)[0]
                best = (rank, f, fam)
        if explicit and f == explicit and ns and best is None:
            best = (99, f, sorted(ns, key=len)[0])
    return (best[1], best[2]) if best else (None, None)


# ---------------- probe ----------------
def probe(path):
    if not os.path.exists(path):
        die("input not found: " + path)
    info = {"path": path, "size_mb": round(os.path.getsize(path) / 1e6, 1)}
    if FP:
        out, _ = run([FP, "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path])
        j = json.loads(out)
        v = next((s for s in j["streams"] if s.get("codec_type") == "video"), None)
        a = next((s for s in j["streams"] if s.get("codec_type") == "audio"), None)
        info["duration"] = float(j["format"].get("duration", 0))
        if v:
            rot = 0
            for sd in v.get("side_data_list", []) or []:
                if "rotation" in sd:
                    rot = int(float(sd["rotation"]))
            rot = rot or int(float((v.get("tags") or {}).get("rotate", 0)))
            w, h = int(v["width"]), int(v["height"])
            if abs(rot) % 180 == 90:
                w, h = h, w
            num, den = (v.get("avg_frame_rate") or v.get("r_frame_rate") or "30/1").split("/")
            fps = float(num) / float(den) if float(den) else 30.0
            info.update(width=w, height=h, fps=round(fps, 3), vcodec=v.get("codec_name"), rotation=rot)
        info["audio"] = bool(a)
        if a:
            info.update(sample_rate=int(a.get("sample_rate", 0)), channels=int(a.get("channels", 0)))
    else:
        _, err = run([FF, "-hide_banner", "-i", path], check=False)
        m = re.search(r"Duration: (\d+):(\d+):([\d.]+)", err)
        info["duration"] = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)) if m else 0.0
        mv = re.search(r"Video: (\w+).*?(\d{2,5})x(\d{2,5})", err)
        mf = re.search(r"([\d.]+) fps", err)
        mr = re.search(r"rotation of (-?[\d.]+)", err) or re.search(r"rotate\s*:\s*(-?\d+)", err)
        if mv:
            w, h = int(mv.group(2)), int(mv.group(3))
            rot = int(float(mr.group(1))) if mr else 0
            if abs(rot) % 180 == 90:
                w, h = h, w
            info.update(width=w, height=h, fps=float(mf.group(1)) if mf else 30.0, vcodec=mv.group(1), rotation=rot)
        info["audio"] = "Audio:" in err
    probs = []
    if "width" not in info:
        probs.append("映像ストリームがありません")
    if info["duration"] < 10:
        probs.append("10秒未満の動画は扱えません")
    if info["duration"] > 3 * 3600:
        probs.append("3時間を超えています（分割を推奨）")
    if not info.get("audio"):
        probs.append("音声がありません（P9以外は字幕・選定ができません）")
    info["problems"] = probs
    return info


# ---------------- transcript ----------------
def ts(s):
    p = s.strip().replace(",", ".").split(":")
    p = [float(x) for x in p]
    return p[0] * 3600 + p[1] * 60 + p[2] if len(p) == 3 else p[0] * 60 + p[1]


def parse_transcript(path):
    raw = open(path, encoding="utf-8-sig", errors="replace").read()
    cues = []
    if path.lower().endswith(".json"):
        j = json.loads(raw)
        items = j.get("segments", j) if isinstance(j, dict) else j
        for it in items:
            cues.append(dict(start=float(it["start"]), end=float(it["end"]), speaker=str(it.get("speaker", "") or ""), text=str(it["text"]).strip()))
        return cues
    blocks = re.split(r"\n\s*\n", raw.replace("\r", ""))
    for b in blocks:
        lines = [l for l in b.split("\n") if l.strip()]
        k = next((i for i, l in enumerate(lines) if "-->" in l), None)
        if k is None:
            continue
        a, e = lines[k].split("-->")
        text_lines = lines[k + 1:]
        spk = ""
        txt = " ".join(text_lines)
        m = re.match(r"\s*<v\s+([^>]+)>", txt)
        if m:
            spk = m.group(1).strip()
        txt = re.sub(r"<[^>]+>", "", txt).strip()
        m2 = re.match(r"^([^:：]{1,30})[:：]\s*(.+)$", txt)
        if not spk and m2 and len(text_lines) == 1 and not re.search(r"[。、！？]", m2.group(1)):
            spk, txt = m2.group(1).strip(), m2.group(2).strip()
        if txt:
            cues.append(dict(start=ts(a.split()[0]), end=ts(e.split()[0]), speaker=spk, text=txt))
    return cues


def asr(video):
    try:
        from faster_whisper import WhisperModel  # type: ignore
    except Exception:
        return None
    try:
        model = WhisperModel(os.environ.get("VC_WHISPER", "small"), device="cpu", compute_type="int8")
        segs, _ = model.transcribe(video, language="ja", vad_filter=True)
        return [dict(start=s.start, end=s.end, speaker="", text=s.text.strip()) for s in segs if s.text.strip()]
    except Exception as ex:
        print("ASR failed: %s" % ex, file=sys.stderr)
        return None


def to_sentences(cues):
    out = []
    for c in sorted(cues, key=lambda c: c["start"]):
        parts = [p for p in re.findall(r"[^。！？!?]+[。！？!?」』]*|[。！？!?]+", c["text"]) if p.strip()]
        total = sum(len(p) for p in parts) or 1
        t = c["start"]
        for p in parts:
            d = (c["end"] - c["start"]) * len(p) / total
            out.append(dict(start=round(t, 3), end=round(t + d, 3), speaker=c["speaker"], text=p.strip()))
            t += d
    merged = []
    for s in out:  # join cue fragments that do not end a sentence and continue without a pause, same speaker
        if merged and not re.search(r"[。！？!?」』]$", merged[-1]["text"]) and s["speaker"] == merged[-1]["speaker"] \
                and s["start"] - merged[-1]["end"] < 0.25 and len(merged[-1]["text"]) + len(s["text"]) <= 60:
            merged[-1]["text"] += s["text"]
            merged[-1]["end"] = s["end"]
        else:
            merged.append(dict(s))
    for i, s in enumerate(merged):
        s["i"] = i
    return merged


def mmss(t):
    return "%02d:%04.1f" % (int(t // 60), t % 60) if t < 3600 else "%d:%02d:%04.1f" % (int(t // 3600), int(t % 3600 // 60), t % 60)


# ---------------- signals ----------------
def detect_silence(video, dur):
    for db in (-40, -35, -30):
        _, err = ff(["-i", video, "-vn", "-af", "silencedetect=noise=%ddB:d=0.3" % db, "-f", "null", "-"])
        st = [float(x) for x in re.findall(r"silence_start: (-?[\d.]+)", err)]
        en = [float(x) for x in re.findall(r"silence_end: ([\d.]+)", err)]
        sil = [(max(0.0, a), b) for a, b in zip(st, en)]
        if len(st) > len(en):
            sil.append((st[-1], dur))
        if sum(b - a for a, b in sil) >= 0.03 * dur or db == -30:
            return sil, db


def signals(video, sents_path=None):
    info = probe(video)
    dur = info["duration"]
    sil, db = detect_silence(video, dur) if info.get("audio") else ([], None)
    _, err = ff(["-i", video, "-an", "-vf", "scale=320:-2,scdet=threshold=10:sc_pass=0,blackdetect=d=0.2:pix_th=0.10", "-f", "null", "-"])
    shots = sorted(set(round(float(x), 2) for x in re.findall(r"lavfi\.scd\.time: ([\d.]+)", err)))
    black = [(float(a), float(b)) for a, b in re.findall(r"black_start:([\d.]+) black_end:([\d.]+)", err)]
    loud = []
    if info.get("audio"):
        _, err = ff(["-i", video, "-vn", "-af", "ebur128", "-f", "null", "-"])
        for m in re.finditer(r"t:\s*([\d.]+)\s+TARGET:[^M]*M:\s*(-?[\d.]+)", err):
            loud.append((float(m.group(1)), float(m.group(2))))
    sec = {}
    for t, m in loud:
        k = int(t)
        sec[k] = max(sec.get(k, -120.0), m)
    peaks = []
    for k in sorted(sec, key=lambda k: -sec[k]):
        if sec[k] < -60:
            break
        if all(abs(k - p[0]) >= 10 for p in peaks):
            peaks.append((k, round(sec[k], 1)))
        if len(peaks) >= 15:
            break
    sig = dict(duration=dur, silence_db=db, silences=[[round(a, 3), round(b, 3)] for a, b in sil], shots=shots, black=black,
               loud_peaks=[dict(t=t, M=m) for t, m in sorted(peaks)], loud_per_sec={str(k): round(v, 1) for k, v in sec.items()})
    return sig


# ---------------- text helpers ----------------
def cw(ch):
    return 1.0 if unicodedata.east_asian_width(ch) in "WFA" else 0.5


def width(s):
    return sum(cw(c) for c in s if not c.isspace())


def chunk_sentence(text, maxw):
    phrases = [p for p in re.findall(r"[^、。！？!?…]+[、。！？!?…」』）]*|[、。！？!?…」』）]+", text) if p]
    chunks, cur = [], ""
    for p in phrases:
        if width(cur + p) <= maxw:
            cur += p
            continue
        if cur:
            chunks.append(cur)
            cur = ""
        while width(p) > maxw:
            cut = split_point(p, maxw)
            chunks.append(p[:cut])
            p = p[cut:]
        cur = p
    if cur:
        chunks.append(cur)
    out = []
    for c in chunks:  # punctuation-only pieces go back to the previous chunk
        if out and all(ch in NO_START or ch in "、。！？!?…" for ch in c.strip()):
            out[-1] += c
        else:
            out.append(c)
    return out


def split_point(p, maxw):
    acc, lim = 0.0, 0
    for i, ch in enumerate(p):
        acc += cw(ch)
        if acc > maxw:
            break
        lim = i + 1
    lim = max(1, lim)
    best = None
    for k in range(lim, max(1, int(lim * 0.5)) - 1, -1):
        if k >= len(p):
            continue
        a, b = p[k - 1], p[k]
        if b in NO_START or a in NO_END:
            continue
        if re.match(r"[A-Za-z0-9.,%]", a) and re.match(r"[A-Za-z0-9.,%]", b):
            continue
        if a in PARTICLES or (a in "てた" and not re.match(r"[ぁ-ん]", b)):
            return k
        if best is None:
            best = k
    return best or lim


def ass_esc(s):
    return s.replace("\\", "＼").replace("{", "｛").replace("}", "｝").replace("\n", " ")


def ass_time(t):
    t = max(0.0, t)
    cs = int(round(t * 100))
    return "%d:%02d:%02d.%02d" % (cs // 360000, cs // 6000 % 60, cs // 100 % 60, cs % 100)


def srt_time(t):
    ms = int(round(max(0.0, t) * 1000))
    return "%02d:%02d:%02d,%03d" % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, ms % 1000)


def fit_px(text, px, outline, scalex=100, extra=0.0):
    avail = W - SAFE["left"] - SAFE["right"] - 2 * outline
    est = (width(text) + extra) * px * GLYPH * scalex / 100.0
    return px if est <= avail else max(24, int(px * avail / est))


def facts(s):
    """Numbers, ASCII words and katakana runs: the tokens H-07 requires to exist in the transcript."""
    s = unicodedata.normalize("NFKC", s)
    return [t for t in re.findall(r"\d[\d,.]*|[A-Za-z][A-Za-z0-9]+|[ァ-ヴー]{2,}", s) if t != "ー"]


def norm(s):
    return re.sub(r"\s+", "", unicodedata.normalize("NFKC", s))


# ---------------- series split ----------------
def edited_len(sents, sil_thr, sils=()):
    """Estimated length of each sentence (plus the following gap) after the jump cuts that build() will make."""
    keep = sil_thr if sil_thr else 9e9
    d = []
    for i, s in enumerate(sents):
        gap = (sents[i + 1]["start"] - s["end"]) if i + 1 < len(sents) else 0.0
        v = max(0.0, s["end"] - s["start"]) + min(max(0.0, gap), keep)
        if sil_thr:
            a0, a1 = s["start"], s["end"] + max(0.0, gap)
            for x, y in sils:
                o = min(a1, y) - max(a0, x)
                if y - x > sil_thr and o > 0.24:
                    v -= o - 0.24
        d.append(max(0.2, v))
    return d


def split_series(sents, parts, prefer, sil_thr, sils=()):
    n = len(sents)
    d = edited_len(sents, sil_thr, sils)
    pre = [0.0]
    for x in d:
        pre.append(pre[-1] + x)
    total = pre[-1]
    mean = total / parts
    if mean < 60:
        mx = int(total // 60)
        die("1本あたり約%.0f秒になり、シリーズの下限（編集後1分）に届きません。%s" % (mean, "本数を%d本以下にしてください。" % mx if mx >= 2 else "この動画はシリーズには短いので、見どころ（最大3本）を作ってください。"))
    bonus = []
    for i in range(n - 1):
        a, b = sents[i], sents[i + 1]
        g = b["start"] - a["end"]
        v = 0.0
        v += 2.5 if i in prefer else 0.0
        v += 0.4 if g >= 0.7 else (0.2 if g >= 0.4 else 0.0)
        v += 0.1 if a["speaker"] != b["speaker"] else 0.0
        v -= 0.8 if not re.search(r"[。！？!?」』]$", a["text"]) else 0.0
        v -= 0.6 if b["text"].startswith(CONTINUATION) or b["text"].startswith(DEMONSTRATIVE) else 0.0
        bonus.append(v)

    def cost(i, j):  # part = sentences i..j-1
        L = pre[j] - pre[i]
        return 25.0 * ((L - mean) / mean) ** 2

    found = []
    for tol in (0.25, 0.40):
        lo, hi = mean * (1 - tol), mean * (1 + tol)
        INF = float("inf")
        best = [[INF] * (n + 1) for _ in range(parts + 1)]
        back = [[-1] * (n + 1) for _ in range(parts + 1)]
        best[0][0] = 0.0
        for k in range(1, parts + 1):
            for j in range(1, n + 1):
                if k == parts and j != n:
                    continue
                for i in range(j - 1, -1, -1):
                    L = pre[j] - pre[i]
                    if L > hi:
                        break
                    if L < lo or best[k - 1][i] == INF:
                        continue
                    c = best[k - 1][i] + cost(i, j) - (bonus[j - 1] if j < n else 0.0)
                    if c < best[k][j]:
                        best[k][j], back[k][j] = c, i
        if best[parts][n] < INF:
            cuts, j = [], n
            for k in range(parts, 0, -1):
                i = back[k][j]
                cuts.append((i, j - 1))
                j = i
            cuts.reverse()
            aligned = sum(1 for (_, b) in cuts[:-1] if b in prefer)
            found.append((aligned, tol, cuts))
            if not prefer or aligned == parts - 1:
                break
    if not found:
        die("均等に近い分け方が見つかりません。本数を変えてください。")
    # a topic-aligned split within ±40 % beats a balanced split that cuts a topic in half
    aligned, tol, cuts = sorted(found, key=lambda x: (-x[0], x[1]))[0]
    labels = PART_LABELS.get(parts, ["第%d話" % (k + 1) for k in range(parts)])
    res = []
    for k, (a, b) in enumerate(cuts):
        res.append(dict(part=k + 1, label=labels[k], body=[a, b], start=sents[a]["start"], end=sents[b]["end"], est_sec=round(pre[b + 1] - pre[a], 1),
                        boundary_after=(None if b == n - 1 else dict(after=b, prefer=(b in prefer), bonus=round(bonus[b], 2),
                                                                      last=sents[b]["text"], next=sents[b + 1]["text"],
                                                                      warning=(None if (b in prefer or not prefer) else "話題の切れ目ではありません（%s）。利用者に伝えてください。" % mmss(sents[b]["end"]))))))
    return dict(parts=res, tolerance=tol, mean_sec=round(mean, 1), note=("長さの差を±40%%まで広げて話題の切れ目に合わせました" if tol > 0.25 else ""))


# ---------------- plan -> clips ----------------
def sentence_span(sents, a, b, dur):
    s0, s1 = sents[a], sents[b]
    gb = s0["start"] - (sents[a - 1]["end"] if a > 0 else 0.0)
    ga = (sents[b + 1]["start"] if b + 1 < len(sents) else dur) - s1["end"]
    return max(0.0, s0["start"] - min(0.15, max(0.0, gb) / 2)), min(dur, s1["end"] + min(0.30, max(0.0, ga) / 2))


def jump_cut(t0, t1, sils, thr, min_shot):
    if not thr:
        return [(t0, t1)]
    cuts = []
    for a, b in sils:
        a, b = max(a, t0), min(b, t1)
        if b - a > thr:
            cuts.append((a + 0.12, b - 0.12))
    pieces, cur = [], t0
    for a, b in cuts:
        if a - cur >= min_shot and t1 - b >= min_shot:
            pieces.append((cur, a))
            cur = b
    pieces.append((cur, t1))
    return pieces


def build_groups(plan, vid, sents, sig, P, series_ctx):
    dur = sig["duration"]
    groups = []  # each: role, sentences [indices], clips [(t0,t1,speed)]
    sils = [tuple(x) for x in sig["silences"]]

    def speech_group(role, a, b, cut=True):
        t0, t1 = sentence_span(sents, a, b, dur)
        pcs = jump_cut(t0, t1, sils, P["sil"] if cut else None, P["min_shot"])
        groups.append(dict(role=role, sents=list(range(a, b + 1)), clips=[(x, y, 1.0) for x, y in pcs]))

    teaser = vid.get("teaser")
    if teaser is not None and (P["cold"] == "teaser" or series_ctx):
        speech_group("teaser", int(teaser), int(teaser), cut=True)
    if series_ctx and series_ctx.get("recap"):
        a, b = series_ctx["recap"]
        speech_group("recap", a, b, cut=True)
    for item in vid["body"]:
        if isinstance(item, dict):
            t0, t1, sp = float(item["t0"]), float(item["t1"]), float(item.get("speed", 1.0))
            if not P.get("speed") and sp != 1.0:
                die("speed is only allowed in P8")
            inside = [s["i"] for s in sents if s["start"] >= t0 - 0.05 and s["end"] <= t1 + 0.05] if sp == 1.0 else []
            groups.append(dict(role="body", sents=inside, clips=[(max(0.0, t0), min(dur, t1), sp)]))
        else:
            a, b = int(item[0]), int(item[1])
            if not (0 <= a <= b < len(sents)):
                die("body range %s is outside 0..%d" % (item, len(sents) - 1))
            speech_group("body", a, b)
    if series_ctx and series_ctx.get("next") is not None:
        speech_group("next", series_ctx["next"], series_ctx["next"], cut=True)
    body_spans = sorted((c[0], c[1]) for g in groups if g["role"] == "body" for c in g["clips"])
    for (a0, b0), (a1, b1) in zip(body_spans, body_spans[1:]):
        if a1 < b0 - 0.2:
            die("本編の区間が重なっています（%s〜%s と %s〜%s）。同じ場面が2回映らないよう body を直してください。" % (mmss(a0), mmss(b0), mmss(a1), mmss(b1)))
    # punch-in alternates on body pieces; teaser is punched in
    k = 0
    for g in groups:
        zs = []
        for c in g["clips"]:
            if g["role"] == "teaser":
                zs.append(P["punch"])
            elif g["role"] == "body":
                zs.append(P["punch"] if k % 2 == 1 else 1.0)
                k += 1
            else:
                zs.append(1.0)
        g["zoom"] = zs
    return groups


def timeline(groups, fps):
    o = 0.0
    for g in groups:
        g["out"] = []
        for (a, b, sp) in g["clips"]:
            n = max(1, int(round((b - a) / sp * fps)))
            g["out"].append((o, o + n / fps, n))
            o += n / fps
    return o


def map_t(g, t):
    for (a, b, sp), (o0, o1, _) in zip(g["clips"], g["out"]):
        if t < a:
            return o0
        if t <= b:
            return min(o1, o0 + (t - a) / sp)
    return g["out"][-1][1]


def captions(groups, sents, P, emphasis, speakers):
    maxw = P["chars"]
    scalex, outline = (85, 10) if P["look"] == "variety" else (100, 7)
    avail = W - SAFE["left"] - SAFE["right"] - 2 * outline
    fitc = int(avail // (P["font"] * GLYPH * scalex / 100.0))
    maxw = max(1, min(maxw, fitc))
    ev = []
    for g in groups:
        if g["clips"][0][2] != 1.0:
            continue
        for i in g["sents"]:
            s = sents[i]
            chunks = chunk_sentence(s["text"], maxw)
            tot = sum(width(c) for c in chunks) or 1
            # distribute chunks over the sentence's OUTPUT interval so a jump cut can never swallow a chunk (captions stay verbatim)
            o0, o1 = map_t(g, s["start"]), map_t(g, s["end"])
            if o1 - o0 < 0.1 * len(chunks):
                o1 = min(g["out"][-1][1], o0 + 0.1 * len(chunks))
            t = o0
            for c in chunks:
                d = (o1 - o0) * width(c) / tot
                ev.append(dict(start=t, end=t + d, text=c, sent=i, role=g["role"], speaker=s["speaker"], gend=g["out"][-1][1]))
                t += d
    ev.sort(key=lambda e: e["start"])
    for k, e in enumerate(ev):  # stretch toward 6 cps (max +0.5s), never over the next caption or out of its group
        nxt = ev[k + 1]["start"] if k + 1 < len(ev) else e["gend"]
        need = width(e["text"]) / 6.0
        if e["end"] - e["start"] < need:
            e["end"] = min(e["start"] + need, e["end"] + 0.5, nxt, e["gend"])
        if k + 1 < len(ev) and e["end"] > ev[k + 1]["start"]:
            e["end"] = ev[k + 1]["start"]
    return ev, maxw, scalex, outline


def write_ass(path, ev, P, headline, labels, tsukkomi, font_family, total, scalex, outline, speakers, emphasis):
    L = ["[Script Info]", "ScriptType: v4.00+", "PlayResX: %d" % W, "PlayResY: %d" % H, "WrapStyle: 2", "ScaledBorderAndShadow: yes", "",
         "[V4+ Styles]",
         "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding"]
    f = font_family
    L.append("Style: Caption,%s,%d,&H00FFFFFF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,%d,100,0,0,1,%d,2,2,%d,%d,%d,1" % (f, P["font"], scalex, outline, SAFE["left"], P["mr"], P["mv"]))
    for k, col in enumerate(["&H00D47800", "&H003C3CD4", "&H0050A030"]):
        L.append("Style: CapS%d,%s,%d,&H00FFFFFF,&H0000FFFF,%s,&H80000000,0,0,0,0,%d,100,0,0,1,%d,2,2,%d,%d,%d,1" % (k + 1, f, P["font"], col, scalex, max(outline, 9), SAFE["left"], P["mr"], P["mv"]))
    L.append("Style: Headline,%s,60,&H00FFFFFF,&H0000FFFF,&H00202020,&HB0202020,0,0,0,0,100,100,0,0,3,14,0,8,%d,%d,300,1" % (f, SAFE["left"], P["mr"]))
    L.append("Style: Label,%s,48,&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,6,2,7,%d,%d,410,1" % (f, SAFE["left"] + 20, P["mr"]))
    L.append("Style: Tsukkomi,%s,64,&H00202020,&H0000FFFF,&H00FFFFFF,&H00FFFFFF,0,0,0,0,100,100,0,0,3,12,0,2,%d,%d,930,1" % (f, SAFE["left"], P["mr"]))
    L += ["", "[Events]", "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text"]
    layout = []
    spk_style = {}
    for e in ev:
        style = "Caption"
        if P.get("speaker_colors") and e["speaker"]:
            spk_style.setdefault(e["speaker"], "CapS%d" % (len(spk_style) % 3 + 1))
            style = spk_style[e["speaker"]]
        emph_here = [w for w in emphasis if w and w in e["text"]] if "emphasis" in P["telop"] else []
        extra =  sum(0.15 * width(w) for w in emph_here)
        px = fit_px(e["text"], P["font"], outline, scalex, extra=extra)
        txt = ass_esc(e["text"])
        for w in emph_here:
            txt = txt.replace(ass_esc(w), "{\\c&H0000F0FF&\\fscx%d\\fscy115}%s{\\r%s}" % (int(scalex * 1.15), ass_esc(w), style))
        pre = "{\\fs%d}" % px if px != P["font"] else ""
        if pre and emph_here:
            txt = txt.replace("{\\r%s}" % style, "{\\r%s\\fs%d}" % (style, px))
        L.append("Dialogue: 1,%s,%s,%s,,0,0,0,,%s%s" % (ass_time(e["start"]), ass_time(e["end"]), style, pre, txt))
        layout.append(dict(kind="caption", text=e["text"], px=px, bottom=P["mv"], right=P["mr"], left=SAFE["left"],
                           width=round((width(e["text"]) + extra) * px * GLYPH * scalex / 100.0 + 2 * outline)))
    if headline and "headline" in P["telop"]:
        px = fit_px(headline, 60, 14)
        L.append("Dialogue: 3,%s,%s,Headline,,0,0,0,,{\\fs%d}%s" % (ass_time(0), ass_time(total), px, ass_esc(headline)))
        layout.append(dict(kind="headline", text=headline, px=px, top=300))
    for (a, b, text) in labels:
        L.append("Dialogue: 3,%s,%s,Label,,0,0,0,,%s" % (ass_time(a), ass_time(b), ass_esc(text)))
        layout.append(dict(kind="label", text=text, px=48, top=410, width=round(width(text) * 48 * GLYPH + 12)))
    for (a, b, text) in tsukkomi:
        px = fit_px(text, 64, 12)
        L.append("Dialogue: 2,%s,%s,Tsukkomi,,0,0,0,,{\\fs%d\\fad(80,80)}%s" % (ass_time(a), ass_time(b), px, ass_esc(text)))
        layout.append(dict(kind="tsukkomi", text=text, px=px, bottom=930, right=P["mr"], left=SAFE["left"], width=round(width(text) * px * GLYPH + 24)))
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")
    return layout


# ---------------- render ----------------
def fnum(x):
    return ("%.4f" % x).rstrip("0").rstrip(".")


def atempo(sp):
    out = []
    while sp > 2.0:
        out.append("atempo=2.0")
        sp /= 2.0
    out.append("atempo=%s" % fnum(sp))
    return ",".join(out)


def build_graph(groups, info, fps, ass, fontsdir, loud, audio_only=False):
    inputs, parts, vlabels, alabels = [], [], [], []
    vertical = info["width"] / float(info["height"]) <= 0.6
    gi = 0
    for g in groups:
        runs = []  # forward-moving runs share one input
        for idx, c in enumerate(g["clips"]):
            if runs and c[0] >= g["clips"][runs[-1][-1]][1] - 1e-6 and (c[0] - g["clips"][runs[-1][-1]][1]) < 60:
                runs[-1].append(idx)
            else:
                runs.append([idx])
        for run_ in runs:
            s0 = max(0.0, g["clips"][run_[0]][0] - 1.0)
            s1 = g["clips"][run_[-1]][1] + 0.5
            inputs += ["-ss", fnum(s0), "-t", fnum(s1 - s0), "-i", info["path"]]
            n = len(run_)
            if not audio_only:
                parts.append("[%d:v]fps=%s,split=%d%s;" % (gi, fnum(fps), n, "".join("[v%d_%d]" % (gi, k) for k in range(n))) if n > 1 else "[%d:v]fps=%s[v%d_0];" % (gi, fnum(fps), gi))
            parts.append("[%d:a]aformat=sample_rates=48000:channel_layouts=stereo,asplit=%d%s;" % (gi, n, "".join("[a%d_%d]" % (gi, k) for k in range(n))) if n > 1 else "[%d:a]aformat=sample_rates=48000:channel_layouts=stereo[a%d_0];" % (gi, gi))
            for k, idx in enumerate(run_):
                a, b, sp = g["clips"][idx]
                frames = g["out"][idx][2]
                odur = frames / fps
                z = g["zoom"][idx]
                lab = "c%d_%d" % (gi, k)
                if not audio_only:
                    v = "[v%d_%d]trim=start=%s:end=%s,setpts=(PTS-STARTPTS)/%s" % (gi, k, fnum(a - s0), fnum(b - s0), fnum(sp))
                    if z > 1.0:
                        v += ",scale=iw*%s:ih*%s,crop=iw/%s:ih/%s" % (fnum(z), fnum(z), fnum(z), fnum(z))
                    if vertical:
                        v += ",scale=%d:%d:force_original_aspect_ratio=increase,crop=%d:%d,setsar=1" % (W, H, W, H)
                    else:
                        v += ",split[f%s][b%s];[b%s]scale=270:480:force_original_aspect_ratio=increase,crop=270:480,boxblur=10:2,scale=%d:%d[bb%s];" \
                             "[f%s]scale=%d:-2[ff%s];[bb%s][ff%s]overlay=(W-w)/2:(H-h)/2-120,setsar=1" % (lab, lab, lab, W, H, lab, lab, W, lab, lab, lab)
                    v += ",fps=%s,tpad=stop_mode=clone:stop_duration=2,trim=end_frame=%d,setpts=PTS-STARTPTS[v%s];" % (fnum(fps), frames, lab)
                    parts.append(v)
                    vlabels.append("[v%s]" % lab)
                af = "[a%d_%d]atrim=start=%s:end=%s,asetpts=PTS-STARTPTS" % (gi, k, fnum(a - s0), fnum(b - s0))
                if sp != 1.0:
                    af += "," + atempo(sp)
                af += ",afade=t=in:d=0.012,apad=whole_dur=%s,atrim=end=%s,afade=t=out:st=%s:d=0.012[a%s];" % (fnum(odur), fnum(odur), fnum(max(0.0, odur - 0.012)), lab)
                parts.append(af)
                alabels.append("[a%s]" % lab)
            gi += 1
    n = len(alabels)
    if audio_only:
        parts.append("%sconcat=n=%d:v=0:a=1[ca];[ca]%s[aout]" % ("".join(alabels), n, loud))
    else:
        parts.append("%sconcat=n=%d:v=1:a=1[cv][ca];" % ("".join(v + a for v, a in zip(vlabels, alabels)), n))
        sub = ("subtitles=filename='%s'%s," % (ff_path(ass), (":fontsdir='%s'" % ff_path(fontsdir)) if fontsdir else "")) if ass else ""
        parts.append("[cv]%sformat=yuv420p[vout];[ca]%s,aresample=48000[aout]" % (sub, loud))
    return inputs, "".join(parts)


def ff_path(p):
    p = os.path.abspath(p).replace("\\", "/")
    return p.replace(":", "\\:").replace("'", "\\'")


def ff_major():
    out, _ = run([FF, "-version"])
    m = re.search(r"ffmpeg version n?(\d+)\.", out)
    return int(m.group(1)) if m else 7


def graph_args(graph, path):
    with open(path, "w", encoding="utf-8") as f:
        f.write(graph)
    return ["-/filter_complex", path] if ff_major() >= 7 else ["-filter_complex_script", path]


def render(groups, info, fps, ass, fontsdir, out, work):
    li, g = build_graph(groups, info, fps, None, None, "loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json", audio_only=True)
    _, err = ff(li + graph_args(g, os.path.join(work, "pass1.txt")) + ["-map", "[aout]", "-f", "null", "-"])
    m = json.loads(err[err.rindex("{"):err.rindex("}") + 1])
    ln = "loudnorm=I=-14:TP=-1.5:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true" % (
        m["input_i"], m["input_tp"], m["input_lra"], m["input_thresh"], m["target_offset"])
    if float(m["input_i"]) < -70:
        ln = "anull"
    inp, g = build_graph(groups, info, fps, ass, fontsdir, ln)
    args = inp + graph_args(g, os.path.join(work, "graph.txt")) + ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-preset", os.environ.get("VC_X264_PRESET", "veryfast"),
                                                                  "-crf", "20", "-profile:v", "high", "-pix_fmt", "yuv420p", "-r", fnum(fps), "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
                                                                  "-movflags", "+faststart", "-y", out]
    with open(os.path.join(work, "ffmpeg_cmd.txt"), "w", encoding="utf-8") as f:
        f.write(json.dumps([FF] + args, ensure_ascii=False))
    ff(args)


# ---------------- QA ----------------
def measure(out):
    info = probe(out)
    _, err = ff(["-i", out, "-vn", "-af", "ebur128=peak=true", "-f", "null", "-"])
    summ = err[err.rfind("Summary:"):]
    I = re.search(r"I:\s*(-?[\d.]+) LUFS", summ)
    TP = re.search(r"Peak:\s*(-?[\d.]+|-inf) dBFS", summ)
    _, err2 = ff(["-i", out, "-an", "-vf", "blackdetect=d=0.5:pix_th=0.10,freezedetect=n=-60dB:d=0.5", "-f", "null", "-"])
    black = re.findall(r"black_start:([\d.]+) black_end:([\d.]+)", err2)
    freeze = re.findall(r"freeze_start: ([\d.]+)", err2)
    return dict(duration=info["duration"], width=info.get("width"), height=info.get("height"), lufs=float(I.group(1)) if I else None,
                true_peak=(float(TP.group(1)) if TP and TP.group(1) != "-inf" else -99.0), black=black, freeze=freeze)


def qa(ctx):
    P, meas, ev, layout, groups, sents, vid = ctx["P"], ctx["meas"], ctx["ev"], ctx["layout"], ctx["groups"], ctx["sents"], ctx["vid"]
    R = []

    def add(cid, hard, ok, detail, how=""):
        R.append(dict(id=cid, level="hard" if hard else "soft", status="pass" if ok is True else ("not_run" if ok is None else "fail" if hard else "warn"), detail=detail, fix=how))

    lo, hi = ctx["range"]
    d = meas["duration"]
    add("H-01", True, lo - 0.5 <= d <= hi + 0.5 and (d <= 180 or ctx["series"]), "尺 %.1f秒（範囲 %.0f〜%.0f秒）" % (d, lo, hi), "文を足す／削る")
    add("H-02", True, meas["width"] == W and meas["height"] == H, "%sx%s" % (meas["width"], meas["height"]))
    add("H-03", True, meas["lufs"] is not None and abs(meas["lufs"] + 14) <= 1.0, "積分ラウドネス %s LUFS（目標 -14±1、ebur128で別測定）" % meas["lufs"])
    add("H-04", True, meas["true_peak"] <= -1.0, "True Peak %.1f dBTP（-1.0以下）" % meas["true_peak"])
    bad = [l for l in layout if l["kind"] in ("caption", "tsukkomi") and (l["bottom"] < SAFE["bottom"] or l["right"] < SAFE["right"] or l["width"] > W - SAFE["left"] - SAFE["right"] + 1)]
    add("H-05", True, not bad, "安全領域外のテロップ %d件%s" % (len(bad), (": " + bad[0]["text"]) if bad else ""), "短くするか無効にする")
    edges_bad = []
    sils = ctx["sils"]
    for g in groups:
        if g["clips"][0][2] != 1.0 or not g["sents"]:
            continue
        for (a, b, _) in g["clips"]:
            for t in (a, b):
                in_sil = any(x - 0.06 <= t <= y + 0.06 for x, y in sils)
                inside = [s for s in sents if s["start"] + 0.15 < t < s["end"] - 0.15]
                if inside and not in_sil:
                    edges_bad.append(round(t, 2))
    add("H-06", True, not edges_bad, "発話途中のカット %d箇所 %s" % (len(edges_bad), edges_bad[:5]), "文の範囲を変える")
    used = "".join(sents[i]["text"] for g in groups for i in g["sents"])
    alltext = norm("".join(s["text"] for s in sents))
    miss = []
    for t in [vid.get("headline", "")] + list(vid.get("emphasis", [])):
        for tok in facts(t):
            if norm(tok) not in (alltext if t == vid.get("headline", "") and ctx["series"] else norm(used)):
                miss.append(tok)
    miss += [w for w in vid.get("emphasis", []) if norm(w) not in norm(used)]
    add("H-07", True, not miss, "書き起こしに無い語 %s" % miss, "書き起こしにある表記に直す")
    unapproved = [t for t in ctx["tsukkomi_raw"] if not t.get("approved")]
    add("H-08", True, not unapproved, "未承認のツッコミ %d件" % len(unapproved), "利用者に承認してもらうか外す")
    add("H-09", True, bool(ctx["rights"]), "権利確認 %s" % ("済" if ctx["rights"] else "未"), "利用者に確認する")
    vb = []
    for g in groups:
        want = norm("".join(sents[i]["text"] for i in g["sents"]))
        got = norm("".join(e["text"] for e in ev if e["role"] == g["role"] and e["sent"] in g["sents"] and g["out"][0][0] - 1e-6 <= e["start"] <= g["out"][-1][1] + 1e-6))
        if "caption" in P["telop"] and g["clips"][0][2] == 1.0 and want != got:
            vb.append(g["role"])
    add("V-01", True, not vb, "字幕と発話の逐語一致 %s" % ("OK" if not vb else "NG: " + ",".join(vb)), "字幕は話した言葉どおり（削除・言い換え禁止）")
    fast = [e for e in ev if width(e["text"]) / max(0.01, e["end"] - e["start"]) > 6.0 + 1e-6]
    add("S-01", False, not fast, "6文字/秒を超える字幕 %d件%s" % (len(fast), (": " + fast[0]["text"]) if fast else ""), "字幕は削らない。速い話し方による警告")
    longl = [e["text"] for e in ev if width(e["text"]) > P["chars"]]
    add("S-02", False, not longl, "1行の文字数超過 %d件" % len(longl))
    if P.get("nohook"):
        add("S-03", False, None, "P9は対象外")
    else:
        starts = [map_t(g, sents[g["sents"][0]]["start"]) for g in groups if g["sents"] and g["clips"][0][2] == 1.0]
        first = min(starts) if starts else 99.0
        add("S-03", False, first <= 2.0, "最初の発話 %.1f秒（2秒以内）" % first, "冒頭に強い一文（先出し）を置く")
    shots = [round(o1 - o0, 2) for g in groups for (o0, o1, _) in g["out"]]
    short = [x for x in shots if x < P["min_shot"] - 0.05]
    add("S-04", False, not short, "最短ショット %.2f秒（下限 %.1f）" % (min(shots), P["min_shot"]))
    if P.get("nohook"):
        add("S-06", False, None, "P9は対象外")
    else:
        add("S-06", False, not meas["black"] and not meas["freeze"], "黒画面 %d・静止 %d（0.5秒以上）" % (len(meas["black"]), len(meas["freeze"])))
    firsts = groups[0]["sents"][0] if groups and groups[0]["sents"] else None
    add("S-07", False, firsts is None or not sents[firsts]["text"].startswith(DEMONSTRATIVE), "冒頭の文: %s" % (sents[firsts]["text"][:20] if firsts is not None else "-"), "指示語で始まらない文から始める")
    kin = [e["text"] for e in ev if e["text"][:1] in NO_START or e["text"][-1:] in NO_END]
    add("S-08", False, not kin, "禁則違反 %d件" % len(kin))
    negs = []
    for t in [vid.get("headline", "")] + list(vid.get("emphasis", [])):
        if t and not NEGATION.search(t):
            src = [sents[i]["text"] for g in groups for i in g["sents"] if any(tok in sents[i]["text"] for tok in facts(t) or [t])]
            if src and all(NEGATION.search(s) for s in src):
                negs.append(t)
    add("S-09", False, not negs, "否定の脱落の疑い %s" % negs)
    if ctx["series"]:
        ch = ctx["chapters"]
        ok = bool(ch) and ch[0][0] == 0 and len(ch) >= 3 and all(ch[i + 1][0] - ch[i][0] >= 10 for i in range(len(ch) - 1))
        add("S-14", False, ok, "チャプター %d個" % len(ch))
        add("S-11", False, d <= 3600, "60分以下")
    hard_ok = all(r["status"] == "pass" for r in R if r["level"] == "hard")
    return dict(hard_pass=hard_ok, results=R)


# ---------------- build ----------------
def safe_name(s):
    return re.sub(r'[\\/:*?"<>|\s]+', "_", s).strip("_")[:60] or "video"


def contact_sheet(out, dur, png):
    n = 6
    times = [dur * (k + 0.5) / n for k in range(n)]
    args = []
    for t in times:
        args += ["-ss", fnum(t), "-i", out]
    fc = "".join("[%d:v]scale=270:480,setsar=1[s%d];" % (k, k) for k in range(n)) + "".join("[s%d]" % k for k in range(n)) + "xstack=inputs=%d:layout=%s[o]" % (
        n, "|".join("%d_%d" % (270 * (k % 3), 480 * (k // 3)) for k in range(n)))
    ff(args + ["-filter_complex", fc, "-map", "[o]", "-frames:v", "1", "-update", "1", "-y", png])
    return times


def cmd_build(a):
    plan = jload(a.plan)
    pid = plan.get("preset", "")
    P = PRESETS.get(pid)
    if not P:
        die("unknown preset %s (P1..P10)" % pid)
    if P.get("supported") is False:
        die("%s はこの版では作れません（BGMのビート解析と楽曲ライブラリが必要）。P4/P8/P9 を提案してください。" % pid)
    if not plan.get("rights_confirmed"):
        die("rights_confirmed が true ではありません。素材の権利（自分が権利を持つ／許諾済み）を利用者に確認してください。(H-09)")
    pending = [t["text"] for t in plan.get("tsukkomi", []) if not t.get("approved")]
    if pending:
        die("未承認のツッコミがあります: %s。利用者に読んでもらい、承認されたものだけ approved=true にし、それ以外は削除してください。(H-08)" % " / ".join(pending))
    work = a.work
    sents = jload(os.path.join(work, "sentences.json"))
    sig = jload(os.path.join(work, "signals.json"))
    info = jload(os.path.join(work, "probe.json"))
    if os.path.abspath(info["path"]) != os.path.abspath(plan.get("source", info["path"])):
        die("plan.source が probe.json の入力と違います")
    font_file, family = find_font(a.font)
    if P["telop"] and not font_file:
        print("WARN: 日本語フォントが見つからないため、字幕・テロップを焼き込みません（SRTは出力します）。", file=sys.stderr)
    fps = info.get("fps") or 30.0
    fps = fps if 23 < fps <= 60.5 else 30.0
    series = plan.get("mode") == "series"
    vids = plan["videos"]
    if not series and len(vids) > 3:
        die("見どころは最大3本です（水増ししない）")
    if not series:  # IoU overlap check between candidates
        spans = []
        for v in vids:
            r = [x for x in v["body"] if not isinstance(x, dict)]
            if r:
                spans.append((sents[min(x[0] for x in r)]["start"], sents[max(x[1] for x in r)]["end"]))
            else:
                spans.append((min(x["t0"] for x in v["body"]), max(x["t1"] for x in v["body"])))
        for i in range(len(spans)):
            for j in range(i + 1, len(spans)):
                inter = max(0.0, min(spans[i][1], spans[j][1]) - max(spans[i][0], spans[j][0]))
                uni = max(spans[i][1], spans[j][1]) - min(spans[i][0], spans[j][0])
                if uni > 0 and inter / uni > 0.5:
                    die("候補 %d と %d が重なっています（IoU %.2f > 0.5）。別の場面を選ぶか本数を減らしてください。" % (i + 1, j + 1, inter / uni))
    else:
        cov = []
        for v in vids:
            for x in v["body"]:
                cov += list(range(int(x[0]), int(x[1]) + 1))
        if sorted(cov) != list(range(len(sents))):
            die("シリーズの本編が全文を重なりなく覆っていません（split の結果をそのまま使ってください）")
    os.makedirs(a.out, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M")
    proj = safe_name(plan.get("project") or os.path.splitext(os.path.basename(info["path"]))[0])
    labels_all = PART_LABELS.get(len(vids), ["第%d話" % (k + 1) for k in range(len(vids))]) if series else [""] * len(vids)
    summary = []
    sils = [tuple(x) for x in sig["silences"]]
    for k, vid in enumerate(vids):
        if a.only and (k + 1) not in a.only:
            continue
        ctxs = None
        if series:
            prev = vids[k - 1]["body"][-1] if k > 0 else None
            recap = None
            if prev:
                b = int(prev[1])
                a0 = b
                while a0 > int(prev[0]) and b - a0 < 2 and sents[b]["end"] - sents[a0 - 1]["start"] <= 10:
                    a0 -= 1
                recap = (a0, b)
            nxt = vids[k + 1].get("teaser") if k + 1 < len(vids) else None
            ctxs = dict(recap=recap, next=nxt)
            if vid.get("teaser") is None:
                die("シリーズの各パートには teaser（そのパートの見どころ1文）が必要です（part %d）" % (k + 1))
        groups = build_groups(plan, vid, sents, sig, P, ctxs)
        total = timeline(groups, fps)
        if series:
            mean = sum(sum(edited_len(sents, P["sil"], sils)[int(x[0]):int(x[1]) + 1]) for v in vids for x in v["body"]) / len(vids)
            rng = (mean * 0.7, mean * 1.3 + 30)
        else:
            rng = P["dur"]
        if not series and not (rng[0] - 0.5 <= total <= rng[1] + 0.5):
            die("候補%d の完成尺が %.1f秒で、%s の範囲 %d〜%d秒に入りません。%s" % (k + 1, total, pid, rng[0], rng[1],
                "文を減らしてください（約%.0f秒）。" % (total - rng[1]) if total > rng[1] else "前後の文を足してください（約%.0f秒）。" % (rng[0] - total)))
        emphasis = [w for w in vid.get("emphasis", [])][:3]
        ev, maxw, scalex, outline = captions(groups, sents, P, emphasis, None)
        ev_burn = ev if "caption" in P["telop"] else []
        labels = []
        for g in groups:
            if g["role"] == "recap":
                labels.append((g["out"][0][0], g["out"][-1][1], "前回のおさらい"))
            if g["role"] == "next":
                labels.append((g["out"][0][0], g["out"][-1][1], "▶ %sにつづく" % (labels_all[k + 1] if k + 1 < len(labels_all) else "次回")))
        tsk_raw = [t for t in plan.get("tsukkomi", []) if int(t.get("video", 0)) == k] if "tsukkomi" in P["telop"] else []
        tsk = []
        for t in tsk_raw:
            if len(t["text"]) > 14:
                die("ツッコミは14文字以内: " + t["text"])
            g = next((g for g in groups if int(t["after"]) in g["sents"] and g["role"] == "body"), None)
            if g and t.get("approved"):
                e = map_t(g, sents[int(t["after"])]["end"])
                tsk.append((max(0.0, e - 0.2), min(total, e + 1.6), t["text"]))
        headline = vid.get("headline", "")
        if series and headline:
            headline = "【%s】%s" % (labels_all[k], headline)
        if len(vid.get("headline", "")) > 16:
            die("見出しは16文字以内: " + vid["headline"])
        label = labels_all[k]
        base = "%s_%s_%02d%s_%s" % (proj, pid, k + 1, label, stamp)
        wk = os.path.join(work, "v%02d" % (k + 1))
        os.makedirs(wk, exist_ok=True)
        ass = os.path.join(wk, "telop.ass")
        layout = write_ass(ass, ev_burn, P, headline, labels, tsk, family or "sans-serif", total, scalex, outline, None, emphasis)
        burn = font_file is not None and (ev_burn or headline or labels or tsk)
        out = os.path.join(a.out, base + ".mp4")
        render(groups, info, fps, ass if burn else None, os.path.dirname(font_file) if burn else None, out, wk)
        meas = measure(out)
        chapters = []
        if series:
            for (si, title) in vid.get("chapters", []):
                g = next((g for g in groups if g["role"] == "body" and int(si) in g["sents"]), None)
                if g:
                    chapters.append((round(map_t(g, sents[int(si)]["start"]), 1), title))
            if chapters:
                chapters[0] = (0, chapters[0][1])
            merged = []
            for c in chapters:
                if merged and c[0] - merged[-1][0] < 10:
                    continue
                merged.append(c)
            chapters = merged
        res = qa(dict(P=P, meas=meas, ev=ev_burn if "caption" in P["telop"] else [], layout=layout, groups=groups, sents=sents, vid=vid, range=rng, series=series,
                      sils=sils, tsukkomi_raw=tsk_raw, rights=plan.get("rights_confirmed"), chapters=chapters))
        with open(os.path.join(a.out, base + ".srt"), "w", encoding="utf-8") as f:  # full verbatim speech of the output
            for n, e in enumerate(ev, 1):
                f.write("%d\n%s --> %s\n%s\n\n" % (n, srt_time(e["start"]), srt_time(e["end"]), e["text"]))
        sheet = os.path.join(a.out, base + "_sheet.png")
        contact_sheet(out, meas["duration"], sheet)
        meta = ["# " + base, "", "## タイトル案"] + ["- " + t for t in vid.get("titles", [])][:3] + ["", "## 説明文", vid.get("description", ""), "", "元動画: （URLを記入）", ""]
        if chapters:
            meta += ["## チャプター"] + ["%s %s" % (mmss(t)[:5] if t < 3600 else mmss(t), c) for t, c in chapters] + [""]
        tags = list(vid.get("hashtags", []))
        if meas["duration"] <= 180 and not series and "#Shorts" not in tags:
            tags.append("#Shorts")
        meta += ["## ハッシュタグ", " ".join(tags), "", "## 選定理由", vid.get("reason", ""), "", "## スコア（0〜10、AIの目安）", json.dumps(vid.get("scores", {}), ensure_ascii=False)]
        with open(os.path.join(a.out, base + "_meta.md"), "w", encoding="utf-8") as f:
            f.write("\n".join(meta) + "\n")
        editplan = dict(tool=VERSION, preset=pid, video=vid, series_label=label, fps=fps, duration=total, font=family, burned=bool(burn),
                        groups=[dict(role=g["role"], sents=[g["sents"][0], g["sents"][-1]] if g["sents"] else [], clips=g["clips"], zoom=g["zoom"], out=[o[:2] for o in g["out"]]) for g in groups],
                        captions=[dict(start=round(e["start"], 3), end=round(e["end"], 3), text=e["text"]) for e in ev], chapters=chapters, measured=meas, qa=res)
        jdump(editplan, os.path.join(a.out, base + ".editplan.json"))
        summary.append(dict(file=base + ".mp4", duration=round(meas["duration"], 1), lufs=meas["lufs"], tp=meas["true_peak"], hard_pass=res["hard_pass"],
                            warnings=[r["id"] + " " + r["detail"] for r in res["results"] if r["status"] in ("warn", "fail")]))
        print(json.dumps(summary[-1], ensure_ascii=False))
    jdump(summary, os.path.join(a.out, "summary.json"))
    if not all(s["hard_pass"] for s in summary):
        sys.exit(3)


def main():
    ap = argparse.ArgumentParser(description="VideoCreator Cowork edition " + VERSION)
    sp = ap.add_subparsers(dest="cmd", required=True)
    e = sp.add_parser("env")
    e.add_argument("--font")
    p = sp.add_parser("prepare", help="probe + transcript + signals into WORK")
    p.add_argument("video")
    p.add_argument("--transcript", help="VTT/SRT/JSON with timestamps")
    p.add_argument("--work", default="vc_work")
    s = sp.add_parser("split")
    s.add_argument("--work", default="vc_work")
    s.add_argument("--preset", required=True)
    s.add_argument("--parts", type=int, required=True)
    s.add_argument("--prefer", default="", help="comma separated sentence indices after which a topic ends")
    b = sp.add_parser("build")
    b.add_argument("plan")
    b.add_argument("--work", default="vc_work")
    b.add_argument("--out", default="vc_out")
    b.add_argument("--font")
    b.add_argument("--only", type=int, nargs="*")
    sh = sp.add_parser("frames", help="contact sheets every N seconds so the AI can look at the video")
    sh.add_argument("video")
    sh.add_argument("--every", type=float, default=10.0)
    sh.add_argument("--work", default="vc_work")
    a = ap.parse_args()
    if a.cmd == "env":
        font_file, fam = find_font(a.font)
        filters = ""
        if FF:
            filters, _ = run([FF, "-hide_banner", "-filters"], check=False)
        need = ["subtitles", "loudnorm", "ebur128", "silencedetect", "scdet", "blackdetect", "freezedetect", "boxblur", "xstack", "tpad", "apad", "atempo"]
        miss = [n for n in need if not re.search(r"\s%s\s" % n, filters)]
        enc, _ = run([FF, "-hide_banner", "-encoders"], check=False) if FF else ("", "")
        r = dict(python=sys.version.split()[0], ffmpeg=FF, ffprobe=FP, ffmpeg_version=(run([FF, "-version"])[0].split("\n")[0] if FF else None), missing_filters=miss,
                 libx264=("libx264" in enc), font=font_file, font_family=fam, whisper=bool(importlib.util.find_spec("faster_whisper")), tool=VERSION)
        r["ok"] = bool(FF) and not miss and r["libx264"]
        print(json.dumps(r, ensure_ascii=False, indent=1))
        sys.exit(0 if r["ok"] else 1)
    if a.cmd == "prepare":
        os.makedirs(a.work, exist_ok=True)
        info = probe(a.video)
        jdump(info, os.path.join(a.work, "probe.json"))
        cues = parse_transcript(a.transcript) if a.transcript else asr(a.video)
        if cues is None:
            die("書き起こしがありません。Teams/Stream の .vtt を添付してもらってください（faster-whisper も使えません）。", 4)
        if not cues:
            die("書き起こしが空です", 4)
        sents = to_sentences(cues)
        if sents[-1]["end"] > info["duration"] + 2:
            die("書き起こしの時刻(%.1f秒)が動画の長さ(%.1f秒)を超えています。別の動画の書き起こしではありませんか。" % (sents[-1]["end"], info["duration"]))
        sig = signals(a.video)
        for s in sents:
            ks = [sig["loud_per_sec"].get(str(t)) for t in range(int(s["start"]), int(s["end"]) + 1)]
            ks = [x for x in ks if x is not None]
            s["loud"] = max(ks) if ks else None
        jdump(sents, os.path.join(a.work, "sentences.json"))
        jdump(sig, os.path.join(a.work, "signals.json"))
        with open(os.path.join(a.work, "transcript.txt"), "w", encoding="utf-8") as f:
            for i, s in enumerate(sents):
                gap = (sents[i + 1]["start"] - s["end"]) if i + 1 < len(sents) else 0
                f.write("[%d] %s-%s %s: %s  (loud %s%s)\n" % (i, mmss(s["start"]), mmss(s["end"]), s["speaker"] or "-", s["text"], s["loud"], ", pause %.1fs" % gap if gap >= 0.7 else ""))
        print(json.dumps(dict(probe=info, sentences=len(sents), speakers=sorted(set(s["speaker"] for s in sents)), silence_db=sig["silence_db"],
                              silences=len(sig["silences"]), shots=len(sig["shots"]), loud_peaks=sig["loud_peaks"]), ensure_ascii=False, indent=1))
        if info["problems"]:
            print("PROBLEMS: " + " / ".join(info["problems"]), file=sys.stderr)
    if a.cmd == "split":
        P = PRESETS.get(a.preset) or die("unknown preset")
        if not P.get("series"):
            die("%s はシリーズに分けられません（P1〜P4・P6・P7・P9・P10 のみ）" % a.preset)
        sents = jload(os.path.join(a.work, "sentences.json"))
        prefer = set(int(x) for x in a.prefer.split(",") if x.strip())
        sig = jload(os.path.join(a.work, "signals.json"))
        r = split_series(sents, a.parts, prefer, P["sil"], [tuple(x) for x in sig["silences"]])
        jdump(r, os.path.join(a.work, "parts.json"))
        print(json.dumps(r, ensure_ascii=False, indent=1))
    if a.cmd == "build":
        if not FF:
            die("ffmpeg がありません")
        cmd_build(a)
    if a.cmd == "frames":
        os.makedirs(a.work, exist_ok=True)
        info = probe(a.video)
        n = max(1, int(info["duration"] // a.every))
        per = 12
        out = []
        for page in range(0, n, per):
            ts_ = [a.every * (k + 0.5) for k in range(page, min(n, page + per))]
            png = os.path.join(a.work, "frames_%03d.png" % (page // per + 1))
            args = []
            for t in ts_:
                args += ["-ss", fnum(t), "-i", a.video]
            m = len(ts_)
            fc = "".join("[%d:v]scale=320:180:force_original_aspect_ratio=decrease,pad=320:180:(ow-iw)/2:(oh-ih)/2,setsar=1[s%d];" % (k, k) for k in range(m))
            fc += "".join("[s%d]" % k for k in range(m)) + ("xstack=inputs=%d:layout=%s[o]" % (m, "|".join("%d_%d" % (320 * (k % 4), 180 * (k // 4)) for k in range(m))) if m > 1 else "null[o]")
            ff(args + ["-filter_complex", fc, "-map", "[o]", "-frames:v", "1", "-update", "1", "-y", png])
            out.append(dict(image=png, grid="4列、左上から右へ", times=[round(t, 1) for t in ts_]))
        jdump(out, os.path.join(a.work, "frames.json"))
        print(json.dumps(out, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
```
<!-- vc_tool.py:end -->

---

## Appendix B — Design rationale and verification

### Four questions that deepen the request

| Question | Answer |
|---|---|
| (1) What would a leading expert do unasked? | First check Python,FFmpeg,Japanese fonts,and the200MBattachment limit;honestly fall back when capabilities are missing. Separate AI scene judgments from deterministic cutting/captions/volume/checks,so the same plan reproduces the same result. Mechanically test verbatimness,numbers/names,safe areas,loudness,and midword cuts. Never pad candidate counts;obtain rights confirmation and approval for AI-proposed text. Explain differences from the app:SFX,BGM,camera switching,and P5. |
| (2) What raw sources would an expert start with? | Start with unprocessed video and timestamped Stream/Teams VTT. Measure silence,shots,and100msloudness directly from original audio/video with FFmpeg. Read actual`presets/P1–P10.json`,`docs/PRD.md`§7functional requirements/§13QA,and Renderer.cs/AssWriter.cs implementations of blurred backgrounds,styles,and two-pass loudnorm,not summaries. Check official Cowork documentation for formats,the200MBattachment limit,50files/500MBoutput limit,and skills. |
| (3) Specific methods/tools? | Select sentence indices/snap boundaries;use`silencedetect`for jump cuts/minimum shots and`scdet`/`blackdetect`/`ebur128`for signals. Use dynamic programming for balanced/topic-aware series splitting,blurred portrait backgrounds/punch-ins,ASS line restrictions/width shrinking,and two-pass loudnorm. QA uses ffprobe-equivalent metrics,independent ebur128,blackdetect/freezedetect,layout calculation,and verbatim comparison. |
| (4) Evidence independent of you/me? | Independently measure with EBU R128/ffprobe. Retranscribe final MP4through Azure Speech to check that intended words are audible and aligned with captions. Compare scripted ground-truth videos and the reference app's QA;ultimately verify YouTube Studio accepts outputs as Shorts. |

### Rewritten request

> Create `/work/VideoCreator-Prompt.md` reproducing VideoCreator's transcription→highlight selection→sentence snapping/silence cuts→9:16conversion→verbatim captions/headings/emphasis→two-pass normalization→QA workflow inside Microsoft365Copilot Cowork. The user chooses only the video,pattern P1–P10or automatic choice,and up to three highlights or an N-part series;numeric settings remain fixed to presets. Respect official limits:200MBattachments,50files/500MBoutputs,no local-file access,and skill behavior. Runtime availability is not guaranteed:include environment checks/fallbacks. Separate AI judgments from deterministic processing and bundle a verified script. Implement measurable app H-01–H-11/S-01–S-14checks and disclose differences. Actually run scripted footage and verify independently using ebur128,ffprobe,and Azure Speech retranscription.

### Verification record: 2026-11-10, Windows/Python3.12/FFmpeg2026-11

| Test | Material | Result |
|---|---|---|
| Two P4highlights |`spikes/media/talk.mp4`,125seconds,1920×1080,two scripted speakers,script-derived VTT.|33.2/25.2seconds,−14.0LUFS/−1.5dBTP;all hard checks pass.|
| Independent content check |Azure Speech retranscription of those outputs.|Intended-speech bigram recall100%/96.2%;all captions' speech is audible within±1second;cold-open speech audible at0.1–0.2seconds.|
| Two-part series |First eight minutes of`spikes/media/long30.mp4`,1280×720,five segments,app transcript converted to VTT.|Boundary matches“Next segment…”topic change;190.6/214.6seconds,−14LUFS,chapters pass. Teaser/recap/next-preview speech is100%audible at planned positions;body recall99.1%/97.7%,caption synchronization100%/98.4%.|
| Three-part series |Same source.|Five topics cannot divide evenly into three;one boundary correctly warns it is not a topic break.|
| P2/P3/P7/P8/P9 |talk.mp4.|All export/pass hard checks. Unapproved P3reaction stops before rendering;approved reaction uses white box. P7uses speaker-colored outlines.|
| Rejection/errors |talk.mp4.|H-07detects heading`1万円`against transcript`一万円`. Invalid duration,overlap,P5,unconfirmed rights,and series parts<1minute stop with correction instructions.|
| Without ffprobe |FFmpeg alone,selected by`VC_FFMPEG`.|Same duration/resolution/fps/audio judgments and successful export.|

Bugs found and fixed:jump cuts dropped caption fragments and violated verbatimness,so distribute captions over output intervals;safe-width calculations omitted emphasis enlargement,so include it;series-duration estimation ignored silence cuts,so estimate from detected silence. Correct the structure to cold open→recap→body→next preview.

### Still unverified

- **Actual Copilot Cowork runtime:** official documentation does not specify Python/FFmpeg/Japanese-font availability. Public community skills sometimes use Python/FFmpeg/imageio-ffmpeg. Always run Step1's environment check and use§3fallback when necessary.
- Stream VTTsegmentation/punctuation varies by source. A cue without punctuation remains one sentence.
