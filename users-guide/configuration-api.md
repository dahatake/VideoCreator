# 設定・APIリファレンス

<!-- language-nav -->
[日本語](configuration-api.md) | [English](en/configuration-api.md) | [한국어](ko/configuration-api.md) | [简体中文](zh-CN/configuration-api.md)
<!-- /language-nav -->

[はじめに](../README.md) / [ガイド目次](README.md) / [詳細概要](overview.md) / [エンジニア向け](technical-reference.md)

[アプリREADME](../README.md) / [ガイド目次](README.md) / [クイックスタート](quickstart.md) / [画面](screens.md) / [困ったとき](troubleshooting.md) / [構成・拡張](technical-reference.md) / [検証と保守](verification.md)

## 起動設定

UIの表示言語はブラウザーの言語切り替えで保存します。動画の音声言語はプロジェクトごとに指定します。`POST /api/projects` は `{"name":"Example","language":"en-US"}`、`PATCH /api/projects/{id}` は `{"language":"ko-KR"}` のように指定できます。対応値は `ja-JP`（省略時の既定）・`en-US`・`zh-CN`・`ko-KR`。非対応値は422、生成・書き出し中の言語変更は409です。変更後は新しい候補を生成してください。既存の編集計画は作成時の言語を保持します。

[appsettings.json](../src/VideoCreator.App/appsettings.json)の `VideoCreator` セクション、環境変数、コマンドラインで設定します。環境変数は `VideoCreator__Asr__Endpoint` のように区切りを `__` にします。下表は[AppOptions](../src/VideoCreator.App/AppOptions.cs)の既定です。配布ファイルの具体的なエンドポイントは自分の値へ変更してください。

| 設定 | 既定・意味 |
|---|---|
| Port / OpenBrowser | `8765` / `true`。loopbackの待受と起動後のブラウザー表示 |
| DataDir | 未指定なら `%LOCALAPPDATA%\VideoCreator`。DBとキャッシュ |
| ProjectsDir | 未指定ならWindowsのMyVideos内 `VideoCreator`。新規プロジェクト用。画面で保存した値が優先 |
| FfmpegBin / FontsDir / PresetsDir | 未指定ならリポジトリ上位のtools等を探索 |
| FaceModel / UpscaleModel | 未指定ならtoolsのUltraFace / Real-ESRGANモデル |
| LocalOnly | `false`。`true`ならLLMを使わないことを固定。ASRは停止しない |
| Asr.Provider / Endpoint / Locale | `azure` / 空 / `ja-JP`。ProviderがazureでEndpointが設定されているとASR有効。アプリからの生成ではプロジェクトの `language` が Locale より優先されます |
| Asr.TenantId / Llm.TenantId | 未指定ならAzure Identityの既定。Entra IDで使うテナント。画面に保存した各接続の設定が優先 |
| Llm.Provider | `azure-openai`。`azure-openai` / `copilot` / `none` |
| Llm.Endpoint / Deployment | 空 / `gpt-chat-latest`。自分のデプロイ名を設定 |
| Llm.CopilotCliPath / CopilotModel | 未指定ならCLI検出／既定モデル |
| Llm.SingleCallCharBudget | `12000`。標準の分割判断用文字量。トークン上限そのものではない |
| Dictionary | `[]`。ASRのフレーズリストと未出現語チェックに使う固有名詞 |

例：別ポートで起動し、AI選定を使わない場合（Speechの設定は別途必要）。

```powershell
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
dotnet run --project src\VideoCreator.App -c Release
```

環境変数はそのPowerShellと子プロセスに適用されます。次のターミナルでは設定し直します。ASRも無効にして画面だけ確認する場合は `VideoCreator__Asr__Provider=none` を指定しますが、字幕生成の通常利用ではありません。

## 設定の寿命

- MAI-Transcribe-2 は個別のモデルデプロイではなく、[対応リージョンの Speech リソース](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe)から利用します。設定の「Azure Speech」で個別の接続を選び、書き起こしモデルとリソースを選択して「接続テスト」「テストして保存」を実行します。モデルも接続と一緒に保存・復元され、Foundry のチャット接続は変わりません。Speech のテスト・保存 API は任意の `model`（`MAI-Transcribe-2` / `standard`）を受け付け、省略時は保存済みの選択、未保存なら `Asr.Model` を使います。非対応リージョンでは標準モデルへ切り替わるため、テスト結果のモデル名を確認してください。下書きのモデル選択は適用されません。従量課金で、Azure の予算アラートは利用額のハード上限ではありません。
- LLM方式・Speech／Foundry／Copilotの画面設定は `DataDir/ai-settings.json` に保存され、再起動後も起動設定より優先。接続情報はテスト合格後に保存。保存した接続を削除すると起動設定へ戻ります。キー・トークンはWindows DPAPIで暗号化し、非対応OSでは保存を拒否します。
- プロジェクト保存先の画面変更はStoreのsettingに永続化。起動設定より優先。
- パターン・生成オプションの画面選択はプロジェクトごとにStoreへ保存し、再起動後も復元。生成時にはJob.Optionsにも記録。
- ワークフローの最終表示段階と対象ジョブ・編集計画・書き出しはProject.WorkflowStateに保存。参照先が無効な場合は前段階へ戻る。
- BrandKitは共通Store、BGMはライブラリまたはプロジェクト、画像生成の作る／作らないはProject。
- プリセットは起動時にロード。ファイルを編集したら再起動。

## HTTP契約

- 既定のbase URL：`http://127.0.0.1:8765`。以下のルートにはすべて `/api` を付けます。
- JSONは `snake_case`。enumも文字列。共有定義は[Json](../src/VideoCreator.Core/Json.cs)と[Models](../src/VideoCreator.Core/Models/Models.cs)。
- 書き込み系は `X-VideoCreator: 1` が必須。JSONは `Content-Type: application/json`。
- GET/HEADにはこのヘッダーを要求しません。ヘッダーは認証やAPIキーではなく、クロスサイト書き込みを防ぐガードです。
- 通常のエラーは `{ "error": { "code": "...", "message": "...", "detail": ... } }`。HTTP statusとcodeの両方を扱う。Host拒否やヘッダー不足はプレーンテキストの場合もあります。
- 非同期書き出しの `202` は受付であり、完了ではありません。返されたrender IDをポーリングします。
- 一般の要求上限は64MiB。**動画uploadルートは独自の上限を解除**し、multipartを一時バッファしてコピーします。BGMはHTTP bodyを2000MiBへ変更します（1回で最大20曲）が、multipart解析にも別の上限があります。他ルートに動画と同じ無制限条件を適用しないでください。

## ルート一覧

完全なハンドラーと要求recordは[Api.cs](../src/VideoCreator.App/Api.cs)が一次資料です。下表の `{id}` は各リソースのIDです。

| 分野 | Method・ルート | 内容 |
|---|---|---|
| 診断 | GET `/health` | FFmpeg・不足フィルタ・ASR・LLM・モデル等。設定・検出でありクラウド疎通を保証しない |
| 設定 | GET / PUT `/settings` | 読取／`{"llm_provider":"none"}` 等でLLM切替 |
| Speech接続 | POST `/settings/ai/speech/test` | `{"endpoint":"https://<resource>.cognitiveservices.azure.com/","auth":"entra","tenant_id":"<tenant-guid>"}`。`auth` は `entra` / `key`。キー方式は `api_key`。合成無音によるテストのみ（保存しない） |
| Speech接続 | PUT / DELETE `/settings/ai/speech` | PUTは同じ要求形式で再テストし、合格時のみ保存。不合格は422。DELETEは保存した接続・キーを削除して起動設定へ戻す |
| Foundry接続 | POST `/settings/ai/foundry/test`、PUT / DELETE `/settings/ai/foundry` | Speechと同じ認証欄に `deployment`・任意の `embedding_deployment` を追加。チャットと指定時の埋め込みを検証。LLM不使用固定ではテスト・保存不可 |
| Azureリソース一覧 | POST `/settings/ai/azure/resources` | `{"tenant_id":"<tenant-guid>"}`（省略可）。サインイン中の Entra ID で ARM を読み、`subscriptions`・`accounts`（`endpoint`・`speech`・`foundry`・`key_auth_disabled`・`custom_domain`・`can_transcribe`/`can_chat`/`can_embed`。権限を読めなければ `null`）・`issues`（読めなかったサブスクリプション）を返す。キーは読まない。失敗は422（`arm_unauthorized`/`arm_forbidden`/`entra_signin` 等） |
| 設定の下書き | GET `/settings/drafts`／PUT・DELETE `/settings/drafts/{speech\|foundry\|copilot\|projects_dir}` | 設定画面の未適用の入力。GETは下書きのある節だけを返す。PUTは文字列を前後空白除去・空はnull・2048字超は422、範囲外の列挙値は捨てて保存し `updated_at` を付けて返す。キー・トークンの欄は持たない。DELETEは204（不明な節は404）。下書きは接続に使われず、保存・削除・保存先の変更が成功すると消える |
| Azureデプロイ一覧 | POST `/settings/ai/azure/deployments` | `{"tenant_id":"…","account_id":"/subscriptions/…/providers/Microsoft.CognitiveServices/accounts/<name>"}`。`deployments`（`name`・`model`・`chat`・`embeddings`・`provisioning_state`）を返す。Cognitive Services 以外のIDは送信前に422（`invalid_input`） |
| 保存先 | GET `/settings/projects-dir` | path、source、configured_path、件数、移動進捗 |
| 保存先 | POST `/settings/projects-dir/plan` | `{"path":"D:\\Videos\\VideoCreator","move":true}` の事前確認 |
| 保存先 | PUT `/settings/projects-dir` | 同形式で実際に変更／移動 |
| パターン | GET `/presets` | 起動時ロード済みプリセット |
| Project | GET / POST `/projects` | 一覧／`{"name":"作業名"}` で作成 |
| Project | GET / PATCH / DELETE `/projects/{id}` | 詳細（`resume_state` に再開段階と対象IDを返す）／`{"name":"新しい名前"}`／削除 |
| Project | PUT `/projects/{id}/generation-settings` | パターンと生成オプションをプロジェクトに保存 |
| Project | PUT `/projects/{id}/workflow-state` | 現在の段階（`project` / `settings` / `job` / `candidates` / `exports` / `review` / `render`）を保存。`exports` は `job_id` 必須で、Job が `ready` / `done` のときだけ |
| Asset | POST `/projects/{id}/assets` | `{"path":"C:\\Videos\\talk.mp4"}`。コピーしない |
| Asset | POST `/projects/{id}/assets/upload` | multipartの `file` フィールドで動画をコピー |
| Rights | POST `/projects/{id}/rights` | `{"asset_id":"...","confirmed":true}` |
| 見積もり | GET `/projects/{id}/estimate?preset=P4` | scope別の送信量・単価ベースの推定 |
| 画面構成 | GET `/projects/{id}/layouts` | パターンごとの画面構成の図データの配列 `[{preset_id, planner, width, height, captions, beats:[{kind, sec, speaker, boxes:[{slot, asset_id, shape, x, y, w, h, backdrop}]}], issues, face_basis}]`。位置は生成と同じ計算（現在の役割・選択シーン・解析済みの顔）、`sec` は代表値。`asset_id` が null の枠は未登録の素材の例示。`issues` は生成できない・映らない組み合わせ（`need_screen`・`no_scenes`・`many_screens`・`only_main`・`auto_screen`・`auto_speaker`・`one_camera`・`implicit_main`・`no_place`） |
| Asset | PATCH `/assets/{id}` | role、speaker、sync_offset、clear_speaker |
| Transcript | GET / PATCH `/assets/{id}/transcript` | 読取／`{"edits":[{"word_id":"...","text":"修正文"}]}` |
| 音 | GET `/bgm?project_id=...` | ライブラリ、プロジェクトの曲、使う設定ならアプリ共通の曲（使わない設定でも、編集計画で使っている共通曲は含む）。各曲の `analysis`（lufs, lra, start, end, bpm, onset_rate, brightness, energy, short_term） |
| 音 | POST `/projects/{id}/bgm` | multipart。file（複数可、最大20）、権利確認 `rights_confirmed`。登録時に曲を測定。201 `{tracks, skipped}`（読めない・5秒未満の曲は skipped に理由） |
| 音 | DELETE `/projects/{id}/bgm/{bgmId}` | 登録を外す（ファイルも削除）。編集計画で使っていると 409 `E-BGM-IN-USE` |
| 音 | GET `/bgm/shared` | アプリ共通の曲（`source: "shared"`）。プロジェクトの生成設定 `use_shared_bgm`（標準 true）が true のとき、そのプロジェクトの `/bgm?project_id=...` にも含まれる（同じファイル名はプロジェクトの曲を優先） |
| 音 | POST `/bgm/shared` | multipart。`/projects/{id}/bgm` と同じ形式で、アプリのデータフォルダーの `bgm` に保存 |
| 音 | DELETE `/bgm/shared/{bgmId}` | アプリ共通から外す（ファイルも削除）。どのプロジェクトでも編集計画で使っていると 409 `E-BGM-IN-USE` |
| 音 | POST `/projects/{id}/bgm/{bgmId}/share` | プロジェクトの曲をアプリ共通にする（同じ id のまま、ファイルをアプリのデータフォルダーへ移動）。同じ名前の共通曲があると 409 `E-BGM-EXISTS` |
| OS | POST `/dialogs/folder` | `{"initial_path":"...","title":"..."}`（`title` は省略可、ダイアログの見出し）。ダイアログは最前面に表示。取消は `{"path":null}` |
| カード | POST `/projects/{id}/scene-cards` | asset_id **か** brand_path、count 2〜6（既定4）。両方指定不可 |
| カード | PUT `/projects/{id}/scene-cards/settings` | `{"make":true}`。以後の生成で画像を使用 |
| カード | GET `/projects/{id}/scene-cards/{index}` | 抽出画像（0始まり） |
| BrandKit | GET / PUT / DELETE `/brandkit` | 共通登録。PUTはpaths、rights_confirmed、card_sec、upscale |
| BrandKit | POST `/brandkit/enhance` | 登録済みの高画質化を開始 |
| 生成 | POST `/projects/{id}/jobs` | preset_ids、instruction、options |
| Job | GET `/jobs/{id}` | 状態、候補ID、警告等 |
| Job | POST `/jobs/{id}/cancel` | 待機／実行中の生成取消 |
| Job | GET `/jobs/{id}/events` | SSE（EventSource）、stage・progress等 |
| 候補 | GET `/jobs/{id}/candidates` | candidate、preview、preview_url、approval、export、final_render_id |
| 承認 | PUT `/jobs/{id}/candidates/{candidateId}/approval` | `{"approved":true}`／falseで取り消し |
| 一括 | POST `/jobs/{id}/exports` | `{"candidate_ids":["..."]}`。全件事前検証後202。不可が1件でもあれば何も始めず、単独の書き出しと同じコード（`E-NOT-APPROVED`・`E-PLAN-STALE`・422 `E-QA-HARDFAIL`・`E-EXPORT-BUSY` のうち最も先に当たったもの）と、候補ごとの `code`・`reason` を返す |
| 再生成 | POST `/jobs/{id}/regenerate` | instruction、exclude_candidate_ids。前候補を除外して再選定 |
| 編集 | GET / PATCH `/editplans/{id}` | 計画と計画QA／opsで編集 |
| QA承認 | PUT `/editplans/{id}/qa/approval` | `plan_hash`、`checks:[{check_id,result_hash}]`、`approved`。結果の確認記録。falseで取り消し |
| Preview | POST `/editplans/{id}/preview` | その計画のプレビュー再作成 |
| Preview | GET `/editplans/{id}/preview/progress` | running、progress、step |
| 直接出力 | POST `/editplans/{id}/render` | 候補承認→承認PlanHash一致→計画ハードQA→処理中の順に検証し、単独書き出し受付 |
| Render | GET `/renders/{id}` | queued / rendering / done / failed / cancelled等。`notes` は書き出し時の補足（拡大表示する画面録画の高画質化の採否と実測値など） |
| Render | POST `/renders/{id}/cancel` | queuedのみ取り消し |
| Artifacts | GET `/renders/{id}/artifacts` | 完了した書き出しの `path`（絶対パス）、`available`、`files:[{key,path,available,preview_type}]`。種類は `video` / `image` / `text`（非対応は省略）。記録済み成果物のみで、投稿文を生成しない |
| Files | GET `/renders/{id}/files/{key}` | outputsのキーに対応するファイル。動画はRange対応 |
| Preview | GET `/renders/{id}/files/{key}/preview` | テキスト成果物の `{text,truncated}`。先頭65,536文字まで。非対応形式は422、ファイル欠損は404、失効は409 |
| Post | GET / POST `/renders/{id}/post` | GETは保存済み投稿文を返す（なければ生成・保存）。POSTは別案で上書き。画面は保存済みファイルの有無を確認し、未生成時は明示操作で呼び出す |
| OS | POST `/renders/{id}/open-folder` | 完了ファイルをエクスプローラーで選択。ファイル欠損は404、起動失敗は500 `E-OPEN-FOLDER`、Windows以外は501 `E-PLATFORM` |
| Media | GET `/media/{renderId}` | 主にプレビュー動画。Range対応 |
| PiP | POST `/assets/{id}/pip-scenes` | ローカルで画面変化を検出し、副映像自身の時刻によるシーン候補 `[{t0,t1,kind,suggested,slides,coverage}]` を返す。`kind` は `slide`（スライド原本と一致）／`fullscreen`（タスクバーのない全画面）／`screen`、`suggested` は初期選択の推奨、`slides` は 0.5 秒以上写るスライド番号。使用許可の保存は行わない |
| PiP | GET `/assets/{id}/frame?t=秒` | 登録素材から候補確認用JPEGを取得 |
| PiP | PATCH `/assets/{id}` | `{"pip_scenes":[{"t0":34,"t1":38}]}` で使用可能シーンを保存。最大500区間、有限・素材内・正の長さ・重複なし。空配列は全解除。P11生成時に1本の基準映像＋1本の `screen` が必要 |
| PiP | POST `/assets/{id}/deck` | multipart で スライド原本（.pptx/.ppsx/.ppt/.pps、またはスライド画像の .zip）を登録。幅 3840 px の PNG に書き出し、録画と照合して `deck`・`slide_spans` を保存した素材を返す。.pptx の変換は Windows の PowerPoint、なければ LibreOffice（環境変数 `VIDEOCREATOR_SOFFICE` で `soffice` を指定可）。クリックのビルドは段階ごとの画像（`slides[].step`）。どちらもない・変換失敗は422 `E-DECK`。`deck.source` は `powerpoint` / `libreoffice` / `images` |
| PiP | DELETE `/assets/{id}/deck` | スライド原本を解除（以後は録画のフレームを表示） |
| PiP | GET `/assets/{id}/slides/{n}` | スライド n のサムネイルJPEG（幅480） |

P11の `pip_scenes` は使用を許可する範囲であり、全区間を表示する指定ではありません。発話との重なりで絞り、顔・字幕を避けて自動配置し、小さすぎる場合や顔位置不明時は資料へ切り替えます。音声同期不能は `E-PIP-SYNC`、使用区間未設定・重なりなしは `E-PIP-SCENES`。主映像の変更は全素材の同期値（手入力を含む）を初期化します。反映には新しい生成が必要です。

会議録画のように遅れが段階的に変わる副映像は `sync_method: "audio-map"` となり、`sync_map: [{"t":…, "d":…}]`（基準映像の秒 `t` で副映像が `d` 秒遅い）に区間ごとの遅れを保存します。`sync_offset` はその中央値です。生成時に選択シーンから資料部分を検出し、`pip_content: {"x","y","w","h"}`（副映像の画素）に保存します。PiP・切り替えはこの範囲だけを表示します。`pip_scenes` を保存し直すと `pip_content` は消え、次回生成時に再検出されます。

スライド原本を登録すると、録画の資料部分を 2fps で各スライドと照合し（正規化相互相関、スコア 0.85 以上かつ他スライドより 0.2 以上高いときだけ一致）、一致区間を `slide_spans` に保存します。生成時は一致した区間だけ録画の代わりに原本画像を表示し、スライドの変わり目でクリップを分けます（QA の最短ショットは同じ配置の連続を1ショットとして数えます）。パターンの `screen.main_min_scale`（0.5〜1、P11 は 0.72）は顔と資料・字幕が重なるときに人物映像を縮小できる下限、`telop.caption.avoid_faces` は字幕を顔・資料と重ならない高さへ移す設定、`allow_outside_safe` はセーフゾーン外への移動の許可です。

BGM multipartの権利確認等は[Api.csの実装](../src/VideoCreator.App/Api.cs)を参照してください。パスを渡すAPIやOSダイアログは単一ローカルユーザー向けで、リモート操作の汎用契約ではありません。

QA承認のハッシュは編集計画のGET応答の `plan_hash` と各QA項目の `review_hash` を使います。個別は1項目、一括は実施済み・未承認の全項目を送ります。承認状態は `review_approval` に返ります。空・重複・未知・未実施の項目は422、計画や結果が変わっていれば409で、部分保存はしません。判定やテロップ・カード・候補の承認状態は変更しません。

## Job.Options

| キー | 値・制約 |
|---|---|
| scope | economy / balanced / full（既定balanced） |
| bgm | auto / none / scene（場面ごとに自動選曲。プロジェクトの曲、なければ内蔵曲から）/ 曲ID |
| sfx / tsukkomi | bool。AIツッコミはP3で使用 |
| tsukkomi_style | `auto`（既定）/ `telop` / `card`。AIツッコミの見せ方。`telop` は映像を止めずに重ねるテロップ、`card` は発言直後に2〜3秒の停止カード（画像／パネル＋文字＋専用BGM）。`auto` は発言が終わり、次の発言が受け答えでなく、本編の端・ほかのカード／オープニングから離れ（8秒以上）、カードの合計が本編の10%以内に収まる所だけカード、それ以外はテロップ。それ以外の値は 422 |
| parts / part_minutes | 本数2〜20 **か** 1本の分数1〜60。互いに排他。両方nullならショート |
| opening / closing / scene_cards | bool。使用可能な共通登録等を確認 |
| mask_words | 伏せる語の配列 |

画面はパターン1つを選びます。APIでpreset_idsが配列でも、現パイプラインは先頭を使うため、1回の生成で複数プリセットを比較するAPIとは扱わないでください。

## 編集操作

PATCH `/editplans/{id}` に `{"ops":[...]}` を送ります。[PlanEditor](../src/VideoCreator.Core/Planning/PlanEditor.cs)とAPIの追加制約に従います。

| op | 追加キー |
|---|---|
| replace_text | target（telop ID）、text |
| set_enabled | target、enabled |
| set_type_enabled | type（caption・emphasis・headline・sfx・tsukkomi・opening・card・closing）、enabled。tsukkomi はツッコミのテロップとカードの両方、card はツッコミ以外の場面転換カード |
| approve / approve_all_tsukkomi | 前者はtarget |
| set_bgm / set_sfx | 前者はtarget（曲ID＝全編この曲 / none / scene＝自動選曲し直し）、後者はenabled |
| set_bgm_cue | target＝場面ID（`bgm1`…）、text＝曲ID / none / auto（自動の選曲に戻す） |
| set_bgm_trim | target＝場面ID、value＝その場面の曲の音量（dB、−12〜+6） |
| set_bgm_level | type＝`ld`（話している間の声−BGM、10〜30 LU）/ `gap`（声のない所のBGM、声に対して −30〜0 LU）、value |
| set_insert_enabled | target（insert ID）、enabled |
| replace_card_text | target、text（20文字まで） |
| approve_card / approve_all_cards | 前者はtarget |
| adjust | edge=start/end、delta。画面では開始+1＝前の文、終了+1＝後の文 |
| replan | 現在の候補範囲から再計画 |

APIはシリーズの `adjust` / `replan`、P5の `set_bgm` を拒否します。Transcript PATCHはキャッシュの単語修正で、すべての既存EditPlanを自動更新するわけではありません。UIはその後replanを要求するため、シリーズでは制約に注意します。

## ローカルAPIの操作例

発話動画と自分のASR設定が使える環境で実行します。料金・外部送信が発生し得ます。実際の素材パスに置き換えます。

```powershell
$base = 'http://127.0.0.1:8765/api'
$headers = @{ 'X-VideoCreator' = '1' }
$p = Invoke-RestMethod "$base/projects" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"name":"初めてのAPI"}'
$a = Invoke-RestMethod "$base/projects/$($p.id)/assets" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"path":"C:\\Videos\\talk.mp4"}'
Invoke-RestMethod "$base/projects/$($p.id)/rights" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body (@{ asset_id=$a.id; confirmed=$true } | ConvertTo-Json)
$j = Invoke-RestMethod "$base/projects/$($p.id)/jobs" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"preset_ids":["P4"],"options":{"scope":"balanced"}}'
Invoke-RestMethod "$base/jobs/$($j.job_id)"
```

`status=ready` 後にcandidatesを取得し、UIで内容を確認します。APIによる承認と一括出力の具体例は既存の[exports.http](../src/VideoCreator.App/exports.http)。進捗は[progress.http](../src/VideoCreator.App/progress.http)、保存先は[projects-dir.http](../src/VideoCreator.App/projects-dir.http)、カードは[scene-cards.http](../src/VideoCreator.App/scene-cards.http)にあります。
