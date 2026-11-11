# VideoCreator 实现与验证报告

[↑ 文档导航](../README.md) | [日本語](../../../docs/IMPLEMENTATION.md) | [English](../../en/docs/IMPLEMENTATION.md) | **简体中文** | [한국어](../../ko/docs/IMPLEMENTATION.md)


- 对象：[PRD.md](PRD.md)，VC-PRD-001 v1.2.3；以前结果和本次再验证分别说明。
- 日期：2026-11-07；再验证2026-11-08；AT-13～15为2026-11-09。
- 实现：C#/.NET 10（ASP.NET Core Minimal API、本地Web应用）＋Fluent 2 Web Components v3。
- 环境：Windows 11 ARM64、FFmpeg N-128532（BtbN win-arm64 GPL）、Azure Japan East。

## 1. 实施方法

对于 PRD 的每项功能，先在 `spikes/` 制作小型验证程序，以机器测量确认符合 PRD 阈值，再整合进应用。实现后执行了 67 项单元与集成测试，并使用真实 Microsoft Edge 浏览器和真实 Azure 服务进行端到端验证。`ui/e2e.mjs` 包含九项检查；v1.1 的 `ui/e2e-v11.mjs` 包含二十九项。

| spike | 验证内容 | 结果 | 发现及设计反映 |
|---|---|---|---|
| [01-ingest](../../../spikes/01-ingest.cs) | ffprobe、VFR、旋转、无效文件、CFR 代理。 | 全通过。 | MKV 元数据看不到 VFR，因此改为**通过包时刻差值判定 VFR**。 |
| [02-signals](../../../spikes/02-signals.cs) | scdet 镜头边界、100ms 音量、静音、峰值。 | 全通过，与正解差 ±0.1 秒。 | FFmpeg 标准 scdet 已足够，可替代 PySceneDetect，即 PRD D-4 的替代方案。 |
| [03-render](../../../spikes/03-render.cs) | ASS、模糊背景、推近、音量、确定性、竖屏素材。 | 全通过。 | ① `cropdetect` 会误判通过，改为自行从**原始像素测量文字外接矩形**。② 单遍 loudnorm 为 −17.3 LUFS；两遍 linear 也会默默回退到 dynamic，得到 −15.1。因此导出后实测，再循环修正音频。 |
| [04-asr](../../../spikes/04-asr.cs) | 无密钥 Fast Transcription、单词时刻、说话人分离。 | CER 11.5%，说话人纯度 97～99%，125 秒音频耗时 3.9 秒。 | 主要差异来自数字表记，如“一万→1万”，因此汉数字与阿拉伯数字按相同数值比较。 |
| [05-llm-select](../../../spikes/05-llm-select.cs) | Microsoft Foundry Structured Outputs。 | 四个候选全部项目通过。 | strict 模式不支持 `maxLength`、`minimum`、`maxItems`（Microsoft Learn），因此约束由代码强制执行。 |
| [06-copilot-sdk](../../../spikes/06-copilot-sdk.cs) | GitHub Copilot SDK 与操作系统已安装 CLI。 | 通过。 | 智能体会分两次发送“中间 JSON”和“修正版”，也可能违反时长、区间数限制；因此提取最新完整 JSON，并使用同一验证器。 |
| [07-make-long](../../../spikes/07-make-long.cs) | 由 LLM 台本和 TTS 生成 64 分钟日语测试视频。 | 1,252 行、64 分钟。 | 用于性能与长视频分块验证。 |

为避免权利问题，本节全部测试素材均由 FFmpeg 测试图与 Azure TTS 音频合成。只有 §2.2 的 AT-10 再验证使用用户指定、明确允许外发的真实视频。§2.3 使用固定转写与 FFmpeg 合成媒体，没有发送给真实 Azure。

## 2. 验收测试（PRD §14.1）

> 此表记录 v1.1 以前条件下的历史结果。修订后 AT-01、AT-03 的判定见 §2.1，AT-10 见 §2.2，AT-13～AT-15 与最新质量门槛判定见 §2.3。PRD v1.2.4 新增的 AT-16、AT-17 见 §17 的“验证”。合成素材或模拟响应的部分验证，不等同于指定语料、真实云服务下的完整验收。

| ID | 结果 | 证据 |
|---|---|---|
| AT-01 | ✅※1 | 64 分钟日语双人视频生成三个 P4 候选，耗时 **193 秒**，目标为 25 分钟内。三条全部通过硬性 QA，均带四轴分数与理由。 |
| AT-02 | ✅ | E2E 导出经 ffprobe 测得 **1080×1920、30.90 秒**，loudnorm 测得 **−14.0 LUFS／−1.5 dBTP**。集成测试为 `Final_render_meets_platform_spec_and_passes_hard_qa`。 |
| AT-03 | ✅ | 同一编辑计划渲染两次，视频流 SHA-256 相同，见 `Rendering_is_deterministic` 和 spike 03。 |
| AT-04 | ✅ | 原先指向句中位置的区间会移到句子边界，并通过 H-06。验证为 `Solver_snaps_mid_sentence_edges_and_never_cuts_words`。 |
| AT-05 | ✅ | 转写中不存在的“500万日元”被 H-07 判为不合格。验证为 `UngroundedClaims_flags_invented_numbers` 和 `Qa_fails_ungrounded_and_unapproved_telops`。 |
| AT-06 | ✅ | 字幕十四字以内，不以助词开头，没有孤立行。见 `Captions_respect_width…`、`Caption_lines_are_balanced_without_orphans`。 |
| AT-07 | ✅ | LLM 连续三次违反 schema 后，回退为信号候选并显示指定消息，见 `Selection_falls_back_to_signals_after_retries`。 |
| AT-08 | ✅ | 真实 API 确认：权利未确认时返回 409 `E-RIGHTS-REQUIRED`。 |
| AT-09 | ✅ | 将文字改成 22 字后，S-02 警告在 39～47ms 内显示。这是 E2E 实测，包含界面往返时间。 |
| AT-10 | ⚠ v1.0 结果，条件变更见右栏 | v1.1 改为“仅本地”时转写也发送 Azure，PRD 1.2 修订条件，修订后真实通信验证见 §2.2。旧结果是在仅本地模式运行 Job 时，每 100ms 监视应用及 FFmpeg 子进程 TCP，外部连接零次；阳性对照 Azure 模式以同样方法检出 `20.191.161.227:443`。 |
| AT-11 | ✅ | 竖屏源素材不添加模糊或黑带，与源素材 PSNR 为 45 dB，见 `Vertical_source_passes_through`。 |
| AT-12 | ✅ | 通过对真实绘制结果的测量判断安全区域，并确认设置成越界布局时确实会判为不合格。验证为 `SafeZone_check_detects_violations`。 |

※1 指定的内部录制对谈 TC-JA-60 并不存在，因此使用 LLM 编写台本、TTS 朗读的 64 分钟合成视频替代。  
※2 上述观测不是数据包捕获，而是对操作系统 TCP 连接表的采样。

### 2.1 PRD 1.2.1 再验证

2026-11-08 重新调查 v1.2 修订时的注意点，完成以下实现与验证。

在 §2.2 遥测修正之前，执行 `dotnet test --project .\tests\VideoCreator.Tests`，**191/191 通过、零跳过，耗时四分四秒**。追加修正后，相关 `PrdVerificationTests` **4/4 通过**，并完成 §2.2 真实生成验证。已有警告 CS8321（V11Tests）、CS8604（V12Tests）仍保留。三个 JavaScript 文件的语法检查及 `git diff --check` 也通过。这些是实现回归验证，不是尚未校正的质量门槛证据。

| 对象 | 结果 | 可复现证据及剩余条件 |
|---|---|---|
| UI 模式名称与发送目标 | 通过。 | 标头、设置页改称“不使用 LLM 模式”。生成按钮之前显示 ASR origin 和 Entra ID 认证发送目标，切换设置时重新显示。[verify-prd.mjs](../../../ui/verify-prd.mjs) 在真实 Edge **12/12 通过**；只有候选响应使用 UI fixture，没有执行云端生成。 |
| FR-TEL-02 延长限制 | 实现边界通过，质量值未校正。 | 最多 0.5 秒，受下一字幕与片段末端限制。[PrdVerificationTests.cs](../../../tests/VideoCreator.Tests/PrdVerificationTests.cs) 以长静音、短片段边界确认：不删正文，仍超速的字幕保留 S-01 警告。没有由日语读者评价可读性、同步感的证据。 |
| AT-01 | 仅部分验证，验收未通过 | [RenderingTests.cs](../../../tests/VideoCreator.Tests/RenderingTests.cs) 使用真实 FFmpeg，确认预览的 H-02～H-04 为 `stage=final,status=not_run`，已执行的计划 QA 带 `stage=plan,plan_hash`。Edge 也确认未执行项不显示为成功色。H-11 已在 §2.3 实现验证，但使用 TC-JA-60 真实对谈的 AT-01 整体仍未通过，按照用户要求保持未完成。 |
| AT-03 / NFR-REL-04 | 合成候选在当前环境通过 | 同一候选两次正式渲染的 `ffmpeg -map 0:v -f framemd5 -` 全部解码帧相同，七项复现条件值也相同。`Final_export_creates_exactly_one_render_record` 检查 SQLite 保存后的 `reproducibility` 和 `plan_hash`。文件路径使用 `{path,sha256}` 数组，以免 JSON 字典键转换破坏路径。 |
| AT-10 | 当前构成通过，见 §2.2。 | 除此前模拟验证外，使用授权真实视频，两模式都完成 P4 生成。不使用 LLM 时 ASR POST 一次、LLM POST 零次；普通模式 ASR POST 一次、LLM POST 七次。也确认生成前目标显示及代理绕过观测。范围、原始记录、限制见该节。 |
| AT-13～AT-15 | 通过，见 §2.3。 | 确认指定反例、真实 FFmpeg 的十秒拒绝和二十七秒单候选生成、真实 API／正式 QA 后的失效与渲染竞争。真实 Edge UI 另以响应 fixture 验证，不是质量门槛实测。 |
| 初始目标 95%／90%／CER +0.5 个百分点 | 未验证。 | 缺指定语料的 Job 日志、首次候选 QA、十素材人工正解，不以合成视频或单测通过率替代。定义、校正步骤和保留素材再评价条件见 [PRD](PRD.md) §15.6。 |

[RenderProvenance.cs](../../../src/VideoCreator.Core/Rendering/RenderProvenance.cs) 实现复现条件记录。当前字幕运行时公开 libass API／source、FriBidi／HarfBuzz 标识，但不公开 FreeType 版本。因此不记录猜测版本，而用静态链接 FFmpeg 可执行文件的 SHA-256 固定内置库。NFR-REL-04 已明确说明区别。

旧预览残留的“540×960 因而 H-02 通过”等结果，启动时迁移为正式检查 `not_run`。迁移是幂等的，不改写正式结果或未知 `plan_hash`。

重跑PowerShell：

```powershell
dotnet test --project .\tests\VideoCreator.Tests
node --check .\src\VideoCreator.App\wwwroot\js\app.js
node --check .\ui\e2e.mjs
node --check .\ui\verify-prd.mjs
```

UI 验证在独立 PowerShell 中用下列配置启动，避免混入普通数据，再从另一终端执行。`example.invalid` 只供显示、禁止 LLM 的检查，并非真实云端目标：

```powershell
$env:VideoCreator__DataDir = Join-Path $env:TEMP 'vc-prd-ui-data'
$env:VideoCreator__ProjectsDir = Join-Path $env:TEMP 'vc-prd-ui-projects'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__Port = '18765'
$env:VideoCreator__LocalOnly = 'true'
$env:VideoCreator__Asr__Endpoint = 'https://speech.example.invalid/'
dotnet run --project .\src\VideoCreator.App --no-build --no-launch-profile
# 另一终端执行；结束后 Ctrl+C 停止上述验证服务器。
node .\ui\verify-prd.mjs http://127.0.0.1:18765
```

**发布判定未通过**。AT-10、AT-13～AT-15 验证已完成，但指定素材 AT-01、人工评价和质量门槛证据尚未齐备，不能把整个 PRD 或 M2／M3 视为完成。AT-01 按用户指示保留。没有覆盖其他用途的修改，也没有提交。

### 2.2 AT-10 真实通信验证

2026-11-08 取得用户明确外发许可，以指定 MOV 全片完成两模式的 **P4 生成**。在本次 Azure CLI 认证构成下，**AT-10 通过**。

- 输入为 `20260615_024326000_iOS.MOV`，1081.42 秒，约十八分一秒，6,494,572,403 bytes，3840×2160 HEVC／AAC 48kHz 双声道。
- 输入 SHA-256 为 `fe9c763fdf6fe7be4f368bfba926a1f3f2407ceaf044ec7d055e0869e65c1a9f`，两项目注册结果一致。
- 按模式隔离项目、数据和 ASR 缓存。只复用本地生成的 `proxy.mp4`、`audio16k.flac`，不复制转写，两模式分别新调用 ASR。每次音频 multipart POST 为 36,502,012 bytes。
- 条件为 P4、balanced，无 BGM、SFX、吐槽、片头或片尾。仅普通模式使用 `gpt-chat-latest`。正式导出不在本次范围内。

| 记录 | 不使用LLM | 普通模式阳性对照 |
|---|---|---|
| Job | job_79d02f446d | job_9c43b77c99 |
| 开始→ready UTC | 15:06:47→15:13:35 | 15:09:24→15:24:53 |
| 候选预览 | 三条全生成、信号 | 三条全生成、LLM |
| ASR POST | 一、200 | 一、200 |
| LLM POST | **零** | **七，全200** |
| 非许可目标尝试／遮断／代理错 | 零 | 零 |
| 应用子进程直接外部TCP观测 | 零 | 零 |
| .NET socket ConnectStart | 一，专用代理 | 八，全专用代理 |
| 生成前显示 | 真实Edge与配置JSON | 真实Edge与配置JSON |

**观测及失败处理**

ASR 与 LLM 使用同一 Azure 主机，不能只依 IP 或主机名区分。为每个模式设置独立回环 HTTPS 代理，临时终止 TLS，以区别 `POST /speechtotext/transcriptions:transcribe` 和 `POST /openai/v1/chat/completions`。只记录时刻、目标、方法、排除查询的路径、响应状态和字节数；代理日志不存认证头、音频、转写或 LLM 响应正文。维持上游 TLS 证书验证，没有发送视频帧。

首次不使用 LLM 运行时，Azure CLI 认证子进程尝试连接 `dc.services.visualstudio.com` 发送遥测。即使被代理阻止，该次仍记录为 **AT-10 不合格**，不作为通过证据。[Runtime.cs](../../../src/VideoCreator.App/Runtime.cs) 在应用自身进程环境设置 `AZURE_CORE_COLLECT_TELEMETRY=false`，由认证子进程继承；没有修改用户全局 Azure CLI 设置。相关四项测试通过后，重跑两模式得到上表结果。

除代理记录外，同时监视 .NET socket 连接事件和应用／后代进程 IPv4、IPv6 TCP 表，确认检出 FFmpeg 并持续到应用结束。不使用 LLM 的 TCP 样本数为 11,439，普通模式 27,172。配置间隔 20ms，但实际最大间隔分别为 484ms、500ms。**仅凭采样不能否定全部短时原生连接**。这不等于全操作系统通信抓包，也不证明所有认证路径，以代理和 .NET 事件为主要证据。本次认证使用 Azure CLI 缓存 token，记录中没有 Entra ID HTTP 请求。

原始记录保留于该验证会话的 `files\at10`：`at10-summary.json` 保存判定、Job ID、输入哈希、观测条件及原始记录 SHA-256；`validate-evidence.py` 保存机械判定；按模式的 JSONL 保存通信、socket 原始记录；`preflight.json` 和画面保存生成前显示；`result.json` 保存生成完成结果。首次遥测失败记录也保留。没有将私有素材、转写、画面复制到仓库。验证应用、监控、代理均已停止，并确认删除临时 CurrentUser Root 证书及私钥文件。

这只是**一个素材、P4、本次 Azure CLI 认证路径**的 AT-10 证据。十八分钟视频不能替代 TC-JA-60 指定真实对谈或十素材人工正解，没有改变 AT-01、H-11、AT-13～AT-15、质量门槛的判定。

### 2.3 AT-13～AT-15 实现与验证

2026-11-09实现指定反例、错误文字、候选数、失效响应，三项通过。AT01按要求未完。质量必需证据未取得，不能报告整体至质量门槛完成。

| 对象 | 结果与原证据 |
|---|---|
| AT-13 | [AcceptanceGroundingTests.cs](../../../tests/VideoCreator.Tests/AcceptanceGroundingTests.cs) 的四个固定反例确认：A/B 公司金额混淆、P7 说话人误归属、仅存在于词典的产品名被 H-11 拒绝；“没卖出”变成“卖出”由 S-09 警告。同时确认 H-07 会漏过这些对应不一致。还验证正确金额和说话人、全角与汉数字、正负号和小数、词 ID 不连续／顺序错误／区间外，以及吐槽不适用的情形。 |
| AT-14 | [AcceptanceGenerationTests.cs](../../../tests/VideoCreator.Tests/AcceptanceGenerationTests.cs)真FFmpeg10/27秒＋固定ASR，10秒ASR前拒，27秒仅一真实预览记原因；[ApprovalExportTests.cs](../../../tests/VideoCreator.Tests/ApprovalExportTests.cs)API422 E-SOL-INFEASIBLE、“素材短于P4下限25秒”，不新增Job |
| AT-15 | ApprovalExportTests真实Kestrel/FFmpeg正式全硬QA及取得后改金额，hash变、批准QA结果失效、H11 fail，单批及附产物／视频／开文件夹409；正式中公开编辑拒不改。直接DB竞争E-PLAN-STALE、不存Render、隔离输出 |
| UI | [verify-acceptance.mjs](../../../ui/verify-acceptance.mjs)真Edge11/11：原话人时刻、改词ID、失效、H11禁导、旧预览QA不冒新成功、隐藏失效链接、正确重改批准、批准后导、单候选原因。API是UI fixture，后端另试 |

历史：

- 首相关22/22，零跳。
- 后跨域全回归208/208，零失败跳，2分35。
- 此后补共享ASR另项目失效及重导最新有效，最新ApprovalExportTests7/7，零失败跳，1分20；208为此前结果。
- JS语法过；CS8321/CS8604未修；UI助手停，18766不监听。

范围兼容：

- [TelopGrounding.cs](../../../src/VideoCreator.Core/Planning/TelopGrounding.cs)共用生成编辑依据：区间连续词、数单位对象连续表达、说话人；数字异体正规化但不同符号小数不混，仅词典无效；自动不一致移除记因。
- 保守词比，不保证所有语义否定专名；未穷识词典外汉字专名。正确改写被拒需贴原话重关联，不用批准绕H11。
- 生成后检真实尺及求解后重叠；预告正文重叠按集合，不用相同素材灌候选。
- [Store.Atomic](../../../src/VideoCreator.Core/Storage/Store.cs)保护批准检查预约最终存；正式各ID目录不覆盖，失效移rejected不分发。
- 不删旧正式，但无当前hash H11 pass需重批QA导；旧预览可警告看，不把旧QA改新证。
- 修转写亦令相关计划hash失效，含同素材hash缓存其他项目；相关正在导出拒。旧仍在不误把新有效标失效。
- 既有的说话人／相机一致性检查和 YouTube 章节检查改为 S-13、S-14，以区别 S-09、S-10。在共享工作期间被另一项工作删除的外部声音事件检测器没有恢复，只移除剩余流水线引用，并保持现有本地笑声检测。

#### 质量门槛判定

许可真实一素材及合成不足必需输入；**率CI和p值未测**。不以零填未知分母、不用测试通过率、不松阈值。

| ID | 当前判定 | 缺少原素材与独立证据 |
|---|---|---|
| QG-01 | 未通过，指定语料未跑 | TC-JA-60/P4/P7、LEC/P6、GAME/P4/P1/P3、COOK/P8/P9、VERT、EDGE及全日志；输出hash/QA、启动／ready／三条目标Job数、未改首次QA。本次安全仅a实现非全量 |
| QG-02 | 未通过，无独立样本 | ≥20授权P4/B1/B2，≥5未制作评者全对原票，次序左右seed刺激hash；素材簇10000bootstrap及混合逻辑敏感；非AI投票同素材重复 |
| QG-03 | 未通过，无参照 | 优先对话HiSum200固定subset/seed，重播前10%参考及P4段，每条1000同尺随机，10000单侧配对排列 |
| QG-04 | 未通过，无人工正解 | 十素材同段ASR字幕，NFKC去空白标点Levenshtein CER，素材10000 seed20261108差95%CI，+0.5百分点未测 |

保留AT01不授权略TC-JA-60或判质量过；无输入不能完成，发布仍未通过。

重跑PowerShell，UI服务器和build不同时，完后停：

```powershell
dotnet test --project .\tests\VideoCreator.Tests
dotnet test --project .\tests\VideoCreator.Tests --filter-class 'VideoCreator.Tests.ApprovalExportTests'
node --check .\src\VideoCreator.App\wwwroot\js\app.js
```

UI独立数据助手，另一终端`node .\ui\verify-acceptance.mjs http://127.0.0.1:18766`：

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

## 3. 非功能需求实测

| ID | 目标 | 实测 |
|---|---|---|
| NFR-PERF-01 | 60 分钟素材生成候选，p50 为 15 分钟 | 实测 193 秒：导入 28、转写 54、分析 19、AI 筛选 5、预览 84 秒。 |
| NFR-PERF-02 | 修改模式后两分钟内重新生成 | 实测 112 秒；转写与分析使用缓存，大部分时间用于预览。 |
| NFR-PERF-03 | 30 秒视频在 60 秒内导出 | spike 03 为 11.8 秒；应用中的 57 秒视频耗时 40 秒，包含两遍音量调整与 QA。 |
| NFR-COST-01 | 事前估价 | 64 分钟估 ¥65.6、实 ¥65.1；价格来自 Azure Retail Prices API 的 JPY、japaneast。 |
| NFR-COST-03 | AI 输入不超过全文 50% | 长片实测 46%，输入 token 从 34.2k 减至 18.8k；取舍见 §6。 |
| NFR-REL-02 | 中途恢复 | 重启时重新投递未完成任务，各阶段产物使用缓存。 |
| NFR-REL-04 | 确定性 | 结果见 AT-03。 |
| NFR-SEC-01/05/06 | 回环、路径、注入防护 | 仅监听 127.0.0.1；Host 非本机返回 400；无自定义头 POST 返回 403；限制写入项目内；FFmpeg 使用参数数组，ASS 做转义，均有测试。 |
| NFR-SEC-02 | 无密钥认证 | 使用 DefaultAzureCredential，资源本地认证已禁用。 |
| NFR-MNT-02 | 检查滤镜 | /api/health 报告必需滤镜缺失；当前环境没有缺失。 |

## 4. E2E 真实浏览器与 Azure

[ui/e2e.mjs](../../../ui/e2e.mjs)操作OS真实Edge headless，最后9/9、91秒。

- UX-01：文件选择→权利确认→模式→生成→候选，共五次点击。
- 所有候选四轴原因、预览硬QA通过。
- 正式导出的 H-01～H-09 九项全部通过，并产生 MP4、SRT、编辑计划、ASS、三张缩略图和元数据建议。

历史界面：[项目](../../../docs/screenshots/02-assets-patterns.png)／[进度](../../../docs/screenshots/03-progress.png)／[候选](../../../docs/screenshots/04-candidates.png)／[修改](../../../docs/screenshots/05-review.png)／[导出](../../../docs/screenshots/06-export.png)；为原日语记录，不冒充中文截图。

## 5. E2E 发现并修复的问题

| 发现 | 原因 | 对策防复发 |
|---|---|---|
| 权利文字不显 | Fluent v3 checkbox/switch无label slot | fluent-field label/input |
| 返回开发堆栈 | launchSettings强制Development | 删除并JSON异常处理 |
| 候选三文字越界H05 | 强调含13字整句不换 | 强调≤10，全部安全宽自动缩，回归 |
| 孤立行“…很多”＋“哟” | 贪心换行 | 动态规划均宽，回归 |
| 强调互重 | 近词同行 | 按时序解除，回归 |
| 步骤上下缺 | 含滚动容器grid行最小高0 | 修布局 |

## 6. 需要判断的问题

1. **长视频预筛选（NFR-COST-03）与质量的取舍，v1.1已可在应用内选择**。项目页“生成设置”可选节省／标准／全文，当场看到各项发送给AI的量与预计费用。64分素材`spikes/media/long.mp4`使用真实Azure实测如下；ASR已缓存，所以耗时从AI筛选开始。

   | 项目 | 发给AI的量 | 输入token | 实际费用 | 最高分 | 时间 |
   |---|---|---|---|---|---|
   | 节省 | 约31%，二区间 | 12010 | ¥62.72 | 9.06 | 66秒 |
   | 标准默认 | 约46%，三区间 | 18710 | ¥63.97 | 8.52 | 85秒 |
   | 全文 | 100% | 34224 | ¥65.09 | 9.40 | 103秒 |

   大部分费用来自转写，每小时¥57，所以每小时仅差约¥2～3。合成语音音量变化小，真实视频的预筛命中情况会不同。希望减少遗漏时推荐全文。
2. **字幕显示速度S-01**：快语速逐字字幕会有超过每秒六字处，仅警告。此处保留原报告历史描述：“FR-TEL-02第三阶段LLM缩短已实现；v1.2.1强制最多延长0.5秒及片段末端”。可读性校正见§2.1。当前实现已经按§7、§17改为不使用AI缩短字幕，不应把这一历史描述理解成当前行为。

## 7. 未实现项目与限制（如实列出）

v1.1实现了v1.0未实现项目。v1.1补充中，上次列为未验证／未实现的以下项目也已完成。

| 项目 | 实现和验证 |
|---|---|
| P8快进真实画面 | NASA公共领域真实影片`spikes/media/rollout.webm`叠已知时间发言成五分素材，110秒压到9.5秒即11.6倍，两处发言仍1倍，正式硬QA全过。将输出重新交Azure Speech转写，保留发言文字100%回收，作为第三方复核 |
| 交叉淡变 FR-SWT-05 | 仅在源时间向前跳过三秒以上的场景转换处使用。P6 为 0.3 秒，由预设 scene_fade_sec 指定。声音硬切，前一个片段画面延长重叠量，因此时长不变。通过真实帧颜色确认前景→混合→后景，并确认连续部分不会混合。 |
| 循环语义 FR-SOL-06 | 对启用循环的模式，LLM 根据与开头句的连接性为五个结束位置评分，采用最高分。E2E 三个候选全部应用。 |
| 字幕原文TEL02 | 生成和自动修复都不AI缩短，保留迟疑、尾语、附和、重复，只改时间分合。未解速度留S01；装饰文字卡AI另处理且需批准 |
| 手修转写 FR-ASR-06 | 在检查修改页“修改转写”修改单词，时刻不变。字幕重新生成，修正也保存至缓存，已通过 E2E 确认。 |
| 笑声 FR-SIG-04 | 无单词的大声音中，只把约 3～8Hz 脉动的“哈哈哈”判为笑声；排除掌声一类稳定噪声和单次冲击，已用合成声音确认。 |
| 自动说话人相机 FR-SWT-01 | 依据各机麦克风声音，分配相对音量最大的说话人。共用麦克风无法判断时警告，退回主机说话人 1、相机 B 说话人 2 的默认值，允许手动修改；已用合成声音确认。 |

剩余限制：

| 范围 | 状态 |
|---|---|
| 全离线转写 | 没有，字幕必需，所以始终Azure AI Speech，声音发Azure |
| 真实笑声精度 | 只合成声验证，无真实笑掌声精度评价，缺评估数据 |
| 自动相机分配 | 仅各机独立麦，按脸位置分配未实现 |
| 拟声吐槽趣味 | 拟声规则、吐槽AI且需批，趣味用户目视判 |
| 循环SSIM | 仅LLM评价，PRD允许二选一 |
| QG01～04实素材质量 | 未通过，用户计划以后做不算完成；必要数据校正见PRD§15.6 |

## 8. Azure 资源

| 资源 | 内容 |
|---|---|
| 资源组 | rg-videocreator-jpe，Japan East |
| AI Services | videocreator-ai-40800，S0，自定义域，local auth禁 |
| 部署 | gpt-chat-latest，2026-10-07 Preview，GlobalStandard，100K TPM，自动升新默认版 |
| 角色 | 用户Cognitive Services User和Cognitive Services OpenAI User |
| 预算 | videocreator-monthly每月100000账单币种，实际80%与预测100%邮件 |

不再需要时可`az group delete -n rg-videocreator-jpe`删除。

## 9. 上下集（指定长度的系列）

把全部长片按**2～20集**或每集**1～60分**二选一拆成上／下集，三集加中集，四集以上第n集，应用原模式相同编辑。适用以发言为轴P1～4、P6、P7、P9、P10。[Series.cs](../../../src/VideoCreator.Core/Planning/Series.cs)、[SeriesTests.cs](../../../tests/VideoCreator.Tests/SeriesTests.cs)、[13-series.cs](../../../spikes/13-series.cs)。

| 阶段 | 内容 |
|---|---|
| 分章 | Structured Outputs AI按带行号转写分话题，返回各边界易分度（前自然结束且想续看）、精彩行、强调词。代码验行号精彩标题强调，出界去，标题数字专名只取转写已有。无AI或失败以TextTiling（Hearst1997，内容词字符bigram词汇连接）与话题转换词代 |
| 集数 | 指定集数则恰好该数；编辑后每集不足一分钟则拒并显上限。指定长度则编辑后总长／指定长四舍五入N，<2报错。UI立即显示估计每集；已有转写按相同分割算法扣静音，否则注明源时长 |
| 分界 | 只句边界，动态规划最小化每集长偏均长平方减边界优度：词汇变、间隔、转题词、说话人变、AI易分度。容差±25%，无解放±40%并显示。非AI章边且非转题词，即集数多过话题时，警告哪两集间及时间。完整话结／承前／笑黑场／缺句点补充见§14 |
| 结构 | 每集开头先出该集一精彩句，第二起加前集末1～3句“上集回顾”，非末集尾放下集精彩“下集待续”。正文静音跳切推近、逐章标题，字幕强调同通常 |
| 覆盖 | 正文不重叠，每词恰属一集正文，回顾预告有意重复 |
| 长片导出 | ①倒序先出回顾预告按各连续向前组独立输入，以-ss准确seek，否则共享会为先播段缓存后段帧。②数百clip filtergraph超Windows32767字，用-/filter_complex文件。③同输入多片用split/asplit，快约25%。④预览直接半分辨率合成，ASS PlayRes同布局。⑤安全区实测用宽文字及均匀抽样最多160件 |
| QA | H-01 使用每集平均长度 ±30% 的范围。系列名称可能涉及其他集，所以 H-07 与全部转写比对；“第2集”等标签不当作事实主张。此处历史编号 S-10 的 YouTube 章节条件为从 00:00 开始、至少三章、每章至少十秒，当前已改为 S-14。S-11 使用不超过 60 分钟的 TikTok 实现上限，官方依据尚待确认，见 PRD Q-9。 |
| 元数据 | 标题加上集等，章<10秒并前满足章条件，各集链接填写栏。竖片>3分YouTube非Shorts，不加#Shorts |

### 验证（真实视频、真实 Azure，`dotnet run spikes/13-series.cs`）

素材为64分LLM台本TTS测试片的前30分。每话题以“接下来的企划是……”起，位置作19个正解话题边界。

| 项目 | 结果 |
|---|---|
| 15分×2分界 | 15:22恰正解，0.0秒；时钟15:00差22.8秒且切“今天”的词中 |
| AI分章 | 19与正解同，Pk0.000/WindowDiff0.000 |
| 无AI词汇间隔转题 | 16:45同正解，Pk0.115 |
| 无AI且去转题词 | 16:51差6秒，Pk0.384，无转题词无AI精度下降，为真实限制 |
| 覆盖 | 5563词恰属一正文 |
| ffprobe | 上13:35下13:31，编辑后26.9分均分、静音剪约三分；1080×1920 |
| ebur128独立测量 | 两者−14.0LUFS，TP−1.4/−1.5dBTP，非导出用loudnorm |
| QA | 两集全硬过，历史S10章9/10、S11过 |
| 第三方重转写 | 正文字符bigram召回98.4%/98.5%含ASR变动；先出回顾下集预告各位置100%听到 |
| seek正确 | 单测独立seek与旧单输入输出视频逐bit相同 |
| ARM64时间 | 生成10分含ASR章脸和两预览；每15分弱正式约11分含两遍音QA，是split前 |

#### 按集数验证（同30分三集，`dotnet run spikes/13-series.cs spikes/media/long30.mp4 parts=3`）

| 项目 | 结果 |
|---|---|
| 数量 | 恰三上中下 |
| 分界 | 10:33/20:06均正解差0；时钟10/20分切句中及“バ”词中 |
| 事前估计比ffprobe | 估8:57，实9:05/8:54/9:22，即+2%/−1%/+5% |
| ebur128 | 全−14.0，TP−1.4～−1.5 |
| 重转写 | 正文98.1%/98.5%/98.2%，先出回顾预告位置全部正确 |
| QA | 全硬过，历史S10六～七章、S11过 |
| 非法 | 集数长度同传422，超上限422，UI告上限 |
| 时间 | 缓存ASR生成五分；约九分每条导4.3～4.9分，为split后 |

## 10. 片头、片尾与场景转换吐槽卡

可插频道片头尾和像暂停一样显示2～3秒、默认2.5秒的吐槽卡。[Packaging.cs](../../../src/VideoCreator.Core/Planning/Packaging.cs)负责映射注册布局，[Renderer.cs](../../../src/VideoCreator.Core/Rendering/Renderer.cs)渲染，[PackagingTests.cs](../../../tests/VideoCreator.Tests/PackagingTests.cs)验证。

P3 AI吐槽生成时以tsukkomi_style选telop（不暂停，红底叠加）、card（发言后暂停卡）或默认auto。[TsukkomiPlacement](../../../src/VideoCreator.Core/Planning/Packaging.cs)逐条判，只有暂停不打断内容处才卡：①距正文端≥3秒；②对象字幕为完整陈述／疑问，SplitCues.Closure≥0.6，不停在“虽然”“这个”等半句；③下字幕非“对对”“因此”等应答连接，即Continuation不能≤−0.6；④距场景卡片头他吐槽卡≥8秒；⑤卡总长≤正文10%。满足②③按完整度＋至下发言间隔最高一秒排序，以④⑤选；落选不删，转叠加。候选备注逐条理由。[PackagingTests.cs](../../../tests/VideoCreator.Tests/PackagingTests.cs)各规则反例、[V11Tests.cs](../../../tests/VideoCreator.Tests/V11Tests.cs)三模式与批准门、[ProjectGenerationSettingsTests.cs](../../../tests/VideoCreator.Tests/ProjectGenerationSettingsTests.cs)保存均验证。

card及auto卡若位于clip中途则拆，维持源画声、相机／屏幕跟踪、PiP起始。卡中停原声，独立合成upbeat短淡入出，不依正文BGM选择；正文BGM静音避免双播。有注册图则图上文字，无图则原创全屏背景内黄框弹字。检查页卡区编辑批准。旧保存计划不自动转换叠字幕／卡声，重生成才新效果；旧注册一秒生成时修为两秒。

追加验证对有／无图真实FFmpeg测加2.5秒、暂停、恢复帧同无插入、文字像素、专用BGM及原声停复。另自动测倍速、独立音、PiP、水平垂直跟踪拆分。参考YouTube指定段未直接看，本规格基于用户结构描述，未复制图或曲。

| 阶段 | 内容 |
|---|---|
| 注册 | 在“3.生成”注册文件夹或文件完整路径，所有项目共用，必须确认权利。按文件名Opening/Intro/オープニング、Closing/Ending/Outro/エンディング区分，不可识别不猜而显示原因。PNG/JPG为卡图。注册以ffprobe测尺寸时长、ebur128测积分响度并保存 |
| 版本 | 同时有9:16/16:9选更近1080×1920者；比例不同则同正文用模糊背景装入 |
| 位置 | 片头放冷开场先出之后，这是日式综艺常见顺序，无先出则最前；片尾最后。短片卡放正文源时间向前跳≥3秒接点，同SWT05交叉定义，以卡代交叉；系列放章头，若四秒内无接点则拆clip造接点，即持续说话亦可转题插。卡间≥8秒，距正文端≥3秒，短片最多三卡 |
| 卡片 | 停前画略暗，注册图全屏。Renderer.CardCovers按与屏幕比例对数差绝对值<0.1时填满裁，其余同图模糊背景装全，透明处见停帧；无图有专用BGM则原创全屏内黄框。黄色粗libass自动缩弹字，播专用BGM与hit。无论有图均可AI从前后字幕提≤14字代观众反应，如“为什么呀！”吐槽、“很棒！”称赞、共鸣惊讶期待；禁原样抄出演者、重复卡吐槽、禁词及字幕无数字专名；日语“イケ”“マジ”“ヤバ”等口语反应不视专名。**批准前禁导**。无AI或不可用以“好，接下来！”等中性模板。检查页可改批逐开关。旧有图空字候选重生成才填字 |
| 时间 | 字幕SE章以正文时间，统一InsertMap按插入后移，ASS/SRT/SE/BGM静音/章/缩略图/QA脸共用。跨插入文字拆两侧，避免压卡片片头 |
| 声音 | 片头尾先测正文mix并匹配响度，再整体−14LUFS。实测若先绝对−14，正文未正规时很小导致片头大12LU，已修。BGM在片头尾停，片尾头淡出 |
| 帧精度 | 原逐clip舍帧且concat取声画较长，导致每条约+17ms，25.95计划成26.10，几百切系列可差数秒。改累计时间定帧数，音频样本48k/30=1600每帧同长；AAC头priming−0.011秒致下段延约0.1，重采样后重打时戳解 |
| QA | H-01 对正文应用模式时长范围，对包含插入物的整体应用 Shorts 180 秒上限，同时确认文件长度与计划一致。H-07、H-08 包括卡片文字；H-09 检查插入素材权利。短片将片头放最前时 S-03 警告，因为观众可能开头数秒就离开。S-06 排除卡片、片头、片尾内的静止画面。S-12 对素材小于输出而被放大时警告。 |

### 验证

- [PackagingTests.cs](../../../tests/VideoCreator.Tests/PackagingTests.cs) 验证映射与拆分、文件名识别及版本选择、冷开场后／跳切／章节／没有接点章节的拆分与布局、QA、编辑继承、ASS/SRT 时刻。真实 FFmpeg 输出后，以 ffprobe 验证视频长度与计划在 ±1 帧内；以 scdet 检查切换时刻在 ±0.05 秒内；测量卡片时刻黄色文字像素、片头片尾颜色、片头与正文响度差小于 4 LU，以及所有硬性 QA 通过。
- 真实`C:\Work\HubRadio\OpeningClosing`为720p、AAC96kHz、文件12.1秒视频12.0；自动9:16，−14.0LUFS/−1.4dBTP，切2.767/14.867/39.033秒，对计划2.76/14.86/39.05，S12警告1.5倍。
- iPhone 4K 实录的四分钟节选使用真实 Azure 转写，三个短片候选都添加片头片尾。有先出片段时片头放在其后，没有时由 S-03 警告。上下集中，下集章头距原接点 16 秒，因此在章头拆分插卡，界面修改文字再导出，全部硬项通过：H-03 为 −14.5 LUFS，H-04 为 −1.0 dBTP。

限制：依规未下载YouTube参考片；先出→OP、转题短吐槽按类型惯例。注册1080×1920片头尾可免放大模糊。

### 小尺寸片头片尾的画质提升

注册片头尾低于1080×1920，如720×1280时，注册一次生成提升版，导出复用。[SuperResolution.cs](../../../src/VideoCreator.Core/Media/SuperResolution.cs)、Packaging.cs的BrandKitEnhancer、[BrandKitWorker.cs](../../../src/VideoCreator.App/BrandKitWorker.cs)实现，[UpscaleTests.cs](../../../tests/VideoCreator.Tests/UpscaleTests.cs)、[14-upscale.cs](../../../spikes/14-upscale.cs)验证。

**以测量决定方法**。有正解超分标准评价，将720×1280缩2/3，各法1.5倍回原，比FFmpeg libvmaf NEG（不因锐化虚增）、XPSNR、SSIM，压缩条件同。

| 片头方法 | VMAF-NEG | XPSNR-Y | SSIM | 判断 |
|---|---|---|---|---|
| 旧lanczos | 92.77 | 44.29 | 0.99792 | 基准 |
| lanczos＋CAS0.4锐化 | 94.81 | 39.86 | 0.99575 | VMAF升但XPSNR−4.4dB，更离原 |
| spline36 | 92.33 | 44.17 | 0.99792 | 无改善 |
| GPU libplacebo ewa_lanczossharp | 普通VMAF93.40，lanczos93.51 | — | 0.99686，lanczos0.99819 | 无改善 |
| 单Real-ESRGAN animevideov3 | 86.53 | 24.50 | 0.99275 | 字锐但改粉彩渐变亮度成斑，差图确认 |
| **lanczos＋模型与lanczos差高频×0.4，采用** | **93.88** | **44.70** | **0.99794** | 三指标均不低基准 |

片尾同设VMAF-NEG+1.11、XPSNR+0.71dB、SSIM+0.00007，确认非过拟单片。

| 阶段 | 内容 |
|---|---|
| 机制 | 色度低频色亮渐变取源YUV lanczos，仅亮细节用4倍模型面积平均降后差的高频高斯σ0.7乘0.4加入。单RGB往返已使XPSNR−4dB，故色不经模型 |
| 逐素材采用 | 注册片中央四秒同评价，VMAF-NEG不降且XPSNR降低≤0.1dB才采，比不忠实则原版并显因 |
| 执行 | 注册后后台显进度，ONNX DirectML GPU720×1280推理0.13秒帧，CPU6.3；无DX12 GPU退CPU。x264 CRF10几近无损，音频不变全部帧保。按名大小修改时缓存，重注册即完；缓存丢用原版 |
| 显示与 QA | 注册页显示“已提升至 1080×1920（GPU，89 秒；本视频实测 VMAF +1.1、XPSNR +0.35dB）”。已经提升画质时 S-12 通过，仍按原片放大时警告。 |
| 依赖 | OnnxRuntime改DirectML1.24.4含CPU，含脸全部旧测过。get-tools.ps1从Hugging Face取模型验SHA256，源Real-ESRGAN v0.2.5.0 BSD3-Clause |

实测发现修复：①progress?.Report(++n)在progress null时连++n亦不算，正常误零帧；②FFmpeg把父stdin当键导致即停，加-nostdin；③4400万元素拷贝逐像素转换0.69秒帧，改直传输出数组及逐行并行0.28，bit相同。

UI注册真实文件夹片头89秒采用（+1.1/+0.35dB），片尾98秒采用（+1.3/+0.84dB）；真实录制生成至导用1080×1920提升版，全硬与S12过。

限制：放大不恢复丢细节，VMAF约+1有限；最佳从制作源重导1080×1920。卡图缩小叠局部不属提升对象。

## 11. 多素材自动同步与屏幕讲解（P10：录屏＋讲者画中画）

同场相机PC录屏无需人工，以**小于一帧**对齐，自动分角色，在屏幕＋圆讲者和讲者全屏间切。[AudioSync.cs](../../../src/VideoCreator.Core/Audio/AudioSync.cs)、[ScreenActivity.cs](../../../src/VideoCreator.Core/Media/ScreenActivity.cs)、[ScreenPlanner.cs](../../../src/VideoCreator.Core/Planning/ScreenPlanner.cs)、Renderer.AppendScreen、[P10.json](../../../presets/P10.json)，[MultiSourceTests.cs](../../../tests/VideoCreator.Tests/MultiSourceTests.cs)验证。

### 同步（P7/P10共用）

旧法开头90秒、±20秒仅一次GCC-PHAT。已知偏差测出两类**静默错误**，现并测漂移：

| 素材 | 正解 | 旧 | 当前 |
|---|---|---|---|
| MP4/AAC基准声比画晚0.28秒，OBS手机常见 | +2秒 | +2.279，差8帧仍称可信 | +2.0002 |
| 相机晚35秒开录 | −35 | 不匹配丢相机 | −34.9999 |
| 150秒快200ppm | +1.5秒/−200ppm | 窗内变判不了 | +1.4999/−209ppm，端差1.5ms |
| 20分60ppm常见机 | 尾差72ms | 无补 | −62.5ppm，尾差3.1ms |
| 真实双人、带限噪声小声机 | +4.217 | — | +4.2168 |
| 无关音 | — | 拒 | 拒 |

| 阶段 | 方法 |
|---|---|
| 时间基准 | aresample=async=1:first_pts=0按时戳PCM，迟音补静、途中缺补；sample i即i/rate文件时，与trim/atrim同。ASR16k同，单素材亦不字幕剪切错位 |
| 粗搜 | 全长50Hz对数能包络流算不存全PCM，GCC-PHAT；范围全素材，含短片始于长基准后半 |
| 细搜 | 全重叠最多24个10秒8k窗GCC-PHAT，峰抛物线内插至亚样本 |
| 时钟 | 各窗偏差Theil–Sen直线，取线内8ms再拟；全重叠斜率变化<2ms视0 |
| 可信 | 不只峰高；漂移时峰14～70仍位置±2ms。≥3独立窗同线、≥半数一致才采用；无关音峰不上同线 |
| 长clip | 漂移片按半帧内长度拆重新对齐，60ppm每4.6分 |
| 无音 | 两文件有creation_time用之，警告约±1秒；无则旧法同始并警告。手填偏秒始终优先 |
| 显示 | 素材区域显示“声音同步：−7300.0 ms，5/5 区间一致，残差 0 ms”。有漂移时显示“每小时补偿 N ms”，对应 FR-ING-05。 |

**第三方验证**：同文件SciPy signal.correlate普通互相关，不用本timeline滤镜，显式加ffprobe stream start，全同+2.0000/−35.0000/−7.3000，20分−62.4ppm对实现−62.5。当前包feed无BBC audio-offset-finder，未用。

### 录屏与讲者（P10）

| 阶段 | 方法 |
|---|---|
| 角色 | 各proxy2fps640×360灰度比几乎不变0.5秒比例，6处×4秒脸检率；静≥30%且最静为screen，脸扣分；余脸最多为讲者。显式screen角色优先。显示依据如presenter静1%脸100%、screen静82%脸0 |
| 屏幕动 | 仅计同时异于前帧及前二帧像素，闪光标或恢复highlight非动。单字19～50像素，阈12，静0～2；存8×8块变化外框及每2秒16×9亮度梯度细节 |
| 切换 | 每0.25秒：−0.6至+1秒变化或句指“这里点击按钮”等→屏幕，否则持续4秒→讲者全屏。首2秒讲者除变化；<2秒吸邻，切贴近跳切，延长screen方向可2秒 |
| 缩放 | 9:16全屏缩1080字不可读，按屏幕带比例截变化范围75百分位加1.3余白；最多1.5倍免糊。中心1秒平滑限速跟随；整屏幻灯切则全显 |
| 布局 | 屏幕带在顶标题与底强调字幕之间，避免字压内容；余区模糊暗屏副本。讲者圆白边≤带高42%，geq仅一帧mask循环 |
| 画中画角 | 四角逆zoom/pan回源坐标，算变化重叠＋细节文字UI总量，最小者，不盖幻灯题或输入行；讲者脸中位中心含头肩方裁 |
| QA | H-09 的权利检查包括画中画素材，防止只在画中画中显示的素材漏掉权利确认。 |

### 验证

- `Screen_and_presenter_are_synced_switched_and_composited`合成嵌真值并实测输出：基准相机已知白闪beep；录屏晚2.5秒、声再晚0.3，右上输入及beep同品红。解码测beep按计划±20ms；每beep当前全屏或画中画闪≤1帧；screen marker≤1帧；screen区圆蓝讲者带灰屏，讲者区全蓝。
- 单测迟音、迟35秒、200ppm、鲁棒直线、漂移重齐、单字动非光标、画中画避幻灯字、带不压文字、系列seek输入声及画中画。
- 真实API不指定角色，用真实ASR/AI：讲者脸照加头动传感噪、真实对话；screen晚7.3秒声晚0.2，编辑器日文→幻灯→终端命令。角色对，同步−7.300、5/5残0。screen开始比编辑输入早0.8、幻灯早2.6（贴旧剪点）、终端恰开始；输入时画中画相反角且不盖题，三候选硬全过。
- 修复：①原阈92漏360p19～50像素单字，改12且排闪；②切比剪早1.3秒造碎片，放宽延screen贴剪；③幻灯无位置数据盖题，加细节图；④系列别机音频从基准解析输入遇audio:ID失败，影响P7，已修。

限制：只以动细节猜重要，不理解哪个按钮；无声无拍摄时刻按同始，需手填偏秒；布局面向9:16。

## 12. 综艺外观（P1/P3）、屏蔽词与缩略图候选

依据两条日式综艺（电视剪辑与YouTuber企划各约14分）实测。所有帧每2秒、峰谷段4fps用Windows OCR，M PLUS1p校正字高，OCR高约0.63×Fontsize，也计算热图与评论时间相关。华丽感**不是快切**，ASL3.2～3.7秒，而是常驻信息、信息／发言／金句约1:3:5的强尺寸差、屏蔽词与编辑吐槽。

| 项目 | 仅telop.look=variety，P1/P3版1.1.0 |
|---|---|
| 字幕 | 旧80→124px，ScaleX85%，描边10，每行旧14→10字；外黑模糊halo为另层透明字粗边加\blur10，白摄影棚亦能读 |
| 强调红色 | AI从转写逐字提有效强调文字，在字幕中红，≥2字、长词先；逐字转义后插色标签 |
| 金句 | 强调黄210px黑边14影4；吐槽红底BorderStyle3白180；拟声160；一次仅一，后来者截已显≥0.8秒前者，否则不出后来，同刻强调>吐槽>拟声 |
| 标题 | 钩子后至尾常驻早已实现，只改Q.条浅蓝带深蓝56px |
| 尺寸表 | 在 AssWriter.Metrics、AssWriter.Band 一处统一字号、描边、ScaleX 和垂直显示带，让绘制、自动缩小、脸部重叠检查 S-05 使用相同值；standard 样式维持原值。 |
| 屏蔽 | 生成最多20词每30字，NFKC大小写不敏感跨ASR词界匹配；先屏蔽转写再制字幕即使跨两条不漏。AI钩子标题强调吐槽卡章名均●。词区间原声静音叠1kHz，只该区、在BGM duck前 |
| 新硬项 H-10 | 仅指定屏蔽词时显示。①有效文字与卡片中无屏蔽词残留，包括手动改回的内容；②独立从原转写重新计算区间，确认全部被提示音覆盖。任一不满足即不能导出。 |
| 缩略图 | 金句强调／已批吐槽／拟声／计数全显时start+0.45候选，在正式检脸选脸最大两张间≥2秒，再旧开头一。无模型按时序，variety饱和1.6倍，参考缩略比正文1.7～1.8 |

### 验证

- **文字频度与避脸**：强调词不仅在第一次匹配时出现，而是在每次出现的时刻显示；空闲区间以原发言短语按约四秒间隔补充，不改变字幕生成或 SRT。新计划的 `telop_faces` 保存素材坐标分析结果，`TelopLayout` 在绘制时重新安排，所以检查页修改文字、时刻或启用状态时也会重判。布局考虑脸部跟随、fit、模糊背景、缩放、screen、画中画、剪切和交叉淡变，在整段显示期间的脸部运动包络外再保护 32px，同时避开录屏显示带。找不到安全位置或分析范围缺失时，只隐藏装饰文字并在候选备注中说明原因。没有 `telop_faces` 的旧计划保留原布局，需重生成才应用。2fps 检测仍有漏脸、急动限制；S-05 在正式视频中独立重新检测，包括标题与 SFX 在开始附近、中央、结束附近的矩形相交。`TelopLayoutTests` 验证同一 30 秒输入的频度、坐标转换、缺失数据、运动、剪切边界，以及 standard/variety 真实 libass 像素与脸不相交。
- **他预设不变**：改前git worktree与后全十预设EditPlan文字音和ASS逐byte比，仅P1/P3不同，P2/P4～10同。
- **尺寸同libass**：1080×1920字幕强调吐槽真实像素外框在Band及安全区；几何验字幕金句、计数金句带不重。发现吐槽底与字幕1.2px重，金句上移10px。
- **行字数**：八字提案P3二十八条十一超6字秒，旧十八条六；十字P3六条P1七条与旧同，故十字，124px85%无缩可容。
- **第三方Windows OCR可读**：真实P3叠白棚，缩Shorts列表270px宽；字幕高49～51→76～81，强调61→129px；旧误“1万円なんで”，新正确。
- **beep**：440Hz发言2～3秒屏蔽，解码PCM Goertzel测区内440Hz比外低≥40dB，1k比外高≥40dB。
- **完整屏蔽**：真实转写1万円跨词，指定全角１万円，文字/SRT/ASS/AI钩子全无残，H10过，手还原或去beep即失败。
- 真实应用API21词或31字422，Edge Playwright验证输入保存重载。

限制：红仅AI选强调非语法主语；halo不论亮暗常加，暗不明显。实测参考横长片，转竖以宽比例换算。

## 13. 视频分析服务（Video Indexer / Content Understanding）验证

**问题**：VI或CU（GA2025-11-01）可提升精度吗？**答案**：只证明笑掌声改善，仅此VI实现，默认关。转写镜头画面语义未优或无法证明。

> 此节保留历史实验完整结果。当前仓库已移除VI集成及部分实验文件；以下标为不可用的路径仅历史名称，不是可打开资源。

### 方法（正解独立于分析服务）

| 评集 | 真值制作 | 测项 |
|---|---|---|
| bench.mp4 515秒，历史spikes/15a-make-ai-bench.cs（不可用） | Hub电台真实固定一机，远／左／右裁已知时接44边：硬41含同构图跳11、溶2暗1；八文字已知时烧。音为Google FLEURS ja_jp test真实34句及人工转写，夹ESC-50真实笑5掌4混淆6 | CER、发言始、镜头F1±0.5秒、笑掌、屏字 |
| events.mp4 1475秒，历史spikes/15b-make-event-bench.cs（不可用） | ESC-50全部笑40掌40，咳敲狗婴打字步40，夹FLEURS120句 | 扩件数验证笑掌非偶然 |
| Hub真实18分 | 无正解 | 三法一致、CU描述、时间费用 |

三法同文件，保存VI Index JSON、CU result.contents、当前transcript/signals，用历史`spikes/15-video-analysis.cs`评分（现不可用）。CER先NFKC去符号、汉数转阿数，一万/1万→10000。VI Advanced音画ja-JP；CU基prebuilt-video区间分析器，gpt-4.1及日语摘要、屏字、非语音、精彩分字段。

发现真值一错：FLEURS静段约−45dB噪，以峰30dB内定发言导致早最多2.5秒，三法中位误1.4秒暴露。改噪底+12dB，仅重真值，声音hash同证明素材不变，始误中位0.08～0.13。

### bench.mp4 结果

| 项目 | 当前 | VI | CU |
|---|---|---|---|
| CER | **4.74%** | 9.74% | **4.34%** |
| 词时刻 | 有 | **无，仅短语** | 有 |
| 开始误中位/90% | 0.12/0.36秒 | 0.08/0.33 | 0.13/12.7，接缝缺 |
| 镜头F1±0.5秒 | 83%，P100% | 84%，P100% | 85%，P97% |
| 同构图跳 | 0/11 | 0/11 | 0/11 |
| 溶暗±1秒 | 0/3 | 1/3 | 2/3 |
| 五笑 | P29/R80 | P75/R60 | P50/R20，LLM猜 |
| 四掌 | 不支持 | **4/4** | 0/4 |
| 八屏字 | 不支持 | 7/8 | 7/8 |

### events.mp4 笑与掌（120事件）

| 方法 | 笑召回 | 笑精确 | 非笑80误判笑 | 掌F1 |
|---|---|---|---|---|
| 当前真实行为 | 23%，9/40 | 60% | 4 | 无 |
| 当前分类器全段 | 68%，27/40 | 39% | **21**，狗11掌9敲5等 | 无 |
| **VI应用仅16kFLAC音** | **68%，27/40** | **82%** | **4婴哭** | **79%，P100%** |

- 当前低召回不是分类器，而LaughDetector.DetectAsync仅首60区间，本素材1475中首310秒。去上限同VI召回但误42对6，狗掌加“哈哈”。去上限改变默认且增误，未做。
- VI对当前真实召回Fisher p=1.0×10⁻⁴。VI上传MP4与仅声音同精度，笑F175/74，掌81/79%。

### 真实录制（18分）

- CU与当前转写差2.6%，同Azure Speech系；VI差16.1%。
- 固定机正解镜头0，当前VI0，CU误十。
- 当前五笑VI四，仅一重；无真值不能判。已备前后音与表供听判，见剩余验证。
- CU十九段全为“两位男性坐椅讨论……”＋话摘要，精彩6～8近恒，不为现全文AI筛选添线索。

### 费用时间（Retail Prices API，JPY japaneast，2026-11-08）

| 方法 | 单价 | 18分 | 时间 |
|---|---|---|---|
| 当前Fast Transcription | ¥56.96/小时 | ¥17 | 20秒 |
| VI Advanced Audio采用路径 | ¥6.33/分 | ¥114 | 9～11分，与AI并行 |
| VI音画Advanced | ¥23.73/分 | ¥427 | 九分 |
| CU抽帧¥158/小时＋上下文¥0.16/1k token＋gpt4.1 | 约¥550/小时 | ¥165 | 13分 |

CU gpt4.1仅50K TPM时八分片429使全Job失败，升300K成功。月一万日元预算VI笑掌约26小时新视频，同源缓存再生成不计费。

### 判断与实现

| 分析 | 判断 | 理由 |
|---|---|---|
| 笑掌 | 任意VI默认关，历史决定 | 误检1/7且掌，误会直接成哈哈或反应镜头 |
| 转写 | 保当前 | VI CER两倍无词时，CU同系同等，1520字差6 |
| 镜头 | 保当前 | 差1～2，跳全0/11，CU真实误 |
| OCR | 不采 | 7/8虽佳，应用无用途 |
| CU画义 | 不采 | 对谈不超转写、分平，费用10倍时间40倍，无人工精彩真值不可称改善 |

历史实现`src/VideoCreator.Core/Audio/VideoIndexerAudioEvents.cs`（现不可用）：ARM generateAccessToken无密钥，AdvancedAudio/ja-JP私有上传→待完→audioEffects→**删上传**。当Enabled=true，PipelineRunner信号后起、与AI并行、计划前取，逐素材audio_events_video-indexer.json缓存，错警告退原不阻生成。UI显目标估实费。历史`infra/video-analysis.bicep`（现不可用）含VI账户、存储禁共享key默认拒仅可信Azure、用户分配MI、CU试验模型。历史`AudioEventTests.cs`现不可用。

### 剩余验证与限制

- **真实笑正确性**无真值；历史15-video-analysis.cs可制前后1.5秒听判表“笑／非笑”，人工齐可算精确；当前该程序已不可用。
- ESC-50单独笑不同对话中边说小笑。VI掌CrowdReactions不分欢呼，尚无掌拟声等消费功能。
- 画面语义可能对游戏、体育、作业演示等画面主导素材有用，但此次只测对谈，未按人工精彩真值如PRD§14.2 HiSum衡量前不能判。
- 组织策略把存储publicNetworkAccess改Disabled，但VI仍成功，配置为默认拒、仅可信Azure服务。

## 14. 将上下集分界对齐完整话语边界（SplitCues）

**问题**：真实Hub电台Ep.014十八分二集，上集以“……我觉得一定是某处，还有一件事呢这个有点”结束，下集“有点有趣的感想……”开始，切在话中。

**原数据原因**：虽然仅句边界，但Azure Fast Transcription将“……我觉得”和“还有一件……”无句点并一句。词时刻显示“思います”后间0.36秒，那才真边界。AI给下一“有点有趣”行易分9，原边界优度未考虑前句完整，所以采。固定相机零镜头，无视觉线索。

### 调查（YouTube/TikTok分法）

| 来源 | 发现 | 反映 |
|---|---|---|
| 投稿YouTube章YTSeg EACL2024及§9 | 章在话题边，前话说完之后 | 下表完整度 |
| TikTok Part1/2指导 | 前集问或铺垫完成才结束，答案留后，不能断铺垫中 | 问视完整，半句强减 |
| J/L声画错切 | 笑反应结束后切，暗场转场示边 | 笑后加，笑中减，黑场镜头加 |
| AutoShot短片约30%渐变、PySceneDetect F1<0.6 | scdet不拾溶暗，§13亦0/3 | blackdetect暗，溶未做 |

### 机制（[SplitCues.cs](../../../src/VideoCreator.Core/Planning/SplitCues.cs)，仅系列）

| 线索 | 判断 | 边界分 |
|---|---|---|
| 补缺句末Resegment | 同句词间左以ます/です/ました/でしょう强句末且间≥0.3秒拆，不改词，每词恰一新句；句文字不同词串联则不做 | 增候选 |
| 前末Closure | 完整+1，问+0.6，けど。等欲言未尽0，逗号接助けど/て/で/から、指词／填充これ/ちょっと/えっと等半句−1 | +0.3×完整，断句−2，强于AI最大+1.6 |
| 后始Continuation | で/だから/それで/そうですね承前−1，跳はい/まあ前置；指词始−0.6 | ×0.8，口语だから可仅前置故较弱 |
| 收束 | ということで/以上/ありがとうございました结尾 | +0.4 |
| 笑 | 边前恰结束／跨边 | +0.3/−0.5 |
| 画面 | blackdetect=d=0.2:pix_th=0.10同scdet解码／镜头 | +0.8/+0.4 |

series-1.1.0提示亦明确只有前行完整陈述问句、后行不承前才高分。容尺内仍无完整则UI警告哪集话中结束，理由显示完整句、黑场、笑后等。缓存改signals.v2.json，下次自动重析黑场，18分约+2秒。

### 验证（[16-split-cues.cs](../../../spikes/16-split-cues.cs)、[16-split-cues-check.py](../../../spikes/16-split-cues-check.py)）

| 项目 | 前 | 后 |
|---|---|---|
| Ep014两集AI重现失败 | 8:32“这个有点｜有点有趣”话中 | **8:30.8“我觉得一定是某处｜还有一件事呢”** |
| 两集无AI | 9:26“因为正度过｜于是完全”承前 | 8:30.8同上 |
| 四集无AI | 第二“这个｜ど”话中 | 三处均完整且后不承前，第二8:30.8 |
| 合成30分回归，接下企划真值 | 16:45.9正确 | 同，AI有无均 |
| janome/IPADIC全313边可共同判者 | — | 224/231=97%，七异为ぜひぜひ/そうそう等口语完整省略 |
| 前句janome独立 | 两集AI以副词ちょっと结束，半句 | 助动词ます原形，完整 |
| 合成52秒已知硬2溶2暗2 | scdet暗0/2 | 加blackdetect暗2/2、误0 |
| [SplitCuesTests.cs](../../../tests/VideoCreator.Tests/SplitCuesTests.cs) | 关线索两分界测试失败，证实起效 | 全238过 |

可输出各分界前后十秒音`spikes/out/split-cues/cut_<方法>_<before|after>_<n>.wav`供听比。

### Azure 使用（月预算十万日元）

无额外资源。原因是语言缺句末和评分不看，§13 VI/CU镜头F183～85同scdet；固定机画面无法供分界。仅既有AI章改提示，费用不变。

### 限制

- 溶解合成0/2不能检，渐变多的已编辑片需TransNetV2等未实现模型。
- 合成一硬切两近亮色scdet7.1低旧阈10未检，阈在§13已评未改。
- 权重手定，缺日语口语分界真值。完整度对形态分析、分界对真实失败和合成确认，但未测想看续集。
- 笑用当前低精分类器，权重小。

## 15. 仅按场景话题边界分集（场景优先）

至§14长度先，无话题边就选范围内容易语言断处，例如Azure七分Copilot三分十片切中央五。改三点：

1. **场景优先**：[Series.cs](../../../src/VideoCreator.Core/Planning/Series.cs) SceneStrength/Split只以话题变证据（词汇变、转题词、AI章始、黑场）造场景边，先仅这些候选分。可偏目标30～170%，但每条≥60秒，超±25%告UI。间隔完整笑镜头仅用于边界评分、不算场景证据；边数不足集数才退旧长优先并警告话中分。
2. **语义话题**：[TopicEmbedding.cs](../../../src/VideoCreator.Core/Planning/TopicEmbedding.cs)以既有Foundry text-embedding-3-large各句向量，前后30/60/90秒均向量TextTiling谷与词汇谷0.7:0.3混。EmbeddingDeployment空或离线仅词。词汇会同题改写ASR摇摆生谷、异题共同词谷浅；存cache/<素材>/topic_embed.json，18分<¥1。仅送既有Foundry，provider none/LocalOnly不启。
3. **多窗词谷**：旧45秒少词偶然谷，改30/60/90平均。

### 验证

| 方法 | 结果 |
|---|---|
| [TopicFidelityTests.cs](../../../tests/VideoCreator.Tests/TopicFidelityTests.cs)无预告有共词，中央放完整长间笑收束陷阱，5/5、6/4、4/6、7/3、3/7与两三分法 | 旧SceneFirst=false七例三落时钟中陷阱，已固定复现；新七均真题始±一句。仅词此合成偶然谷会错，需语义，单测代向量，真模型下面确认 |
| 单题十分钟 | 警告无边，明确话中分 |
| Ep014真实18分，别AI gpt4.1隐条件前后90秒0～10同题，[18-scene-check.cs](../../../spikes/18-scene-check.cs) | 全54处明确异≤2为11%，同≥7为63%，随机九成话中。两集8:31前后同位置2～3；三集旧6:35分7同题、13:04分2，新7:09分1、12:59分2；四集新三处全2，均异题 |
| 全测试 | 247/247 |

仅前后90秒cos不足区分同领域技术话题，0.81～0.88差小，故嵌入只造谷，另AI盲判核实。

### 限制

- 几乎同题连续时词语义差小也会成候选，会警告。
- 可大偏如7/3分，要均衡请选择合话题的集数长度。
- 仅一个AI判，未测人工一致；YTSeg无日语。

### 15.1 全文交给 AI 按时间码分章是否可行（包括非技术话题）

**可行，但只说章会过细，要要求主话题改变处**。应用原有全文AI[OutlineAsync](../../../src/VideoCreator.Core/Planning/Series.cs)，未区分细章主题；现返回major主题变／false同题小节，不把小节当场景，提示明确含非技术闲聊日常兴趣工作。

[19-topic-methods.cs](../../../spikes/19-topic-methods.cs)验证。gpt4.1分别写各题口语对话，大半非技术：料理、夜哭、职业棒球、家计、京都游、睡眠、电影、夏祭、婚恋、猫、转职、年金、登山、搬家、韩剧、饮食和运动相邻近题、副业、町内会、足球；技术仅Azure/Copilot一题。保留真始连三条38～48分、每六转换，判者不见真值。

| 检主题始±3句 | 精确 | 召回 | F1 |
|---|---|---|---|
| 仅词离线 | 30% | 83% | 44% |
| 词＋嵌入 | 77% | 94% | 85% |
| AI行号全部细章 | 23% | 100% | 38% |
| AI时间码全部细章 | 23% | 100% | 38% |
| **AI行号仅major** | **100%** | 94% | **97%** |
| AI时间码仅主变 | 100% | 100% | 100% |

- AI九成以上恰真始差零句，mm:ss与最近句头均差0.3～0.5秒。两种可定位，保留行号因直连句、无时舍或幻觉。
- 实分2/3/4集×三片十八边真主变率：时钟11%，词长优先72%，词场景72%，词语义场景83%，AI章场景100%，AI＋语义100%。
- Ep014改后四集盲分2/4/2，三集2/1，随机明确异7%。
- 局限：LLM写另LLM判合成，比真ASR摇摆跑题可能容易；无真实非技术或人工一致。AI不可用仅词F144%，更可能非题边，会警告。

## 16. 一个项目保留多个模式 Job，并从前面步骤重做

Project1:nJob，每次候选新Job即模式，旧不消。候选计划导出属Job，文件render_id目录互不覆盖。

- GET /api/projects/{id}/patterns列模式名设置状态候选批准已导数是否处理中，在素材模式页“已制作的模式”。
- “从此配置重做”读配置可改再生成新Job，derived_from_job_id留源；步骤已完成素材、模式、生成、候选、检查修改均可返回。
- DELETE /api/jobs/{jobId}由JobDeletion删该Job候选计划全部预览正式及exports/render进回收站、preview、rejected/render、QA；保素材权利BGM他Job缓存及项目文件夹。
- 生成导出预览重建中409 E-JOB-BUSY，文件删不了不改DB E-FILE-IN-USE。保存位置指被删Job则回素材模式。
- [PatternTests.cs](../../../tests/VideoCreator.Tests/PatternTests.cs)验证删一保另素材文件、忙拒、文件不能删不变；真实Edge Playwright测列表继承删除确认步骤链接。
- 素材角色偏移转写修改会反映既有计划，但不重选候选；需新模式则从配置重做。

### 16.1 复用上次分析还是从头分析

生成页“上次分析结果（其他模式制作）”逐次选，JobOptions.ReuseAnalysis/JSON reuse_analysis默认复用。选择不保存，每次从复用始。更新分析机制等可重做。

| 对象 | 复用默认 | 重做 |
|---|---|---|
| proxy/16k声/转写/镜头音量静音笑脸屏判话题嵌入BGM节奏 | DataDir/cache/<sha256[..16]>有则用 | 本次各重一次覆盖，之后复用 |
| 自动同步 | 有测用 | 重测，保手填偏秒 |
| 说话人指定 | 不变 | 不变 |
| 手修转写 | 不变 | 新替换，UI警告 |
| AI精彩计划预览 | 每次 | 每次 |

- estimate的scopes/analysis.redo_scopes及Job.cost复用ASR计零分钟；选重做显增费和analysis.updated_at。
- 从配置重做及候选regenerate都复用，不继承重做。
- 缓存按素材hash跨项目共享，重做结果其他项目以后亦用；临时文件原子替换，并发预览不读半成。
- [AnalysisReuseTests.cs](../../../tests/VideoCreator.Tests/AnalysisReuseTests.cs)：首ASR一次，次复用ASR无增、缓存mtime同费0，重做再ASR并重proxy信号转写。

## 17. 自动检查建议修正、“用 Copilot 自动修复”与右侧时间码面板

检查修改页中的“自动检查”已成为独立面板，放在界面最右侧。宽度不超过 1240px 时，面板排列在预览与字幕下方；不超过 900px 时采用单列布局。每条指摘都使用最终导出视频的时刻，说明“哪里有问题、为什么、如何修正”。

- 逐检查批准／撤销与批量批准，记录用户看过已执行结果，包括通过，不含未执行。批量只批未批已执行；保留判定建议问题，不豁免硬失败，也不批准文字卡或候选；显示时间数量。
  - PUT /api/editplans/{id}/qa/approval传plan_hash、checks[{check_id,result_hash}]、approved，仅当前计划和看过结果hash匹配才全目标一次存。空、重复、未知、未执行422，旧计划结果409，失败零部分保存。
  - EditPlan.qa_approvals存时人计划结果hash。取／编辑／修／撤／预览更新重判，匹配才QaResult.review_approval。编辑后全重批，外部条件变则该项重批。批准记录排EditPlanFingerprint，不令既有候选批准预览正式失效；修复方法等显示建议不入结果hash。
  - 若存在与当前计划一致的有效预览，面板还显示安全区域实测 H-05 和需要在正式输出时检查的未执行项。H-05 可单独或批量确认；重新测量导致结果变化时，需要再次确认。不混入编辑前预览的旧测量结果。
- 位置QaResult.items：QaItem含目标ID、telop/insert/clip/asset/audio类型、内容、成品start/end。文字原正文时间经InsertMap.ToFinal含片头卡尾，[QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs)。上带列问题，点定位并强调相应字幕场景行；播放中带位置当前问题行跟视频。
- 建议QaResult.advice/QaAdvice含建议及rule规则／ai／manual／none不可自动，逐检查表[QaAdvisor.cs](../../../src/VideoCreator.Core/Qa/QaAdvisor.cs)。
- 事前QaRepair.Assess在取存修撤响应逐指摘不执行而判断，给QaItem.repair及不能原因note。S01/02/08字幕走与实际同词级LocalOk，任一候选过才rule；H10/11规则重跑全QA确认；仅LLM配置才ai。advice.fixable及remaining含按因余数，零则repair none。S01从词时算发言X字秒、用尽空隙仍Y、标准Z，说明不改词最长仍超。
  - UI只≥1可修项有“修此项，N件目标”，可修先、不可折叠含原因N；零显不能自动无按钮。顶部显项目数件数，零禁。
  - 真实12条各520～570字幕P3/P4无LLM，事前实际一致S02 66/66、S08 3/3、H11 9/9、S01 346/359=96%；前修占空隙会使后不修。未预判但修一件。GET最大0.52秒。旧S02两计划46/23均0，新66/76。
- POST /api/editplans/{id}/qa/repair可check_ids筛，面板修此项。[QaRepair.cs](../../../src/VideoCreator.Core/Qa/QaRepair.cs)顺：
  1. 规则候选由轻至重，重QA取首过：H10屏蔽；H11重关联，即句含别人词亦以区间最短连续词按人专名数字、近显示时优先；S01延至下字幕前两帧、末词后0.5秒、首词前0.25，前条余韵可在仍可读内让；S08首助移前；S01/02/08邻并、边词移、词界分二，优先间文节再均长，后半新ID从其首词发时；合移分后亦延。不跨说话人剪或>0.6间，原间标点以全角空格分。**词本身不改**。
  2. 配置LLM Foundry/Copilot修装饰卡H07/11/S09及长强调S02，不含字幕。先硬再时间，每次200、每批25、并四，某请求失败其他仍用，余再按续；送文字前后转写，按钮下显目标。
  3. 全重QaEngine.PlanChecks，仅目标消失、其他不恶化、无新问题留；唯一例外AI改文目标H08未批准。字幕S01/02/08按SourceWordIds不按ID，分合后目标词脱离且无新增问题词才留。全重前先以LocalOk同标准查变更及前后条。不留附因，UI常显各因数量。
- 字幕和演出政策：
  - 字幕逐字，删除生成AI缩短，不改写摘要省词删条，含迟疑尾语附和重复，除显式屏蔽及标点空格显示整形。
  - 采修机械验全显示词顺和SourceWordIds序前后同；时间分合不能解留S01/02/08因；S01与仅字幕S02建议rule。
  - 强调同字幕重时省强调不省字幕，字幕和SRT全文保、免双显。
  - 装饰卡优先外观可读；强调标题按依据验证后未批，吐槽卡经TsukkomiService.ValidateText旧标准后未批。
- **S-08 的误检修正**：开头助词检查以实际显示在画面上的字幕为准。若先前去除迟疑或手动修正已让原首词不再显示，例如“でも｜Copilotはね”中的“でも”，则不再指摘。转写中紧接句号的句首接续词，例如“。で、えっと”中的“で”，由 `JapaneseText.CanStartSentence` 允许。前一字幕属于另一说话人时，也不将其作为违规指出。
- 修前计划存qarepair，POST /api/editplans/{id}/qa/repair/{repairId}/undo精确回含hash，所以旧批准导出有效性亦回。修后又编409 E-PLAN-STALE，第二撤404；删模式项目一起删。
- 结果在点击项卡内，若全部问题消失则面板顶，并滚入；常显已修未修及最多四种最多原因，全部折叠未修项。
- 验证：
  - [QaRepairTests.cs](../../../tests/VideoCreator.Tests/QaRepairTests.cs)：成品时、延合移二分、全文词ID保、迟疑尾语短重复不删、字幕不发AI、仅装饰AI短未批、S08误、不跨人剪、拒恶化、限额分批、无LLM、事前none快语因及事前实际同、无LLM H11 rule。
  - [V12Tests.cs](../../../tests/VideoCreator.Tests/V12Tests.cs)有无LLM都逐字依据全文、未解S01留。
  - AT16 [AcceptanceQaRepairTests.cs](../../../tests/VideoCreator.Tests/AcceptanceQaRepairTests.cs) Repair_promise_matches_result_and_can_be_undone通过GET→repair→undo真实UI所用API：S02 rule1/S01 none0且15.0字秒因同实际；S01无改hash无撤对象；S02二分字词ID同；撤hash回。
  - AT17 Caption_words_are_never_shortened_even_when_ai_could_clear_the_warning、Short_repetitions_and_fillers_are_not_deleted、Presentation_telop_can_be_shortened_by_ai_while_captions_stay_verbatim：mock建议缩删仍字幕零改不送，装饰仅未批短。
  - 当前政策相关80测试全过，涵QA修生成布局PRD边界旧文字，JS语法及差分空白亦过。
  - 以前真实数据浏览器结果属允许删字幕旧政策，AI删除数量不计当前修复实绩。
- 限制：语快仅时间分隔无法合标准留警告；此改不自动恢复旧字幕。旧修删若undo有效可还原，生成已短需从转写重建。

## 18. 逐条检查一个模式生成的多个视频，并批量导出

同Job有候选或系列多条。“5检查修改”逐条，“6导出”批量已批。后端原hash批准及全件预检任一不可则零启动不变，仅添界面流程。

- 顺序：标题下同Job前后／含状态选单。主“批准并下一视频（名）”找下个未批，到尾回头且排当前。批准后重读列表，因此他页进展不回已批；其他全批则主批准进导出。当前同内容已批不重复记录，硬失败任何批准禁。单条旧批准并导出。
- 进度：候选含系列、导出列“批准n/N，已导m/N”，逐未检查／已批／需重批／导中／已导；步骤检查首未批、导出列表链接。
- 列表#/job/{id}/exports默认选已批未导，用同批API。workflow-state存step exports且job_id必需，仅ready/done，不可edit_plan_id/render_id，下次恢复；完成可回候选检查其他或导出列表。
- router递增routeGuard防旧页迟API覆盖新页。
- 验证：
  - [ProjectWorkflowResumeTests.cs](../../../tests/VideoCreator.Tests/ProjectWorkflowResumeTests.cs)exports往返、带edit_plan_id422；全293过。
  - [verify-multi-export.mjs](../../../ui/verify-multi-export.mjs) 使用真实 Edge 和 Playwright，API 为模拟响应。三条视频 Job 共 23 项检查，对应 PRD AT-18：显示进度、切换视频、只批准当前视频后进入下一个、不重复批准已批准内容、最后一个视频进入导出列表、步骤链接、仅默认选择已批准未导出内容、一次批量请求导出所选视频、检查期间另一界面已批准时不再返回它，以及批量拒绝时零启动并逐视频显示硬性失败项。
  - verify-acceptance十一单条批准先于导、[verify-qa-approval.mjs](../../../ui/verify-qa-approval.mjs)十七全过；前者workflow-state新增后缺mock停，已补。
- 限制：按排名部分串行非并行，选择不存。
- code统一PRD1.2.6 Q10b：逐候选同单render依未批→批后改→硬失败→忙。detail逐code，硬含check结果、忙含render_id；全局取所有最先404 NOT-FOUND→409 NOT-APPROVED→409 PLAN-STALE→422 QA-HARDFAIL→409 EXPORT-BUSY。只选一即同单。旧忙硬皆NOT-APPROVED；现列表硬下列项。ApprovalExportTests验证硬忙单批同、混因全局和逐code。

## 19. 多首 BGM、按场景选曲与不干扰人声的自动音量

需求：注册多首，按场景自动选，不妨人声自动调，导出前检查可改。

### 机制

| 阶段 | 实现 | 依据想法 |
|---|---|---|
| 注册测量 | [BgmAnalyzer.cs](../../../src/VideoCreator.Core/Audio/BgmAnalyzer.cs)：FFmpeg ebur128每100ms M400ms/S3s，BS1770门−70LUFS/−10LU积分，EBUTech3342 LRA，去较均值低20LU头尾可用段及3s趋势。16k PCM规范−20dBFS RMS，对数谱通量峰Dixon2006绝阈40测onset，振幅加权谱心亮度与节奏，无拍不明 | 母带响度可差>10dB，用LU非固定dB。pad通量<25，打击onset100～1000，[21-onset-calibration.cs](../../../spikes/21-onset-calibration.cs) |
| 曲能量 | BgmAnalyzer.Energy：70→150BPM权0.4，onset1→7次秒0.35，谱心150→3000Hz0.25 | 比快乐悲伤更易推唤醒度，只配能量，情绪人选 |
| 场景 | [BgmPlanner.Scenes](../../../src/VideoCreator.Core/Planning/BgmPlanner.cs)：角色换卡章强，交叉源跳≥3秒弱，不造<20秒 | 防频繁换 |
| 场景能量 | 语速3→9字秒0.45、声−6→+6dB0.35、笑0→6次分0.2，场景间z值仅加1/4 | 日语常5～7字秒，略热闹短景不换，故相对弱 |
| 选曲 | Assign场景×曲Viterbi，cost能量差×景长＋每换0.15；人选固定，前后重选 | 逐景显测因 |
| 播放 | Renderer.AppendCueBus可用段aloop，同曲继续同位置0.3秒线淡无缝，返回曲从停处续；异曲2秒等功率，片头／有曲卡停BGM插入内完成换 | |
| 音量 | 无声人声LU＋bgm_gap_lu默认−8；讲话声−乐≥bgm_speech_ld默认14最小10。ASR词时，小于0.8秒间连。下降=gap＋目标−段相对声＋该曲处高均量最多10dB。提前0.25降，0.6恢复，以1kHz gain WAV amultiply | Torcoli等JAES67(12)2019推荐≥10LU，非专业更偏好比专家大约4，故14。导出ebur128实测编辑后声，非计划估 |
| 检查 | 逐景曲自动／无、−12～+6dB，全局讲话差无声量，BGM流降带播放位 | PATCH set_bgm/set_bgm_cue/set_bgm_trim/set_bgm_level记史，每改重降 |
| 测量 S-15 | 预览与正式输出均使用与导出完全相同的流程，分别生成对话与 BGM。以 `ebur128@dlg`、`ebur128@mus` 的 400ms 块计算讲话期间的积分响度差、差值最低 10% 的块，以及没有人声处的 BGM 响度。 | 使用第三方计量器，从实际导出的声音本身测量，而不是用计划估值代替。 |

P1/P8无声−6LU，P5主乐0差10，P6−14差18，其他−8/14。旧无场景计划仍单曲sidechaincompress，调区间重建才新。P5拍同步按单曲剪不能换曲但可改量。

### 验证

- [BgmSceneTests.cs](../../../tests/VideoCreator.Tests/BgmSceneTests.cs)十四真实FFmpeg：积分与ebur128/loudnorm±0.3LU；能量chill<upbeat<hype；去头3秒静淡；词→讲话间连及速度clip；景≥20；Viterbi能量／短景不换／固定人选；降幅声与乐涨；gain预读静音淡；交叉循环位置；实际BGM首词前降、计划深0.6回，以不降同乐差测；讲话差满足目标与混声减纯声功率独立法±1.5LU；换景无断；UI与重建继承。
- 真实音[20-bgm-scene.cs](../../../spikes/20-bgm-scene.cs)，30分实录三处93秒P3：差14.3目标14，不降8.1；无声−8目标−8；估声−17实−17.5LUFS。混声Azure重转相纯声bigram94.6%，不降96.1；机器8LU亦能听故仅最低可听确认，效果以ebur128差表示。
- [verify-bgm.mjs](../../../ui/verify-bgm.mjs)真Edge一次两曲去头2秒，静pad能0.01快鼓0.84，自动P6预览S15 18.5目标18；改鼓与差24，全降深6dB实24.4，曲差2.4仍目标。
- 旧注册用视频probe要求画轨，无封面MP3 500；改MediaProbe.ProbeAudioAsync。

### 限制

- 权重刻度可解释、显测值，但无标注集评估，不判快乐凄美等情绪，需人重选。
- 附BtbN master N128532 arm64 AAC192kbps在全静开始再有声会停，128不现；正式192，建议get-tools.ps1考虑固定release，此功能未处理。

## 20. 界面、文档与视频多语言

- 界面支持日语、英语、简体中文、韩语，显示与视频语言分离。同结构译文位于locales/en、zh-CN、ko。历史日语图是当时实画，不是翻译UI证据。
- Project.language为ja-JP默认、en-US、zh-CN、ko-KR，创建修改API验证，生成导出中拒变。Runtime.AsrForLanguage传Speech locales，转写笑缓存分语言，旧计划QA编辑文案读计划语言转写。
- 字幕逐字，英韩复原ASR词间空格，日中连接字。CaptionText按语拆换正规，QA修同连接保词ID时对应，跨词屏蔽含空格检查。
- 筛选系列章装饰卡QA AI修投稿提示指定视频语言；定型卡效果字回顾预告模板亦同。AI改演出仍未批需人。
- EditPlan.language存创建时语言，日英M PLUS1p，中Noto Sans CJK SC，韩KR。get-tools.ps1取OFL字体许可；缺所需CJK明确失败，不靠OS替代。
- [LocalizationTests.cs](../../../tests/VideoCreator.Tests/LocalizationTests.cs) 验证 API 持久化和非法值、Speech 请求语言、按语言区分的缓存、单词／空格／屏蔽词保持、AI 提示、ASS/SRT、libass 实际绘制与字形缺失、长英语／中文／韩语字幕的安全区域像素实测，以及 MP4 预览。非日语宽度估计也考虑较宽的拉丁字母，绘制和避脸布局使用相同的字体适配逻辑。文档结构、覆盖完整性和链接通过 `node scripts/verify-localized-docs.mjs` 检查。
- 限制：自动测用合成声画固定响应，不保证英中韩真实Speech精度、AI自然、母语校译。无自动字幕翻译或混语自动检测。
---

[↑ 文档导航](../README.md) | [日本語](../../../docs/IMPLEMENTATION.md) | [English](../../en/docs/IMPLEMENTATION.md) | **简体中文** | [한국어](../../ko/docs/IMPLEMENTATION.md)
