# 制作方针：深化文档请求及其执行结果

[↑ 文档导航](../README.md) | [日本語](../../../users-guide/documentation-brief.md) | [English](../../en/users-guide/documentation-brief.md) | **简体中文** | [한국어](../../ko/users-guide/documentation-brief.md)


[应用 README](../README.md) / [指南目录](README.md) / [验证与维护](verification.md)

本页记录如何将整套指南的请求变得“更深入”，而不是“更庞大”。针对最初请求，列明 (1) 专家会额外做什么、(2) 作为起点的原始素材、(3) 方法与工具、(4) 第三方验证方式，重写请求后执行。最后将每项重写需求与产物、证据对应。

## 原始请求（概述）

- 在 `/users-guide` 创建面向首次用户的快速入门、界面组件含义与说明、界面截图。
- 为自定义实现的软件工程师创建技术参考和组件图。
- 在 `/README.md` 用图补充首次用户需要准备什么、在应用中做什么、得到哪些产物。
- 所有图使用 SVG。

## (1) 顶尖专家会额外做什么

技术写作者和开发者体验（DX）专家在增加页数前会做：

1. **聚焦读者与一个目标**：首次用户成功条件为“导出第一个视频”，确定最短路径（一个视频＋P4＋标准设置），其他功能归入“成功后再尝试”。
2. **先说明前提与失败条件**：在操作前说明 Azure 资源、权限、发送目标、费用、权利确认及无法离线的范围。不只写成功案例，也写容易卡住的位置和排查方法。
3. **从实现反查界面说明**：不只看外观，还从源码确认 UI 标签、默认值、显示条件、禁用条件及发送 API，说明每个按钮和输入框改变什么。
4. **让截图可重现**：不是手动截图，而是隔离数据启动真实应用并自动获取，遮盖个人信息，留下重建步骤。
5. **为工程师说明“更改入口”和“不能破坏的契约”**：给出组件边界、持久化、时间轴转换、QA／批准及扩展时应执行的测试。
6. **区分写出的说明与已确认内容**：明确实际运行范围、仅以源码为依据的范围、未验证范围（真实云发送、第三方可用性评价等）。
7. **以可验证形式制作图**：SVG 带 title/desc，机械确认文字不溢出且无外部引用，区分真实界面 SVG 与示意图。

## (2) 专家作为起点的最原始素材

不是概述 README 或历史说明，而是以下一手资料。

| 原始素材 | 确认什么 |
|---|---|
| 运行中的应用界面（真实浏览器） | 实际标签、初始状态、操作后状态 |
| UI 源码 [app.js](../../../src/VideoCreator.App/wwwroot/js/app.js)、[index.html](../../../src/VideoCreator.App/wwwroot/index.html) | 显示条件、禁用条件、输入发送目标 |
| API 与配置 [Api.cs](../../../src/VideoCreator.App/Api.cs)、[Program.cs](../../../src/VideoCreator.App/Program.cs)、[AppOptions.cs](../../../src/VideoCreator.App/AppOptions.cs) | 路由、标头、JSON 格式、启动配置 |
| 核心处理 [Models.cs](../../../src/VideoCreator.Core/Models/Models.cs)、[PipelineRunner.cs](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs)、[QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs) | 数据结构、处理顺序、QA 条件 |
| 启动安装脚本 [run.cmd](../../../run.cmd)、[setup.ps1](../../../scripts/setup.ps1)、[get-tools.ps1](../../../scripts/get-tools.ps1) | 用户准备内容及自动准备内容 |
| 预设 [presets](../../../presets) | 各模式时长、文字和音频方针 |
| 已有测试与 HTTP 示例 [tests](../../../tests)、[exports.http](../../../src/VideoCreator.App/exports.http) | 契约保证的行为 |
| 实际导出文件（MP4、SRT、ASS、JSON、PNG） | 用 ffprobe 等确认产物格式 |
| Microsoft Learn（Azure Speech Fast Transcription） | 云端前提与限制 |

## (3) 具体方法与工具

| 方法 | 工具 | 本指南用法 |
|---|---|---|
| 任务分析与最短路径设计 | 阅读源码、操作真实界面 | [快速入门](quickstart.md)分为“准备→启动确认→素材→生成→检查→导出”六阶段 |
| 从实现提取 UI 规格 | grep／源码阅读 | [界面说明](screens.md)各栏记录含义、默认值、使用场景 |
| 隔离环境执行 | `VideoCreator__DataDir`、`__ProjectsDir`、`__Port` 等环境变量，合成测试素材 [seed.cs](../../../users-guide/tools/seed.cs) | 不接触用户数据或云服务，重现界面与输出 |
| 自动截图 | Playwright＋Microsoft Edge [capture.mjs](../../../users-guide/tools/capture.mjs) | 真实界面 PNG 嵌入 SVG，遮盖路径 |
| 制图 | 手写 SVG（带 title/desc） | 全貌、架构、处理流程、时间轴、QA 五张图 |
| 输出确认 | ffprobe、API 获取 | 检查编解码器、分辨率、音频格式、时长、输出键 |
| 文档机械验证 | [validate.mjs](../../../users-guide/tools/validate.mjs)、`git diff --check` | 链接、标题锚点、SVG XML、嵌入 PNG、文字溢出、渲染 |

## (4) 除你我之外，什么能证明正确性

| 第三方判定者 | 证明什么 | 状态 |
|---|---|---|
| 真实应用本身 | 截图是真实界面而非想象图 | 已执行：Edge 自动获取 |
| 导出文件与 ffprobe | README 所述产物实际输出（H.264/AAC、1080×1920、8 类输出） | 已执行 |
| 应用内自动 QA | 导出硬性条件 | 已执行：全部 pass（合成素材） |
| 浏览器 SVG 渲染与 XML 解析器 | 图未损坏，文字不溢出 | 已执行：20 张全部成功 |
| 链接验证脚本 | 资料间及源码链接未断 | 已执行 |
| 构建与现有测试 | 说明的契约与代码一致 | 部分执行（构建成功，未重跑全部测试） |
| 首次使用的第三方 | 仅凭指南即可导出一个视频 | **未执行**，步骤见[更新检查清单](verification.md#更新检查清单)第 6 项 |
| 发送到真实 Azure | 真实讲话识别与费用 | **未执行**，明确区别于合成素材结果 |

## 重写后的请求

> 为首次使用 VideoCreator 的用户及自定义实现的软件工程师制作文档，以当前工作树实现和真实界面为一手资料。不要以摘要 README 或现有说明为依据，而应通过 UI 源码、API、模型、启动脚本、预设、现有测试、实际导出文件确认。
>
> 1. **README（`/README.md`）**：用一个表格及一张 SVG，展示首次用户“准备什么（PC、已确认权利的视频、自己的 Azure 资源与权限）”“在应用做什么（启动、添加素材与权利确认、选模式、生成、检查修改、导出）”“得到什么（MP4、SRT、ASS、编辑计划、缩略图、元数据建议）”。提醒数据发送目标、不是离线应用、自动 QA 不保证权利或内容。
> 2. **快速入门**：以导出第一个视频为成功条件，聚焦最短路径（一个视频＋P4＋标准设置），各阶段设置“确认后进入下一步”判定。
> 3. **界面与组件说明**：隔离环境启动真实应用，通过 Playwright＋Edge 自动获取真实界面嵌入 SVG。根据源码说明各按钮、输入框、开关的含义、默认值、显示或禁用条件、使用场景，注明合成素材及个人信息遮盖。
> 4. **遇到问题**：失败排查顺序、常见症状、QA 结果读法、保存与备份。
> 5. **技术参考**：组件图（SVG）、生成与导出流程、数据模型与持久化、易混时间轴、批准／QA／可复现性契约、自定义入口与所需测试、构建步骤。配置和 API 另页列举。
> 6. **图**：全部 SVG，示意图带 title/desc，与真实界面 SVG 区别处理。
> 7. **验证**：提供并执行脚本，自动验证链接、标题锚点、SVG 语法、嵌入图像、文字溢出及浏览器渲染。实际导出一个视频，通过 ffprobe 和 API 检查产物。分别记录运行确认、仅据源码、未验证（真实云端、第三方评价等）内容，留下截图重建步骤。
> 8. 不撤销工作树已有应用更改。

## 与执行结果的对应

| 重写需求 | 产物 | 正确性证据 |
|---|---|---|
| 1. README：准备→操作→产物 | [README](../README.md#首次使用准备--操作--产物)、[全貌图](images/overview.svg) | 实际导出八个产物键，用 ffprobe 检查格式（[验证记录](verification.md#本次执行确认)） |
| 2. 快速入门 | [quickstart.md](quickstart.md) | 合成素材在真实 UI 执行相同步骤（权利确认→P4 生成→检查→导出） |
| 3. 界面说明与截图 | [screens.md](screens.md)、15 张真实界面 SVG（[原始 images](../../../users-guide/images)） | [capture.mjs](../../../users-guide/tools/capture.mjs) 从真实应用自动获取，验证脚本检查嵌入 PNG 与渲染 |
| 4. 遇到问题 | [troubleshooting.md](troubleshooting.md)、[QA 图](images/qa-gates.svg) | QA 条件对照 [QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs)，实际执行 QA 全部 pass |
| 5. 技术参考 | [technical-reference.md](technical-reference.md#1-组件架构)、[组件图](images/components.svg)、[处理流程](images/pipeline.svg)、[时间轴](images/timelines.svg)、[配置与 API](configuration-api.md) | 构建成功（0 警告、0 错误），路由与配置对照源码 |
| 6. 全部图为 SVG | 5 张示意图＋15 张实际界面＝20 张 | [validate.mjs](../../../users-guide/tools/validate.mjs)：检查没有 SVG 以外文件、XML、title/desc、外部引用、文字溢出、渲染 |
| 7. 验证与记录 | [verification.md](verification.md) | 验证脚本 PASS，明确未验证项 |
| 8. 保留现有更改 | — | 未撤销应用源码更改 |

未执行项（第三方首次用户评价、真实 Azure 讲话测试、重跑全部测试）未被当作结果正确的证据。
---

[↑ 文档导航](../README.md) | [日本語](../../../users-guide/documentation-brief.md) | [English](../../en/users-guide/documentation-brief.md) | **简体中文** | [한국어](../../ko/users-guide/documentation-brief.md)
