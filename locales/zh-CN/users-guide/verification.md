# 资料验证与维护

[↑ 文档导航](../README.md) | [日本語](../../../users-guide/verification.md) | [English](../../en/users-guide/verification.md) | **简体中文** | [한국어](../../ko/users-guide/verification.md)


[应用 README](../README.md) / [指南目录](README.md) / [技术参考](technical-reference.md) / [制作方针](documentation-brief.md)

## 一手资料与验证范围

本指南调查了 2026-11-08 的工作树。未撤销工作期间存在的应用更改，说明依据实现、配置和测试。由于也包含未提交到 Git 的更改，不是对特定发布标签的规格保证。

| 说明对象 | 确认方法 |
|---|---|
| 启动与准备 | [run.cmd](../../../run.cmd)、[setup.ps1](../../../scripts/setup.ps1)、[AppOptions](../../../src/VideoCreator.App/AppOptions.cs) |
| 界面、输入与操作 | [app.js](../../../src/VideoCreator.App/wwwroot/js/app.js) 与实际浏览器 |
| API、批准与处理条件 | [Api.cs](../../../src/VideoCreator.App/Api.cs)、[JobQueue](../../../src/VideoCreator.App/JobQueue.cs)、现有 [HTTP 示例](../../../src/VideoCreator.App/exports.http) |
| 模型、处理与输出 | [Models](../../../src/VideoCreator.Core/Models/Models.cs)、[PipelineRunner](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs)、Renderer、QaEngine |
| Azure 前提 | Microsoft Learn 的 [Fast Transcription](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create) 和实现中的连接及认证代码 |
| 界面 SVG | 用 Edge 显示真实应用，将 PNG 嵌入 SVG，区别于示意图 |
| 说明与图的一致性 | 对本地链接、标题锚点、XML、图像嵌入、文字区域及浏览器渲染进行机械验证 |

### 本次执行确认

- 常规 Release 输出位置被已运行进程锁定，因此未停止该进程，而使用**独立 artifacts 输出位置**构建。成功（0 警告、0 错误）。
- 使用专用 DataDir、ProjectsDir 和端口启动，确认 health 响应和所需滤镜检测。
- 使用合成视频＋转写缓存，在真实 UI 执行权利确认→P4 候选生成→检查界面→直接最终导出。
- 确认最终 QA 硬性结果全部 pass。
- 确认存在 `mp4` / `srt` / `ass` / `editplan` / `metadata` / `thumb1` / `thumb2` / `thumb3` 八个键，可通过对应 API 获取非空文件。
- 用 ffprobe 确认实际文件：H.264、1080×1920、AAC、48kHz、2ch。P4 输出 27.20 秒，QA 测量 −14.0 LUFS、−12.5 dBTP（正弦波 fixture 结果，不是真实讲话音质评价）。
- 从相同素材生成两条系列候选，检查系列结构和界面。
- 使用[验证脚本](../../../users-guide/tools/validate.mjs)检查指南全部 SVG 和本地链接。

最终验证结果：**9 个 Markdown 文件（含根 README 和[制作方针](documentation-brief.md)）、211 个本地链接与锚点、20 个 SVG（5 张示意图、15 张实际界面）**。XML、嵌入 PNG、图中文字区域、Edge 渲染、辅助脚本语法和差异空白检查全部成功。最终截图还验证了 SRT 时间格式、ASS Events、编辑计划 Canvas/Timeline、缩略图 PNG 格式、MP4 编解码器、分辨率、音频格式和 P4 时长。

**本次未执行**：向真实 Azure/Copilot 发送素材、真实讲话识别精度测试、全部 P1～P10 生成、所有 OS 对话框／保存位置迁移／删除／批量批准操作、重新运行仓库全套测试、社交平台投稿、由第三方初学者进行可用性评价。相关功能说明依据源代码和已有测试，与本次实际执行结果区分。

使用能通过音量等 QA 的合成数据，并不能证明验证了与真实语音内容的一致性。实际使用请先用自己获授权的素材检查第一个视频。

## 导出产物 UI 回归确认（2026-11-11 追加）

- [ExportArtifactTests](../../../tests/VideoCreator.Tests/ExportArtifactTests.cs)：通过真实 API 检查产物绝对路径、缺失、格式、已保存投稿文案、文本 65,536 字符限制、全文获取、视频 Range 响应及编辑后失效。
- [verify-export-artifacts.mjs](../../../ui/verify-export-artifacts.mjs)：在 Edge 检查实际合成 H.264 视频播放及定位、各视频路径与复制对象、保存时原文件名、投稿文案只读显示、图片与文本预览、完成后自动显示、失败显示与重试、窄屏及导航竞争。API 使用 fixture，剪贴板也是不覆盖用户内容的测试实现。不实际启动资源管理器或投稿。
- [verify-multi-export.mjs](../../../ui/verify-multi-export.mjs)：回归检查现有批准和批量导出流程。

启动隔离应用并确认 health 响应后执行（URL 与实际端口一致）。

```powershell
dotnet test --project tests\VideoCreator.Tests --filter-class VideoCreator.Tests.ExportArtifactTests VideoCreator.Tests.ApprovalExportTests VideoCreator.Tests.PostCopyTests
node ui\verify-export-artifacts.mjs http://127.0.0.1:18788
node ui\verify-multi-export.mjs http://127.0.0.1:18788
```

## 重新制作截图

### 1. 前提

使用 .NET SDK 10、FFmpeg、字体、模型、Node.js 和 OS 已安装的 Microsoft Edge。应用用户不需要 Node.js，但**重新制作界面的开发者需要**。Playwright 使用 [ui/package.json](../../../ui/package.json) 的已有依赖。缺少依赖时在 `ui` 执行 `npm ci`，无需额外下载浏览器。

命令在仓库根目录的 PowerShell 执行，请将示例路径改为自己的工作路径。

### 2. 准备隔离 fixture

[seed.cs](../../../users-guide/tools/seed.cs) 拒绝已有数据的文件夹，请指定新的空目录。

```powershell
dotnet run --file users-guide\tools\seed.cs -- C:\Temp\VideoCreatorGuideCapture
```

生成 260 秒测试图视频与正弦波、项目／素材 DB 记录，以及将 [talk_transcript.json](../../../tests/VideoCreator.Tests/Fixtures/talk_transcript.json) 偏移 130 秒重复排列两次的转写缓存。字幕**不是识别该正弦波得到的结果**。最初 130 秒 fixture 在移除静音后无法满足两条视频的最短时长，因此延长。

fixture 键对应当前 ASR 缓存方式和空词典。修改缓存实现、词典或 ASR Name 时也需更新 seed。不要将验证端点用于实际使用。

### 3. 使用专用端口启动

在另一个 PowerShell 执行以下命令，不要指定运行中应用或真实项目的 DataDir。使用没有遗留 Dictionary 等实际覆盖环境配置的新终端。

```powershell
$env:VideoCreator__DataDir = 'C:\Temp\VideoCreatorGuideCapture\data'
$env:VideoCreator__ProjectsDir = 'C:\Temp\VideoCreatorGuideCapture\projects'
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
$env:VideoCreator__Asr__Provider = 'azure'
$env:VideoCreator__Asr__Endpoint = 'http://127.0.0.1:1/'
dotnet run --project src\VideoCreator.App -c Release
```

端口占用时指定其他端口，后续 URL 保持一致。禁用 LLM，ASR 读取 fixture 缓存。即使缓存不匹配，也只会请求回环未用端口并失败，而不会访问外部服务。

若与已运行二进制冲突，使用独立输出位置构建和启动。

```powershell
dotnet build src\VideoCreator.App\VideoCreator.App.csproj -c Release `
  --artifacts-path C:\Temp\VideoCreatorGuideBuild
dotnet C:\Temp\VideoCreatorGuideBuild\bin\VideoCreator.App\release\VideoCreator.App.dll `
  --contentRoot "$PWD\src\VideoCreator.App"
```

也在配置上述环境变量的同一个 PowerShell 中执行。

### 4. 获取并验证

确认启动响应后，在另一个终端执行。

```powershell
Invoke-RestMethod http://127.0.0.1:18765/api/health
node users-guide\tools\capture.mjs http://127.0.0.1:18765
node users-guide\tools\validate.mjs
git diff --check -- README.md users-guide
```

[capture.mjs](../../../users-guide/tools/capture.mjs) 检查环境仅含 `guide-demo`、不使用 LLM、ASR 为回环端点后才操作。会在真实应用中留下新的生成和导出，并更新[原始 images](../../../users-guide/images)中的界面 SVG。可能包含个人名称的 CLI 路径会在界面遮盖。获取图像后，在启动终端按 Ctrl+C，仅清理已不需要的专用工作目录。

## 更新检查清单

1. UI 标签、默认值、显示条件改变时更新[界面说明](screens.md)和截图。
2. 配置、API record、错误、状态改变时更新[配置与 API](configuration-api.md)和操作示例。
3. 输出键、QA、缓存、时间轴改变时更新[技术资料](technical-reference.md)和 seed/capture。
4. 首次使用权限、费用、发送目标改变时更新[快速入门](quickstart.md)和 [README](../README.md)。
5. 机械验证 SVG，并目视确认主要图可读。链接验证不能证明外部 URL 可用或说明真实。
6. 请开发者之外的首次用户仅凭指南导出一个视频，记录困惑的输入框和失败条件。本次未执行此第三方评价。
---

[↑ 文档导航](../README.md) | [日本語](../../../users-guide/verification.md) | [English](../../en/users-guide/verification.md) | **简体中文** | [한국어](../../ko/users-guide/verification.md)
