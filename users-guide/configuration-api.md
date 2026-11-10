# 設定・APIリファレンス

[アプリREADME](../README.md) / [ガイド目次](README.md) / [クイックスタート](quickstart.md) / [画面](screens.md) / [困ったとき](troubleshooting.md) / [構成・拡張](technical-reference.md) / [検証と保守](verification.md)

## 起動設定

[appsettings.json](../src/VideoCreator.App/appsettings.json)の `VideoCreator` セクション、環境変数、コマンドラインで設定します。環境変数は `VideoCreator__Asr__Endpoint` のように区切りを `__` にします。下表は[AppOptions](../src/VideoCreator.App/AppOptions.cs)の既定です。配布ファイルの具体的なエンドポイントは自分の値へ変更してください。

| 設定 | 既定・意味 |
|---|---|
| Port / OpenBrowser | `8765` / `true`。loopbackの待受と起動後のブラウザー表示 |
| DataDir | 未指定なら `%LOCALAPPDATA%\VideoCreator`。DBとキャッシュ |
| ProjectsDir | 未指定ならWindowsのMyVideos内 `VideoCreator`。新規プロジェクト用。画面で保存した値が優先 |
| FfmpegBin / FontsDir / PresetsDir | 未指定ならリポジトリ上位のtools等を探索 |
| FaceModel / UpscaleModel | 未指定ならtoolsのUltraFace / Real-ESRGANモデル |
| LocalOnly | `false`。`true`ならLLMを使わないことを固定。ASRは停止しない |
| Asr.Provider / Endpoint / Locale | `azure` / 空 / `ja-JP`。ProviderがazureでEndpointが設定されているとASR有効 |
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

- LLM方式の画面変更はRuntime内に反映。再起動すると起動設定に戻ります。
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
- 一般の要求上限は64MiB。**動画uploadルートは独自の上限を解除**し、multipartを一時バッファしてコピーします。BGMはHTTP bodyを500MiBへ変更しますが、multipart解析にも別の上限があります。他ルートに動画と同じ無制限条件を適用しないでください。

## ルート一覧

完全なハンドラーと要求recordは[Api.cs](../src/VideoCreator.App/Api.cs)が一次資料です。下表の `{id}` は各リソースのIDです。

| 分野 | Method・ルート | 内容 |
|---|---|---|
| 診断 | GET `/health` | FFmpeg・不足フィルタ・ASR・LLM・モデル等。設定・検出でありクラウド疎通を保証しない |
| 設定 | GET / PUT `/settings` | 読取／`{"llm_provider":"none"}` 等でLLM切替 |
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
| Asset | PATCH `/assets/{id}` | role、speaker、sync_offset、clear_speaker |
| Transcript | GET / PATCH `/assets/{id}/transcript` | 読取／`{"edits":[{"word_id":"...","text":"修正文"}]}` |
| 音 | GET `/bgm?project_id=...` | ライブラリとプロジェクトの曲 |
| 音 | POST `/projects/{id}/bgm` | multipart。file、権利確認 `rights_confirmed` |
| OS | POST `/dialogs/folder` | `{"initial_path":"..."}`。取消は `{"path":null}` |
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
| 直接出力 | POST `/editplans/{id}/render` | 計画QAを確認して単独書き出し受付 |
| Render | GET `/renders/{id}` | queued / rendering / done / failed / cancelled等 |
| Render | POST `/renders/{id}/cancel` | queuedのみ取り消し |
| Files | GET `/renders/{id}/files/{key}` | outputsのキーに対応するファイル。動画はRange対応 |
| OS | POST `/renders/{id}/open-folder` | 完了ファイルをエクスプローラーで選択 |
| Media | GET `/media/{renderId}` | 主にプレビュー動画。Range対応 |

BGM multipartの権利確認等は[Api.csの実装](../src/VideoCreator.App/Api.cs)を参照してください。パスを渡すAPIやOSダイアログは単一ローカルユーザー向けで、リモート操作の汎用契約ではありません。

QA承認のハッシュは編集計画のGET応答の `plan_hash` と各QA項目の `review_hash` を使います。個別は1項目、一括は実施済み・未承認の全項目を送ります。承認状態は `review_approval` に返ります。空・重複・未知・未実施の項目は422、計画や結果が変わっていれば409で、部分保存はしません。判定やテロップ・カード・候補の承認状態は変更しません。

## Job.Options

| キー | 値・制約 |
|---|---|
| scope | economy / balanced / full（既定balanced） |
| bgm | auto / none / 曲ID |
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
| set_bgm / set_sfx | 前者はtarget（曲ID/none）、後者はenabled |
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
