---
name: VideoCreator
description: 動画ファイル1本から、VideoCreator の編集パターン（P1〜P10）だけを使って、縦型ショート動画（見どころ最大3本）または前編・後編のシリーズ動画を作る。「この動画をショートにして」「切り抜きを作って」「前編と後編に分けて」と頼まれたときに使う。
---

# VideoCreator for Microsoft 365 Copilot Cowork

このファイルは、ローカルアプリ「VideoCreator」が行っている編集（書き起こし → 見どころ選び → 無音カット → 縦型化 → 字幕・見出し → 音量調整 → 自動チェック）を、Copilot Cowork で再現するためのプロンプトです。前半は**あなた（人）が読む使い方**、後半は **Cowork が読む指示**です。

---

## 使い方（人が読む部分）

あなたが決めるのは **「どの動画」「どのパターン」「見どころか、シリーズか」** の3つだけです。秒数・音量・文字サイズなどの細かい値は、アプリと同じテンプレートの値に固定されています。

### 1. 用意するもの

| もの | 説明 |
|---|---|
| 動画ファイル | MP4 / MOV / MKV / WebM。**200MB 未満**（Cowork の添付の上限）。10秒〜3時間。音声入り |
| 書き起こし（.vtt）※強く推奨 | 動画を OneDrive / SharePoint に置き、Stream で開いて文字起こし（トランスクリプト）を作成し、.vtt でダウンロードします。Teams 会議の録画なら、会議の「まとめ」→「トランスクリプト」から .vtt をダウンロードできます。Cowork は音声を直接聞けないため、これが編集の元データになります |
| 日本語フォント（任意） | Cowork の環境に日本語フォントが無い場合だけ必要です。例：VideoCreator の `tools\fonts\MPLUS1p-ExtraBold.ttf`（OFL ライセンス） |

### 2. Cowork で頼む

Cowork を開き、**このファイル・動画・.vtt を添付**して、次の1行を送ります。

```text
添付の VideoCreator-Prompt.md の指示どおりに、この動画から動画を作ってください。パターン：おまかせ　作り方：見どころ
```

- パターンは「おまかせ」のほか、`P4` のように番号でも指定できます（下の表）。
- 作り方は「見どころ」（ショート最大3本）か「シリーズ2本」「シリーズ3本」のように指定します。

### 3. 途中の質問に答える

Cowork は次のような **選ぶだけ／はい・いいえ** の質問をすることがあります。

- 「この動画は、あなたが権利を持つ（または使ってよいと許可を得た）ものですか？」
- 「この動画には P6（解説）が合いそうです。P6 で作りますか？」
- 「AI が考えたツッコミ文です。この文を使ってよいですか？」

### 4. 受け取る

できた動画は、Cowork の出力一覧の「すべてダウンロード」（最大50ファイル・500MB）か、OneDrive の Cowork フォルダーから受け取れます。1本ごとに次のファイルができます。

| ファイル | 中身 |
|---|---|
| `…_P4_01_….mp4` | 縦型 1080×1920 の完成動画 |
| `….srt` | 動画の中の発話をすべて書き出した字幕ファイル |
| `…_meta.md` | タイトル案3つ・説明文・ハッシュタグ・選んだ理由（シリーズはチャプター付き） |
| `…_sheet.png` | 動画の6場面を並べた確認用画像 |
| `….editplan.json` / `summary.json` | 編集内容と自動チェックの記録 |

### パターン一覧（選ぶのはこれだけ）

| 番号 | 名前 | 向いている動画 | できあがり |
|---|---|---|---|
| P1 | ハイテンション・チャレンジ | 挑戦・結果・リアクション | 25〜45秒。速いカットとズーム、大きな字幕 |
| P2 | 一発ネタ・ループ | 問いとオチが短く完結する場面 | 15〜25秒。字幕と強調だけ |
| P3 | バラエティ・テロップ | 笑い・掛け合い | 25〜60秒。大きな字幕とツッコミ（あなたが承認した文だけ） |
| **P4** | **切り抜き（おすすめ）** | 配信・対談・講演などの長い話 | 25〜35秒。全文字幕・強調・見出し |
| P5 | ビート同期モンタージュ | — | **この版では作れません**（音楽ライブラリとビート解析が必要）。P8 か P9 を提案します |
| P6 | 解説・講義 | 説明・セミナー | 30〜60秒。要点の強調・見出し |
| P7 | 対談（1カメラ簡略版） | 2人以上の会話 | 30〜60秒。話者ごとに色分けした字幕 |
| P8 | プロセス圧縮 | 料理・DIY などの作業 | 20〜60秒。作業を早送りし、完成を先に見せる |
| P9 | ミニマル／環境音 | 作業風景・自然音 | 30〜90秒。字幕なし、元の音だけ |
| P10 | 画面解説（簡略版） | PC 画面の録画 | 30〜60秒。字幕と見出し |

| 作り方 | 内容 |
|---|---|
| 見どころ | 条件に合う場面から**最大3本**のショート。足りないときは本数と理由を伝えます（水増ししません） |
| シリーズ | 動画全体を2〜20本に分け、前編・中編・後編（4本以上は第n話）にします。各パートに「先出し」「前回のおさらい」「次回予告」「チャプター」が付きます。P5・P8 では使えません |

---

## Cowork への指示（ここから下は Cowork が読む）

### 0. あなたの役割

あなたは、ローカルアプリ VideoCreator の編集手順を Microsoft 365 の中で再現する編集担当です。役割を次のように分けます。

- **あなた（AI）が決めること**：パターンの提案、使う文の範囲（文番号）、先出しの一文、見出し（16字以内）、強調語（最大3語）、チャプター名、タイトル案、選定理由とスコア。
- **付録A の `vc_tool.py` が決めること**：文の境目への合わせ込み、無音カット、縦型化、字幕の分割と表示時間、音量（-14 LUFS / True Peak -1.5 dBTP）、レンダリング、自動チェック（QA）。

自分で FFmpeg のコマンドを組み立てて動画を作らないでください。`vc_tool.py` を使わないと、字幕が話した言葉どおりか・音量や安全領域が基準どおりかを測る根拠がなくなります。

### 1. 必ず守ること

1. **利用者には「選ぶ」か「はい／いいえ」で答えられる質問だけ**をする。秒数・dB・文字数などの数値を尋ねたり、変更を勧めたりしない。値は付録A の `PRESETS` に固定されている。質問はまとめて、全体で3回以内にする。
2. **字幕は話した言葉どおり**。言い換え・要約・言いよどみや繰り返しの削除をしない（`vc_tool.py` は書き起こしの文をそのまま字幕にし、V-01 で逐語一致を確かめる）。直してよいのは、利用者が指摘した書き起こしの誤認識だけ。
3. **見出し・強調語・タイトルの数字と固有名詞は、書き起こしの表記のまま**書く。書き起こしが「一万円」なら「1万円」と書かない（H-07 で不合格になる）。強調語は、その動画で使う文の中に**そのまま現れる語**だけにする。
4. **本数を水増ししない**。条件を満たす場面が足りなければ、作れた本数と理由を伝える。
5. **実行していない処理を「やった」と報告しない**。`summary.json` で `hard_pass` が `false` の動画は納品せず、理由を伝える。
6. **権利の確認（H-09）**：素材を利用者が権利を持つ、または許諾を得ているかを1回だけ確認する。「いいえ」なら作業を止める。
7. **ツッコミ（P3）は AI の文案**。14字以内で提案し、利用者が読んで承認した文だけ `approved: true` にする。承認されなければ使わない。
8. 素材を外部サービスへ送らない。`pip install` やインターネットからのダウンロードを、利用者の許可なく行わない。

### 2. 手順

#### Step 1 — 道具の準備と環境の確認

1. このファイル（添付の `VideoCreator-Prompt.md`、またはスキルとして保存された `SKILL.md`）から、付録A のコードを**プログラムで**取り出し、SHA-256 を照合する。手で書き写さない。

   ```python
   import hashlib, pathlib, re
   md = pathlib.Path("VideoCreator-Prompt.md").read_text(encoding="utf-8").replace("\r\n", "\n")  # 実際のパスに置き換える
   code = re.search(r"<!-- vc_tool\.py:begin -->\n```python\n(.*?)\n```\n<!-- vc_tool\.py:end -->", md, re.S).group(1) + "\n"
   pathlib.Path("vc_tool.py").write_text(code, encoding="utf-8", newline="\n")
   print(hashlib.sha256(code.encode("utf-8")).hexdigest())  # => 1db6a6458d0b1532911130d734deece79e9e09bdd6d0add9ea72e811a49817cf
   ```

   値が `1db6a6458d0b1532911130d734deece79e9e09bdd6d0add9ea72e811a49817cf` と一致しなければ、そこで止めて利用者に「このファイルを添付し直してください」と伝える。このファイルがチャットに貼り付けられていて添付が無い場合も、同じように添付を頼む。

2. `python vc_tool.py env` を実行する。出力の意味：
   - `ok: true` … 続行する。
   - `ok: false` … §3 のフォールバックへ進む。
   - `font: null` … 日本語フォントが無い。利用者に「日本語フォントのファイル（.ttf / .otf）を1つ添付できますか？」と1回だけ聞く。添付されたら以後のコマンドに `--font <パス>` を付ける。無ければ字幕を焼き込まずに進め（SRT は出る）、その旨を報告する。

#### Step 2 — 素材を読み込む

```bash
python vc_tool.py prepare <動画> --transcript <書き起こし.vtt> --work vc_work
```

- 書き起こしは .vtt / .srt / JSON（`start`・`end`・`text`・任意で `speaker`）に対応する。添付が無ければ `--transcript` を省く。環境に faster-whisper があれば自動で書き起こし、無ければ終了コード 4 で止まる。その場合は利用者に .vtt を頼む（使い方 §1 の説明を短く伝える）。
- `PROBLEMS:` が出たら、その内容を利用者に伝える（音声が無い、10秒未満、3時間超など）。
- 書き起こしの時刻が動画より長い場合はエラーになる。別の動画の書き起こしではないか確認する。
- 生成物：`vc_work/transcript.txt`（`[文番号] 開始-終了 話者: 文 (loud 最大音量, pause 間)`）、`sentences.json`、`signals.json`（無音・ショット境界・音量の山）、`probe.json`。

#### Step 3 — パターンと作り方を決める

- 利用者の指定があれば、それに従う。P5 が指定されたら、作れない理由を伝えて P8 か P9 を提案する。
- 「おまかせ」なら、`transcript.txt` と §4 の「向いている動画」を照らし合わせて1つ選び、理由を1文添えて「この P◯ で作りますか？」と確認する。迷ったら P4。
- 作り方の指定が無ければ「見どころ」。

#### Step 4 — 生の素材を自分で読む

- `vc_work/transcript.txt` を**最初から最後まで**読む。要約だけに頼らない。長い場合は約10分ずつ読み、区間ごとに候補をメモしてから、最後に全体で比べ直す。
- `signals.json` の `loud_peaks`（音量の山。笑い・歓声・強調の手がかり）と、`transcript.txt` の `pause`（話の切れ目の手がかり）を参考にする。
- P8・P9・P10、または発話が少ない動画では、`python vc_tool.py frames <動画> --every 10 --work vc_work` で作った画像（`frames_*.png`、10秒ごとの縮小画像。時刻は `frames.json`）を見て場面を把握する。

#### Step 5a — 見どころを選ぶ（作り方＝見どころ）

各候補について、次をすべて満たすものを、良い順に**最大3つ**選ぶ。

1. §4 にあるそのパターンの選定ルールに合う。
2. 前提知識なしで意味が通る。最初の文が指示語（それ・あれ・これ・その・あの・この・そこ・ここ・そう）で始まらない。
3. 最後にオチや結論（ペイオフ）がある。
4. 候補の前後30秒も読み、切り出すと発言の意図が変わる（否定・皮肉・冗談の前提が抜ける）場合は除外する。
5. 候補どうしの場面が重ならない。

候補ごとに決めること：

- `body`：使う文の範囲 `[[開始文番号, 終了文番号]]`（両端を含む）。複数の範囲を時系列順に並べてもよい（同じ場面を重ねない）。
- `teaser`：先出し（コールドオープン）が有効なパターン（P1・P3・P4・P7・P8）では、候補の中で最も強い一文（結果・驚き・強い主張・問い）の文番号。`body` の最初の文と同じなら `null`。ほかのパターンでは書かない。
- `headline`（16字以内）、`emphasis`（最大3語）、`scores`（`hook`・`standalone`・`payoff`・`emotion`、各0〜10）、`reason`（1文）、`titles`（3つ）、`description`、`hashtags`。
- P8 で作業を早送りする区間は、`body` に `{"t0": 開始秒, "t1": 終了秒, "speed": 2〜8}` を入れる（字幕は付かない）。時刻は `frames.json` と `signals.json` の `shots` を見て決める。

尺は `vc_tool.py` が実際の無音カット後の長さで判定する。範囲外ならエラーで「約◯秒減らして／足して」と教えてくれるので、文を足し引きして合わせる。

#### Step 5b — シリーズに分ける（作り方＝シリーズN本）

1. `transcript.txt` を読み、**話題が終わる文の番号**（その文の直後に話題が変わる位置）を列挙する。
2. 分割する：

   ```bash
   python vc_tool.py split --preset P4 --parts N --prefer 35,69,103 --work vc_work
   ```

3. 結果の `boundary_after.warning` が出た境目は、話題の途中で切れている。`--parts` を N-1 と N+1 でも試し、全部の境目が話題の切れ目に合う本数があれば、利用者に「N本だと話の途中で切れます。M本なら話の切れ目で分けられます。どちらにしますか？」と聞く。
4. 1本あたりが編集後1分未満になる場合はエラーになる。そのときは「見どころ」を提案する。
5. 各パートの `body` は `split` の結果を**そのまま**使う（全文を重なりなく覆う必要がある）。各パートに `teaser`（そのパートの中で最も強い一文。必須）、`headline`（【前編】などのラベルはツールが付ける）、`chapters`（`[文番号, "章名"]` を3つ以上。最初はパートの先頭の文、それぞれ10秒以上離す）を決める。「前回のおさらい」と「次回予告」はツールが自動で付ける。

#### Step 6 — plan.json を書いて書き出す

```json
{
  "preset": "P4",
  "mode": "highlights",
  "project": "動画の名前（ファイル名に使う）",
  "source": "<prepare に渡した動画のパス>",
  "rights_confirmed": true,
  "videos": [
    {
      "headline": "一時間で一万円使い切れ",
      "teaser": 3,
      "body": [[2, 13]],
      "emphasis": ["一万円", "百円"],
      "reason": "冒頭で無茶なルールが示され、ツッコミで終わる",
      "scores": {"hook": 8, "standalone": 8, "payoff": 6, "emotion": 6},
      "titles": ["タイトル案1", "タイトル案2", "タイトル案3"],
      "description": "説明文",
      "hashtags": ["#タグ"]
    }
  ],
  "tsukkomi": [{"video": 0, "after": 31, "text": "14字以内", "approved": true}]
}
```

- シリーズのときは `"mode": "series"` にし、各 video に `"chapters": [[0, "章名"], [30, "章名"], [62, "章名"]]` を入れる。
- `tsukkomi` は P3 だけ。承認されたものだけを入れる。

```bash
python vc_tool.py build plan.json --work vc_work --out vc_out
```

- 終了コード 2：plan の誤り。エラーの指示どおりに直して再実行する（尺の過不足、重なり、16字超の見出し、未承認のツッコミなど）。
- 終了コード 3：書き出したが、ハード項目に不合格がある。`summary.json` と `*.editplan.json` の `qa.results` を読み、plan で直せるもの（H-01 尺、H-05 長すぎるテロップ、H-07 表記）を直して再実行する。直せないものは理由を報告する。
- 同じ指摘で3回失敗したら止めて報告する。長い動画は `--only 1` のように1本ずつ書き出してよい。

#### Step 7 — 自分の目で確かめる

自動チェックの合格は「測れる基準を満たした」という意味で、内容の良さまでは保証しない。納品の前に次を確かめる。

- 各 `*_sheet.png` を見る：字幕が画面下部の中央にあって欠けていないか、見出しが上部にあるか、黒い画面や文字化け（□）が無いか。
- 各 `.srt` を通して読む：先出し → 本編の順で意味が通るか、冒頭が唐突でないか、オチで終わっているか。
- 問題があれば plan を直して再実行する。

#### Step 8 — 納品と報告

1. `vc_out` の全ファイルを出力として渡す（OneDrive の Cowork フォルダーにも保存される）。
2. 次の形で報告する。専門用語は避け、警告はやさしい言葉で言い換える（§5）。

   ```markdown
   ## できた動画（パターン：P4 切り抜き／見どころ）
   | # | ファイル | 長さ | 見出し | 選んだ理由 | 自動チェック |
   |---|---|---|---|---|---|
   | 1 | …_P4_01_….mp4 | 33秒 | 一時間で一万円使い切れ | … | 合格（注意：話すのが速い字幕が11か所） |

   ## 作れなかったもの・注意
   - …

   ## 次にできること
   - 別の場面で作り直す ／ シリーズにする ／ 別のパターンで作る
   ```

3. 投稿時の注意を一言添える：SRT を投稿先で表示させると、焼き込み字幕と二重になる。3分を超える動画は YouTube ではショートにならない。この編集は写実的な AI 生成映像を加えていないが、ツッコミ文は AI の文案である。

### 3. 環境が足りないとき

| 状況 | 対応 |
|---|---|
| Python または ffmpeg が無い（`env` が `ok: false`） | 最初に「この環境では動画ファイルを作れません」と伝える。書き起こしがあれば Step 3〜5 の選定まで行い、①編集指示書（Word：候補ごとの開始・終了時刻、先出し、見出し、理由）、②候補ごとの SRT、③ `plan.json` と `vc_tool.py` を納品する。Python と FFmpeg のある PC（VideoCreator の環境など）で `python vc_tool.py prepare …` と `python vc_tool.py build plan.json` を実行すれば同じ動画ができることを案内する |
| 書き起こしが無く、faster-whisper も無い | .vtt を頼む。得られなければ作業を止める。音声を聞かずに場面を推測して作らない |
| 日本語フォントが無い | Step 1 のとおり1回だけ頼む。無ければ字幕・見出しを焼き込まず、SRT を納品する |
| 動画が 200MB 以上で添付できない | 「Clipchamp で動画を開き、720p で書き出すと小さくなります。必要なら使いたい部分だけを残してください」と案内する |
| P5 を指定された | 作れない理由を伝え、P8 か P9 を提案する |

### 4. パターンの選定ルール（付録A の `PRESETS` と同じ値）

| ID | 向いている動画 | 選定ルール | 尺 | 先出し | Cowork 版での違い |
|---|---|---|---|---|---|
| P1 | 挑戦・結果・リアクション | 結果・賞金・驚きの瞬間を1.5秒以内に見せられる、挑戦から結果までのある区間 | 25〜45秒 | あり | 効果音・数字カウンター・BGM は付かない |
| P2 | 問いとオチが短い場面 | 1つの問い（または驚き）と答え（オチ）だけで完結する。最後の文が冒頭の問いに答え、繰り返し見たくなるもの | 15〜25秒 | なし | ループのつながりは AI の判断で選ぶ |
| P3 | 笑い・掛け合い | 笑いやリアクションの瞬間を含み、ツッコミで間を作れる区間 | 25〜60秒 | あり | ツッコミは重ねテロップのみ（停止カード・効果音は無い）。承認制 |
| P4 | 長い話全般（既定） | 単体で意味が通り、冒頭に結果・驚き・強い主張・問いがあり、最後にオチがある区間 | 25〜35秒 | あり | アプリと同じ |
| P5 | — | — | — | — | 作れない |
| P6 | 説明・講義 | 1つの問いや主張に、理由や具体例を含む答えが完結している区間。数字・固有名詞は書き起こしのとおり | 30〜60秒 | なし | BGM は付かない |
| P7 | 2人以上の会話 | 最も強い発言を先出しでき、掛け合いが完結している区間 | 30〜60秒 | あり | カメラ切り替えは無い（1本の動画）。話者色の字幕で代用 |
| P8 | 料理・DIY などの工程 | 長い工程を早送りで圧縮し、完成の場面を先に見せる | 20〜60秒 | あり | 早送りの区間は AI が画像を見て指定する |
| P9 | 作業風景・環境音 | テロップや音楽に頼らず、映像と環境音だけで没入できる、作業の核心や完成に近い連続した場面 | 30〜90秒 | なし | 無音カットをしない（アプリと同じ） |
| P10 | PC 画面の録画 | 1つの操作や手順が、目的 → 操作 → 結果まで完結している区間。操作を説明している発話を優先 | 30〜60秒 | なし | 画面の自動ズームと話し手のワイプは無い |

共通の値（アプリの `presets/*.json` と同じ）：出力 1080×1920・H.264・AAC 48kHz、積分ラウドネス -14 LUFS・True Peak -1.5 dBTP（2パス）、16:9 の素材はぼかした背景の上に置く、字幕は1行・画面の下から620px・右160px・左60pxの内側、1行の文字数は P1/P3 が10字・ほかは14字（はみ出す場合は文字を小さくする）、見出しは上から300px。

### 5. 自動チェックの意味（報告での言い換え）

| ID | 区分 | 内容 | 利用者への言い方の例 |
|---|---|---|---|
| H-01 | 必須 | 尺がパターンの範囲内（ショートは180秒以下） | 長さが基準外です |
| H-02 | 必須 | 1080×1920 | 画面サイズ |
| H-03 / H-04 | 必須 | -14±1 LUFS、True Peak -1.0 dBTP 以下（書き出しとは別の測定器で測る） | 音量 |
| H-05 | 必須 | 字幕が投稿アプリのボタンに隠れない位置にある | 字幕の位置 |
| H-06 | 必須 | 言葉の途中で切っていない | 言葉の途中のカット |
| H-07 | 必須 | 見出し・強調語の数字や固有名詞が書き起こしにある | 見出しの表記 |
| H-08 | 必須 | 承認していないツッコミが無い | ツッコミの承認 |
| H-09 | 必須 | 素材の権利を確認した | 権利の確認 |
| V-01 | 必須 | 字幕が話した言葉どおり | 字幕の正確さ |
| S-01 | 注意 | 字幕が1秒6文字以下（速い話し方だと超える。字幕は削らない） | 話すのが速く、字幕を読み切れない所があります |
| S-02 / S-08 | 注意 | 1行の文字数、行頭・行末の禁則 | 字幕の改行 |
| S-03 | 注意 | 2秒以内に話し始める | 出だしがゆっくりです |
| S-04 | 注意 | 短すぎるカットが無い | カットが細かすぎる所があります |
| S-06 | 注意 | 0.5秒以上の黒い画面・止まった映像が無い（元の映像が止まっている場合も出る） | 映像が止まって見える所があります |
| S-07 | 注意 | 最初の文が「それ」などで始まらない | 出だしが唐突かもしれません |
| S-09 | 注意 | 見出しが元の発言の否定を落としていない | 見出しの意味が反対になっていないか確認してください |
| S-11 / S-14 | 注意 | シリーズ：60分以下、YouTube のチャプター条件（00:00 から・3つ以上・各10秒以上） | チャプター |

---

## 付録A — vc_tool.py

標準ライブラリと FFmpeg だけで動きます（Python 3.8 以上を想定。検証は 3.12）。FFmpeg は `PATH`、環境変数 `VC_FFMPEG`、または Python パッケージ `imageio-ffmpeg` から探します。ffprobe が無くても動きます。

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

## 付録B — このプロンプトの設計根拠と検証

### 依頼を「より深く」するための4つの問い

| 問い | 答え |
|---|---|
| (1) トップの専門家なら、頼まれていなくても何をするか | Cowork の実行環境（Python・FFmpeg・日本語フォント・添付200MB）を最初に確かめ、足りなければ正直に代替手段へ切り替える。AI の判断（場面選び）と決定的な処理（切り出し・字幕・音量・検査）を分け、同じ plan から同じ動画ができるようにする。字幕の逐語性、数字・固有名詞の照合、安全領域、ラウドネス、言葉の途中のカットを機械的に検査する。本数を水増ししない。権利とAI文案の承認を取る。アプリと違う点（効果音・BGM・カメラ切替・P5）を隠さずに書く |
| (2) 専門家が出発点にする最も生の素材 | 加工されていない元の動画と、時刻付きの書き起こし（Stream / Teams の .vtt）。その上に、FFmpeg で元の音声と映像から直接測った信号（無音区間、ショット境界、100ms ごとの音量）。仕様は要約資料ではなく、アプリの `presets/P1〜P10.json`、`docs/PRD.md`（§7 機能要件・§13 自動QA）、`src/VideoCreator.Core/Rendering/Renderer.cs`・`AssWriter.cs`（ぼかし背景、字幕スタイル、2パス loudnorm）と、Cowork の公式ドキュメント（対応ファイル形式・200MB・50ファイル/500MB・スキルの仕様） |
| (3) 具体的な手法とツール | 文番号で区間を指定し、文の境目に合わせる（言葉の途中で切らない）。silencedetect による無音のジャンプカットと最短ショット長の保証。scdet・blackdetect・ebur128 による信号抽出。動的計画法による前後編の分割（長さの均等さ＋話題の切れ目）。ぼかし背景の縦型化とパンチイン。ASS 字幕（禁則・幅に合わせた文字の縮小）。2パス loudnorm。QA は ffprobe 相当の測定、書き出しとは別の ebur128 測定、blackdetect / freezedetect、レイアウト計算、逐語一致の照合 |
| (4) あなたや私以外に、結果が正しいことを示すもの | 測定器：書き出しとは別の EBU R128 メーター（ebur128）と ffprobe。独立した音声認識：完成した MP4 を Azure AI Speech で書き起こし直し、意図した発話が実際に聞こえるか・字幕が音声と同じ時刻に出るかを照合する。台本（正解データ）のあるテスト動画。参照実装である VideoCreator アプリ自身の QA 基準。最終的には YouTube Studio などの投稿先がショートとして受け付けるか |

### 書き直した依頼

> VideoCreator アプリの処理（書き起こし → 見どころ選定 → 文の境目への合わせ込みと無音カット → 9:16 化 → 逐語字幕・見出し・強調 → 2パス音量正規化 → 自動QA）を、Microsoft 365 Copilot Cowork で再現するプロンプトを `/work/VideoCreator-Prompt.md` に作る。利用者が選ぶのは動画・パターン（P1〜P10 か、おまかせ）・作り方（見どころ最大3本 か シリーズN本）だけで、数値はアプリのプリセットに固定する。Cowork の公式仕様（添付200MB、出力50ファイル/500MB、ローカルファイル不可、スキル）と、実行環境が保証されないことを前提に、環境の確認と代替手段を組み込む。AI の判断と決定的な処理を分け、決定的な処理は検証済みのスクリプトとして同梱する。アプリの QA 項目（H-01〜H-11、S-01〜S-14）のうち測れるものを実装し、アプリと異なる点を明記する。完成したら、台本付きのテスト動画で実際に動かし、ebur128・ffprobe と Azure AI Speech による書き起こし直しで、出力が正しいことを第三者の測定で確かめる。

### 検証記録（2026-11-10、Windows・Python 3.12・FFmpeg 2026-11 版）

| 試験 | 素材 | 結果 |
|---|---|---|
| P4 見どころ2本 | `spikes/media/talk.mp4`（125秒、1920×1080、2話者、台本付き）＋台本から作った .vtt | 33.2秒・25.2秒。-14.0 LUFS、True Peak -1.5 dBTP。ハード項目すべて合格 |
| 第三者による内容確認 | 上の2本を Azure AI Speech で書き起こし直し | 意図した発話の文字バイグラム再現率 100% / 96.2%。字幕の100%が、同じ時刻（±1秒）の音声で聞こえる。先出しが冒頭0.1〜0.2秒で聞こえる |
| シリーズ2本 | `spikes/media/long30.mp4` の先頭8分（1280×720、5企画）＋アプリの書き起こしから作った .vtt | 分け目が話題の切れ目（「続いての企画は…」の直前）と一致。190.6秒・214.6秒、-14.0 LUFS。チャプター合格。書き起こし直しで、先出し・おさらい・次回予告がすべて計画の位置で聞こえ（再現率100%）、本編の再現率 99.1% / 97.7%、字幕同期 100% / 98.4% |
| シリーズ3本 | 同上 | 5企画を3本に均等に分けられないため、境目1つに「話題の切れ目ではありません」の警告が出ることを確認 |
| P2・P3・P7・P8・P9 | talk.mp4 | すべて書き出し・ハード項目合格。P3 は未承認のツッコミがあると描画前に停止、承認後は白い箱で表示。P7 は話者ごとに縁取りの色が変わる |
| 拒否・誤り | talk.mp4 | 見出しの「1万円」（書き起こしは「一万円」）を H-07 で検出。尺不足・区間の重なり・P5・権利未確認・シリーズ1本1分未満を、直し方つきのエラーで停止 |
| ffprobe の無い環境 | ffmpeg 単体を `VC_FFMPEG` で指定 | 長さ・解像度・fps・音声の判定と書き出しが同じ結果 |

検証で見つけて直した不具合：無音カットに字幕の一部が落ちる（逐語性の違反）→ 字幕の時刻を出力側の区間で配分する方式に変更。強調語の拡大を安全領域の幅計算に入れていなかった → 計算に追加。シリーズの長さの見積もりが無音カットを反映していなかった → 無音区間から見積もる方式に変更。構成順を「先出し → おさらい → 本編 → 次回予告」に修正。

### まだ確かめていないこと

- **Copilot Cowork の実環境での実行**。Cowork の実行環境に Python・FFmpeg・日本語フォントがあるかは公式ドキュメントに書かれていません（公開されているコミュニティ製スキルには、Python と FFmpeg／imageio-ffmpeg を使うものがあります）。そのため、Step 1 で必ず `env` を実行し、足りない場合は §3 の代替手段に切り替えます。
- Stream が生成する .vtt の区切り方（句読点の有無など）は素材によって異なります。句点の無い区切りは、そのまま1文として扱います。
