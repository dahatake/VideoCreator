# Azure setup and authentication for shared users

<!-- language-nav -->
[日本語](../azure-access.md) | [English](azure-access.md) | [한국어](../ko/azure-access.md) | [简体中文](../zh-CN/azure-access.md)
<!-- /language-nav -->

[Start](start.md) / [Quickstart](quickstart.md) / [Configuration/API](configuration-api.md)

VideoCreator runs on each user's PC. Hosting the app in Azure, a video Storage
account, and Azure SQL are not required. Media, SQLite, rendering, face detection
and upscaling remain local. GitHub Copilot is an optional alternative, not an
Azure service to provision.

## Required services

| Purpose | Requirement | Entra ID role |
|---|---|---|
| Caption transcription (required) | Azure AI Services / Speech, S0, custom subdomain. MAI-Transcribe-2 requires a supported region (below); other regions use the standard model | Cognitive Services Speech User |
| AI selection, telops, post copy | Azure AI Services / Azure OpenAI with a Structured Outputs-capable chat deployment | Cognitive Services OpenAI User |
| Semantic topic boundaries | Embedding deployment in the same resource; vocabulary-based detection if unset | Cognitive Services OpenAI User |

Caption transcription uses Microsoft's speech recognition model **MAI-Transcribe-2** by default.
It calls the Azure Speech Fast Transcription API with `enhancedMode`, so no deployment is needed.
MAI-Transcribe-2 is available in Central India, East US, North Europe, Southeast Asia,
West Europe, West US and West US 2 (as of 2026-11; Japan East is not supported).
In unsupported regions, or for recordings of 2 hours / 250 MB or more, the app automatically
switches to the standard model and says so in the connection test, settings screen and job warnings.
To pin the standard model, set `VideoCreator:Asr:Model` to `standard`. Image analysis (Azure AI Vision) is not used.

**One Azure AI Services resource can serve all three purposes.** A Foundry project,
Video Indexer, Event Grid and an additional managed identity are not required by
the current execution path. Existing resources do not need to be deleted.

### Environment inspected on 2026-11-20

- Subscription: `dahatake Internal Subscription`
  (`0e930937-43f5-484d-ba9e-1dd1151cf7ba`)
- Tenant: `16b3c013-d300-468d-ac64-7eda0820b6d3`
- Resource group: `rg-videocreator-jpe`
- Resource (the only one): `videocreator-mai-9709` (AIServices / S0 / Southeast Asia).
  It serves chat, embeddings, image generation and captions (MAI-Transcribe-2).
  The former `videocreator-ai-40800` was deleted and purged.
- Foundry / Speech destination: `https://videocreator-mai-9709.cognitiveservices.azure.com/`
- Chat deployment: `gpt-chat-latest`
- Embeddings: `text-embedding-3-large`
- Viva Engage image: `FLUX-1.1-pro`

The required resource and deployments already existed. No duplicate services or
model capacity are needed. No additional fixed capacity cost does **not** mean
free usage: Speech processing, chat and embedding tokens, and connection tests
are metered. Shared users' requests are billed to the resource owner's
subscription. Manage Azure budgets and monitor usage.
The existing model deployments use **GlobalStandard**. Japan East resource
location does not guarantee inference stays in Japan; review your organization's
data residency requirements.

**Verification (2026-11-18):**

- `what-if` for [infra/main.bicep](../../infra/main.bicep) reported differences in account tags,
  `armFeatures`, `associatedProjects`, `defaultProject` and deployment `currentCapacity`.
  Redeployment with empty tags returned `Succeeded` (no duplicates).
- [scripts/grant-azure-access.ps1](../../scripts/grant-azure-access.ps1) was run for real: grant, rerun (no duplicates) and removal.
  The owner account now has both roles at RG scope.
- The app's connection tests succeeded for Speech, strict JSON Schema chat and embeddings with Entra ID.
- Key authentication is **blocked by organization policy**. The management-group assignment
  "AzTB 17 - Disable Local auth for all Cognitive Services" (effect: modify) forces
  `disableLocalAuth=true` on every Cognitive Services account. An update to `false` was
  rewritten to `true` at request time. The app's key mode sends the key and reports 401 as "key rejected".
- Conditional Access in this tenant blocks creating app registrations, so access with a
  separate identity that holds no other permissions was not tested.

## Administrator: create the Azure resources

[infra/main.bicep](../../infra/main.bicep) creates everything required: an AIServices account (S0,
custom subdomain, key authentication disabled by default), the chat and embedding deployments,
and optionally user roles. Review changes to existing resources with `what-if`.
`userObjectIds` does not detect equivalent role assignments created by IAM or the script;
it can conflict with them. Use the script below instead for existing users.

```powershell
az login --tenant <tenant-id>
az group create -n rg-videocreator-jpe -l japaneast
az deployment group what-if -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<unique-name>
az deployment group create  -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<unique-name>
```

Enter the outputs `speechEndpoint`, `foundryEndpoint`, `chatDeployment`, `embeddingDeployment` and
`tenantId` in Settings. Pass model names, versions, capacity and region available in your region as parameters.

## Administrator: add a user to the resource group

"Add an account" means **assign roles in the resource group's Access control
(IAM)**. Granting Contributor or Owner alone does not grant token-based Speech
API execution.

1. Confirm the user is a member of the resource's tenant, or an invited guest who
   has accepted the invitation.
2. Run the script (the operator needs Owner, User Access Administrator or RBAC Administrator):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\grant-azure-access.ps1 -User alice@contoso.com
   ```

   `-User` accepts a UPN, a guest's email address or an object ID. If Microsoft Graph lookup is
   blocked or email lookup finds no match, pass the object ID shown in the target tenant's Entra ID > Users.
   The Azure CLI default tenant must match the target subscription's tenant.
   Use `-SpeechOnly` for transcription-only users and `-Remove` to revoke. Existing RG-level
   assignments are not duplicated; removal only affects assignments directly at the RG scope.
3. Allow several minutes for propagation, then test from the user's PC.

In the portal: RG > **Access control (IAM)** > **Add role assignment**, and assign the two user roles above.

RG-scoped assignments are inherited by the matching resources in that RG.
For access limited to one resource, assign the same roles at resource scope.
Ordinary users do not need Owner, Contributor or Cognitive Services Contributor.
Speech User / OpenAI User do not grant permission to retrieve resource keys.

To revoke access, remove the assignments in IAM. Also check access inherited
through security groups, resource-level assignments and subscription assignments.

## User: choose authentication in Settings

### Microsoft Entra ID (recommended for sharing)

Install Azure CLI on the user's PC and sign in to the resource's tenant.

```powershell
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3
```

**Settings → AI connections** has exactly one Azure connection, **Microsoft Foundry**.
Captions (Azure Speech) use the same resource; there is no separate Speech connection.

1. **Microsoft Foundry**: choose the authentication method first (**Microsoft Entra ID** or **API key**);
   the inputs of the other method are hidden. Then enter the endpoint, the chat / embedding / Viva Engage
   image deployment names and the tenant ID.
2. Use **Test connection**, then **Test and save**. The test checks chat, embeddings and caption
   transcription (Speech); a connection whose caption transcription fails is not saved. Outside the
   MAI-Transcribe-2 regions the standard model is used. Testing alone does not save.
3. A separate Speech connection saved by an older version is ignored and removed on the next save.

#### Choose from Azure (no typing of endpoints or deployment names)

Select **Choose from Azure** to list the Microsoft Foundry
resources visible to the signed-in Entra ID identity (default sign-in when the tenant ID box is empty).

- The app reads Cognitive Services accounts in every accessible subscription through Azure Resource
  Manager (`https://management.azure.com`) and keeps only Speech (has a speech-to-text API) and
  Foundry (has the Azure OpenAI API) resources. Computer Vision and others are not shown.
- For Foundry, the deployments of the selected resource are split into **chat** and **embedding**
  candidates by model capability (`chatCompletion` / `embeddings`). Failed or in-progress deployments are hidden.
- Selecting fills the endpoint and deployment boxes below. When key authentication is disabled
  (`disableLocalAuth`), Microsoft Entra ID is selected. Nothing is saved: confirm with
  **Test connection**, then **Test and save**.
- Listing needs read (control-plane) access to the resource, which is included in
  `Cognitive Services Speech User` / `Cognitive Services OpenAI User` granted above. Usage
  (data-plane) permission is checked with the Azure `permissions` API; if missing, the screen says so.
  Deny assignments and propagation delays can change the outcome, so the connection test decides.
- **Keys are never read** while listing (`listKeys` is not called). For key authentication, get the key
  from your administrator.
- If some subscriptions cannot be read, the readable ones are still listed and the others are named.
  Speech resources without a custom domain cannot use Entra ID, and a warning is shown.

Do not enter your Entra password or a client secret. The backend uses Azure
Identity's `DefaultAzureCredential` to obtain tokens for the selected tenant
through existing sign-ins, such as Azure CLI. It does not automatically launch
a browser login from the app. Credentials configured through environment
variables may also participate in that credential chain.

### API key

Select **API key** for each connection, enter the key for **that resource**
received through an approved secure channel, and choose **Test and save**.
Do not reuse another resource's key. On Windows, DPAPI binds encrypted storage
to the current Windows user and PC. Copying the settings file to another user or
PC does not make the key usable. On platforms without implemented OS encryption,
key storage is refused, although testing a key is allowed.

**On the current shared resource, organization policy keeps key authentication disabled.**
Choosing a key in this subscription results in 401 (key rejected). To use keys you need a
resource created with `disableLocalAuth=false` in a subscription whose policy allows it
(Bicep `-p disableLocalAuth=false`). Do not create policy exemptions that bypass security
controls on your own. The app's radio button does not change Azure. A key is a shared
resource credential, not a per-user permission; Entra ID users need no key.

Saved keys are never returned by Settings or the settings API. A blank key field
reuses an existing key only for the same destination (HTTPS host and port).
Saving Entra ID removes that connection's saved key. **Authentication failure
does not automatically fall back to another authentication method.**

## Persistence, tests and troubleshooting

- UI connections are stored in `DataDir/ai-settings.json`, override startup
  configuration, and survive restart. **Remove saved connection** restores the
  startup configuration.
- Changes apply to newly started operations, not already-running jobs.
- Speech testing sends **one second of synthetic silence** through the same fast
  transcription API as generation; it does not send user audio. This verifies
  authentication/connectivity, not recognition accuracy on real media. The result shows
  which model will be used (MAI-Transcribe-2 or standard).
- If you see "Transcribing with the standard model", the resource's region does not support
  MAI-Transcribe-2. The **Select from Azure** list also shows whether each region is supported.
- Foundry testing checks strict JSON Schema chat output and the embedding
  deployment when specified.
- LLM-disabled mode still needs Speech. Speech tests and saves remain enabled.
- `401`: check key validity/local-auth policy, or tenant/token. `403`: check the
  correct user's data-plane roles, scope, propagation and network restrictions.
  `404`: check endpoint/deployment. `429`: check shared quotas/rate limits.
- Speech expects an HTTPS resource root, not `/openai/v1` or `speechtotext/...`.
  Entra ID requires the resource's custom subdomain, not a regional shared host.
- Azure diagnostics in `run.cmd --check` read startup configuration. To test
  UI-saved connections, use the corresponding Settings connection test.
- Never paste keys/access tokens into documentation, screenshots or support requests.

Official references:
[Speech RBAC](https://learn.microsoft.com/azure/ai-services/speech-service/role-based-access-control) /
[Fast transcription](https://learn.microsoft.com/azure/ai-services/speech-service/fast-transcription-create) /
[MAI-Transcribe-2](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe) /
[Azure OpenAI RBAC](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control).
