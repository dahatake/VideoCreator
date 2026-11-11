# 配置与 API 参考

[应用 README](../README.md) / [指南目录](README.md) / [快速入门](quickstart.md) / [界面](screens.md) / [遇到问题](troubleshooting.md) / [架构与扩展](technical-reference.md) / [验证与维护](verification.md)

## 启动配置

界面显示语言通过浏览器内的语言切换保存；视频语音语言按项目指定。`POST /api/projects` 可传 `{"name":"Example","language":"en-US"}`，`PATCH /api/projects/{id}` 可传 `{"language":"ko-KR"}`。支持 `ja-JP`（省略时默认）、`en-US`、`zh-CN`、`ko-KR`。不支持的值返回 422；生成或导出过程中修改语言返回 409。修改后请生成新候选；已有编辑计划保留创建时的语言。

通过 [appsettings.json](../../../src/VideoCreator.App/appsettings.json) 的 `VideoCreator` 节、环境变量或命令行配置。环境变量用 `__` 分隔，如 `VideoCreator__Asr__Endpoint`。下表为 [AppOptions](../../../src/VideoCreator.App/AppOptions.cs) 的默认值。请将发布文件中的具体端点改为自己的值。

| 配置 | 默认值与含义 |
|---|---|
| Port / OpenBrowser | `8765` / `true`。回环监听及启动后打开浏览器 |
| DataDir | 未指定时为 `%LOCALAPPDATA%\VideoCreator`。存放 DB 和缓存 |
| ProjectsDir | 未指定时为 Windows MyVideos 下的 `VideoCreator`。用于新项目，界面保存的值优先 |
| FfmpegBin / FontsDir / PresetsDir | 未指定时向仓库上级寻找 tools 等 |
| FaceModel / UpscaleModel | 未指定时使用 tools 中的 UltraFace / Real-ESRGAN 模型 |
| LocalOnly | `false`。`true` 强制不使用 LLM，但不停止 ASR |
| Asr.Provider / Endpoint / Locale | `azure` / 空 / `ja-JP`。Provider 为 azure 且配置 Endpoint 时启用 ASR。应用生成时项目 `language` 优先于 Locale |
| Llm.Provider | `azure-openai`。可选 `azure-openai` / `copilot` / `none` |
| Llm.Endpoint / Deployment | 空 / `gpt-chat-latest`。填写自己的部署名称 |
| Llm.CopilotCliPath / CopilotModel | 未指定时检测 CLI／使用默认模型 |
| Llm.SingleCallCharBudget | `12000`。标准分块判断的字符量，不是令牌上限本身 |
| Dictionary | `[]`。用于 ASR 短语列表和未出现词检查的专有名词 |

示例：使用其他端口启动，不使用 AI 筛选（Speech 需另行配置）。

```powershell
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
dotnet run --project src\VideoCreator.App -c Release
```

环境变量作用于当前 PowerShell 及子进程，新终端需重新配置。若仅检查界面且也禁用 ASR，可指定 `VideoCreator__Asr__Provider=none`，但这不是正常生成字幕的使用方式。

## 配置的生命周期

- 界面修改 LLM 方式仅影响 Runtime，重启后恢复启动配置。
- 界面修改项目保存位置会持久化到 Store 的 setting，优先于启动配置。
- 界面选择的模式与生成选项按项目保存到 Store，重启后恢复，生成时也记录到 Job.Options。
- 工作流最后显示阶段及目标任务、编辑计划和导出保存到 Project.WorkflowState。引用无效时退回前一阶段。
- BrandKit 存在共享 Store；BGM 存在库或项目中；是否生成图像保存在 Project 中。
- 预设在启动时加载，修改文件后需重启。

## HTTP 契约

- 默认 base URL：`http://127.0.0.1:8765`。以下所有路由前加 `/api`。
- JSON 使用 `snake_case`，枚举也是字符串。共享定义见 [Json](../../../src/VideoCreator.Core/Json.cs) 和 [Models](../../../src/VideoCreator.Core/Models/Models.cs)。
- 写入操作必须带 `X-VideoCreator: 1`。JSON 使用 `Content-Type: application/json`。
- GET/HEAD 不要求此标头。它不是身份验证或 API 密钥，而是防止跨站写入的保护。
- 通常错误为 `{ "error": { "code": "...", "message": "...", "detail": ... } }`。同时处理 HTTP 状态和 code。Host 拒绝或缺少标头时也可能返回纯文本。
- 异步导出的 `202` 表示已接受，不表示完成。需轮询返回的 render ID。
- 一般请求上限为 64MiB。**视频 upload 路由取消了自身上限**，对 multipart 临时缓冲后复制。BGM 将 HTTP body 上限改为 2000MiB（一次最多 20 首），但 multipart 解析另有上限。不要将视频的无限制条件应用到其他路由。

## 路由列表

完整处理器及请求 record 以 [Api.cs](../../../src/VideoCreator.App/Api.cs) 为一手资料。下表 `{id}` 为对应资源 ID。

| 领域 | Method 与路由 | 内容 |
|---|---|---|
| 诊断 | GET `/health` | FFmpeg、缺少的滤镜、ASR、LLM、模型等。仅配置与检测，不保证云端连通 |
| 配置 | GET / PUT `/settings` | 读取／使用 `{"llm_provider":"none"}` 等切换 LLM |
| 保存位置 | GET `/settings/projects-dir` | path、source、configured_path、数量、迁移进度 |
| 保存位置 | POST `/settings/projects-dir/plan` | 预检查 `{"path":"D:\\Videos\\VideoCreator","move":true}` |
| 保存位置 | PUT `/settings/projects-dir` | 以相同格式实际修改／迁移 |
| 模式 | GET `/presets` | 启动时已加载的预设 |
| Project | GET / POST `/projects` | 列表／以 `{"name":"工作名称","language":"zh-CN"}` 创建。language 支持 ja-JP、en-US、zh-CN、ko-KR，默认 ja-JP |
| Project | GET / PATCH / DELETE `/projects/{id}` | 详情（`resume_state` 返回恢复阶段和目标 ID）／`{"name":"新名称","language":"zh-CN"}`／删除。修改语言后需重新生成，已有 EditPlan 保留原语言 |
| Project | PUT `/projects/{id}/generation-settings` | 将模式及生成选项保存到项目 |
| Project | PUT `/projects/{id}/workflow-state` | 保存当前阶段（`project` / `settings` / `job` / `candidates` / `exports` / `review` / `render`）。`exports` 必须带 `job_id`，且 Job 仅可为 `ready` / `done` |
| Asset | POST `/projects/{id}/assets` | `{"path":"C:\\Videos\\talk.mp4"}`，不复制 |
| Asset | POST `/projects/{id}/assets/upload` | 通过 multipart 的 `file` 字段复制视频 |
| Rights | POST `/projects/{id}/rights` | `{"asset_id":"...","confirmed":true}` |
| 估算 | GET `/projects/{id}/estimate?preset=P4` | 按 scope 估算发送量及基于单价的费用 |
| Asset | PATCH `/assets/{id}` | role、speaker、sync_offset、clear_speaker |
| Transcript | GET / PATCH `/assets/{id}/transcript` | 读取／`{"edits":[{"word_id":"...","text":"修正文"}]}` |
| 音频 | GET `/bgm?project_id=...` | 曲库和项目曲目，每首有 `analysis`（lufs, lra, start, end, bpm, onset_rate, brightness, energy, short_term） |
| 音频 | POST `/projects/{id}/bgm` | multipart，file（可多个，最多 20）、权利确认 `rights_confirmed`。注册时测量曲目。201 `{tracks, skipped}`（无法读取或不足 5 秒的曲目在 skipped 中附原因） |
| 音频 | DELETE `/projects/{id}/bgm/{bgmId}` | 移除注册并删除文件。编辑计划使用中返回 409 `E-BGM-IN-USE` |
| OS | POST `/dialogs/folder` | `{"initial_path":"..."}`，取消返回 `{"path":null}` |
| 卡片 | POST `/projects/{id}/scene-cards` | asset_id **或** brand_path，count 2～6（默认 4），不可同时指定二者 |
| 卡片 | PUT `/projects/{id}/scene-cards/settings` | `{"make":true}`，后续生成使用图像 |
| 卡片 | GET `/projects/{id}/scene-cards/{index}` | 提取图像（索引从 0 开始） |
| BrandKit | GET / PUT / DELETE `/brandkit` | 共享注册。PUT 带 paths、rights_confirmed、card_sec、upscale |
| BrandKit | POST `/brandkit/enhance` | 开始提升已注册视频的画质 |
| 生成 | POST `/projects/{id}/jobs` | preset_ids、instruction、options |
| Job | GET `/jobs/{id}` | 状态、候选 ID、警告等 |
| Job | POST `/jobs/{id}/cancel` | 取消等待／执行中的生成 |
| Job | GET `/jobs/{id}/events` | SSE（EventSource），stage、progress 等 |
| 候选 | GET `/jobs/{id}/candidates` | candidate、preview、preview_url、approval、export、final_render_id |
| 批准 | PUT `/jobs/{id}/candidates/{candidateId}/approval` | `{"approved":true}`／false 撤销 |
| 批量 | POST `/jobs/{id}/exports` | `{"candidate_ids":["..."]}`。全部预验证后返回 202。任一项不可导出则不启动任何操作，返回与单独导出相同的代码（`E-NOT-APPROVED`、`E-PLAN-STALE`、422 `E-QA-HARDFAIL`、`E-EXPORT-BUSY` 中最先遇到的），以及每个候选的 `code`、`reason` |
| 重新生成 | POST `/jobs/{id}/regenerate` | instruction、exclude_candidate_ids，排除前次候选后重新筛选 |
| 编辑 | GET / PATCH `/editplans/{id}` | 计划及计划 QA／通过 ops 编辑 |
| QA 批准 | PUT `/editplans/{id}/qa/approval` | `plan_hash`、`checks:[{check_id,result_hash}]`、`approved`，记录结果确认，false 撤销 |
| Preview | POST `/editplans/{id}/preview` | 重新生成该计划的预览 |
| Preview | GET `/editplans/{id}/preview/progress` | running、progress、step |
| 直接输出 | POST `/editplans/{id}/render` | 检查计划 QA 后接受单独导出 |
| Render | GET `/renders/{id}` | queued / rendering / done / failed / cancelled 等 |
| Render | POST `/renders/{id}/cancel` | 仅取消 queued |
| Artifacts | GET `/renders/{id}/artifacts` | 已完成导出的 `path`（绝对路径）、`available`、`files:[{key,path,available,preview_type}]`。类型为 `video` / `image` / `text`（不支持的省略）。仅列已记录产物，不生成投稿文案 |
| Files | GET `/renders/{id}/files/{key}` | outputs 中对应键的文件，视频支持 Range |
| Preview | GET `/renders/{id}/files/{key}/preview` | 文本产物的 `{text,truncated}`，最多前 65,536 字符。不支持格式返回 422，文件缺失 404，失效 409 |
| Post | GET / POST `/renders/{id}/post` | GET 返回已保存投稿文案（没有则生成并保存），POST 以其他方案覆盖。界面先检查已保存文件，未生成时通过显式操作调用 |
| OS | POST `/renders/{id}/open-folder` | 在资源管理器中选中完成文件。缺失 404，启动失败 500 `E-OPEN-FOLDER`，非 Windows 为 501 `E-PLATFORM` |
| Media | GET `/media/{renderId}` | 主要用于预览视频，支持 Range |

BGM multipart 的权利确认等见 [Api.cs 实现](../../../src/VideoCreator.App/Api.cs)。传入路径的 API 和 OS 对话框面向单个本地用户，不是通用远程操作契约。

QA 批准的哈希使用编辑计划 GET 响应的 `plan_hash` 和各 QA 项的 `review_hash`。单项批准发送一项，批量批准发送所有已执行、未批准项。批准状态通过 `review_approval` 返回。空项、重复、未知或未执行项返回 422；计划或结果已变化返回 409，不会部分保存。不会改变判定以及屏幕文字、卡片、候选的批准状态。

## Job.Options

| 键 | 值与限制 |
|---|---|
| scope | economy / balanced / full（默认 balanced） |
| bgm | auto / none / scene（按场景自动选曲，优先项目曲目，否则用内置曲目）/ 曲目 ID |
| sfx / tsukkomi | bool。AI 吐槽用于 P3 |
| tsukkomi_style | `auto`（默认）/ `telop` / `card`。AI 吐槽展示方式。`telop` 不暂停视频而叠加文字；`card` 在发言后插入 2～3 秒暂停卡（图像／面板＋文字＋专用 BGM）。`auto` 仅在发言结束、下一发言不是回应、距离正文边缘及其他卡片／片头至少 8 秒、卡片总时长不超过正文 10% 时使用卡片，否则为叠加文字。其他值返回 422 |
| parts / part_minutes | 数量 2～20 **或**单条分钟数 1～60，互斥。二者均 null 时为短视频 |
| opening / closing / scene_cards | bool，检查可用的共享注册等 |
| mask_words | 要屏蔽的词语数组 |

界面选择一个模式。即使 API 的 preset_ids 是数组，当前流水线也只使用第一项，不应视作一次生成比较多个预设的 API。

## 编辑操作

向 PATCH `/editplans/{id}` 发送 `{"ops":[...]}`，遵守 [PlanEditor](../../../src/VideoCreator.Core/Planning/PlanEditor.cs) 及 API 附加限制。

| op | 附加键 |
|---|---|
| replace_text | target（telop ID）、text |
| set_enabled | target、enabled |
| set_type_enabled | type（caption、emphasis、headline、sfx、tsukkomi、opening、card、closing）、enabled。tsukkomi 同时指吐槽文字和卡片，card 为非吐槽转场卡 |
| approve / approve_all_tsukkomi | 前者需 target |
| set_bgm / set_sfx | 前者 target（曲目 ID＝全程此曲 / none / scene＝重新自动选曲），后者 enabled |
| set_bgm_cue | target＝场景 ID（`bgm1`…），text＝曲目 ID / none / auto（恢复自动选曲） |
| set_bgm_trim | target＝场景 ID，value＝该场景曲目音量（dB，−12～+6） |
| set_bgm_level | type＝`ld`（讲话时人声−BGM，10～30 LU）/ `gap`（无讲话处 BGM，相对人声 −30～0 LU），value |
| set_insert_enabled | target（insert ID）、enabled |
| replace_card_text | target、text（最多 20 字符） |
| approve_card / approve_all_cards | 前者需 target |
| adjust | edge=start/end、delta。界面中开始 +1＝前一句，结束 +1＝后一句 |
| replan | 从当前候选范围重新规划 |

API 拒绝系列的 `adjust` / `replan` 和 P5 的 `set_bgm`。Transcript PATCH 修改缓存词语，不会自动更新所有现有 EditPlan。UI 随后会请求 replan，因此系列需注意限制。

## 本地 API 操作示例

在可使用讲话视频及自己的 ASR 配置的环境中执行。可能产生费用及外部发送。替换为实际素材路径。

```powershell
$base = 'http://127.0.0.1:8765/api'
$headers = @{ 'X-VideoCreator' = '1' }
$p = Invoke-RestMethod "$base/projects" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"name":"首次使用API"}'
$a = Invoke-RestMethod "$base/projects/$($p.id)/assets" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"path":"C:\\Videos\\talk.mp4"}'
Invoke-RestMethod "$base/projects/$($p.id)/rights" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body (@{ asset_id=$a.id; confirmed=$true } | ConvertTo-Json)
$j = Invoke-RestMethod "$base/projects/$($p.id)/jobs" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"preset_ids":["P4"],"options":{"scope":"balanced"}}'
Invoke-RestMethod "$base/jobs/$($j.job_id)"
```

`status=ready` 后获取 candidates，在 UI 中检查内容。API 批准与批量导出的具体示例见现有 [exports.http](../../../src/VideoCreator.App/exports.http)。进度见 [progress.http](../../../src/VideoCreator.App/progress.http)，保存位置见 [projects-dir.http](../../../src/VideoCreator.App/projects-dir.http)，卡片见 [scene-cards.http](../../../src/VideoCreator.App/scene-cards.http)。
