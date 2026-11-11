# 자료 검증과 유지 관리

[앱 README](../README.md) / [가이드 목차](README.md) / [기술 참조](technical-reference.md) / [제작 방침](documentation-brief.md)

## 1차 자료와 검증 범위

이 가이드는 2026-11-08 작업 트리를 조사한 것입니다. 작업 중 존재한 앱 변경을 되돌리지 않고 구현·설정·테스트를 근거로 설명했습니다. Git에 커밋되지 않은 변경도 포함하므로 특정 릴리스 태그에 대한 사양 보증은 아닙니다.

| 설명 대상 | 확인 방법 |
|---|---|
| 시작·준비 | [run.cmd](../../../run.cmd), [setup.ps1](../../../scripts/setup.ps1), [AppOptions](../../../src/VideoCreator.App/AppOptions.cs) |
| 화면·입력·조작 | [app.js](../../../src/VideoCreator.App/wwwroot/js/app.js)와 실제 브라우저 |
| API·승인·처리 조건 | [Api.cs](../../../src/VideoCreator.App/Api.cs), [JobQueue](../../../src/VideoCreator.App/JobQueue.cs), 기존 [HTTP 예시](../../../src/VideoCreator.App/exports.http) |
| 모델·처리·출력 | [Models](../../../src/VideoCreator.Core/Models/Models.cs), [PipelineRunner](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs), Renderer, QaEngine |
| Azure 전제 | Microsoft Learn [Fast Transcription](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create) 및 구현의 연결·인증 코드 |
| 화면 SVG | 실제 앱을 Edge에서 표시하고 PNG를 SVG에 삽입. 개념도와 구분 |
| 설명·그림의 일관성 | 로컬 링크·제목 앵커·XML·이미지 삽입·문자 영역·브라우저 렌더링 기계 검증 |

### 이번 실행 확인

- 일반 Release 출력 위치가 실행 중 프로세스에 잠겨 해당 프로세스를 중지하지 않고 **독립 artifacts 출력 위치**로 빌드. 성공(경고 0, 오류 0).
- 전용 DataDir·ProjectsDir·port로 실행하고 health 응답과 필수 필터 탐지 확인.
- 합성 영상+전사 캐시로 실제 UI의 권리 확인→P4 후보 생성→확인 화면→직접 최종 내보내기 수행.
- 최종 QA 하드 결과가 모두 pass임을 확인.
- `mp4` / `srt` / `ass` / `editplan` / `metadata` / `thumb1` / `thumb2` / `thumb3` 8개 키와 대응 API에서 비어 있지 않은 파일을 얻을 수 있음 확인.
- 실제 파일을 ffprobe로 확인: H.264, 1080×1920, AAC, 48kHz, 2ch. P4 출력은 27.20초, QA 측정값은 −14.0 LUFS, −12.5 dBTP(정현파 fixture 결과이며 실제 발화의 음질 평가는 아님).
- 같은 원본으로 시리즈 후보 2편을 생성하고 시리즈 구조·화면 확인.
- 가이드 전체 SVG와 로컬 링크를 [검증 스크립트](../../../users-guide/tools/validate.mjs)로 확인.

최종 검증 결과: **Markdown 9개(루트 README와 [제작 방침](documentation-brief.md) 포함), 로컬 링크·앵커 211개, SVG 20개(개념도 5개·실제 화면 15개)**. XML, 삽입 PNG, 그림 문자 영역, Edge 렌더링, 보조 스크립트 문법, 차이의 공백 검사 모두 성공했습니다. 최종 캡처에서는 SRT 시간 형식, ASS Events, 편집 계획 Canvas/Timeline, 썸네일 PNG 형식, MP4 코덱·해상도·음성 형식·P4 길이도 검증했습니다.

**이번에 실행하지 않은 것**: 실제 Azure/Copilot 원본 전송, 실제 발화 인식 정확도 시험, P1~P10 전체 생성, 모든 OS 대화 상자·저장 위치 이동·삭제·일괄 승인 조작, 저장소 전체 테스트 재실행, SNS 게시, 제3자 초보자의 사용성 평가. 이 기능 설명은 소스·기존 테스트를 근거로 하며 이번 실행 결과와 구분합니다.

음량 등 QA를 통과하는 합성 데이터를 사용했다는 사실은 실제 음성 내용 일치를 검증한 증거가 아닙니다. 실제로는 허가받은 본인 원본으로 첫 동영상을 확인하세요.

## 내보내기 결과물 UI 회귀 확인(2026-11-11 추가)

- [ExportArtifactTests](../../../tests/VideoCreator.Tests/ExportArtifactTests.cs): 결과물 절대 경로·누락·형식, 저장된 게시글, 텍스트 65,536자 제한, 전체 가져오기, 영상 Range 응답, 편집 후 무효화를 실제 API로 확인.
- [verify-export-artifacts.mjs](../../../ui/verify-export-artifacts.mjs): Edge에서 실제 합성 H.264 영상 재생·탐색, 영상별 경로·복사 대상, 저장 시 원래 파일명, 게시글 읽기 전용 표시, 이미지·텍스트 미리 보기, 완료 후 자동 표시, 실패 표시·재시도, 좁은 화면, 화면 전환 경쟁 확인. API는 fixture이며 클립보드도 사용자 내용을 덮어쓰지 않는 테스트 구현. 탐색기 실제 실행·SNS 게시 미실행.
- [verify-multi-export.mjs](../../../ui/verify-multi-export.mjs): 기존 승인·일괄 내보내기 흐름 회귀 확인.

격리 앱을 시작해 health 응답을 확인한 후 실행합니다(URL은 실행 포트에 맞춤).

```powershell
dotnet test --project tests\VideoCreator.Tests --filter-class VideoCreator.Tests.ExportArtifactTests VideoCreator.Tests.ApprovalExportTests VideoCreator.Tests.PostCopyTests
node ui\verify-export-artifacts.mjs http://127.0.0.1:18788
node ui\verify-multi-export.mjs http://127.0.0.1:18788
```

## 스크린샷 재생성

### 1. 전제

.NET SDK 10, FFmpeg·글꼴·모델, Node.js, OS에 설치된 Microsoft Edge를 사용합니다. 앱 사용자는 Node.js가 필요 없지만 **화면을 다시 만드는 개발자에게는 필요**합니다. Playwright는 [ui/package.json](../../../ui/package.json)의 기존 의존성을 사용합니다. 의존성이 없다면 `ui`에서 `npm ci`를 실행하세요. 브라우저 추가 다운로드는 필요 없습니다.

명령은 저장소 루트 PowerShell에서 실행합니다. 예시 경로를 본인 작업용 경로로 바꾸세요.

### 2. 격리 fixture 준비

[seed.cs](../../../users-guide/tools/seed.cs)는 기존 데이터가 있는 폴더를 거부합니다. 새 빈 디렉터리를 지정하세요.

```powershell
dotnet run --file users-guide\tools\seed.cs -- C:\Temp\VideoCreatorGuideCapture
```

생성 내용은 260초 테스트 패턴 영상과 정현파, 프로젝트/원본 DB 기록, [talk_transcript.json](../../../tests/VideoCreator.Tests/Fixtures/talk_transcript.json)을 130초 이동해 두 번 나열한 전사 캐시입니다. 자막은 **이 정현파를 인식한 결과가 아닙니다**. 처음의 130초 fixture는 무음 컷 후 2편 최소 길이를 충족하지 못해 연장했습니다.

이 fixture 키는 현재 ASR 캐시 방식과 빈 사전에 대응합니다. 캐시 구현·사전·ASR Name을 바꿨다면 seed도 갱신하세요. 검증 엔드포인트를 실제 사용에 전용하지 마세요.

### 3. 전용 포트 실행

별도 PowerShell에서 다음을 실행합니다. 실행 중 앱이나 실제 프로젝트의 DataDir는 지정하지 않습니다. 추가 Dictionary 환경 변수 등 실제 사용의 재정의 설정이 남아 있지 않은 새 터미널을 사용하세요.

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

port 사용 중이면 다른 port를 지정하고 이후 URL도 맞추세요. LLM은 비활성화, ASR은 fixture 캐시를 읽습니다. 캐시가 일치하지 않아도 외부 서비스가 아니라 loopback 미사용 포트에서 실패하도록 구성합니다.

실행 중 바이너리와 충돌하면 독립 출력 위치에서 빌드·실행합니다.

```powershell
dotnet build src\VideoCreator.App\VideoCreator.App.csproj -c Release `
  --artifacts-path C:\Temp\VideoCreatorGuideBuild
dotnet C:\Temp\VideoCreatorGuideBuild\bin\VideoCreator.App\release\VideoCreator.App.dll `
  --contentRoot "$PWD\src\VideoCreator.App"
```

이 역시 위 환경 변수를 지정한 같은 PowerShell에서 실행합니다.

### 4. 캡처·검증

시작 응답을 확인한 후 다른 터미널에서 실행합니다.

```powershell
Invoke-RestMethod http://127.0.0.1:18765/api/health
node users-guide\tools\capture.mjs http://127.0.0.1:18765
node users-guide\tools\validate.mjs
git diff --check -- README.md users-guide
```

[capture.mjs](../../../users-guide/tools/capture.mjs)는 `guide-demo`만 있는 전용 환경, LLM 미사용, ASR loopback endpoint임을 검사한 뒤 조작합니다. 실제 앱에 새 생성·내보내기를 남기고 [images](../../../users-guide/images)의 화면 SVG를 갱신합니다. 개인 이름이 포함될 수 있는 CLI 경로는 화면에서 가립니다. 이미지 획득 후 시작 터미널에서 Ctrl+C를 누르고 불필요해진 전용 작업 디렉터리만 정리하세요.

## 갱신 체크리스트

1. UI 레이블·초기값·표시 조건이 바뀌면 [화면 설명](screens.md)과 캡처를 갱신한다.
2. 설정·API record·오류·상태가 바뀌면 [설정·API](configuration-api.md)와 조작 예시를 갱신한다.
3. 출력 키·QA·캐시·시간축이 바뀌면 [기술 자료](technical-reference.md)와 seed/capture를 갱신한다.
4. 처음 필요한 권한·요금·전송 대상이 바뀌면 [빠른 시작](quickstart.md)과 [README](../README.md)를 갱신한다.
5. SVG를 기계 검증하고 주요 그림을 눈으로 읽을 수 있는지 확인한다. 링크 검증은 외부 URL 가용성이나 설명의 진위까지 증명하지 않는다.
6. 개발자가 아닌 첫 사용자에게 가이드만으로 한 편을 내보내도록 하고 헷갈린 입력란·실패 조건을 기록한다. 이번에는 이 제3자 평가를 실행하지 않았다.
