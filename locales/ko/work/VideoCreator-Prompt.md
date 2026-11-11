---
name: VideoCreator
description: 동영상 파일 한 개에서 VideoCreator 편집 패턴(P1~P10)만 사용하여 세로형 짧은 영상(하이라이트 최대 3개) 또는 전편·후편 시리즈를 만든다. 「이 영상을 쇼츠로 만들어 줘」「클립을 만들어 줘」「전편과 후편으로 나눠 줘」라는 요청에 사용한다.
---

# VideoCreator for Microsoft 365 Copilot Cowork

[↑ 문서 안내](../README.md) | [日本語](../../../work/VideoCreator-Prompt.md) | [English](../../en/work/VideoCreator-Prompt.md) | [简体中文](../../zh-CN/work/VideoCreator-Prompt.md) | **한국어**


이 파일은 로컬 앱 VideoCreator의 편집(전사 → 하이라이트 선정 → 무음 컷 → 세로 변환 → 자막·제목 → 음량 조정 → 자동 검사)을 Copilot Cowork에서 재현하는 프롬프트입니다. 앞부분은 **사용자(사람)가 읽는 사용법**, 뒷부분은 **Cowork가 읽는 지시**입니다.

---

## 사용법(사람이 읽는 부분)

사용자가 정할 것은 **어떤 영상·어떤 패턴·하이라이트인지 시리즈인지** 세 가지뿐입니다. 초·음량·문자 크기 등 세부 값은 앱과 같은 템플릿 값으로 고정됩니다.

### 1. 준비물

| 준비물 | 설명 |
|---|---|
| 동영상 파일 | MP4 / MOV / MKV / WebM. **200MB 미만**(Cowork 첨부 상한). 10초~3시간. 음성 포함 |
| 전사(.vtt) ※강력 권장 | OneDrive / SharePoint에 영상을 놓고 Stream에서 열어 전사를 만든 뒤 .vtt로 다운로드합니다. Teams 회의 녹화는 회의 「요약」→「대화 내용」에서 .vtt를 받을 수 있습니다. Cowork는 음성을 직접 들을 수 없어 이 파일이 편집 원데이터가 됩니다 |
| 일본어 폰트(선택) | Cowork 환경에 일본어 폰트가 없을 때만 필요. 예: VideoCreator `tools\fonts\MPLUS1p-ExtraBold.ttf`(OFL) |

### 2. Cowork에 요청

Cowork를 열어 **이 파일·동영상·.vtt를 첨부**하고 다음 한 줄을 보냅니다.

```text
첨부한 VideoCreator-Prompt.md 지시대로 이 영상으로 동영상을 만들어 주세요. 패턴: 자동 선택  제작 방식: 하이라이트
```

- 자동 선택 대신 `P4`처럼 패턴 번호를 지정할 수도 있습니다(아래 표).
- 제작 방식은 하이라이트(짧은 영상 최대 3개) 또는 시리즈 2편·시리즈 3편 등으로 지정합니다.

### 3. 도중 질문에 답하기

Cowork는 다음과 같은 **선택형/예·아니요** 질문을 할 수 있습니다.

- 「이 영상의 권리가 있거나 사용 허가를 받았나요?」
- 「이 영상에는 P6(해설)이 어울립니다. P6으로 만들까요?」
- 「AI가 제안한 반응 문구입니다. 사용해도 될까요?」

### 4. 결과 받기

Cowork 출력 목록의 모두 다운로드(최대 50파일·500MB) 또는 OneDrive Cowork 폴더에서 받습니다. 영상마다 다음 파일이 만들어집니다.

| 파일 | 내용 |
|---|---|
| `…_P4_01_….mp4` | 세로 1080×1920 완성 영상 |
| `….srt` | 영상 속 발화 전체를 담은 자막 |
| `…_meta.md` | 제목 제안 3개·설명·해시태그·선정 이유(시리즈는 챕터 포함) |
| `…_sheet.png` | 6장면을 나열한 확인 이미지 |
| `….editplan.json` / `summary.json` | 편집 내용·자동 검사 기록 |

### 패턴 목록(이 중에서만 선택)

| 번호 | 이름 | 적합한 영상 | 결과 |
|---|---|---|---|
| P1 | 하이텐션·도전 | 도전·결과·반응 | 25~45초. 빠른 컷·줌·큰 자막 |
| P2 | 단발 소재·루프 | 질문·결말이 짧게 완결 | 15~25초. 자막·강조만 |
| P3 | 예능 텔롭 | 웃음·주고받는 대화 | 25~60초. 큰 자막·반응(사용자 승인 문구만) |
| **P4** | **클립 추출(권장)** | 방송·대담·강연 등 긴 이야기 | 25~35초. 전문 자막·강조·제목 |
| P5 | 비트 동기 몽타주 | — | **이 버전에서는 불가**(음악 라이브러리·비트 분석 필요). P8/P9 제안 |
| P6 | 해설·강의 | 설명·세미나 | 30~60초. 요점 강조·제목 |
| P7 | 대담(1카메라 간소판) | 2명 이상 대화 | 30~60초. 화자별 색 자막 |
| P8 | 과정 압축 | 요리·DIY 작업 | 20~60초. 작업 빨리감기·완성 선공개 |
| P9 | 미니멀/환경음 | 작업·자연음 | 30~90초. 자막 없이 원음만 |
| P10 | 화면 해설(간소판) | PC 화면 녹화 | 30~60초. 자막·제목 |

| 제작 방식 | 내용 |
|---|---|
| 하이라이트 | 적합 장면에서 짧은 영상 **최대 3개**. 부족하면 편수·이유를 알림(부풀리지 않음) |
| 시리즈 | 전체를 2~20편, 전편·중편·후편(4편 이상 제n화)으로 분할. 각 파트 선공개·지난 편 복습·다음 예고·챕터. P5/P8 불가 |

---

## Cowork 지시(이하 Cowork가 읽는 부분)

### 0. 역할

당신은 로컬 앱 VideoCreator 편집 절차를 Microsoft 365 안에서 재현하는 편집 담당자입니다. 역할을 다음처럼 나눕니다.

- **AI 결정**: 패턴 제안·사용 문장 범위(문장 번호)·선공개 한 문장·제목(16자 이내)·강조어(최대 3개)·챕터명·게시 제목·선정 이유·점수.
- **부록 A `vc_tool.py` 결정**: 문장 경계 맞춤·무음 컷·세로 변환·자막 분할/표시 시간·음량(-14 LUFS / True Peak -1.5 dBTP)·렌더링·자동 QA.

직접 FFmpeg 명령을 구성해 영상을 만들지 마세요. `vc_tool.py` 없이는 자막이 발화 그대로인지, 음량·안전 영역이 기준에 맞는지 측정할 근거가 사라집니다.

### 1. 필수 규칙

1. **선택 또는 예/아니요로 답할 질문만** 합니다. 초·dB·글자 수 등을 묻거나 변경을 권하지 않습니다. 값은 부록 A `PRESETS`에 고정되어 있습니다. 질문을 묶어 전체 3회 이내로 합니다.
2. **자막은 발화 그대로**. 바꿔쓰기·요약·머뭇거림/반복 삭제 금지(`vc_tool.py`는 전사 원문을 자막으로 만들고 V-01로 일치 확인). 사용자가 지적한 전사 오인식만 수정할 수 있습니다.
3. **제목·강조어·게시 제목의 숫자/고유 명사는 전사 표기 그대로**. 「一万円」을 「1万円」으로 바꾸지 않습니다(H-07 실패). 강조어는 사용 문장에 **그대로 등장하는 말**만.
4. **편수를 부풀리지 않습니다**. 부족하면 생성 편수·이유를 알립니다.
5. **안 한 처리를 했다고 보고하지 않습니다**. `summary.json`의 `hard_pass: false` 영상은 납품하지 말고 이유를 알립니다.
6. **권리 확인(H-09)**: 사용자 권리·허가 여부를 한 번만 확인, 아니요면 중지합니다.
7. **P3 반응은 AI 문안**. 14자 이내 제안, 읽고 승인한 문구만 `approved: true`. 미승인 사용 금지.
8. 원본 외부 서비스 전송 금지. 허가 없는 `pip install`·인터넷 다운로드 금지.

### 2. 절차

#### Step 1 — 도구 준비·환경 확인

1. 첨부 `VideoCreator-Prompt.md` 또는 스킬로 저장한 `SKILL.md`에서 부록 A 코드를 **프로그램으로 추출**하여 SHA-256을 확인합니다. 손으로 옮기지 않습니다.

   ```python
   import hashlib, pathlib, re
   md = pathlib.Path("VideoCreator-Prompt.md").read_text(encoding="utf-8").replace("\r\n", "\n")  # 실제 경로로 바꾼다
   code = re.search(r"<!-- vc_tool\.py:begin -->\n```python\n(.*?)\n```\n<!-- vc_tool\.py:end -->", md, re.S).group(1) + "\n"
   pathlib.Path("vc_tool.py").write_text(code, encoding="utf-8", newline="\n")
   print(hashlib.sha256(code.encode("utf-8")).hexdigest())  # => 1db6a6458d0b1532911130d734deece79e9e09bdd6d0add9ea72e811a49817cf
   ```

   `1db6a6458d0b1532911130d734deece79e9e09bdd6d0add9ea72e811a49817cf`와 다르면 중지하고 파일 재첨부를 요청합니다. 채팅에 붙여넣기만 하고 첨부하지 않은 경우도 첨부를 요청합니다.

2. `python vc_tool.py env` 실행 결과:
   - `ok: true`: 계속.
   - `ok: false`: §3 대체 절차.
   - `font: null`: 일본어 폰트 없음. 「일본어 폰트(.ttf/.otf)를 하나 첨부할 수 있나요?」를 한 번만 질문합니다. 있으면 이후 `--font <경로>`, 없으면 자막 합성 없이 진행(SRT는 생성)하고 알립니다.

#### Step 2 — 원본 읽기

```bash
python vc_tool.py prepare <동영상> --transcript <전사.vtt> --work vc_work
```

- .vtt/.srt/JSON(`start`·`end`·`text`·선택 `speaker`) 지원. 첨부 없으면 `--transcript` 생략. faster-whisper 있으면 자동 전사, 없으면 종료 코드 4. 이때 .vtt 요청(사용법 §1 간단 안내).
- `PROBLEMS:`는 사용자에게 설명(음성 없음·10초 미만·3시간 초과 등).
- 전사 시각이 영상보다 길면 오류. 다른 영상 전사인지 확인합니다.
- 산출물: `vc_work/transcript.txt`(`[문장번호] 시작-끝 화자: 문장 (loud 최대음량, pause 쉼)`), `sentences.json`, `signals.json`(무음·숏 경계·음량 피크), `probe.json`.

#### Step 3 — 패턴·제작 방식 결정

- 사용자 지정 우선. P5면 불가 이유·P8/P9 제안.
- 자동 선택이면 전사·§4 적합 영상을 비교하여 하나 선택, 이유 한 문장과 「이 P◯로 만들까요?」 확인. 애매하면 P4.
- 방식 미지정은 하이라이트.

#### Step 4 — 원자료 직접 읽기

- 전사를 **처음부터 끝까지**, 요약에만 의존하지 않고 읽습니다. 길면 약 10분씩 후보를 메모하고 마지막에 전체 비교.
- `signals.json`의 `loud_peaks`(웃음·환호·강조 단서), 전사 `pause`(이야기 경계) 참고.
- P8/P9/P10·발화 적은 영상은 `python vc_tool.py frames <동영상> --every 10 --work vc_work` 이미지(`frames_*.png`, 10초 축소 이미지·시각 `frames.json`)로 장면 파악.

#### Step 5a — 하이라이트 선정(방식=하이라이트)

모든 조건을 충족하는 후보를 좋은 순서로 **최대 3개** 선택합니다.

1. §4 패턴 선정 규칙에 맞음.
2. 사전 지식 없이 이해·첫 문장 지시어(それ/あれ/これ/その/あの/この/そこ/ここ/そう) 시작 금지.
3. 끝에 결말·결론(페이오프).
4. 앞뒤 30초도 읽고 부정·반어·농담 전제가 빠져 의도가 바뀌면 제외.
5. 후보 장면 중복 없음.

후보별 결정:

- `body`: `[[시작문장번호, 끝문장번호]]`(양끝 포함). 여러 범위를 시간순으로 배치 가능·중복 금지.
- `teaser`: 콜드 오픈 패턴 P1/P3/P4/P7/P8은 가장 강한 한 문장(결과·놀람·주장·질문) 번호. 본문 첫 문장과 같으면 `null`, 다른 패턴은 생략.
- `headline`(16자), `emphasis`(최대 3어), `scores`(`hook`·`standalone`·`payoff`·`emotion` 각 0~10), `reason`(한 문장), `titles`(3개), `description`, `hashtags`.
- P8 빨리감기는 본문에 `{"t0": 시작초, "t1": 끝초, "speed": 2~8}`(자막 없음). frames·signals shots로 시각 결정.

길이는 도구가 실제 무음 컷 이후로 판정합니다. 범위 밖이면 약 몇 초를 더하거나 줄일지 오류로 안내하므로 문장을 증감하여 맞춥니다.

#### Step 5b — 시리즈 분할(방식=시리즈 N편)

1. 전사를 읽고 **주제가 끝나는 문장 번호**(직후 주제 변화)를 나열합니다.
2. 분할:

   ```bash
   python vc_tool.py split --preset P4 --parts N --prefer 35,69,103 --work vc_work
   ```

3. `boundary_after.warning` 경계는 주제 도중입니다. N-1/N+1도 시도, 전부 경계에 맞는 편수가 있으면 「N편은 도중에 끊기고 M편은 주제별로 나뉩니다. 어느 쪽으로 할까요?」 질문.
4. 편집 후 편당 1분 미만은 오류, 하이라이트 제안.
5. `body`는 split 결과 **그대로**(전문 비중복 포괄). 각 파트 `teaser`(파트 최강 문장·필수), `headline`(전편 등 라벨은 도구), `chapters`(`[문장번호,"챕터명"]` 최소 3개·첫 문장부터·각 10초 이상 간격). 복습·예고는 자동.

#### Step 6 — plan.json 작성·출력

```json
{
  "preset": "P4",
  "mode": "highlights",
  "project": "영상 이름(파일명에 사용)",
  "source": "<prepare에 전달한 영상 경로>",
  "rights_confirmed": true,
  "videos": [
    {
      "headline": "一時間で一万円使い切れ",
      "teaser": 3,
      "body": [[2, 13]],
      "emphasis": ["一万円", "百円"],
      "reason": "처음에 무리한 규칙을 제시하고 반응으로 끝난다",
      "scores": {"hook": 8, "standalone": 8, "payoff": 6, "emotion": 6},
      "titles": ["제목 제안1", "제목 제안2", "제목 제안3"],
      "description": "설명",
      "hashtags": ["#태그"]
    }
  ],
  "tsukkomi": [{"video": 0, "after": 31, "text": "14자 이내", "approved": true}]
}
```

- 시리즈는 `"mode": "series"`, 각 video에 `"chapters": [[0, "챕터명"], [30, "챕터명"], [62, "챕터명"]]`.
- `tsukkomi`는 P3 승인본만.

```bash
python vc_tool.py build plan.json --work vc_work --out vc_out
```

- 종료 2: 계획 오류. 길이·중복·16자 초과·미승인 반응 등 지시에 따라 수정·재실행.
- 종료 3: 출력했으나 하드 실패. summary·editplan `qa.results`에서 H-01 길이·H-05 긴 문자·H-07 표기 등 수정·재실행. 불가 이유 보고.
- 같은 지적 3번 실패 시 중지·보고. 긴 영상은 `--only 1` 등 개별 출력 가능.

#### Step 7 — 직접 눈으로 확인

자동 통과는 측정 기준 충족이며 내용의 질 보장은 아닙니다. 납품 전 확인:

- sheet: 하단 중앙 자막 잘림·상단 제목·검은 화면·깨진 글리프(□) 확인.
- SRT 전체: 선공개→본문 의미 연결·갑작스러운 시작·결말 확인.
- 문제면 계획 수정·재실행.

#### Step 8 — 납품·보고

1. vc_out 전체 파일 전달(OneDrive Cowork 폴더에도 저장).
2. 아래 형식, 전문 용어 없이 쉬운 경고(§5):

   ```markdown
   ## 완성 영상(패턴: P4 클립/하이라이트)
   | # | 파일 | 길이 | 제목 | 선정 이유 | 자동 검사 |
   |---|---|---|---|---|---|
   | 1 | …_P4_01_….mp4 | 33초 | 한 시간에 만 엔 쓰기 | … | 통과(주의: 빠른 발화 자막 11곳) |

   ## 만들지 못한 것·주의
   - …

   ## 다음 작업
   - 다른 장면으로 재생성 / 시리즈 / 다른 패턴
   ```

3. 게시 주의: SRT 표시 시 합성 자막과 중복. 3분 초과는 YouTube Shorts 아님. 사실적인 AI 영상은 추가하지 않았으나 반응 문구는 AI 제안.

### 3. 환경이 부족할 때

| 상황 | 대응 |
|---|---|
| Python/ffmpeg 없음(`ok:false`) | 먼저 영상 생성 불가 안내. 전사 있으면 Step3~5 선정 후 ①Word 편집 지시(후보 시작/끝·선공개·제목·이유) ②후보 SRT ③plan.json·vc_tool.py 납품. Python/FFmpeg PC(앱 환경 등)에서 prepare·build 실행하면 같은 영상을 얻는다고 안내 |
| 전사·faster-whisper 없음 | .vtt 요청, 없으면 중지. 듣지 않고 추측 제작 금지 |
| 일본어 폰트 없음 | Step1처럼 한 번 요청, 없으면 합성 없이 SRT 납품 |
| 200MB 이상 첨부 불가 | Clipchamp에서 720p 출력으로 축소, 필요하면 쓸 부분만 남기도록 안내 |
| P5 지정 | 불가 이유·P8/P9 제안 |

### 4. 패턴 선정 규칙(부록 A PRESETS와 동일)

| ID | 적합 영상 | 선정 규칙 | 길이 | 선공개 | Cowork 차이 |
|---|---|---|---|---|---|
| P1 | 도전·결과·반응 | 결과/상금/놀람을 1.5초 내 보여주고 도전→결과가 있는 구간 | 25~45초 | 있음 | SFX·숫자 카운터·BGM 없음 |
| P2 | 짧은 질문·결말 | 한 질문/놀람과 답/결말만으로 완결, 끝이 첫 질문에 답해 반복 욕구 | 15~25초 | 없음 | 루프 연결 AI 판단 |
| P3 | 웃음·대화 | 웃음/반응이 있고 반응 문구로 쉼을 만드는 구간 | 25~60초 | 있음 | 겹침 텔롭만·정지 카드/SFX 없음·승인제 |
| P4 | 긴 이야기 전반(기본) | 독립 이해·첫 결과/놀람/강한 주장/질문·끝 결말 | 25~35초 | 있음 | 앱과 동일 |
| P5 | — | — | — | — | 불가 |
| P6 | 설명·강의 | 한 질문/주장에 이유·예시 포함 답이 완결. 숫자·명칭 전사 그대로 | 30~60초 | 없음 | BGM 없음 |
| P7 | 2인 이상 대화 | 최강 발언 선공개·주고받기 완결 | 30~60초 | 있음 | 단일 영상·카메라 전환 대신 화자 색 |
| P8 | 요리·DIY | 긴 과정 빨리감기·완성 선공개 | 20~60초 | 있음 | AI가 이미지 보고 배속 구간 지정 |
| P9 | 작업·환경음 | 텔롭/음악 없이 영상·환경음으로 몰입, 핵심/완성 근처 연속 장면 | 30~90초 | 없음 | 앱처럼 무음 컷 없음 |
| P10 | PC 녹화 | 한 조작의 목적→수행→결과 완결·설명 발화 우선 | 30~60초 | 없음 | 자동 줌·발표자 인셋 없음 |

공통값(앱 `presets/*.json`과 동일): 1080×1920·H.264·AAC48kHz, 통합 -14LUFS·피크 -1.5dBTP(2패스), 16:9는 흐린 배경, 자막 한 행·아래620px/오른쪽160px/왼쪽60px 안쪽, P1/P3 행당10자·기타14자(넘치면 축소), 제목 위300px.

### 5. 자동 검사 의미(쉬운 보고 표현)

| ID | 구분 | 내용 | 사용자 설명 예 |
|---|---|---|---|
| H-01 | 필수 | 패턴 길이(쇼트180초 이하) | 길이 기준 밖 |
| H-02 | 필수 | 1080×1920 | 화면 크기 |
| H-03 / H-04 | 필수 | -14±1LUFS·피크≤-1.0dBTP(독립 측정기) | 음량 |
| H-05 | 필수 | 앱 버튼이 자막을 가리지 않는 위치 | 자막 위치 |
| H-06 | 필수 | 단어 도중 컷 없음 | 단어 도중 잘림 |
| H-07 | 필수 | 제목/강조의 숫자·고유 명사 전사에 존재 | 제목 표기 |
| H-08 | 필수 | 미승인 반응 없음 | 반응 승인 |
| H-09 | 필수 | 원본 권리 확인 | 권리 확인 |
| V-01 | 필수 | 발화 그대로 자막 | 자막 정확성 |
| S-01 | 주의 | 초당6자 이하(빠르면 초과·삭제 안 함) | 빨라서 자막을 다 읽기 어려운 곳 |
| S-02 / S-08 | 주의 | 행당 문자·행 시작/끝 금칙 | 자막 개행 |
| S-03 | 주의 | 2초 내 발화 | 느린 시작 |
| S-04 | 주의 | 너무 짧은 컷 없음 | 지나치게 잦은 컷 |
| S-06 | 주의 | 0.5초 이상 검정/정지 없음(원본 정지도 검출) | 멈춰 보이는 부분 |
| S-07 | 주의 | 첫 문장 「それ」 등 시작 안 함 | 갑작스러운 시작 가능 |
| S-09 | 주의 | 원발화 부정 누락 없음 | 제목 의미가 반대인지 확인 |
| S-11 / S-14 | 주의 | 시리즈≤60분·챕터00:00/3개/각10초 이상 | 챕터 |

---

## 부록 A — vc_tool.py

표준 라이브러리·FFmpeg만 사용합니다(Python3.8 이상 가정·3.12 검증). PATH·`VC_FFMPEG`·`imageio-ffmpeg`에서 FFmpeg를 찾고 ffprobe 없이도 동작합니다. 아래 실행 코드는 추출 SHA-256을 보존하기 위해 일본어 문자열·주석을 포함하여 원본 그대로입니다. 이 한국어 문서는 역사적 일본어용 Cowork 도구를 자동으로 다국어화하지 않습니다.

<!-- vc_tool.py:begin -->
```python
#!/usr/bin/env python3
# vc_tool.py - VideoCreator (Cowork edition). Deterministic part: probe, transcript, signals, split, build (render+QA).
# The LLM chooses sentences/headlines; this tool cuts, captions, normalizes loudness and measures. Stdlib + ffmpeg only.
import argparse, datetime, glob, importlib.util, json, math, os, re, shutil, struct, subprocess, sys, unicodedata

VERSION = "1.0.0"
W, H = 1080, 1920
SAFE = {"top": 250, "bottom": 580, "right": 160, "left": 60}
GLYPH = 0.75
CAP_TOP = {"teaser": None, "recap": "前回のおさらい", "next": None}

PRESETS = {
 "P1": dict(name="ハイテンション・チャレンジ", dur=(25, 45), cold="teaser", punch=1.2, min_shot=0.8, sil=0.4, telop=["caption", "emphasis", "headline"], look="variety", font=124, chars=10, mv=620, mr=160, series=True,
            rules="結果・賞金・驚きの瞬間を1.5秒以内に見せられる、挑戦→結果のある区間を選ぶ。"),
 "P2": dict(name="一発ネタ・ループ", dur=(15, 25), cold="none", punch=1.1, min_shot=1.0, sil=0.5, telop=["caption", "emphasis"], look="", font=80, chars=14, mv=620, mr=160, series=True, loop=True,
            rules="1つの問い（または驚き）とその答え（オチ）だけで完結する15〜25秒。最後の文が冒頭の問いに答え、繰り返し見たくなるものを優先。"),
 "P3": dict(name="バラエティ・テロップ", dur=(25, 60), cold="teaser", punch=1.15, min_shot=1.0, sil=0.5, telop=["caption", "emphasis", "headline", "tsukkomi"], look="variety", font=124, chars=10, mv=620, mr=160, series=True,
            rules="笑い・リアクションの瞬間を含み、ツッコミで間を作れる区間を選ぶ。"),
 "P4": dict(name="切り抜き（長尺→30秒）", dur=(25, 35), cold="teaser", punch=1.15, min_shot=1.0, sil=0.7, telop=["caption", "emphasis", "headline"], look="", font=78, chars=14, mv=620, mr=160, series=True,
            rules="単体で意味が通り、冒頭に結果・驚き・強い主張・問いがあり、最後にオチ（ペイオフ）がある25〜35秒。"),
 "P5": dict(name="ビート同期モンタージュ", supported=False),
 "P6": dict(name="解説・講義（要点テロップ）", dur=(30, 60), cold="none", punch=1.1, min_shot=1.5, sil=0.6, telop=["caption", "emphasis", "headline"], look="", font=76, chars=14, mv=620, mr=160, series=True,
            rules="1つの問いや主張に対して、理由や具体例を含む答えが完結している30〜60秒。数字・固有名詞は書き起こしのとおりに扱う。"),
 "P7": dict(name="対談スイッチング（1カメラ簡略版）", dur=(30, 60), cold="teaser", punch=1.0, min_shot=2.0, sil=1.2, telop=["caption", "emphasis", "headline"], look="", font=76, chars=14, mv=620, mr=160, series=True, speaker_colors=True,
            rules="最も強い発言を先出しでき、話者の掛け合いが完結している30〜60秒。"),
 "P8": dict(name="プロセス圧縮（料理/DIY）", dur=(20, 60), cold="teaser", punch=1.1, min_shot=1.0, sil=0.5, telop=["emphasis", "headline"], look="", font=78, chars=14, mv=620, mr=160, series=False, speed=True,
            rules="長い工程を早送りで圧縮し、完成の場面を先に見せる。"),
 "P9": dict(name="ミニマル/環境音", dur=(30, 90), cold="none", punch=1.0, min_shot=3.0, sil=None, telop=[], look="", font=78, chars=14, mv=620, mr=160, series=True, nohook=True,
            rules="テロップや音楽に頼らず、映像と環境音だけで没入できる、作業の核心や完成に近い連続した場面を選ぶ。"),
 "P10": dict(name="画面解説（1ファイル簡略版）", dur=(30, 60), cold="none", punch=1.0, min_shot=2.0, sil=0.8, telop=["caption", "emphasis", "headline"], look="", font=76, chars=14, mv=620, mr=160, series=True,
             rules="1つの操作や手順が、目的→操作→結果まで完結している30〜60秒。画面の操作を説明している発話を優先する。"),
}
PART_LABELS = {2: ["前編", "後編"], 3: ["前編", "中編", "後編"]}
NO_START = set("、。，．・：；？！ー」』）】〕〉》’”ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ々〜…‥),.!?]}%")
NO_END = set("「『（【〔〈《‘“([{")
PARTICLES = set("はがをにでともへやねよかな")
DEMONSTRATIVE = ("それ", "あれ", "これ", "その", "あの", "この", "そこ", "あそこ", "ここ", "そう")
CONTINUATION = ("でも", "だから", "それで", "そして", "で、", "つまり", "なので", "ただ", "けど", "しかも", "あと")
NEGATION = re.compile(r"(ない|なかった|ません|ず|ぬ|じゃない|ではない|無い|不可|ダメ)")
FONT_PREF = ["M PLUS 1p ExtraBold", "M PLUS 1p", "Noto Sans CJK JP", "Noto Sans JP", "Source Han Sans", "IPAexGothic", "IPAGothic", "BIZ UDPGothic",
             "Yu Gothic", "Meiryo", "Hiragino", "TakaoGothic", "VL Gothic", "Noto Sans CJK", "WenQuanYi Zen Hei", "Droid Sans Fallback", "MS Gothic"]


def die(msg, code=2):
    print("ERROR: " + msg, file=sys.stderr)
    sys.exit(code)


def jdump(o, path):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(o, f, ensure_ascii=False, indent=1)
    os.replace(tmp, path)


def jload(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


# ---------------- ffmpeg discovery ----------------
def find_ffmpeg():
    exe = os.environ.get("VC_FFMPEG") or shutil.which("ffmpeg")
    if not exe:
        try:
            import imageio_ffmpeg  # type: ignore
            exe = imageio_ffmpeg.get_ffmpeg_exe()
        except Exception:
            exe = None
    probe = None
    if exe:
        cand = os.path.join(os.path.dirname(exe), "ffprobe" + (".exe" if exe.lower().endswith(".exe") else ""))
        probe = cand if os.path.exists(cand) else shutil.which("ffprobe")
    return exe, probe


FF, FP = find_ffmpeg()


def run(args, check=True):
    p = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    out, err = p.stdout.decode("utf-8", "replace"), p.stderr.decode("utf-8", "replace")
    if check and p.returncode != 0:
        die("command failed (%d): %s\n%s" % (p.returncode, " ".join(args[:12]), err[-3000:]))
    return out, err


def ff(args, check=True):
    if not FF:
        die("ffmpeg not found (PATH, VC_FFMPEG, or python package imageio-ffmpeg)")
    return run([FF, "-hide_banner", "-nostats"] + args, check)


# ---------------- fonts ----------------
def font_names(path):
    """Family names from a TTF/OTF/TTC name table (nameID 1 and 4)."""
    names = set()
    try:
        with open(path, "rb") as f:
            data = f.read()
        offs = [0]
        if data[:4] == b"ttcf":
            n = struct.unpack(">I", data[8:12])[0]
            offs = list(struct.unpack(">%dI" % n, data[12:12 + 4 * n]))
        for base in offs:
            num = struct.unpack(">H", data[base + 4:base + 6])[0]
            for i in range(num):
                rec = base + 12 + 16 * i
                if data[rec:rec + 4] != b"name":
                    continue
                off = struct.unpack(">I", data[rec + 8:rec + 12])[0]
                cnt, sto = struct.unpack(">HH", data[off + 2:off + 6])
                for k in range(cnt):
                    pid, eid, lid, nid, ln, o = struct.unpack(">HHHHHH", data[off + 6 + 12 * k:off + 18 + 12 * k])
                    if nid not in (1, 4):
                        continue
                    raw = data[off + sto + o:off + sto + o + ln]
                    try:
                        s = raw.decode("utf-16-be") if pid in (0, 3) else raw.decode("latin-1")
                    except Exception:
                        continue
                    if s.strip():
                        names.add(s.strip())
    except Exception:
        pass
    return names


def find_font(explicit=None):
    files = []
    if explicit:
        files.append(explicit)
    for d in [".", "fonts", os.path.expanduser("~/.fonts"), os.path.expanduser("~/.local/share/fonts"), "/usr/share/fonts", "/usr/local/share/fonts",
              "/System/Library/Fonts", "/Library/Fonts", os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts"), "/mnt/data", "/mnt/user-data"]:
        for ext in ("ttf", "otf", "ttc", "TTF", "OTF", "TTC"):
            files += glob.glob(os.path.join(d, "**", "*." + ext), recursive=True)
    best = None
    for f in files:
        ns = font_names(f)
        for rank, pref in enumerate(FONT_PREF):
            hit = [n for n in ns if n.lower().startswith(pref.lower())]
            if hit and (best is None or rank < best[0]):
                fam = sorted(hit, key=len)[0]
                best = (rank, f, fam)
        if explicit and f == explicit and ns and best is None:
            best = (99, f, sorted(ns, key=len)[0])
    return (best[1], best[2]) if best else (None, None)


# ---------------- probe ----------------
def probe(path):
    if not os.path.exists(path):
        die("input not found: " + path)
    info = {"path": path, "size_mb": round(os.path.getsize(path) / 1e6, 1)}
    if FP:
        out, _ = run([FP, "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path])
        j = json.loads(out)
        v = next((s for s in j["streams"] if s.get("codec_type") == "video"), None)
        a = next((s for s in j["streams"] if s.get("codec_type") == "audio"), None)
        info["duration"] = float(j["format"].get("duration", 0))
        if v:
            rot = 0
            for sd in v.get("side_data_list", []) or []:
                if "rotation" in sd:
                    rot = int(float(sd["rotation"]))
            rot = rot or int(float((v.get("tags") or {}).get("rotate", 0)))
            w, h = int(v["width"]), int(v["height"])
            if abs(rot) % 180 == 90:
                w, h = h, w
            num, den = (v.get("avg_frame_rate") or v.get("r_frame_rate") or "30/1").split("/")
            fps = float(num) / float(den) if float(den) else 30.0
            info.update(width=w, height=h, fps=round(fps, 3), vcodec=v.get("codec_name"), rotation=rot)
        info["audio"] = bool(a)
        if a:
            info.update(sample_rate=int(a.get("sample_rate", 0)), channels=int(a.get("channels", 0)))
    else:
        _, err = run([FF, "-hide_banner", "-i", path], check=False)
        m = re.search(r"Duration: (\d+):(\d+):([\d.]+)", err)
        info["duration"] = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)) if m else 0.0
        mv = re.search(r"Video: (\w+).*?(\d{2,5})x(\d{2,5})", err)
        mf = re.search(r"([\d.]+) fps", err)
        mr = re.search(r"rotation of (-?[\d.]+)", err) or re.search(r"rotate\s*:\s*(-?\d+)", err)
        if mv:
            w, h = int(mv.group(2)), int(mv.group(3))
            rot = int(float(mr.group(1))) if mr else 0
            if abs(rot) % 180 == 90:
                w, h = h, w
            info.update(width=w, height=h, fps=float(mf.group(1)) if mf else 30.0, vcodec=mv.group(1), rotation=rot)
        info["audio"] = "Audio:" in err
    probs = []
    if "width" not in info:
        probs.append("映像ストリームがありません")
    if info["duration"] < 10:
        probs.append("10秒未満の動画は扱えません")
    if info["duration"] > 3 * 3600:
        probs.append("3時間を超えています（分割を推奨）")
    if not info.get("audio"):
        probs.append("音声がありません（P9以外は字幕・選定ができません）")
    info["problems"] = probs
    return info


# ---------------- transcript ----------------
def ts(s):
    p = s.strip().replace(",", ".").split(":")
    p = [float(x) for x in p]
    return p[0] * 3600 + p[1] * 60 + p[2] if len(p) == 3 else p[0] * 60 + p[1]


def parse_transcript(path):
    raw = open(path, encoding="utf-8-sig", errors="replace").read()
    cues = []
    if path.lower().endswith(".json"):
        j = json.loads(raw)
        items = j.get("segments", j) if isinstance(j, dict) else j
        for it in items:
            cues.append(dict(start=float(it["start"]), end=float(it["end"]), speaker=str(it.get("speaker", "") or ""), text=str(it["text"]).strip()))
        return cues
    blocks = re.split(r"\n\s*\n", raw.replace("\r", ""))
    for b in blocks:
        lines = [l for l in b.split("\n") if l.strip()]
        k = next((i for i, l in enumerate(lines) if "-->" in l), None)
        if k is None:
            continue
        a, e = lines[k].split("-->")
        text_lines = lines[k + 1:]
        spk = ""
        txt = " ".join(text_lines)
        m = re.match(r"\s*<v\s+([^>]+)>", txt)
        if m:
            spk = m.group(1).strip()
        txt = re.sub(r"<[^>]+>", "", txt).strip()
        m2 = re.match(r"^([^:：]{1,30})[:：]\s*(.+)$", txt)
        if not spk and m2 and len(text_lines) == 1 and not re.search(r"[。、！？]", m2.group(1)):
            spk, txt = m2.group(1).strip(), m2.group(2).strip()
        if txt:
            cues.append(dict(start=ts(a.split()[0]), end=ts(e.split()[0]), speaker=spk, text=txt))
    return cues


def asr(video):
    try:
        from faster_whisper import WhisperModel  # type: ignore
    except Exception:
        return None
    try:
        model = WhisperModel(os.environ.get("VC_WHISPER", "small"), device="cpu", compute_type="int8")
        segs, _ = model.transcribe(video, language="ja", vad_filter=True)
        return [dict(start=s.start, end=s.end, speaker="", text=s.text.strip()) for s in segs if s.text.strip()]
    except Exception as ex:
        print("ASR failed: %s" % ex, file=sys.stderr)
        return None


def to_sentences(cues):
    out = []
    for c in sorted(cues, key=lambda c: c["start"]):
        parts = [p for p in re.findall(r"[^。！？!?]+[。！？!?」』]*|[。！？!?]+", c["text"]) if p.strip()]
        total = sum(len(p) for p in parts) or 1
        t = c["start"]
        for p in parts:
            d = (c["end"] - c["start"]) * len(p) / total
            out.append(dict(start=round(t, 3), end=round(t + d, 3), speaker=c["speaker"], text=p.strip()))
            t += d
    merged = []
    for s in out:  # join cue fragments that do not end a sentence and continue without a pause, same speaker
        if merged and not re.search(r"[。！？!?」』]$", merged[-1]["text"]) and s["speaker"] == merged[-1]["speaker"] \
                and s["start"] - merged[-1]["end"] < 0.25 and len(merged[-1]["text"]) + len(s["text"]) <= 60:
            merged[-1]["text"] += s["text"]
            merged[-1]["end"] = s["end"]
        else:
            merged.append(dict(s))
    for i, s in enumerate(merged):
        s["i"] = i
    return merged


def mmss(t):
    return "%02d:%04.1f" % (int(t // 60), t % 60) if t < 3600 else "%d:%02d:%04.1f" % (int(t // 3600), int(t % 3600 // 60), t % 60)


# ---------------- signals ----------------
def detect_silence(video, dur):
    for db in (-40, -35, -30):
        _, err = ff(["-i", video, "-vn", "-af", "silencedetect=noise=%ddB:d=0.3" % db, "-f", "null", "-"])
        st = [float(x) for x in re.findall(r"silence_start: (-?[\d.]+)", err)]
        en = [float(x) for x in re.findall(r"silence_end: ([\d.]+)", err)]
        sil = [(max(0.0, a), b) for a, b in zip(st, en)]
        if len(st) > len(en):
            sil.append((st[-1], dur))
        if sum(b - a for a, b in sil) >= 0.03 * dur or db == -30:
            return sil, db


def signals(video, sents_path=None):
    info = probe(video)
    dur = info["duration"]
    sil, db = detect_silence(video, dur) if info.get("audio") else ([], None)
    _, err = ff(["-i", video, "-an", "-vf", "scale=320:-2,scdet=threshold=10:sc_pass=0,blackdetect=d=0.2:pix_th=0.10", "-f", "null", "-"])
    shots = sorted(set(round(float(x), 2) for x in re.findall(r"lavfi\.scd\.time: ([\d.]+)", err)))
    black = [(float(a), float(b)) for a, b in re.findall(r"black_start:([\d.]+) black_end:([\d.]+)", err)]
    loud = []
    if info.get("audio"):
        _, err = ff(["-i", video, "-vn", "-af", "ebur128", "-f", "null", "-"])
        for m in re.finditer(r"t:\s*([\d.]+)\s+TARGET:[^M]*M:\s*(-?[\d.]+)", err):
            loud.append((float(m.group(1)), float(m.group(2))))
    sec = {}
    for t, m in loud:
        k = int(t)
        sec[k] = max(sec.get(k, -120.0), m)
    peaks = []
    for k in sorted(sec, key=lambda k: -sec[k]):
        if sec[k] < -60:
            break
        if all(abs(k - p[0]) >= 10 for p in peaks):
            peaks.append((k, round(sec[k], 1)))
        if len(peaks) >= 15:
            break
    sig = dict(duration=dur, silence_db=db, silences=[[round(a, 3), round(b, 3)] for a, b in sil], shots=shots, black=black,
               loud_peaks=[dict(t=t, M=m) for t, m in sorted(peaks)], loud_per_sec={str(k): round(v, 1) for k, v in sec.items()})
    return sig


# ---------------- text helpers ----------------
def cw(ch):
    return 1.0 if unicodedata.east_asian_width(ch) in "WFA" else 0.5


def width(s):
    return sum(cw(c) for c in s if not c.isspace())


def chunk_sentence(text, maxw):
    phrases = [p for p in re.findall(r"[^、。！？!?…]+[、。！？!?…」』）]*|[、。！？!?…」』）]+", text) if p]
    chunks, cur = [], ""
    for p in phrases:
        if width(cur + p) <= maxw:
            cur += p
            continue
        if cur:
            chunks.append(cur)
            cur = ""
        while width(p) > maxw:
            cut = split_point(p, maxw)
            chunks.append(p[:cut])
            p = p[cut:]
        cur = p
    if cur:
        chunks.append(cur)
    out = []
    for c in chunks:  # punctuation-only pieces go back to the previous chunk
        if out and all(ch in NO_START or ch in "、。！？!?…" for ch in c.strip()):
            out[-1] += c
        else:
            out.append(c)
    return out


def split_point(p, maxw):
    acc, lim = 0.0, 0
    for i, ch in enumerate(p):
        acc += cw(ch)
        if acc > maxw:
            break
        lim = i + 1
    lim = max(1, lim)
    best = None
    for k in range(lim, max(1, int(lim * 0.5)) - 1, -1):
        if k >= len(p):
            continue
        a, b = p[k - 1], p[k]
        if b in NO_START or a in NO_END:
            continue
        if re.match(r"[A-Za-z0-9.,%]", a) and re.match(r"[A-Za-z0-9.,%]", b):
            continue
        if a in PARTICLES or (a in "てた" and not re.match(r"[ぁ-ん]", b)):
            return k
        if best is None:
            best = k
    return best or lim


def ass_esc(s):
    return s.replace("\\", "＼").replace("{", "｛").replace("}", "｝").replace("\n", " ")


def ass_time(t):
    t = max(0.0, t)
    cs = int(round(t * 100))
    return "%d:%02d:%02d.%02d" % (cs // 360000, cs // 6000 % 60, cs // 100 % 60, cs % 100)


def srt_time(t):
    ms = int(round(max(0.0, t) * 1000))
    return "%02d:%02d:%02d,%03d" % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, ms % 1000)


def fit_px(text, px, outline, scalex=100, extra=0.0):
    avail = W - SAFE["left"] - SAFE["right"] - 2 * outline
    est = (width(text) + extra) * px * GLYPH * scalex / 100.0
    return px if est <= avail else max(24, int(px * avail / est))


def facts(s):
    """Numbers, ASCII words and katakana runs: the tokens H-07 requires to exist in the transcript."""
    s = unicodedata.normalize("NFKC", s)
    return [t for t in re.findall(r"\d[\d,.]*|[A-Za-z][A-Za-z0-9]+|[ァ-ヴー]{2,}", s) if t != "ー"]


def norm(s):
    return re.sub(r"\s+", "", unicodedata.normalize("NFKC", s))


# ---------------- series split ----------------
def edited_len(sents, sil_thr, sils=()):
    """Estimated length of each sentence (plus the following gap) after the jump cuts that build() will make."""
    keep = sil_thr if sil_thr else 9e9
    d = []
    for i, s in enumerate(sents):
        gap = (sents[i + 1]["start"] - s["end"]) if i + 1 < len(sents) else 0.0
        v = max(0.0, s["end"] - s["start"]) + min(max(0.0, gap), keep)
        if sil_thr:
            a0, a1 = s["start"], s["end"] + max(0.0, gap)
            for x, y in sils:
                o = min(a1, y) - max(a0, x)
                if y - x > sil_thr and o > 0.24:
                    v -= o - 0.24
        d.append(max(0.2, v))
    return d


def split_series(sents, parts, prefer, sil_thr, sils=()):
    n = len(sents)
    d = edited_len(sents, sil_thr, sils)
    pre = [0.0]
    for x in d:
        pre.append(pre[-1] + x)
    total = pre[-1]
    mean = total / parts
    if mean < 60:
        mx = int(total // 60)
        die("1本あたり約%.0f秒になり、シリーズの下限（編集後1分）に届きません。%s" % (mean, "本数を%d本以下にしてください。" % mx if mx >= 2 else "この動画はシリーズには短いので、見どころ（最大3本）を作ってください。"))
    bonus = []
    for i in range(n - 1):
        a, b = sents[i], sents[i + 1]
        g = b["start"] - a["end"]
        v = 0.0
        v += 2.5 if i in prefer else 0.0
        v += 0.4 if g >= 0.7 else (0.2 if g >= 0.4 else 0.0)
        v += 0.1 if a["speaker"] != b["speaker"] else 0.0
        v -= 0.8 if not re.search(r"[。！？!?」』]$", a["text"]) else 0.0
        v -= 0.6 if b["text"].startswith(CONTINUATION) or b["text"].startswith(DEMONSTRATIVE) else 0.0
        bonus.append(v)

    def cost(i, j):  # part = sentences i..j-1
        L = pre[j] - pre[i]
        return 25.0 * ((L - mean) / mean) ** 2

    found = []
    for tol in (0.25, 0.40):
        lo, hi = mean * (1 - tol), mean * (1 + tol)
        INF = float("inf")
        best = [[INF] * (n + 1) for _ in range(parts + 1)]
        back = [[-1] * (n + 1) for _ in range(parts + 1)]
        best[0][0] = 0.0
        for k in range(1, parts + 1):
            for j in range(1, n + 1):
                if k == parts and j != n:
                    continue
                for i in range(j - 1, -1, -1):
                    L = pre[j] - pre[i]
                    if L > hi:
                        break
                    if L < lo or best[k - 1][i] == INF:
                        continue
                    c = best[k - 1][i] + cost(i, j) - (bonus[j - 1] if j < n else 0.0)
                    if c < best[k][j]:
                        best[k][j], back[k][j] = c, i
        if best[parts][n] < INF:
            cuts, j = [], n
            for k in range(parts, 0, -1):
                i = back[k][j]
                cuts.append((i, j - 1))
                j = i
            cuts.reverse()
            aligned = sum(1 for (_, b) in cuts[:-1] if b in prefer)
            found.append((aligned, tol, cuts))
            if not prefer or aligned == parts - 1:
                break
    if not found:
        die("均等に近い分け方が見つかりません。本数を変えてください。")
    # a topic-aligned split within ±40 % beats a balanced split that cuts a topic in half
    aligned, tol, cuts = sorted(found, key=lambda x: (-x[0], x[1]))[0]
    labels = PART_LABELS.get(parts, ["第%d話" % (k + 1) for k in range(parts)])
    res = []
    for k, (a, b) in enumerate(cuts):
        res.append(dict(part=k + 1, label=labels[k], body=[a, b], start=sents[a]["start"], end=sents[b]["end"], est_sec=round(pre[b + 1] - pre[a], 1),
                        boundary_after=(None if b == n - 1 else dict(after=b, prefer=(b in prefer), bonus=round(bonus[b], 2),
                                                                      last=sents[b]["text"], next=sents[b + 1]["text"],
                                                                      warning=(None if (b in prefer or not prefer) else "話題の切れ目ではありません（%s）。利用者に伝えてください。" % mmss(sents[b]["end"]))))))
    return dict(parts=res, tolerance=tol, mean_sec=round(mean, 1), note=("長さの差を±40%%まで広げて話題の切れ目に合わせました" if tol > 0.25 else ""))


# ---------------- plan -> clips ----------------
def sentence_span(sents, a, b, dur):
    s0, s1 = sents[a], sents[b]
    gb = s0["start"] - (sents[a - 1]["end"] if a > 0 else 0.0)
    ga = (sents[b + 1]["start"] if b + 1 < len(sents) else dur) - s1["end"]
    return max(0.0, s0["start"] - min(0.15, max(0.0, gb) / 2)), min(dur, s1["end"] + min(0.30, max(0.0, ga) / 2))


def jump_cut(t0, t1, sils, thr, min_shot):
    if not thr:
        return [(t0, t1)]
    cuts = []
    for a, b in sils:
        a, b = max(a, t0), min(b, t1)
        if b - a > thr:
            cuts.append((a + 0.12, b - 0.12))
    pieces, cur = [], t0
    for a, b in cuts:
        if a - cur >= min_shot and t1 - b >= min_shot:
            pieces.append((cur, a))
            cur = b
    pieces.append((cur, t1))
    return pieces


def build_groups(plan, vid, sents, sig, P, series_ctx):
    dur = sig["duration"]
    groups = []  # each: role, sentences [indices], clips [(t0,t1,speed)]
    sils = [tuple(x) for x in sig["silences"]]

    def speech_group(role, a, b, cut=True):
        t0, t1 = sentence_span(sents, a, b, dur)
        pcs = jump_cut(t0, t1, sils, P["sil"] if cut else None, P["min_shot"])
        groups.append(dict(role=role, sents=list(range(a, b + 1)), clips=[(x, y, 1.0) for x, y in pcs]))

    teaser = vid.get("teaser")
    if teaser is not None and (P["cold"] == "teaser" or series_ctx):
        speech_group("teaser", int(teaser), int(teaser), cut=True)
    if series_ctx and series_ctx.get("recap"):
        a, b = series_ctx["recap"]
        speech_group("recap", a, b, cut=True)
    for item in vid["body"]:
        if isinstance(item, dict):
            t0, t1, sp = float(item["t0"]), float(item["t1"]), float(item.get("speed", 1.0))
            if not P.get("speed") and sp != 1.0:
                die("speed is only allowed in P8")
            inside = [s["i"] for s in sents if s["start"] >= t0 - 0.05 and s["end"] <= t1 + 0.05] if sp == 1.0 else []
            groups.append(dict(role="body", sents=inside, clips=[(max(0.0, t0), min(dur, t1), sp)]))
        else:
            a, b = int(item[0]), int(item[1])
            if not (0 <= a <= b < len(sents)):
                die("body range %s is outside 0..%d" % (item, len(sents) - 1))
            speech_group("body", a, b)
    if series_ctx and series_ctx.get("next") is not None:
        speech_group("next", series_ctx["next"], series_ctx["next"], cut=True)
    body_spans = sorted((c[0], c[1]) for g in groups if g["role"] == "body" for c in g["clips"])
    for (a0, b0), (a1, b1) in zip(body_spans, body_spans[1:]):
        if a1 < b0 - 0.2:
            die("本編の区間が重なっています（%s〜%s と %s〜%s）。同じ場面が2回映らないよう body を直してください。" % (mmss(a0), mmss(b0), mmss(a1), mmss(b1)))
    # punch-in alternates on body pieces; teaser is punched in
    k = 0
    for g in groups:
        zs = []
        for c in g["clips"]:
            if g["role"] == "teaser":
                zs.append(P["punch"])
            elif g["role"] == "body":
                zs.append(P["punch"] if k % 2 == 1 else 1.0)
                k += 1
            else:
                zs.append(1.0)
        g["zoom"] = zs
    return groups


def timeline(groups, fps):
    o = 0.0
    for g in groups:
        g["out"] = []
        for (a, b, sp) in g["clips"]:
            n = max(1, int(round((b - a) / sp * fps)))
            g["out"].append((o, o + n / fps, n))
            o += n / fps
    return o


def map_t(g, t):
    for (a, b, sp), (o0, o1, _) in zip(g["clips"], g["out"]):
        if t < a:
            return o0
        if t <= b:
            return min(o1, o0 + (t - a) / sp)
    return g["out"][-1][1]


def captions(groups, sents, P, emphasis, speakers):
    maxw = P["chars"]
    scalex, outline = (85, 10) if P["look"] == "variety" else (100, 7)
    avail = W - SAFE["left"] - SAFE["right"] - 2 * outline
    fitc = int(avail // (P["font"] * GLYPH * scalex / 100.0))
    maxw = max(1, min(maxw, fitc))
    ev = []
    for g in groups:
        if g["clips"][0][2] != 1.0:
            continue
        for i in g["sents"]:
            s = sents[i]
            chunks = chunk_sentence(s["text"], maxw)
            tot = sum(width(c) for c in chunks) or 1
            # distribute chunks over the sentence's OUTPUT interval so a jump cut can never swallow a chunk (captions stay verbatim)
            o0, o1 = map_t(g, s["start"]), map_t(g, s["end"])
            if o1 - o0 < 0.1 * len(chunks):
                o1 = min(g["out"][-1][1], o0 + 0.1 * len(chunks))
            t = o0
            for c in chunks:
                d = (o1 - o0) * width(c) / tot
                ev.append(dict(start=t, end=t + d, text=c, sent=i, role=g["role"], speaker=s["speaker"], gend=g["out"][-1][1]))
                t += d
    ev.sort(key=lambda e: e["start"])
    for k, e in enumerate(ev):  # stretch toward 6 cps (max +0.5s), never over the next caption or out of its group
        nxt = ev[k + 1]["start"] if k + 1 < len(ev) else e["gend"]
        need = width(e["text"]) / 6.0
        if e["end"] - e["start"] < need:
            e["end"] = min(e["start"] + need, e["end"] + 0.5, nxt, e["gend"])
        if k + 1 < len(ev) and e["end"] > ev[k + 1]["start"]:
            e["end"] = ev[k + 1]["start"]
    return ev, maxw, scalex, outline


def write_ass(path, ev, P, headline, labels, tsukkomi, font_family, total, scalex, outline, speakers, emphasis):
    L = ["[Script Info]", "ScriptType: v4.00+", "PlayResX: %d" % W, "PlayResY: %d" % H, "WrapStyle: 2", "ScaledBorderAndShadow: yes", "",
         "[V4+ Styles]",
         "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding"]
    f = font_family
    L.append("Style: Caption,%s,%d,&H00FFFFFF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,%d,100,0,0,1,%d,2,2,%d,%d,%d,1" % (f, P["font"], scalex, outline, SAFE["left"], P["mr"], P["mv"]))
    for k, col in enumerate(["&H00D47800", "&H003C3CD4", "&H0050A030"]):
        L.append("Style: CapS%d,%s,%d,&H00FFFFFF,&H0000FFFF,%s,&H80000000,0,0,0,0,%d,100,0,0,1,%d,2,2,%d,%d,%d,1" % (k + 1, f, P["font"], col, scalex, max(outline, 9), SAFE["left"], P["mr"], P["mv"]))
    L.append("Style: Headline,%s,60,&H00FFFFFF,&H0000FFFF,&H00202020,&HB0202020,0,0,0,0,100,100,0,0,3,14,0,8,%d,%d,300,1" % (f, SAFE["left"], P["mr"]))
    L.append("Style: Label,%s,48,&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,6,2,7,%d,%d,410,1" % (f, SAFE["left"] + 20, P["mr"]))
    L.append("Style: Tsukkomi,%s,64,&H00202020,&H0000FFFF,&H00FFFFFF,&H00FFFFFF,0,0,0,0,100,100,0,0,3,12,0,2,%d,%d,930,1" % (f, SAFE["left"], P["mr"]))
    L += ["", "[Events]", "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text"]
    layout = []
    spk_style = {}
    for e in ev:
        style = "Caption"
        if P.get("speaker_colors") and e["speaker"]:
            spk_style.setdefault(e["speaker"], "CapS%d" % (len(spk_style) % 3 + 1))
            style = spk_style[e["speaker"]]
        emph_here = [w for w in emphasis if w and w in e["text"]] if "emphasis" in P["telop"] else []
        extra =  sum(0.15 * width(w) for w in emph_here)
        px = fit_px(e["text"], P["font"], outline, scalex, extra=extra)
        txt = ass_esc(e["text"])
        for w in emph_here:
            txt = txt.replace(ass_esc(w), "{\\c&H0000F0FF&\\fscx%d\\fscy115}%s{\\r%s}" % (int(scalex * 1.15), ass_esc(w), style))
        pre = "{\\fs%d}" % px if px != P["font"] else ""
        if pre and emph_here:
            txt = txt.replace("{\\r%s}" % style, "{\\r%s\\fs%d}" % (style, px))
        L.append("Dialogue: 1,%s,%s,%s,,0,0,0,,%s%s" % (ass_time(e["start"]), ass_time(e["end"]), style, pre, txt))
        layout.append(dict(kind="caption", text=e["text"], px=px, bottom=P["mv"], right=P["mr"], left=SAFE["left"],
                           width=round((width(e["text"]) + extra) * px * GLYPH * scalex / 100.0 + 2 * outline)))
    if headline and "headline" in P["telop"]:
        px = fit_px(headline, 60, 14)
        L.append("Dialogue: 3,%s,%s,Headline,,0,0,0,,{\\fs%d}%s" % (ass_time(0), ass_time(total), px, ass_esc(headline)))
        layout.append(dict(kind="headline", text=headline, px=px, top=300))
    for (a, b, text) in labels:
        L.append("Dialogue: 3,%s,%s,Label,,0,0,0,,%s" % (ass_time(a), ass_time(b), ass_esc(text)))
        layout.append(dict(kind="label", text=text, px=48, top=410, width=round(width(text) * 48 * GLYPH + 12)))
    for (a, b, text) in tsukkomi:
        px = fit_px(text, 64, 12)
        L.append("Dialogue: 2,%s,%s,Tsukkomi,,0,0,0,,{\\fs%d\\fad(80,80)}%s" % (ass_time(a), ass_time(b), px, ass_esc(text)))
        layout.append(dict(kind="tsukkomi", text=text, px=px, bottom=930, right=P["mr"], left=SAFE["left"], width=round(width(text) * px * GLYPH + 24)))
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")
    return layout


# ---------------- render ----------------
def fnum(x):
    return ("%.4f" % x).rstrip("0").rstrip(".")


def atempo(sp):
    out = []
    while sp > 2.0:
        out.append("atempo=2.0")
        sp /= 2.0
    out.append("atempo=%s" % fnum(sp))
    return ",".join(out)


def build_graph(groups, info, fps, ass, fontsdir, loud, audio_only=False):
    inputs, parts, vlabels, alabels = [], [], [], []
    vertical = info["width"] / float(info["height"]) <= 0.6
    gi = 0
    for g in groups:
        runs = []  # forward-moving runs share one input
        for idx, c in enumerate(g["clips"]):
            if runs and c[0] >= g["clips"][runs[-1][-1]][1] - 1e-6 and (c[0] - g["clips"][runs[-1][-1]][1]) < 60:
                runs[-1].append(idx)
            else:
                runs.append([idx])
        for run_ in runs:
            s0 = max(0.0, g["clips"][run_[0]][0] - 1.0)
            s1 = g["clips"][run_[-1]][1] + 0.5
            inputs += ["-ss", fnum(s0), "-t", fnum(s1 - s0), "-i", info["path"]]
            n = len(run_)
            if not audio_only:
                parts.append("[%d:v]fps=%s,split=%d%s;" % (gi, fnum(fps), n, "".join("[v%d_%d]" % (gi, k) for k in range(n))) if n > 1 else "[%d:v]fps=%s[v%d_0];" % (gi, fnum(fps), gi))
            parts.append("[%d:a]aformat=sample_rates=48000:channel_layouts=stereo,asplit=%d%s;" % (gi, n, "".join("[a%d_%d]" % (gi, k) for k in range(n))) if n > 1 else "[%d:a]aformat=sample_rates=48000:channel_layouts=stereo[a%d_0];" % (gi, gi))
            for k, idx in enumerate(run_):
                a, b, sp = g["clips"][idx]
                frames = g["out"][idx][2]
                odur = frames / fps
                z = g["zoom"][idx]
                lab = "c%d_%d" % (gi, k)
                if not audio_only:
                    v = "[v%d_%d]trim=start=%s:end=%s,setpts=(PTS-STARTPTS)/%s" % (gi, k, fnum(a - s0), fnum(b - s0), fnum(sp))
                    if z > 1.0:
                        v += ",scale=iw*%s:ih*%s,crop=iw/%s:ih/%s" % (fnum(z), fnum(z), fnum(z), fnum(z))
                    if vertical:
                        v += ",scale=%d:%d:force_original_aspect_ratio=increase,crop=%d:%d,setsar=1" % (W, H, W, H)
                    else:
                        v += ",split[f%s][b%s];[b%s]scale=270:480:force_original_aspect_ratio=increase,crop=270:480,boxblur=10:2,scale=%d:%d[bb%s];" \
                             "[f%s]scale=%d:-2[ff%s];[bb%s][ff%s]overlay=(W-w)/2:(H-h)/2-120,setsar=1" % (lab, lab, lab, W, H, lab, lab, W, lab, lab, lab)
                    v += ",fps=%s,tpad=stop_mode=clone:stop_duration=2,trim=end_frame=%d,setpts=PTS-STARTPTS[v%s];" % (fnum(fps), frames, lab)
                    parts.append(v)
                    vlabels.append("[v%s]" % lab)
                af = "[a%d_%d]atrim=start=%s:end=%s,asetpts=PTS-STARTPTS" % (gi, k, fnum(a - s0), fnum(b - s0))
                if sp != 1.0:
                    af += "," + atempo(sp)
                af += ",afade=t=in:d=0.012,apad=whole_dur=%s,atrim=end=%s,afade=t=out:st=%s:d=0.012[a%s];" % (fnum(odur), fnum(odur), fnum(max(0.0, odur - 0.012)), lab)
                parts.append(af)
                alabels.append("[a%s]" % lab)
            gi += 1
    n = len(alabels)
    if audio_only:
        parts.append("%sconcat=n=%d:v=0:a=1[ca];[ca]%s[aout]" % ("".join(alabels), n, loud))
    else:
        parts.append("%sconcat=n=%d:v=1:a=1[cv][ca];" % ("".join(v + a for v, a in zip(vlabels, alabels)), n))
        sub = ("subtitles=filename='%s'%s," % (ff_path(ass), (":fontsdir='%s'" % ff_path(fontsdir)) if fontsdir else "")) if ass else ""
        parts.append("[cv]%sformat=yuv420p[vout];[ca]%s,aresample=48000[aout]" % (sub, loud))
    return inputs, "".join(parts)


def ff_path(p):
    p = os.path.abspath(p).replace("\\", "/")
    return p.replace(":", "\\:").replace("'", "\\'")


def ff_major():
    out, _ = run([FF, "-version"])
    m = re.search(r"ffmpeg version n?(\d+)\.", out)
    return int(m.group(1)) if m else 7


def graph_args(graph, path):
    with open(path, "w", encoding="utf-8") as f:
        f.write(graph)
    return ["-/filter_complex", path] if ff_major() >= 7 else ["-filter_complex_script", path]


def render(groups, info, fps, ass, fontsdir, out, work):
    li, g = build_graph(groups, info, fps, None, None, "loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json", audio_only=True)
    _, err = ff(li + graph_args(g, os.path.join(work, "pass1.txt")) + ["-map", "[aout]", "-f", "null", "-"])
    m = json.loads(err[err.rindex("{"):err.rindex("}") + 1])
    ln = "loudnorm=I=-14:TP=-1.5:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true" % (
        m["input_i"], m["input_tp"], m["input_lra"], m["input_thresh"], m["target_offset"])
    if float(m["input_i"]) < -70:
        ln = "anull"
    inp, g = build_graph(groups, info, fps, ass, fontsdir, ln)
    args = inp + graph_args(g, os.path.join(work, "graph.txt")) + ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-preset", os.environ.get("VC_X264_PRESET", "veryfast"),
                                                                  "-crf", "20", "-profile:v", "high", "-pix_fmt", "yuv420p", "-r", fnum(fps), "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
                                                                  "-movflags", "+faststart", "-y", out]
    with open(os.path.join(work, "ffmpeg_cmd.txt"), "w", encoding="utf-8") as f:
        f.write(json.dumps([FF] + args, ensure_ascii=False))
    ff(args)


# ---------------- QA ----------------
def measure(out):
    info = probe(out)
    _, err = ff(["-i", out, "-vn", "-af", "ebur128=peak=true", "-f", "null", "-"])
    summ = err[err.rfind("Summary:"):]
    I = re.search(r"I:\s*(-?[\d.]+) LUFS", summ)
    TP = re.search(r"Peak:\s*(-?[\d.]+|-inf) dBFS", summ)
    _, err2 = ff(["-i", out, "-an", "-vf", "blackdetect=d=0.5:pix_th=0.10,freezedetect=n=-60dB:d=0.5", "-f", "null", "-"])
    black = re.findall(r"black_start:([\d.]+) black_end:([\d.]+)", err2)
    freeze = re.findall(r"freeze_start: ([\d.]+)", err2)
    return dict(duration=info["duration"], width=info.get("width"), height=info.get("height"), lufs=float(I.group(1)) if I else None,
                true_peak=(float(TP.group(1)) if TP and TP.group(1) != "-inf" else -99.0), black=black, freeze=freeze)


def qa(ctx):
    P, meas, ev, layout, groups, sents, vid = ctx["P"], ctx["meas"], ctx["ev"], ctx["layout"], ctx["groups"], ctx["sents"], ctx["vid"]
    R = []

    def add(cid, hard, ok, detail, how=""):
        R.append(dict(id=cid, level="hard" if hard else "soft", status="pass" if ok is True else ("not_run" if ok is None else "fail" if hard else "warn"), detail=detail, fix=how))

    lo, hi = ctx["range"]
    d = meas["duration"]
    add("H-01", True, lo - 0.5 <= d <= hi + 0.5 and (d <= 180 or ctx["series"]), "尺 %.1f秒（範囲 %.0f〜%.0f秒）" % (d, lo, hi), "文を足す／削る")
    add("H-02", True, meas["width"] == W and meas["height"] == H, "%sx%s" % (meas["width"], meas["height"]))
    add("H-03", True, meas["lufs"] is not None and abs(meas["lufs"] + 14) <= 1.0, "積分ラウドネス %s LUFS（目標 -14±1、ebur128で別測定）" % meas["lufs"])
    add("H-04", True, meas["true_peak"] <= -1.0, "True Peak %.1f dBTP（-1.0以下）" % meas["true_peak"])
    bad = [l for l in layout if l["kind"] in ("caption", "tsukkomi") and (l["bottom"] < SAFE["bottom"] or l["right"] < SAFE["right"] or l["width"] > W - SAFE["left"] - SAFE["right"] + 1)]
    add("H-05", True, not bad, "安全領域外のテロップ %d件%s" % (len(bad), (": " + bad[0]["text"]) if bad else ""), "短くするか無効にする")
    edges_bad = []
    sils = ctx["sils"]
    for g in groups:
        if g["clips"][0][2] != 1.0 or not g["sents"]:
            continue
        for (a, b, _) in g["clips"]:
            for t in (a, b):
                in_sil = any(x - 0.06 <= t <= y + 0.06 for x, y in sils)
                inside = [s for s in sents if s["start"] + 0.15 < t < s["end"] - 0.15]
                if inside and not in_sil:
                    edges_bad.append(round(t, 2))
    add("H-06", True, not edges_bad, "発話途中のカット %d箇所 %s" % (len(edges_bad), edges_bad[:5]), "文の範囲を変える")
    used = "".join(sents[i]["text"] for g in groups for i in g["sents"])
    alltext = norm("".join(s["text"] for s in sents))
    miss = []
    for t in [vid.get("headline", "")] + list(vid.get("emphasis", [])):
        for tok in facts(t):
            if norm(tok) not in (alltext if t == vid.get("headline", "") and ctx["series"] else norm(used)):
                miss.append(tok)
    miss += [w for w in vid.get("emphasis", []) if norm(w) not in norm(used)]
    add("H-07", True, not miss, "書き起こしに無い語 %s" % miss, "書き起こしにある表記に直す")
    unapproved = [t for t in ctx["tsukkomi_raw"] if not t.get("approved")]
    add("H-08", True, not unapproved, "未承認のツッコミ %d件" % len(unapproved), "利用者に承認してもらうか外す")
    add("H-09", True, bool(ctx["rights"]), "権利確認 %s" % ("済" if ctx["rights"] else "未"), "利用者に確認する")
    vb = []
    for g in groups:
        want = norm("".join(sents[i]["text"] for i in g["sents"]))
        got = norm("".join(e["text"] for e in ev if e["role"] == g["role"] and e["sent"] in g["sents"] and g["out"][0][0] - 1e-6 <= e["start"] <= g["out"][-1][1] + 1e-6))
        if "caption" in P["telop"] and g["clips"][0][2] == 1.0 and want != got:
            vb.append(g["role"])
    add("V-01", True, not vb, "字幕と発話の逐語一致 %s" % ("OK" if not vb else "NG: " + ",".join(vb)), "字幕は話した言葉どおり（削除・言い換え禁止）")
    fast = [e for e in ev if width(e["text"]) / max(0.01, e["end"] - e["start"]) > 6.0 + 1e-6]
    add("S-01", False, not fast, "6文字/秒を超える字幕 %d件%s" % (len(fast), (": " + fast[0]["text"]) if fast else ""), "字幕は削らない。速い話し方による警告")
    longl = [e["text"] for e in ev if width(e["text"]) > P["chars"]]
    add("S-02", False, not longl, "1行の文字数超過 %d件" % len(longl))
    if P.get("nohook"):
        add("S-03", False, None, "P9は対象外")
    else:
        starts = [map_t(g, sents[g["sents"][0]]["start"]) for g in groups if g["sents"] and g["clips"][0][2] == 1.0]
        first = min(starts) if starts else 99.0
        add("S-03", False, first <= 2.0, "最初の発話 %.1f秒（2秒以内）" % first, "冒頭に強い一文（先出し）を置く")
    shots = [round(o1 - o0, 2) for g in groups for (o0, o1, _) in g["out"]]
    short = [x for x in shots if x < P["min_shot"] - 0.05]
    add("S-04", False, not short, "最短ショット %.2f秒（下限 %.1f）" % (min(shots), P["min_shot"]))
    if P.get("nohook"):
        add("S-06", False, None, "P9は対象外")
    else:
        add("S-06", False, not meas["black"] and not meas["freeze"], "黒画面 %d・静止 %d（0.5秒以上）" % (len(meas["black"]), len(meas["freeze"])))
    firsts = groups[0]["sents"][0] if groups and groups[0]["sents"] else None
    add("S-07", False, firsts is None or not sents[firsts]["text"].startswith(DEMONSTRATIVE), "冒頭の文: %s" % (sents[firsts]["text"][:20] if firsts is not None else "-"), "指示語で始まらない文から始める")
    kin = [e["text"] for e in ev if e["text"][:1] in NO_START or e["text"][-1:] in NO_END]
    add("S-08", False, not kin, "禁則違反 %d件" % len(kin))
    negs = []
    for t in [vid.get("headline", "")] + list(vid.get("emphasis", [])):
        if t and not NEGATION.search(t):
            src = [sents[i]["text"] for g in groups for i in g["sents"] if any(tok in sents[i]["text"] for tok in facts(t) or [t])]
            if src and all(NEGATION.search(s) for s in src):
                negs.append(t)
    add("S-09", False, not negs, "否定の脱落の疑い %s" % negs)
    if ctx["series"]:
        ch = ctx["chapters"]
        ok = bool(ch) and ch[0][0] == 0 and len(ch) >= 3 and all(ch[i + 1][0] - ch[i][0] >= 10 for i in range(len(ch) - 1))
        add("S-14", False, ok, "チャプター %d個" % len(ch))
        add("S-11", False, d <= 3600, "60分以下")
    hard_ok = all(r["status"] == "pass" for r in R if r["level"] == "hard")
    return dict(hard_pass=hard_ok, results=R)


# ---------------- build ----------------
def safe_name(s):
    return re.sub(r'[\\/:*?"<>|\s]+', "_", s).strip("_")[:60] or "video"


def contact_sheet(out, dur, png):
    n = 6
    times = [dur * (k + 0.5) / n for k in range(n)]
    args = []
    for t in times:
        args += ["-ss", fnum(t), "-i", out]
    fc = "".join("[%d:v]scale=270:480,setsar=1[s%d];" % (k, k) for k in range(n)) + "".join("[s%d]" % k for k in range(n)) + "xstack=inputs=%d:layout=%s[o]" % (
        n, "|".join("%d_%d" % (270 * (k % 3), 480 * (k // 3)) for k in range(n)))
    ff(args + ["-filter_complex", fc, "-map", "[o]", "-frames:v", "1", "-update", "1", "-y", png])
    return times


def cmd_build(a):
    plan = jload(a.plan)
    pid = plan.get("preset", "")
    P = PRESETS.get(pid)
    if not P:
        die("unknown preset %s (P1..P10)" % pid)
    if P.get("supported") is False:
        die("%s はこの版では作れません（BGMのビート解析と楽曲ライブラリが必要）。P4/P8/P9 を提案してください。" % pid)
    if not plan.get("rights_confirmed"):
        die("rights_confirmed が true ではありません。素材の権利（自分が権利を持つ／許諾済み）を利用者に確認してください。(H-09)")
    pending = [t["text"] for t in plan.get("tsukkomi", []) if not t.get("approved")]
    if pending:
        die("未承認のツッコミがあります: %s。利用者に読んでもらい、承認されたものだけ approved=true にし、それ以外は削除してください。(H-08)" % " / ".join(pending))
    work = a.work
    sents = jload(os.path.join(work, "sentences.json"))
    sig = jload(os.path.join(work, "signals.json"))
    info = jload(os.path.join(work, "probe.json"))
    if os.path.abspath(info["path"]) != os.path.abspath(plan.get("source", info["path"])):
        die("plan.source が probe.json の入力と違います")
    font_file, family = find_font(a.font)
    if P["telop"] and not font_file:
        print("WARN: 日本語フォントが見つからないため、字幕・テロップを焼き込みません（SRTは出力します）。", file=sys.stderr)
    fps = info.get("fps") or 30.0
    fps = fps if 23 < fps <= 60.5 else 30.0
    series = plan.get("mode") == "series"
    vids = plan["videos"]
    if not series and len(vids) > 3:
        die("見どころは最大3本です（水増ししない）")
    if not series:  # IoU overlap check between candidates
        spans = []
        for v in vids:
            r = [x for x in v["body"] if not isinstance(x, dict)]
            if r:
                spans.append((sents[min(x[0] for x in r)]["start"], sents[max(x[1] for x in r)]["end"]))
            else:
                spans.append((min(x["t0"] for x in v["body"]), max(x["t1"] for x in v["body"])))
        for i in range(len(spans)):
            for j in range(i + 1, len(spans)):
                inter = max(0.0, min(spans[i][1], spans[j][1]) - max(spans[i][0], spans[j][0]))
                uni = max(spans[i][1], spans[j][1]) - min(spans[i][0], spans[j][0])
                if uni > 0 and inter / uni > 0.5:
                    die("候補 %d と %d が重なっています（IoU %.2f > 0.5）。別の場面を選ぶか本数を減らしてください。" % (i + 1, j + 1, inter / uni))
    else:
        cov = []
        for v in vids:
            for x in v["body"]:
                cov += list(range(int(x[0]), int(x[1]) + 1))
        if sorted(cov) != list(range(len(sents))):
            die("シリーズの本編が全文を重なりなく覆っていません（split の結果をそのまま使ってください）")
    os.makedirs(a.out, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M")
    proj = safe_name(plan.get("project") or os.path.splitext(os.path.basename(info["path"]))[0])
    labels_all = PART_LABELS.get(len(vids), ["第%d話" % (k + 1) for k in range(len(vids))]) if series else [""] * len(vids)
    summary = []
    sils = [tuple(x) for x in sig["silences"]]
    for k, vid in enumerate(vids):
        if a.only and (k + 1) not in a.only:
            continue
        ctxs = None
        if series:
            prev = vids[k - 1]["body"][-1] if k > 0 else None
            recap = None
            if prev:
                b = int(prev[1])
                a0 = b
                while a0 > int(prev[0]) and b - a0 < 2 and sents[b]["end"] - sents[a0 - 1]["start"] <= 10:
                    a0 -= 1
                recap = (a0, b)
            nxt = vids[k + 1].get("teaser") if k + 1 < len(vids) else None
            ctxs = dict(recap=recap, next=nxt)
            if vid.get("teaser") is None:
                die("シリーズの各パートには teaser（そのパートの見どころ1文）が必要です（part %d）" % (k + 1))
        groups = build_groups(plan, vid, sents, sig, P, ctxs)
        total = timeline(groups, fps)
        if series:
            mean = sum(sum(edited_len(sents, P["sil"], sils)[int(x[0]):int(x[1]) + 1]) for v in vids for x in v["body"]) / len(vids)
            rng = (mean * 0.7, mean * 1.3 + 30)
        else:
            rng = P["dur"]
        if not series and not (rng[0] - 0.5 <= total <= rng[1] + 0.5):
            die("候補%d の完成尺が %.1f秒で、%s の範囲 %d〜%d秒に入りません。%s" % (k + 1, total, pid, rng[0], rng[1],
                "文を減らしてください（約%.0f秒）。" % (total - rng[1]) if total > rng[1] else "前後の文を足してください（約%.0f秒）。" % (rng[0] - total)))
        emphasis = [w for w in vid.get("emphasis", [])][:3]
        ev, maxw, scalex, outline = captions(groups, sents, P, emphasis, None)
        ev_burn = ev if "caption" in P["telop"] else []
        labels = []
        for g in groups:
            if g["role"] == "recap":
                labels.append((g["out"][0][0], g["out"][-1][1], "前回のおさらい"))
            if g["role"] == "next":
                labels.append((g["out"][0][0], g["out"][-1][1], "▶ %sにつづく" % (labels_all[k + 1] if k + 1 < len(labels_all) else "次回")))
        tsk_raw = [t for t in plan.get("tsukkomi", []) if int(t.get("video", 0)) == k] if "tsukkomi" in P["telop"] else []
        tsk = []
        for t in tsk_raw:
            if len(t["text"]) > 14:
                die("ツッコミは14文字以内: " + t["text"])
            g = next((g for g in groups if int(t["after"]) in g["sents"] and g["role"] == "body"), None)
            if g and t.get("approved"):
                e = map_t(g, sents[int(t["after"])]["end"])
                tsk.append((max(0.0, e - 0.2), min(total, e + 1.6), t["text"]))
        headline = vid.get("headline", "")
        if series and headline:
            headline = "【%s】%s" % (labels_all[k], headline)
        if len(vid.get("headline", "")) > 16:
            die("見出しは16文字以内: " + vid["headline"])
        label = labels_all[k]
        base = "%s_%s_%02d%s_%s" % (proj, pid, k + 1, label, stamp)
        wk = os.path.join(work, "v%02d" % (k + 1))
        os.makedirs(wk, exist_ok=True)
        ass = os.path.join(wk, "telop.ass")
        layout = write_ass(ass, ev_burn, P, headline, labels, tsk, family or "sans-serif", total, scalex, outline, None, emphasis)
        burn = font_file is not None and (ev_burn or headline or labels or tsk)
        out = os.path.join(a.out, base + ".mp4")
        render(groups, info, fps, ass if burn else None, os.path.dirname(font_file) if burn else None, out, wk)
        meas = measure(out)
        chapters = []
        if series:
            for (si, title) in vid.get("chapters", []):
                g = next((g for g in groups if g["role"] == "body" and int(si) in g["sents"]), None)
                if g:
                    chapters.append((round(map_t(g, sents[int(si)]["start"]), 1), title))
            if chapters:
                chapters[0] = (0, chapters[0][1])
            merged = []
            for c in chapters:
                if merged and c[0] - merged[-1][0] < 10:
                    continue
                merged.append(c)
            chapters = merged
        res = qa(dict(P=P, meas=meas, ev=ev_burn if "caption" in P["telop"] else [], layout=layout, groups=groups, sents=sents, vid=vid, range=rng, series=series,
                      sils=sils, tsukkomi_raw=tsk_raw, rights=plan.get("rights_confirmed"), chapters=chapters))
        with open(os.path.join(a.out, base + ".srt"), "w", encoding="utf-8") as f:  # full verbatim speech of the output
            for n, e in enumerate(ev, 1):
                f.write("%d\n%s --> %s\n%s\n\n" % (n, srt_time(e["start"]), srt_time(e["end"]), e["text"]))
        sheet = os.path.join(a.out, base + "_sheet.png")
        contact_sheet(out, meas["duration"], sheet)
        meta = ["# " + base, "", "## タイトル案"] + ["- " + t for t in vid.get("titles", [])][:3] + ["", "## 説明文", vid.get("description", ""), "", "元動画: （URLを記入）", ""]
        if chapters:
            meta += ["## チャプター"] + ["%s %s" % (mmss(t)[:5] if t < 3600 else mmss(t), c) for t, c in chapters] + [""]
        tags = list(vid.get("hashtags", []))
        if meas["duration"] <= 180 and not series and "#Shorts" not in tags:
            tags.append("#Shorts")
        meta += ["## ハッシュタグ", " ".join(tags), "", "## 選定理由", vid.get("reason", ""), "", "## スコア（0〜10、AIの目安）", json.dumps(vid.get("scores", {}), ensure_ascii=False)]
        with open(os.path.join(a.out, base + "_meta.md"), "w", encoding="utf-8") as f:
            f.write("\n".join(meta) + "\n")
        editplan = dict(tool=VERSION, preset=pid, video=vid, series_label=label, fps=fps, duration=total, font=family, burned=bool(burn),
                        groups=[dict(role=g["role"], sents=[g["sents"][0], g["sents"][-1]] if g["sents"] else [], clips=g["clips"], zoom=g["zoom"], out=[o[:2] for o in g["out"]]) for g in groups],
                        captions=[dict(start=round(e["start"], 3), end=round(e["end"], 3), text=e["text"]) for e in ev], chapters=chapters, measured=meas, qa=res)
        jdump(editplan, os.path.join(a.out, base + ".editplan.json"))
        summary.append(dict(file=base + ".mp4", duration=round(meas["duration"], 1), lufs=meas["lufs"], tp=meas["true_peak"], hard_pass=res["hard_pass"],
                            warnings=[r["id"] + " " + r["detail"] for r in res["results"] if r["status"] in ("warn", "fail")]))
        print(json.dumps(summary[-1], ensure_ascii=False))
    jdump(summary, os.path.join(a.out, "summary.json"))
    if not all(s["hard_pass"] for s in summary):
        sys.exit(3)


def main():
    ap = argparse.ArgumentParser(description="VideoCreator Cowork edition " + VERSION)
    sp = ap.add_subparsers(dest="cmd", required=True)
    e = sp.add_parser("env")
    e.add_argument("--font")
    p = sp.add_parser("prepare", help="probe + transcript + signals into WORK")
    p.add_argument("video")
    p.add_argument("--transcript", help="VTT/SRT/JSON with timestamps")
    p.add_argument("--work", default="vc_work")
    s = sp.add_parser("split")
    s.add_argument("--work", default="vc_work")
    s.add_argument("--preset", required=True)
    s.add_argument("--parts", type=int, required=True)
    s.add_argument("--prefer", default="", help="comma separated sentence indices after which a topic ends")
    b = sp.add_parser("build")
    b.add_argument("plan")
    b.add_argument("--work", default="vc_work")
    b.add_argument("--out", default="vc_out")
    b.add_argument("--font")
    b.add_argument("--only", type=int, nargs="*")
    sh = sp.add_parser("frames", help="contact sheets every N seconds so the AI can look at the video")
    sh.add_argument("video")
    sh.add_argument("--every", type=float, default=10.0)
    sh.add_argument("--work", default="vc_work")
    a = ap.parse_args()
    if a.cmd == "env":
        font_file, fam = find_font(a.font)
        filters = ""
        if FF:
            filters, _ = run([FF, "-hide_banner", "-filters"], check=False)
        need = ["subtitles", "loudnorm", "ebur128", "silencedetect", "scdet", "blackdetect", "freezedetect", "boxblur", "xstack", "tpad", "apad", "atempo"]
        miss = [n for n in need if not re.search(r"\s%s\s" % n, filters)]
        enc, _ = run([FF, "-hide_banner", "-encoders"], check=False) if FF else ("", "")
        r = dict(python=sys.version.split()[0], ffmpeg=FF, ffprobe=FP, ffmpeg_version=(run([FF, "-version"])[0].split("\n")[0] if FF else None), missing_filters=miss,
                 libx264=("libx264" in enc), font=font_file, font_family=fam, whisper=bool(importlib.util.find_spec("faster_whisper")), tool=VERSION)
        r["ok"] = bool(FF) and not miss and r["libx264"]
        print(json.dumps(r, ensure_ascii=False, indent=1))
        sys.exit(0 if r["ok"] else 1)
    if a.cmd == "prepare":
        os.makedirs(a.work, exist_ok=True)
        info = probe(a.video)
        jdump(info, os.path.join(a.work, "probe.json"))
        cues = parse_transcript(a.transcript) if a.transcript else asr(a.video)
        if cues is None:
            die("書き起こしがありません。Teams/Stream の .vtt を添付してもらってください（faster-whisper も使えません）。", 4)
        if not cues:
            die("書き起こしが空です", 4)
        sents = to_sentences(cues)
        if sents[-1]["end"] > info["duration"] + 2:
            die("書き起こしの時刻(%.1f秒)が動画の長さ(%.1f秒)を超えています。別の動画の書き起こしではありませんか。" % (sents[-1]["end"], info["duration"]))
        sig = signals(a.video)
        for s in sents:
            ks = [sig["loud_per_sec"].get(str(t)) for t in range(int(s["start"]), int(s["end"]) + 1)]
            ks = [x for x in ks if x is not None]
            s["loud"] = max(ks) if ks else None
        jdump(sents, os.path.join(a.work, "sentences.json"))
        jdump(sig, os.path.join(a.work, "signals.json"))
        with open(os.path.join(a.work, "transcript.txt"), "w", encoding="utf-8") as f:
            for i, s in enumerate(sents):
                gap = (sents[i + 1]["start"] - s["end"]) if i + 1 < len(sents) else 0
                f.write("[%d] %s-%s %s: %s  (loud %s%s)\n" % (i, mmss(s["start"]), mmss(s["end"]), s["speaker"] or "-", s["text"], s["loud"], ", pause %.1fs" % gap if gap >= 0.7 else ""))
        print(json.dumps(dict(probe=info, sentences=len(sents), speakers=sorted(set(s["speaker"] for s in sents)), silence_db=sig["silence_db"],
                              silences=len(sig["silences"]), shots=len(sig["shots"]), loud_peaks=sig["loud_peaks"]), ensure_ascii=False, indent=1))
        if info["problems"]:
            print("PROBLEMS: " + " / ".join(info["problems"]), file=sys.stderr)
    if a.cmd == "split":
        P = PRESETS.get(a.preset) or die("unknown preset")
        if not P.get("series"):
            die("%s はシリーズに分けられません（P1〜P4・P6・P7・P9・P10 のみ）" % a.preset)
        sents = jload(os.path.join(a.work, "sentences.json"))
        prefer = set(int(x) for x in a.prefer.split(",") if x.strip())
        sig = jload(os.path.join(a.work, "signals.json"))
        r = split_series(sents, a.parts, prefer, P["sil"], [tuple(x) for x in sig["silences"]])
        jdump(r, os.path.join(a.work, "parts.json"))
        print(json.dumps(r, ensure_ascii=False, indent=1))
    if a.cmd == "build":
        if not FF:
            die("ffmpeg がありません")
        cmd_build(a)
    if a.cmd == "frames":
        os.makedirs(a.work, exist_ok=True)
        info = probe(a.video)
        n = max(1, int(info["duration"] // a.every))
        per = 12
        out = []
        for page in range(0, n, per):
            ts_ = [a.every * (k + 0.5) for k in range(page, min(n, page + per))]
            png = os.path.join(a.work, "frames_%03d.png" % (page // per + 1))
            args = []
            for t in ts_:
                args += ["-ss", fnum(t), "-i", a.video]
            m = len(ts_)
            fc = "".join("[%d:v]scale=320:180:force_original_aspect_ratio=decrease,pad=320:180:(ow-iw)/2:(oh-ih)/2,setsar=1[s%d];" % (k, k) for k in range(m))
            fc += "".join("[s%d]" % k for k in range(m)) + ("xstack=inputs=%d:layout=%s[o]" % (m, "|".join("%d_%d" % (320 * (k % 4), 180 * (k // 4)) for k in range(m))) if m > 1 else "null[o]")
            ff(args + ["-filter_complex", fc, "-map", "[o]", "-frames:v", "1", "-update", "1", "-y", png])
            out.append(dict(image=png, grid="4列、左上から右へ", times=[round(t, 1) for t in ts_]))
        jdump(out, os.path.join(a.work, "frames.json"))
        print(json.dumps(out, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
```
<!-- vc_tool.py:end -->

---

## 부록 B — 프롬프트 설계 근거·검증

### 요청을 더 깊게 만드는 네 질문

| 질문 | 답 |
|---|---|
| (1) 최고 전문가라면 요청 없이도 무엇을 하나 | Cowork 환경(Python·FFmpeg·일본어 폰트·첨부200MB)을 먼저 확인하고 부족하면 정직하게 대체합니다. AI 판단(장면 선정)·결정적 처리(추출/자막/음량/검사)를 분리하여 같은 계획→같은 영상. 발화 보존·숫자/고유명사·안전영역·라우드니스·단어 도중 컷을 기계 검사. 편수 부풀림 금지·권리/AI문안 승인·앱과 차이(SFX/BGM/카메라/P5) 공개 |
| (2) 전문가 출발점인 원자료 | 가공 없는 영상·Stream/Teams 시각 전사.vtt, 원음/영상에서 직접 측정한 FFmpeg 무음/숏/100ms음량. 요약 아닌 presets/P1~P10.json·docs/PRD.md §7/§13·Renderer.cs/AssWriter.cs(흐림/스타일/2패스)·Cowork 공식 파일형식/200MB/50파일500MB/스킬 규격 |
| (3) 구체 방법·도구 | 문장번호·문장경계(단어 도중 금지), silencedetect 무음점프·최소숏, scdet/blackdetect/ebur128 신호, 동적 계획법 균등+주제 분할, 흐림 세로·펀치인·ASS 금칙/폭축소·2패스loudnorm. QA ffprobe상당·독립ebur128·blackdetect/freezedetect·배치계산·원문일치 |
| (4) 제3자 정확성 증거 | 별도 EBU R128계기·ffprobe, 완성MP4 Azure AI Speech 재전사로 의도발화/자막시각 검증, 대본 정답 테스트영상·참조 앱QA. 최종적으로 YouTube Studio 등이 Shorts로 수용하는지 |

### 재작성한 요청

> VideoCreator 처리(전사→선정→문장 경계/무음 컷→9:16→원문 자막/제목/강조→2패스 음량→QA)를 Microsoft365 Copilot Cowork에서 재현하는 프롬프트를 `/work/VideoCreator-Prompt.md`에 만든다. 사용자는 영상·패턴(P1~P10/자동)·방식(최대3하이라이트/N편)만 선택, 수치는 앱 프리셋 고정. 공식 첨부200MB·출력50파일/500MB·로컬파일불가·스킬과 실행환경 미보장을 전제로 환경 확인·대체 절차 포함. AI 판단/결정적 처리 분리·검증 스크립트 동봉. H-01~H-11/S-01~S-14 중 측정 가능 항목 구현·앱 차이 명시. 대본 영상으로 실제 실행·ebur128/ffprobe/재ASR 독립 측정으로 검증.

### 검증 기록(2026-11-10, Windows·Python3.12·FFmpeg2026-11)

| 시험 | 원본 | 결과 |
|---|---|---|
| P4 하이라이트2개 | `spikes/media/talk.mp4`125초·1920×1080·2화자·대본 + 대본.vtt | 33.2/25.2초·-14.0LUFS·피크-1.5dBTP·전 하드 통과 |
| 제3자 내용 | 두 출력 재ASR | 문자 바이그램100%/96.2%·자막100%가 같은 시각±1초 들림·선공개0.1~0.2초 |
| 시리즈2편 | long30 첫8분·1280×720·5기획 + 앱전사.vtt | 「続いての企画は…」직전 주제 경계 일치·190.6/214.6초·-14LUFS·챕터통과. 재ASR 선공개/복습/예고100% 계획위치·본문99.1/97.7%·동기100/98.4% |
| 시리즈3편 | 동일 | 5기획을3균등 불가·경계1곳 주제경계아님 경고 확인 |
| P2/P3/P7/P8/P9 | talk.mp4 | 전출력/하드통과·P3미승인은 렌더전중지/승인후흰상자·P7화자별테두리색 |
| 거부·오류 | talk.mp4 | 전사「一万円」대비 제목「1万円」H-07·길이부족/중복/P5/권리미확인/편당1분미만은 수정 안내 오류 |
| ffprobe없음 | VC_FFMPEG로 ffmpeg만 지정 | 길이/해상도/fps/음성판정·출력 동일 |

발견·수정: 무음 컷 자막 일부 누락(원문 위반)→출력 구간에 자막 시각 배분. 강조 확대가 안전 폭 계산에서 빠짐→추가. 시리즈 추정이 무음 컷 미반영→무음 구간 기반. 구성은 선공개→복습→본문→예고로 수정.

### 아직 확인하지 않은 사항

- **실제 Copilot Cowork 실행**. Python/FFmpeg/일본어 폰트 존재는 공식 문서 미기재(공개 커뮤니티 스킬 일부는 Python·FFmpeg/imageio-ffmpeg 사용). 반드시 env부터 실행·부족하면 §3 대체.
- Stream .vtt 구분·문장부호 유무는 원본별 다름. 마침표 없는 구분은 그대로 한 문장.
---

[↑ 문서 안내](../README.md) | [日本語](../../../work/VideoCreator-Prompt.md) | [English](../../en/work/VideoCreator-Prompt.md) | [简体中文](../../zh-CN/work/VideoCreator-Prompt.md) | **한국어**
