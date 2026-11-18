# Azure 준비 및 공유 사용자의 인증

<!-- language-nav -->
[日本語](../azure-access.md) | [English](../en/azure-access.md) | [한국어](azure-access.md) | [简体中文](../zh-CN/azure-access.md)
<!-- /language-nav -->

[시작](start.md) / [빠른 시작](quickstart.md) / [설정·API](configuration-api.md)

VideoCreator는 각 사용자의 PC에서 실행합니다. 앱을 Azure에 호스팅하거나 영상 저장용
Storage, Azure SQL을 만들 필요가 없습니다. 미디어·SQLite·렌더링·얼굴 탐지·고화질화는
계속 로컬에서 처리합니다. GitHub Copilot은 선택 가능한 대안이며 Azure 서비스가 아닙니다.

## 필요한 서비스

| 용도 | 필요한 항목 | Entra ID 이용 역할 |
|---|---|---|
| 자막 받아쓰기(필수) | Azure AI Services / Speech, S0, 사용자 지정 하위 도메인. MAI-Transcribe-2를 사용하려면 지원 지역(아래) 필요. 그 밖의 지역은 표준 모델 | Cognitive Services Speech User |
| AI 선정·텔롭·게시글 | Azure AI Services / Azure OpenAI, Structured Outputs 지원 채팅 배포 | Cognitive Services OpenAI User |
| 의미 기반 주제 분할 | 같은 리소스의 임베딩 배포(미설정 시 어휘 기반) | Cognitive Services OpenAI User |

자막 받아쓰기는 기본적으로 Microsoft의 음성 인식 모델 **MAI-Transcribe-2**를 사용합니다.
Azure Speech의 빠른 전사 API에 `enhancedMode`를 붙여 호출하므로 배포를 만들 필요가 없습니다.
MAI-Transcribe-2를 사용할 수 있는 지역은 Central India, East US, North Europe, Southeast Asia,
West Europe, West US, West US 2입니다(2026-11 기준. Japan East는 미지원).
지원되지 않는 지역이나 2시간·250MB 이상의 녹음에서는 자동으로 표준 모델로 전환하고,
연결 테스트·설정 화면·작업 경고에 그 사실을 표시합니다. 표준 모델로 고정하려면
`VideoCreator:Asr:Model`을 `standard`로 설정합니다. 이미지 분석(Azure AI Vision)은 사용하지 않습니다.

**Azure AI Services 리소스 하나를 세 용도로 함께 사용할 수 있습니다.**
Foundry 프로젝트·Video Indexer·Event Grid·추가 관리 ID는 현재 앱 실행 경로에
필요하지 않습니다. 기존 리소스를 삭제할 필요도 없습니다.

### 이 환경의 확인 결과(2026-11-18)

- 구독: `dahatake Internal Subscription`
  (`0e930937-43f5-484d-ba9e-1dd1151cf7ba`)
- 테넌트: `16b3c013-d300-468d-ac64-7eda0820b6d3`
- 리소스 그룹: `rg-videocreator-jpe`
- 리소스: `videocreator-ai-40800`(AIServices / S0 / Japan East)
- 자막용 리소스: `videocreator-mai-9709`(AIServices / S0 / Southeast Asia. MAI-Transcribe-2용으로 2026-11에 추가)
- Speech: `https://videocreator-mai-9709.cognitiveservices.azure.com/`
- Foundry: `https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1/`
- 채팅 배포: `gpt-chat-latest`
- 임베딩 배포: `text-embedding-3-large`

필요한 리소스와 배포는 이미 존재했습니다. 중복 서비스나 모델 용량을 추가하지 않습니다.
추가 고정 용량 비용이 없다는 것은 무료라는 뜻이 아닙니다. Speech 처리 시간,
채팅·임베딩 토큰과 연결 테스트에는 사용량 요금이 적용됩니다. 공유 사용자의 요청도
리소스 소유 구독에 청구됩니다. Azure 예산 및 사용량을 관리하세요.
기존 모델 배포는 **GlobalStandard**입니다. 리소스 위치가 Japan East여도 추론이
일본 내에만 머문다는 보장은 아닙니다. 조직의 데이터 위치 요건을 확인하세요.

**검증 결과(2026-11-18):**

- 기존 리소스에 대한 [infra/main.bicep](../../infra/main.bicep)의 `what-if`는 계정 태그,
  `armFeatures`, `associatedProjects`, `defaultProject`와 배포의 `currentCapacity` 차이를 보고했습니다.
  빈 태그를 지정한 재배포는 `Succeeded`입니다(중복 생성 없음).
- [scripts/grant-azure-access.ps1](../../scripts/grant-azure-access.ps1)로 RG 역할 부여·재실행(중복 없음)·삭제를 실제로 확인했습니다.
  소유자 계정에는 RG 범위의 두 역할을 부여해 두었습니다.
- 앱 연결 테스트에서 Entra ID로 Speech·엄격한 JSON Schema 채팅·임베딩이 성공했습니다.
- 키 인증은 **조직 정책 때문에 사용할 수 없습니다.** 관리 그룹에 할당된
  "AzTB 17 - Disable Local auth for all Cognitive Services"(effect: modify)가 모든
  Cognitive Services에 `disableLocalAuth=true`를 강제합니다. `false`로 업데이트해도 요청 시점에
  `true`로 바뀌는 것을 확인했습니다. 앱의 키 인증은 키를 보내고 401을 "키 거부"로 진단합니다.
- 이 테넌트는 조건부 액세스로 앱 등록 생성을 차단하므로, 다른 권한이 없는 별도 ID로는 검증하지 않았습니다.

## 관리자: Azure 리소스 만들기

[infra/main.bicep](../../infra/main.bicep)은 필요한 것을 한 번에 만듭니다. AIServices(S0, 사용자 지정 하위 도메인,
기본값은 키 인증 비활성), 채팅·임베딩 배포, 선택적으로 사용자 역할입니다. 기존 리소스의 변경은 `what-if`로 확인하세요.
`userObjectIds`는 IAM이나 스크립트로 이미 만든 동일한 역할 할당을 감지하지 못해 충돌할 수 있습니다.
기존 사용자에게는 이 매개 변수 대신 아래 스크립트를 사용하세요.

```powershell
az login --tenant <테넌트 ID>
az group create -n rg-videocreator-jpe -l japaneast
az deployment group what-if -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<고유한 이름>
az deployment group create  -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<고유한 이름>
```

출력의 `speechEndpoint`, `foundryEndpoint`, `chatDeployment`, `embeddingDeployment`, `tenantId`를
설정 화면에 입력합니다. 모델 이름·버전·용량·지역은 해당 지역에서 사용 가능한 값을 매개 변수로 지정합니다.

## 관리자: 리소스 그룹에 사용자 추가

"계정을 추가한다"는 것은 리소스 그룹의 **액세스 제어(IAM)에서 역할을 할당**하는 것입니다.
RG에 Contributor나 Owner만 부여해서는 토큰 기반 Speech API 실행 권한이 생기지 않습니다.

1. 사용자가 리소스와 같은 Entra 테넌트의 멤버이거나 초대를 수락한 게스트인지 확인합니다.
2. 다음 스크립트를 실행합니다(실행자에게 Owner, User Access Administrator 또는 RBAC 관리자 권한 필요).

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\grant-azure-access.ps1 -User alice@contoso.com
   ```

   `-User`에는 UPN, 게스트의 이메일 주소 또는 개체 ID를 지정합니다. 조직 제한으로 Microsoft Graph 검색이
   실패하거나 이메일 검색 결과가 없으면 대상 테넌트의 Entra ID "사용자" 화면에서 확인한 개체 ID를 지정합니다.
   Azure CLI의 기본 테넌트와 대상 구독의 테넌트가 일치해야 합니다. 받아쓰기만 하면 `-SpeechOnly`,
   취소는 `-Remove`입니다. RG 범위에 이미 있는 역할은 중복으로 만들지 않으며 삭제도 RG 범위 할당만 대상으로 합니다.
3. 역할이 반영될 때까지 몇 분 기다린 후 사용자 PC에서 연결 테스트합니다.

포털에서는 대상 RG → **액세스 제어(IAM)** → **역할 할당 추가**에서 위 표의 두 사용자 역할을 부여합니다.

RG 범위의 역할은 그 RG 안의 해당 리소스에 상속됩니다. 하나의 리소스로만 접근을 제한하려면
리소스 범위에 같은 역할을 부여합니다. 일반 사용자에게 Owner, Contributor, Cognitive Services Contributor는
필요하지 않습니다. Speech User / OpenAI User는 리소스 키를 조회하는 권한이 아닙니다.

사용을 중지할 때는 IAM에서 해당 역할 할당을 삭제합니다. 그룹·리소스 단위·구독에서 상속된 할당이
남아 있지 않은지도 확인하세요.

## 사용자: 설정 화면에서 선택

### Microsoft Entra ID(공유 이용에 권장)

사용자 PC에 Azure CLI를 설치하고 리소스의 테넌트에 로그인합니다.

```powershell
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3
```

앱의 **설정 → AI 연결**에서 다음 두 연결을 각각 설정합니다.

1. **Azure Speech**: 엔드포인트, **Microsoft Entra ID**, 테넌트 ID.
2. **Microsoft Foundry**: 엔드포인트, 채팅·임베딩 배포 이름,
   **Microsoft Entra ID**, 테넌트 ID.
3. 각각 **연결 테스트**, 이상이 없으면 **테스트 후 저장**을 선택합니다.
   테스트만 하면 저장하지 않습니다. 저장 버튼은 다시 테스트한 후 저장합니다.

#### Azure에서 선택(엔드포인트·배포 이름 입력 없이)

각 연결의 **Azure에서 선택**을 누르면 로그인한 Entra ID로 볼 수 있는 Azure Speech /
Microsoft Foundry 리소스를 나열하고 선택할 수 있습니다(테넌트 ID가 비어 있으면 기본 로그인).

- Azure Resource Manager(`https://management.azure.com`)에서 액세스 가능한 모든 구독의
  Cognitive Services 계정을 읽고, Speech(음성 텍스트 변환 API가 있는 것)와 Foundry(Azure OpenAI API가
  있는 것)만 표시합니다. Computer Vision 등은 표시하지 않습니다.
- Foundry에서는 선택한 리소스의 배포를 모델 기능(`chatCompletion` / `embeddings`)으로 **채팅**과
  **임베딩** 후보로 나눕니다. 실패했거나 생성 중인 배포는 표시하지 않습니다.
- 선택하면 아래 입력란(엔드포인트·배포 이름)에 값을 넣습니다. 키 인증이 비활성화된
  (`disableLocalAuth`) 리소스는 Microsoft Entra ID로 전환합니다. 저장은 하지 않으므로
  반드시 **연결 테스트** → **테스트 후 저장**으로 확인하세요.
- 나열에는 리소스 읽기(관리 평면) 권한이 필요하며, 위에서 부여하는
  `Cognitive Services Speech User` / `Cognitive Services OpenAI User`에 포함됩니다. 사용(데이터 평면)
  권한은 Azure `permissions` API로 확인하고, 없으면 화면에 표시합니다. 거부 할당이나 반영 지연으로
  결과가 달라질 수 있으므로 최종 판단은 연결 테스트입니다.
- 나열할 때 **키를 읽지 않습니다**(`listKeys`를 호출하지 않음). 키 인증은 관리자에게 키를 받아 입력합니다.
- 일부 구독을 읽지 못해도 읽은 만큼 나열하고, 읽지 못한 구독 이름을 표시합니다. 사용자 지정
  도메인이 없는 Speech 리소스는 Entra ID로 사용할 수 없으므로 경고를 표시합니다.

Entra 암호나 클라이언트 비밀을 앱에 입력하지 않습니다. 백엔드는 Azure Identity의
`DefaultAzureCredential`로 Azure CLI 등 기존 로그인에서 대상 테넌트의 토큰을 얻습니다.
앱이 브라우저 로그인을 자동 실행하는 방식이 아닙니다. 환경 변수 등으로 별도 자격 증명을
설정한 PC에서는 그 자격 증명도 인증 체인의 대상이 될 수 있습니다.

### API 키

각 연결에서 **API 키**를 선택하고 안전한 경로로 받은 **해당 리소스의 키**를 입력한 후
**테스트 후 저장**합니다. 다른 리소스의 키를 재사용하지 마세요. Windows에서는 DPAPI로
현재 Windows 사용자·PC에 연결해 암호화합니다. 다른 PC나 사용자에게 설정 파일을 복사해도
복호화할 수 없습니다. OS 암호화를 구현하지 않은 환경에서는 키 저장을 거부하지만
키를 이용한 연결 테스트는 가능합니다.

**현재 공유 리소스는 조직 정책으로 키 인증이 비활성으로 고정되어 있습니다.**
이 구독에서는 키를 선택하면 401(키 거부)이 됩니다. 키 인증을 사용하려면 정책이 허용하는 다른 구독에
`disableLocalAuth=false`로 만든 리소스가 필요합니다(Bicep `-p disableLocalAuth=false`).
보안 통제를 우회하는 정책 예외를 사용자가 임의로 만들지 마세요. 앱의 라디오 버튼은 Azure를 변경하지 않습니다.
키는 개인별 권한이 아니라 리소스 공용 자격 증명이며, Entra ID 사용자에게는 키 배포가 필요하지 않습니다.

저장한 키는 화면이나 설정 조회 API로 반환하지 않습니다. 빈 키는 같은 대상
(HTTPS 호스트·포트)에 저장한 키만 재사용합니다. Entra ID로 전환해 저장하면 해당 연결의
저장된 키를 삭제합니다. **인증 실패 시 다른 방식으로 자동 폴백하지 않습니다.**

## 저장·테스트·문제 해결

- 화면 연결은 `DataDir/ai-settings.json`에 저장되어 시작 설정보다 우선하며 재시작 후에도
  유지됩니다. **저장된 연결 삭제**를 선택하면 시작 설정으로 돌아갑니다.
- 변경은 다음에 시작하는 작업부터 적용됩니다. 이미 실행 중인 작업의 연결은 바꾸지 않습니다.
- Speech 테스트는 생성과 같은 빠른 전사 API로 **1초의 합성 무음**을 전송합니다.
  사용자 음성은 보내지 않습니다. 인증·연결 검증이며 실제 음성 인식 정확도를 보증하지 않습니다.
  결과에는 사용할 모델(MAI-Transcribe-2 또는 표준)을 표시합니다.
- 「표준 모델로 받아씁니다」라고 표시되면 해당 리소스의 지역이 MAI-Transcribe-2를
  지원하지 않습니다. **Azure에서 선택** 목록에도 지원 여부를 표시합니다.
- Foundry는 엄격한 JSON Schema 채팅 응답 및 지정한 경우 임베딩 배포를 검증합니다.
- LLM 미사용 모드에서도 Speech가 필요하므로 Speech 테스트·저장을 사용할 수 있습니다.
- `401`: 키 및 로컬 인증 정책, 또는 테넌트·토큰을 확인합니다. `403`: 사용자 역할·범위·
  적용 대기·네트워크 제한을 확인합니다. `404`: 엔드포인트·배포 이름,
  `429`: 공유 할당량과 속도 제한을 확인합니다.
- Speech 엔드포인트는 HTTPS 리소스 루트입니다. `/openai/v1`이나 `speechtotext/...`를
  붙이지 않습니다. Entra ID에는 지역 공통 호스트가 아니라 사용자 지정 하위 도메인이 필요합니다.
- `run.cmd --check`의 Azure 진단은 시작 설정을 읽습니다. 화면에서 저장한 연결은
  설정 화면의 연결 테스트로 확인하세요.
- 키·액세스 토큰을 문서·스크린샷·지원 요청에 붙여 넣지 마세요.

공식 근거:
[Speech RBAC](https://learn.microsoft.com/azure/ai-services/speech-service/role-based-access-control) /
[빠른 전사](https://learn.microsoft.com/azure/ai-services/speech-service/fast-transcription-create) /
[MAI-Transcribe-2](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe) /
[Azure OpenAI RBAC](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control).
