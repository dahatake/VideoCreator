# 제작 방침: 문서 의뢰를 「더 깊게」 다듬고 실행한 결과

[↑ 문서 안내](../README.md) | [日本語](../../../users-guide/documentation-brief.md) | [English](../../en/users-guide/documentation-brief.md) | [简体中文](../../zh-CN/users-guide/documentation-brief.md) | **한국어**


[앱 README](../README.md) / [가이드 목차](README.md) / [검증과 유지 관리](verification.md)

이 페이지는 가이드 제작 의뢰를 「더 크게」가 아니라 「더 깊게」 만들기 위해 정리한 기록입니다. 첫 의뢰에 대해 (1) 전문가가 추가로 할 일, (2) 출발점인 원시 자료, (3) 방법·도구, (4) 제3자 검증 수단을 제시하고 의뢰를 다시 쓴 뒤 실행했습니다. 끝에는 수정한 요구 사항별 결과물·증거를 연결했습니다.

## 원래 의뢰(요약)

- `/users-guide`에 처음 사용자를 위한 빠른 시작, 화면 구성 요소의 의미·설명, 화면 스크린샷을 만든다.
- 사용자 지정하는 소프트웨어 엔지니어용 기술 참조·구성 요소도를 만든다.
- `/README.md`에 첫 사용자가 준비할 것, 앱에서 할 일, 얻을 결과물을 그림과 함께 추가한다.
- 모든 그림을 SVG로 만든다.

## (1) 최고 전문가라면 추가로 할 일

테크니컬 라이터나 개발자 경험(DX) 전문가는 페이지 수를 늘리기 전에 다음을 수행합니다.

1. **독자·달성 목표를 하나로 좁힌다**: 첫 사용자의 성공을 「첫 동영상 내보내기」로 정의하고 최단 경로(동영상 한 편+P4+기본 설정)를 정한다. 다른 기능은 「성공 후 시도할 것」으로 분리한다.
2. **전제·실패 조건을 먼저 제시한다**: Azure 리소스, 권한, 데이터 전송 대상, 비용, 권리 확인, 오프라인 불가 범위를 조작 절차 전에 적는다. 성공 예뿐 아니라 막히기 쉬운 위치·원인 구분법도 적는다.
3. **구현에서 화면 설명을 역추적한다**: 외관이 아니라 UI 레이블, 초기값, 표시 조건, 비활성 조건, 전송 API를 소스에서 확인하고 버튼·입력란이 무엇을 바꾸는지 적는다.
4. **스크린샷을 재현 가능하게 만든다**: 수동 캡처 대신 격리 데이터로 실제 앱을 실행해 자동 획득하고, 개인정보를 가리며 재생성 절차를 남긴다.
5. **엔지니어에게 「변경 진입점」「깨지면 안 되는 계약」을 설명한다**: 구성 요소 경계, 데이터 영구 저장, 시간축 변환, QA·승인, 확장 시 실행할 테스트를 제시한다.
6. **설명한 것과 확인한 것을 분리한다**: 실행 확인 범위, 소스만 근거로 한 범위, 미검증 범위(실제 클라우드 전송, 제3자 사용성 평가 등)를 명시한다.
7. **그림을 검증 가능하게 만든다**: SVG에 title/desc를 붙이고 글자 넘침·외부 참조가 없음을 기계 확인한다. 실제 화면 SVG와 개념도를 구분한다.

## (2) 전문가가 출발점으로 삼는 가장 원시적인 자료

요약 README나 과거 설명이 아니라 다음을 1차 자료로 삼았습니다.

| 원시 자료 | 확인 내용 |
|---|---|
| 실행 중 앱 화면(실제 브라우저) | 실제 레이블, 초기 상태, 조작 후 상태 |
| UI 소스 [app.js](../../../src/VideoCreator.App/wwwroot/js/app.js), [index.html](../../../src/VideoCreator.App/wwwroot/index.html) | 표시 조건, 비활성 조건, 입력값 전송 대상 |
| API·설정 [Api.cs](../../../src/VideoCreator.App/Api.cs), [Program.cs](../../../src/VideoCreator.App/Program.cs), [AppOptions.cs](../../../src/VideoCreator.App/AppOptions.cs) | 경로, 헤더, JSON 형식, 시작 설정 |
| 핵심 처리 [Models.cs](../../../src/VideoCreator.Core/Models/Models.cs), [PipelineRunner.cs](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs), [QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs) | 데이터 구조, 처리 순서, QA 조건 |
| 시작·설치 스크립트 [run.cmd](../../../run.cmd), [setup.ps1](../../../scripts/setup.ps1), [get-tools.ps1](../../../scripts/get-tools.ps1) | 사용자가 준비할 것, 자동 준비되는 것 |
| 프리셋 [presets](../../../presets) | 패턴별 길이·텔롭·소리 방침 |
| 기존 테스트·HTTP 예시 [tests](../../../tests), [exports.http](../../../src/VideoCreator.App/exports.http) | 계약으로 유지되는 동작 |
| 실제 내보낸 파일(MP4·SRT·ASS·JSON·PNG) | 결과물 형식. ffprobe 등으로 확인 |
| Microsoft Learn(Azure Speech Fast Transcription) | 클라우드 측 전제·제약 |

## (3) 구체적인 방법과 도구

| 방법 | 도구 | 이 가이드의 사용법 |
|---|---|---|
| 작업 분석·최단 경로 설계 | 소스 해독, 실제 화면 조작 | [빠른 시작](quickstart.md)을 「준비→실행 확인→원본→생성→확인→내보내기」 6단계로 구성 |
| 구현에서 UI 사양 추출 | grep/소스 해독 | [화면 설명](screens.md)의 각 입력란에 의미·초기값·사용 상황 기록 |
| 격리 환경 실행 | `VideoCreator__DataDir`·`__ProjectsDir`·`__Port` 등 환경 변수, 합성 원본 [seed.cs](../../../users-guide/tools/seed.cs) | 사용자 데이터·클라우드에 접근하지 않고 화면·출력 재현 |
| 자동 스크린샷 | Playwright+Microsoft Edge [capture.mjs](../../../users-guide/tools/capture.mjs) | 실제 화면 PNG를 SVG에 삽입하고 경로 가림 |
| 그림 작성 | 수작업 SVG(title/desc 포함) | 전체 모습·구성도·처리 흐름·시간축·QA 5개 |
| 출력 확인 | ffprobe, API 획득 | 코덱, 해상도, 음성 형식, 길이, 출력 키 확인 |
| 문서 기계 검증 | [validate.mjs](../../../users-guide/tools/validate.mjs), `git diff --check` | 링크·제목 앵커·SVG XML·삽입 PNG·글자 넘침·렌더링 |

## (4) 여러분이나 저 이외에 정확성을 보여 주는 것

| 제3자 판정자 | 보여 주는 것 | 상태 |
|---|---|---|
| 실제 앱 자체 | 스크린샷이 상상도가 아닌 실제 화면임 | 실행: Edge 자동 캡처 |
| 내보낸 파일·ffprobe | README의 결과물이 실제 출력됨(H.264/AAC, 1080×1920, 8종 출력) | 실행 |
| 앱 자동 QA | 내보내기 하드 조건 | 실행: 전부 pass(합성 원본) |
| 브라우저 SVG 렌더링·XML 파서 | 그림이 깨지거나 글자가 넘치지 않음 | 실행: 20개 모두 성공 |
| 링크 검증 스크립트 | 자료 사이·소스 링크가 끊기지 않음 | 실행 |
| 빌드·기존 테스트 | 설명한 계약과 코드의 일치 | 일부 실행(빌드 성공. 전체 테스트 재실행 안 함) |
| 처음 사용하는 제3자 | 가이드만으로 한 편 내보내기 가능 | **미실행**. [갱신 체크리스트](verification.md#갱신-체크리스트) 6번에 절차 기록 |
| 실제 Azure 전송 | 실제 발화의 인식·비용 | **미실행**. 합성 결과와 구분해 기록 |

## 다시 쓴 의뢰

> VideoCreator를 처음 쓰는 사람과 사용자 지정하는 소프트웨어 엔지니어용 문서를 현재 작업 트리 구현과 실제 화면을 1차 자료로 작성하세요. 요약 README나 기존 설명을 근거로 삼지 말고 UI 소스, API, 모델, 시작 스크립트, 프리셋, 기존 테스트, 실제 내보낸 파일로 확인하세요.
>
> 1. **README(`/README.md`)**: 첫 사용자가 「무엇을 준비하고(PC, 권리 확인한 영상, 본인 Azure 리소스·권한)」「앱에서 무엇을 하며(실행, 원본 추가·권리 확인, 패턴 선택, 생성, 확인·수정, 내보내기)」「무엇을 얻는지(MP4·SRT·ASS·편집 계획·썸네일·메타데이터 제안)」 표 하나·SVG 하나로 보여 주세요. 전송 대상, 오프라인이 아닌 점, 자동 QA가 권리·내용을 보장하지 않는 점을 주의로 적으세요.
> 2. **빠른 시작**: 첫 동영상 내보내기를 성공 조건으로 삼고 최단 경로(영상 한 편+P4+표준 설정)로 좁히세요. 각 단계에 「확인되면 다음으로」 판정을 두세요.
> 3. **화면·구성 요소 설명**: 실제 앱을 격리 환경에서 시작하고 Playwright+Edge로 자동 캡처해 SVG에 삽입하세요. 버튼·입력란·스위치별 의미, 초기값, 표시·비활성 조건, 사용 상황을 소스에서 확인해 적으세요. 합성 원본임과 화면 개인정보 가림을 명시하세요.
> 4. **문제 해결**: 실패 원인 구분 순서, 자주 발생하는 증상, QA 결과 읽기, 저장·백업.
> 5. **기술 참조**: 구성 요소도(SVG), 생성·내보내기 흐름, 데이터 모델·영구 저장, 혼동하기 쉬운 시간축, 승인·QA·재현성 계약, 사용자 지정 진입점·필요 테스트, 빌드 절차. 설정·API는 별도 페이지로 나열하세요.
> 6. **그림**: 전부 SVG. 개념도에 title/desc를 붙이고 실제 화면 SVG와 구분하세요.
> 7. **검증**: 링크·제목 앵커·SVG 문법·삽입 이미지·글자 넘침·브라우저 렌더링을 자동 검증하는 스크립트를 마련해 실행하세요. 실제 한 편을 내보내고 ffprobe·API로 확인하세요. 실행 확인, 소스만 근거로 한 것, 미검증(실제 클라우드·제3자 평가 등)을 분리 기록하고 스크린샷 재생성 절차를 남기세요.
> 8. 작업 트리에 이미 있는 앱 변경은 되돌리지 마세요.

## 실행 결과와의 대응

| 수정 요구 사항 | 결과물 | 정확성 증거 |
|---|---|---|
| 1. README: 준비→조작→결과물 | [README](../README.md#처음-사용하는-분께-준비--조작--결과물), [전체 모습](images/overview.svg) | 8키 결과물을 실제 내보내고 ffprobe 형식 확인([검증 기록](verification.md#이번-실행-확인)) |
| 2. 빠른 시작 | [quickstart.md](quickstart.md) | 합성 원본으로 같은 절차(권리 확인→P4 생성→확인→내보내기)를 실제 UI 실행 |
| 3. 화면 설명+스크린샷 | [screens.md](screens.md), 실제 화면 SVG 15개([images](../../../users-guide/images)) | [capture.mjs](../../../users-guide/tools/capture.mjs)로 실제 앱 자동 획득. 스크립트로 삽입 PNG·렌더링 확인 |
| 4. 문제 해결 | [troubleshooting.md](troubleshooting.md), [QA 그림](images/qa-gates.svg) | [QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs)와 조건 대조. 실행 QA 전부 pass |
| 5. 기술 참조 | [technical-reference.md](technical-reference.md#1-구성-요소), [구성 요소도](images/components.svg), [처리 흐름](images/pipeline.svg), [시간축](images/timelines.svg), [설정·API](configuration-api.md) | 빌드 성공(경고 0·오류 0). 경로·설정 소스 대조 |
| 6. 그림 전부 SVG | 개념도 5+실제 화면 15=20개 | [validate.mjs](../../../users-guide/tools/validate.mjs): SVG 외 파일 없음, XML, title/desc, 외부 참조, 글자 넘침, 렌더링 |
| 7. 검증·기록 | [verification.md](verification.md) | 검증 스크립트 PASS. 미검증 항목 명시 |
| 8. 기존 변경 유지 | — | 앱 소스 변경을 되돌리지 않음 |

미실행 항목(제3자 첫 사용자 평가, 실제 Azure 실제 발화 시험, 전체 테스트 재실행)은 결과가 정확하다는 증거로 취급하지 않습니다.
---

[↑ 문서 안내](../README.md) | [日本語](../../../users-guide/documentation-brief.md) | [English](../../en/users-guide/documentation-brief.md) | [简体中文](../../zh-CN/users-guide/documentation-brief.md) | **한국어**
