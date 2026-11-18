# Azure 准备与共享用户的身份验证

<!-- language-nav -->
[日本語](../azure-access.md) | [English](../en/azure-access.md) | [한국어](../ko/azure-access.md) | [简体中文](azure-access.md)
<!-- /language-nav -->

[开始](start.md) / [快速入门](quickstart.md) / [配置与 API](configuration-api.md)

VideoCreator 在每位用户的电脑上运行。不需要把应用托管到 Azure，也不需要为视频
新建 Storage 或 Azure SQL。媒体、SQLite、渲染、人脸检测和超分辨率仍在本地处理。
GitHub Copilot 是可选的替代方案，不是需要配置的 Azure 服务。

## 所需服务

| 用途 | 必要配置 | Entra ID 使用角色 |
|---|---|---|
| 字幕转录（必需） | 支持快速转录的区域中的 Azure AI Services / Speech，S0，自定义子域名 | Cognitive Services Speech User |
| AI 筛选、字幕特效、帖子文案 | Azure AI Services / Azure OpenAI，支持 Structured Outputs 的聊天部署 | Cognitive Services OpenAI User |
| 基于语义的话题分段 | 同一资源中的嵌入部署，未设置时使用词汇检测 | Cognitive Services OpenAI User |

**一个 Azure AI Services 资源可以同时提供上述三种能力。** 当前应用的执行路径
不需要 Foundry 项目、Video Indexer、Event Grid 或额外的托管标识。也无需删除已有资源。

### 此环境的检查结果（2026-11-18）

- 订阅：`dahatake Internal Subscription`
  (`0e930937-43f5-484d-ba9e-1dd1151cf7ba`)
- 租户：`16b3c013-d300-468d-ac64-7eda0820b6d3`
- 资源组：`rg-videocreator-jpe`
- 资源：`videocreator-ai-40800`（AIServices / S0 / Japan East）
- Speech：`https://videocreator-ai-40800.cognitiveservices.azure.com/`
- Foundry：`https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1/`
- 聊天部署：`gpt-chat-latest`
- 嵌入部署：`text-embedding-3-large`

所需资源和部署已经存在，因此不添加重复服务或模型容量。没有额外固定容量费用
**不代表免费使用**：Speech 处理时长、聊天和嵌入令牌，以及连接测试均按用量计费。
共享用户的请求也由资源所属订阅承担费用。管理员应设置 Azure 预算并监控使用量。
现有模型部署使用 **GlobalStandard**。资源位置为 Japan East 不保证推理仅在日本
境内执行，请确认组织的数据驻留要求。

**验证结果（2026-11-18）：**

- 针对现有资源运行 [infra/main.bicep](../../infra/main.bicep) 的 `what-if`，报告了账户标签、
  `armFeatures`、`associatedProjects`、`defaultProject` 和部署的 `currentCapacity` 差异。
  指定空标签重新部署返回 `Succeeded`（未重复创建）。
- 已实际使用 [scripts/grant-azure-access.ps1](../../scripts/grant-azure-access.ps1) 验证 RG 角色的分配、重复运行（不重复）和删除。
  所有者账户已在 RG 范围拥有两个角色。
- 应用连接测试中，使用 Entra ID 的 Speech、严格 JSON Schema 聊天和嵌入均成功。
- 密钥身份验证**因组织策略而无法使用**。管理组分配的
  "AzTB 17 - Disable Local auth for all Cognitive Services"（effect: modify）对所有
  Cognitive Services 强制 `disableLocalAuth=true`。已确认即使更新为 `false`，也会在请求时被改写为 `true`。
  应用的密钥模式会发送密钥，并将 401 诊断为“密钥被拒绝”。
- 此租户的条件访问阻止创建应用注册，因此未使用不具备其他权限的独立身份进行验证。

## 管理员：创建 Azure 资源

[infra/main.bicep](../../infra/main.bicep) 一次创建所需内容：AIServices（S0、自定义子域，默认禁用密钥身份验证）、
聊天和嵌入部署，以及可选的用户角色。请使用 `what-if` 检查对现有资源的更改。
`userObjectIds` 不会检测 IAM 或脚本已创建的相同角色分配，因此可能产生冲突。
对于现有用户，请改用下面的脚本。

```powershell
az login --tenant <租户 ID>
az group create -n rg-videocreator-jpe -l japaneast
az deployment group what-if -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<唯一名称>
az deployment group create  -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<唯一名称>
```

将输出的 `speechEndpoint`、`foundryEndpoint`、`chatDeployment`、`embeddingDeployment`、`tenantId`
填入设置界面。模型名称、版本、容量和区域请通过参数指定该区域可用的值。

## 管理员：将用户添加到资源组

“添加账户”是指在资源组的**访问控制 (IAM) 中分配角色**。仅授予 Contributor 或 Owner
并不能获得基于令牌调用 Speech API 的权限。

1. 确认用户是资源所在 Entra 租户的成员，或已接受邀请的来宾。
2. 运行以下脚本（执行者需要 Owner、User Access Administrator 或 RBAC 管理员权限）：

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\grant-azure-access.ps1 -User alice@contoso.com
   ```

   `-User` 可指定 UPN、来宾的电子邮件地址或对象 ID。如果组织限制导致 Microsoft Graph 查询失败，
   或电子邮件查询无结果，请指定在目标租户的 Entra ID“用户”页面中确认的对象 ID。
   Azure CLI 的默认租户必须与目标订阅的租户一致。仅需转写时使用 `-SpeechOnly`，撤销时使用 `-Remove`。
   不会重复创建 RG 范围内已有的角色；删除也仅影响直接在 RG 范围的分配。
3. 等待几分钟让角色生效，然后在用户的电脑上进行连接测试。

在门户中：目标 RG → **访问控制 (IAM)** → **添加角色分配**，分配上表中的两个用户角色。

RG 范围的角色会继承到该 RG 内的相应资源。若只允许访问一个资源，请在资源范围分配相同角色。
普通用户不需要 Owner、Contributor 或 Cognitive Services Contributor。
Speech User / OpenAI User 不是获取资源密钥的权限。

停止使用时，请在 IAM 中删除相应的角色分配，并确认没有通过组、资源级别或订阅继承的分配残留。

## 用户：在设置界面选择

### Microsoft Entra ID（共享使用时推荐）

在用户电脑安装 Azure CLI，登录资源所属租户。

```powershell
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3
```

在应用的 **设置 → AI 连接** 中分别配置以下两个连接：

1. **Azure Speech**：资源终结点、**Microsoft Entra ID**、租户 ID。
2. **Microsoft Foundry**：终结点、聊天和嵌入部署名称、
   **Microsoft Entra ID**、租户 ID。
3. 对每项选择 **测试连接**，确认成功后选择 **测试并保存**。
   仅测试不会保存；保存操作会再次测试，成功后才持久化。

#### 从 Azure 选择（无需输入终结点和部署名称）

在任一连接中选择 **从 Azure 选择**，即可列出当前登录的 Entra ID 可见的 Azure Speech /
Microsoft Foundry 资源并进行选择（租户 ID 为空时使用默认登录）。

- 通过 Azure Resource Manager（`https://management.azure.com`）读取所有可访问订阅中的
  Cognitive Services 帐户，只显示 Speech（具有语音转文本 API）和 Foundry（具有 Azure OpenAI API）资源。
  不显示 Computer Vision 等资源。
- 对于 Foundry，会按模型功能（`chatCompletion` / `embeddings`）把所选资源的部署分为 **聊天** 和
  **嵌入** 候选项。失败或正在创建的部署不会显示。
- 选择后会填入下方的终结点和部署名称。若资源禁用了密钥认证（`disableLocalAuth`），则切换为
  Microsoft Entra ID。此操作不会保存，请务必通过 **测试连接** → **测试并保存** 确认。
- 列出资源需要资源的读取（管理平面）权限，上文授予的 `Cognitive Services Speech User` /
  `Cognitive Services OpenAI User` 已包含该权限。使用（数据平面）权限通过 Azure `permissions` API 检查，
  缺少时会在界面上提示。拒绝分配或生效延迟可能影响结果，最终以连接测试为准。
- 列出时 **不会读取密钥**（不调用 `listKeys`）。使用密钥认证时，请向管理员获取密钥。
- 即使部分订阅无法读取，也会列出可读取的部分，并显示无法读取的订阅名称。没有自定义域的
  Speech 资源无法使用 Entra ID，会显示警告。

不要向应用输入 Entra 密码或客户端密码。后端使用 Azure Identity 的
`DefaultAzureCredential`，通过 Azure CLI 等已有登录取得目标租户令牌。
应用不会自动打开浏览器登录窗口。若电脑通过环境变量等配置了其他凭据，
这些凭据也可能参与身份验证链，应确认实际使用的是预期账户。

### API 密钥

在每项连接选择 **API 密钥**，输入通过安全渠道收到的 **对应资源的密钥**，
然后选择 **测试并保存**。不要使用其他资源的密钥。在 Windows 上使用 DPAPI
加密保存，绑定当前电脑和 Windows 用户；把配置文件复制给其他电脑或用户
也无法解密。在尚未实现操作系统加密的平台上，会拒绝保存密钥，但仍允许连接测试。

**当前共享资源因组织策略而固定禁用密钥身份验证。**
在此订阅中选择密钥会得到 401（密钥被拒绝）。要使用密钥，需要在策略允许的其他订阅中以
`disableLocalAuth=false` 创建资源（Bicep `-p disableLocalAuth=false`）。请勿自行创建绕过安全控制的策略豁免。
应用中的单选按钮不会更改 Azure。密钥是资源共用凭据，而非个人权限；使用 Entra ID 的用户无需分发密钥。

保存的密钥不会返回到界面或配置查询 API。空密钥字段仅可复用同一目标
（HTTPS 主机与端口）已保存的密钥。改为 Entra ID 并保存后，会删除该连接的
已保存密钥。**身份验证失败时不会自动回退到其他身份验证方式。**

## 保存、测试和故障排除

- 界面连接保存在 `DataDir/ai-settings.json`，优先于启动配置，重启后仍有效。
  **删除已保存的连接** 会恢复该连接的启动配置。
- 变更仅对之后启动的操作生效，不切换正在运行的任务。
- Speech 测试通过与生成相同的快速转录 API 发送 **一秒合成静音**，不会发送
  用户音频。这是身份验证和连通性测试，不保证真实视频的语音识别准确率。
- Foundry 测试严格 JSON Schema 聊天输出；若指定嵌入部署，也会测试嵌入。
- 即使禁用 LLM，Speech 仍然必需，所以 Speech 测试和保存保持可用。
- `401`：检查密钥及本地身份验证策略，或租户和令牌。`403`：检查对应用户的
  数据平面角色、分配范围、权限生效等待和网络限制。`404`：检查终结点和部署名称。
  `429`：检查共享配额和速率限制，不要无休止地重复请求。
- Speech 终结点必须是 HTTPS 资源根地址，不是 `/openai/v1` 或 `speechtotext/...`。
  Entra ID 使用资源自定义子域名，不使用区域共享地址。
- `run.cmd --check` 中的 Azure 诊断读取启动配置。要验证界面保存的连接，
  请使用对应设置区域的连接测试。
- 不要把密钥或访问令牌粘贴到文档、截图或支持请求中。

官方依据：
[Speech RBAC](https://learn.microsoft.com/azure/ai-services/speech-service/role-based-access-control) /
[快速转录](https://learn.microsoft.com/azure/ai-services/speech-service/fast-transcription-create) /
[Azure OpenAI RBAC](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control)。
