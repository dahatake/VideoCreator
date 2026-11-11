# 설정·API 참조

[↑ 문서 안내](../README.md) | [日本語](../../../users-guide/configuration-api.md) | [English](../../en/users-guide/configuration-api.md) | [简体中文](../../zh-CN/users-guide/configuration-api.md) | **한국어**


[앱 README](../README.md) / [가이드 목차](README.md) / [빠른 시작](quickstart.md) / [화면](screens.md) / [문제 해결](troubleshooting.md) / [구성·확장](technical-reference.md) / [검증과 유지 관리](verification.md)

## 시작 설정

UI 표시 언어는 브라우저의 언어 선택으로 저장합니다. 동영상의 음성 언어는 프로젝트별로 지정합니다. `POST /api/projects`에는 `{"name":"Example","language":"en-US"}`, `PATCH /api/projects/{id}`에는 `{"language":"ko-KR"}`처럼 지정할 수 있습니다. 지원값은 `ja-JP`(생략 시 기본값)·`en-US`·`zh-CN`·`ko-KR`입니다. 미지원 값은 422, 생성·내보내기 중 언어 변경은 409를 반환합니다. 변경 후에는 새 후보를 생성하세요. 기존 편집 계획은 생성 당시 언어를 유지합니다.

[appsettings.json](../../../src/VideoCreator.App/appsettings.json)의 `VideoCreator` 섹션, 환경 변수, 명령줄로 설정합니다. 환경 변수는 `VideoCreator__Asr__Endpoint`처럼 구분자를 `__`로 씁니다. 아래 표는 [AppOptions](../../../src/VideoCreator.App/AppOptions.cs)의 기본값입니다. 배포 파일의 실제 엔드포인트는 본인의 값으로 바꾸세요.

| 설정 | 기본값·의미 |
|---|---|
| Port / OpenBrowser | `8765` / `true`. loopback 수신과 시작 후 브라우저 표시 |
| DataDir | 미지정 시 `%LOCALAPPDATA%\VideoCreator`. DB와 캐시 |
| ProjectsDir | 미지정 시 Windows MyVideos의 `VideoCreator`. 새 프로젝트용. 화면에서 저장한 값 우선 |
| FfmpegBin / FontsDir / PresetsDir | 미지정 시 저장소 상위의 tools 등 탐색 |
| FaceModel / UpscaleModel | 미지정 시 tools의 UltraFace / Real-ESRGAN 모델 |
| LocalOnly | `false`. `true`이면 LLM 미사용 고정. ASR은 중지하지 않음 |
| Asr.Provider / Endpoint / Locale | `azure` / 빈 값 / `ja-JP`. Provider가 azure이고 Endpoint가 설정되어 있으면 ASR 활성. 실제 전사 언어에는 프로젝트의 `language`(동영상 언어)를 우선 사용 |
| Llm.Provider | `azure-openai`. `azure-openai` / `copilot` / `none` |
| Llm.Endpoint / Deployment | 빈 값 / `gpt-chat-latest`. 본인 배포 이름 설정 |
| Llm.CopilotCliPath / CopilotModel | 미지정 시 CLI 탐지/기본 모델 |
| Llm.SingleCallCharBudget | `12000`. 표준 분할 판단용 글자 수. 토큰 상한 자체가 아님 |
| Dictionary | `[]`. ASR 구문 목록과 미등장 단어 검사에 쓰는 고유명사 |

예: 다른 포트에서 시작하고 AI 선정을 사용하지 않는 경우(Speech 설정은 별도 필요).

```powershell
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
dotnet run --project src\VideoCreator.App -c Release
```

환경 변수는 해당 PowerShell과 자식 프로세스에 적용됩니다. 다음 터미널에서는 다시 설정합니다. ASR도 비활성화하고 화면만 확인하려면 `VideoCreator__Asr__Provider=none`을 지정하지만, 일반적인 자막 생성 사용 방식은 아닙니다.

## 설정의 수명

- 화면에서 바꾼 LLM 방식은 Runtime에 반영됩니다. 재시작하면 시작 설정으로 돌아갑니다.
- 화면에서 바꾼 프로젝트 저장 위치는 Store의 setting에 영구 저장됩니다. 시작 설정보다 우선합니다.
- 화면에서 선택한 패턴·생성 옵션은 프로젝트별로 Store에 저장하고 재시작 후에도 복원합니다. 생성 시 Job.Options에도 기록합니다.
- 워크플로의 마지막 표시 단계와 대상 작업·편집 계획·내보내기는 Project.WorkflowState에 저장합니다. 참조가 무효하면 이전 단계로 돌아갑니다.
- BrandKit은 공통 Store, BGM은 라이브러리 또는 프로젝트, 이미지 생성 여부는 Project에 있습니다.
- 프리셋은 시작 시 로드합니다. 파일을 편집했다면 재시작하세요.

## HTTP 계약

- 기본 base URL: `http://127.0.0.1:8765`. 아래 모든 경로 앞에 `/api`를 붙입니다.
- JSON은 `snake_case`이며 enum도 문자열입니다. 공유 정의는 [Json](../../../src/VideoCreator.Core/Json.cs)과 [Models](../../../src/VideoCreator.Core/Models/Models.cs).
- 쓰기 요청에는 `X-VideoCreator: 1`이 필수입니다. JSON은 `Content-Type: application/json`.
- GET/HEAD에는 이 헤더를 요구하지 않습니다. 헤더는 인증이나 API 키가 아니라 교차 사이트 쓰기를 막는 방어 장치입니다.
- 일반 오류는 `{ "error": { "code": "...", "message": "...", "detail": ... } }`. HTTP status와 code를 모두 처리하세요. Host 거부·헤더 누락은 일반 텍스트일 수도 있습니다.
- 비동기 내보내기의 `202`는 접수이며 완료가 아닙니다. 반환된 render ID를 폴링하세요.
- 일반 요청 상한은 64MiB입니다. **영상 upload 경로는 자체 상한을 해제**하고 multipart를 임시 버퍼링한 뒤 복사합니다. BGM은 HTTP body를 2000MiB로 바꾸지만(한 번에 최대 20곡) multipart 파싱에도 별도 상한이 있습니다. 다른 경로에 영상과 같은 무제한 조건을 적용하지 마세요.

## 경로 목록

전체 핸들러와 요청 record의 1차 자료는 [Api.cs](../../../src/VideoCreator.App/Api.cs)입니다. 아래 `{id}`는 각 리소스의 ID입니다.

| 분야 | Method·경로 | 내용 |
|---|---|---|
| 진단 | GET `/health` | FFmpeg·부족 필터·ASR·LLM·모델 등. 설정·탐지이며 클라우드 연결을 보장하지 않음 |
| 설정 | GET / PUT `/settings` | 읽기/`{"llm_provider":"none"}` 등으로 LLM 전환 |
| 저장 위치 | GET `/settings/projects-dir` | path, source, configured_path, 건수, 이동 진행률 |
| 저장 위치 | POST `/settings/projects-dir/plan` | `{"path":"D:\\Videos\\VideoCreator","move":true}` 사전 확인 |
| 저장 위치 | PUT `/settings/projects-dir` | 같은 형식으로 실제 변경/이동 |
| 패턴 | GET `/presets` | 시작 시 로드한 프리셋 |
| Project | GET / POST `/projects` | 목록/`{"name":"작업 이름"}`으로 생성 |
| Project | GET / PATCH / DELETE `/projects/{id}` | 상세(`resume_state`에 재개 단계·대상 ID 반환)/`{"name":"새 이름"}`/삭제 |
| Project | PUT `/projects/{id}/generation-settings` | 패턴과 생성 옵션을 프로젝트에 저장 |
| Project | PUT `/projects/{id}/workflow-state` | 현재 단계(`project` / `settings` / `job` / `candidates` / `exports` / `review` / `render`) 저장. `exports`는 `job_id` 필수이며 Job이 `ready` / `done`일 때만 가능 |
| Asset | POST `/projects/{id}/assets` | `{"path":"C:\\Videos\\talk.mp4"}`. 복사하지 않음 |
| Asset | POST `/projects/{id}/assets/upload` | multipart `file` 필드로 영상 복사 |
| Rights | POST `/projects/{id}/rights` | `{"asset_id":"...","confirmed":true}` |
| 견적 | GET `/projects/{id}/estimate?preset=P4` | scope별 전송량·단가 기반 추정 |
| Asset | PATCH `/assets/{id}` | role, speaker, sync_offset, clear_speaker |
| Transcript | GET / PATCH `/assets/{id}/transcript` | 읽기/`{"edits":[{"word_id":"...","text":"수정문"}]}` |
| 소리 | GET `/bgm?project_id=...` | 라이브러리·프로젝트 곡. 각 곡의 `analysis`(lufs, lra, start, end, bpm, onset_rate, brightness, energy, short_term) |
| 소리 | POST `/projects/{id}/bgm` | multipart. file(복수 가능, 최대 20), 권리 확인 `rights_confirmed`. 등록 시 곡 측정. 201 `{tracks, skipped}`(읽지 못하거나 5초 미만인 곡은 skipped에 이유) |
| 소리 | DELETE `/projects/{id}/bgm/{bgmId}` | 등록 해제(파일도 삭제). 편집 계획에서 사용 중이면 409 `E-BGM-IN-USE` |
| OS | POST `/dialogs/folder` | `{"initial_path":"..."}`. 취소는 `{"path":null}` |
| 카드 | POST `/projects/{id}/scene-cards` | asset_id **또는** brand_path, count 2~6(기본 4). 둘 다 지정 불가 |
| 카드 | PUT `/projects/{id}/scene-cards/settings` | `{"make":true}`. 이후 생성에서 이미지 사용 |
| 카드 | GET `/projects/{id}/scene-cards/{index}` | 추출 이미지(0부터 시작) |
| BrandKit | GET / PUT / DELETE `/brandkit` | 공통 등록. PUT은 paths, rights_confirmed, card_sec, upscale |
| BrandKit | POST `/brandkit/enhance` | 등록된 영상의 화질 개선 시작 |
| 생성 | POST `/projects/{id}/jobs` | preset_ids, instruction, options |
| Job | GET `/jobs/{id}` | 상태, 후보 ID, 경고 등 |
| Job | POST `/jobs/{id}/cancel` | 대기/실행 중 생성 취소 |
| Job | GET `/jobs/{id}/events` | SSE(EventSource), stage·progress 등 |
| 후보 | GET `/jobs/{id}/candidates` | candidate, preview, preview_url, approval, export, final_render_id |
| 승인 | PUT `/jobs/{id}/candidates/{candidateId}/approval` | `{"approved":true}`/false로 취소 |
| 일괄 | POST `/jobs/{id}/exports` | `{"candidate_ids":["..."]}`. 전체 사전 검증 후 202. 불가 항목이 하나라도 있으면 아무것도 시작하지 않고 개별 내보내기와 같은 코드(`E-NOT-APPROVED`·`E-PLAN-STALE`·422 `E-QA-HARDFAIL`·`E-EXPORT-BUSY` 중 가장 먼저 해당한 것)와 후보별 `code`·`reason` 반환 |
| 재생성 | POST `/jobs/{id}/regenerate` | instruction, exclude_candidate_ids. 이전 후보 제외 후 다시 선정 |
| 편집 | GET / PATCH `/editplans/{id}` | 계획과 계획 QA/ops로 편집 |
| QA 승인 | PUT `/editplans/{id}/qa/approval` | `plan_hash`, `checks:[{check_id,result_hash}]`, `approved`. 결과 확인 기록. false로 취소 |
| Preview | POST `/editplans/{id}/preview` | 해당 계획 미리 보기 재생성 |
| Preview | GET `/editplans/{id}/preview/progress` | running, progress, step |
| 직접 출력 | POST `/editplans/{id}/render` | 계획 QA 확인 후 개별 내보내기 접수 |
| Render | GET `/renders/{id}` | queued / rendering / done / failed / cancelled 등 |
| Render | POST `/renders/{id}/cancel` | queued만 취소 |
| Artifacts | GET `/renders/{id}/artifacts` | 완료 내보내기의 `path`(절대 경로), `available`, `files:[{key,path,available,preview_type}]`. 종류는 `video` / `image` / `text`(미지원은 생략). 기록된 결과물만 반환하며 게시글은 생성하지 않음 |
| Files | GET `/renders/{id}/files/{key}` | outputs 키에 대응하는 파일. 영상은 Range 지원 |
| Preview | GET `/renders/{id}/files/{key}/preview` | 텍스트 결과물의 `{text,truncated}`. 앞 65,536자까지. 미지원 형식 422, 파일 누락 404, 무효화 409 |
| Post | GET / POST `/renders/{id}/post` | GET은 저장된 게시글 반환(없으면 생성·저장). POST는 다른 제안으로 덮어쓰기. 화면은 저장 파일 유무를 확인하고 미생성 시 명시적 조작으로 호출 |
| OS | POST `/renders/{id}/open-folder` | 탐색기에서 완료 파일 선택. 누락 404, 실행 실패 500 `E-OPEN-FOLDER`, Windows 외 501 `E-PLATFORM` |
| Media | GET `/media/{renderId}` | 주로 미리 보기 영상. Range 지원 |

BGM multipart의 권리 확인 등은 [Api.cs 구현](../../../src/VideoCreator.App/Api.cs)을 참조하세요. 경로를 전달하는 API와 OS 대화 상자는 로컬 단일 사용자용이며 원격 조작용 범용 계약이 아닙니다.

QA 승인 해시는 편집 계획 GET 응답의 `plan_hash`와 각 QA 항목의 `review_hash`를 사용합니다. 개별은 1항목, 일괄은 실행됨·미승인 전체 항목을 보냅니다. 승인 상태는 `review_approval`에 반환됩니다. 빈 항목·중복·알 수 없음·미실행 항목은 422, 계획이나 결과가 바뀌었으면 409이며 부분 저장하지 않습니다. 판정이나 텔롭·카드·후보의 승인 상태는 바꾸지 않습니다.

## Job.Options

| 키 | 값·제약 |
|---|---|
| scope | economy / balanced / full(기본 balanced) |
| bgm | auto / none / scene(장면별 자동 선곡. 프로젝트 곡, 없으면 내장곡)/곡 ID |
| sfx / tsukkomi | bool. AI 반응 코멘트는 P3에서 사용 |
| tsukkomi_style | `auto`(기본)/`telop`/`card`. AI 반응 코멘트 표시 방식. `telop`은 영상을 멈추지 않는 오버레이, `card`는 발언 직후 2~3초 정지 카드(이미지/패널+문자+전용 BGM). `auto`는 발언이 끝나고 다음 발언이 문답이 아니며, 본편 끝·다른 카드/오프닝에서 8초 이상 떨어지고, 카드 합계가 본편의 10% 이내인 곳만 카드, 나머지는 텔롭. 그 밖의 값은 422 |
| parts / part_minutes | 편수 2~20 **또는** 한 편의 분수 1~60. 상호 배타적. 둘 다 null이면 짧은 영상 |
| opening / closing / scene_cards | bool. 사용 가능한 공통 등록 등 확인 |
| mask_words | 가릴 단어 배열 |

화면에서는 패턴 하나를 고릅니다. API에서 preset_ids가 배열이어도 현재 파이프라인은 첫 항목을 사용하므로, 한 번의 생성으로 여러 프리셋을 비교하는 API로 취급하지 마세요.

## 편집 조작

PATCH `/editplans/{id}`에 `{"ops":[...]}`를 보냅니다. [PlanEditor](../../../src/VideoCreator.Core/Planning/PlanEditor.cs)와 API 추가 제약을 따릅니다.

| op | 추가 키 |
|---|---|
| replace_text | target(텔롭 ID), text |
| set_enabled | target, enabled |
| set_type_enabled | type(caption·emphasis·headline·sfx·tsukkomi·opening·card·closing), enabled. tsukkomi는 반응 코멘트 텔롭과 카드 모두, card는 반응 코멘트 이외 장면 전환 카드 |
| approve / approve_all_tsukkomi | 전자는 target |
| set_bgm / set_sfx | 전자는 target(곡 ID=전체에 이 곡 / none / scene=자동 재선곡), 후자는 enabled |
| set_bgm_cue | target=장면 ID(`bgm1`…), text=곡 ID / none / auto(자동 선곡으로 복귀) |
| set_bgm_trim | target=장면 ID, value=그 장면 곡 음량(dB, −12~+6) |
| set_bgm_level | type=`ld`(말하는 동안 음성−BGM, 10~30 LU)/`gap`(음성이 없는 곳 BGM, 음성 대비 −30~0 LU), value |
| set_insert_enabled | target(insert ID), enabled |
| replace_card_text | target, text(20자까지) |
| approve_card / approve_all_cards | 전자는 target |
| adjust | edge=start/end, delta. 화면에서 시작+1=앞 문장, 종료+1=뒤 문장 |
| replan | 현재 후보 범위에서 재계획 |

API는 시리즈의 `adjust` / `replan`, P5의 `set_bgm`을 거부합니다. Transcript PATCH는 캐시 단어 수정이며 모든 기존 EditPlan을 자동 갱신하지 않습니다. UI는 그 뒤 replan을 요청하므로 시리즈에서는 제약에 주의하세요.

## 로컬 API 조작 예시

발화 영상과 본인 ASR 설정을 사용할 수 있는 환경에서 실행합니다. 요금·외부 전송이 발생할 수 있습니다. 실제 원본 경로로 바꾸세요.

```powershell
$base = 'http://127.0.0.1:8765/api'
$headers = @{ 'X-VideoCreator' = '1' }
$p = Invoke-RestMethod "$base/projects" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"name":"첫 API"}'
$a = Invoke-RestMethod "$base/projects/$($p.id)/assets" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"path":"C:\\Videos\\talk.mp4"}'
Invoke-RestMethod "$base/projects/$($p.id)/rights" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body (@{ asset_id=$a.id; confirmed=$true } | ConvertTo-Json)
$j = Invoke-RestMethod "$base/projects/$($p.id)/jobs" -Method Post -Headers $headers `
  -ContentType 'application/json' -Body '{"preset_ids":["P4"],"options":{"scope":"balanced"}}'
Invoke-RestMethod "$base/jobs/$($j.job_id)"
```

`status=ready` 뒤 candidates를 받아 UI에서 내용을 확인합니다. API 승인·일괄 출력 예시는 기존 [exports.http](../../../src/VideoCreator.App/exports.http), 진행률은 [progress.http](../../../src/VideoCreator.App/progress.http), 저장 위치는 [projects-dir.http](../../../src/VideoCreator.App/projects-dir.http), 카드는 [scene-cards.http](../../../src/VideoCreator.App/scene-cards.http)에 있습니다.
---

[↑ 문서 안내](../README.md) | [日本語](../../../users-guide/configuration-api.md) | [English](../../en/users-guide/configuration-api.md) | [简体中文](../../zh-CN/users-guide/configuration-api.md) | **한국어**
