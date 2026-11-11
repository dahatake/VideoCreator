// Explicit UI resources. Values never pass through generated or user-authored text.
export const translated = `
1|Calm|平静|차분함
2|Moderate|适中|보통
3|Energetic|活跃|활기참
4|Media|素材|미디어
5|Preset|模式|프리셋
6|Generate|生成|생성
7|Candidates|候选|후보
8|Review & edit|检查与修改|검토 및 수정
9|Export|导出|내보내기
10|Analysis proxy|分析代理|분석용 프록시
11|Transcription|转写|받아쓰기
12|Shot, volume and silence analysis|镜头、音量和静音分析|장면·음량·무음 분석
13|AI highlight selection|AI 精彩片段选择|AI 하이라이트 선택
14|Edit plan|编辑计划|편집 계획
15|Create previews|创建预览|미리 보기 생성
16|Confirm your rights to each media file by checking its box.|请勾选每项素材的权利确认框。|각 미디어의 사용 권리를 확인하고 체크하세요.
17|Unable to load this video. Choose an MP4, MOV, MKV or WebM file.|无法读取此视频。请选择 MP4、MOV、MKV 或 WebM 文件。|동영상을 읽을 수 없습니다. MP4 / MOV / MKV / WebM 파일을 선택하세요.
18|Unable to connect to transcription. Check your network and Azure sign-in (az login), then retry.|无法连接转写服务。请检查网络及 Azure 登录（az login）后重试。|받아쓰기 서비스에 연결할 수 없습니다. 네트워크와 Azure 로그인(az login)을 확인하고 다시 시도하세요.
19|No suitable segments found. Choose another preset or a longer video.|未找到符合条件的片段。请选择其他模式或更长的视频。|조건에 맞는 구간이 없습니다. 다른 프리셋이나 더 긴 영상을 사용하세요.
20|Some automatic checks failed. Fix the items below before exporting again.|自动检查中有项目不符合发布标准。请修复下方项目后重新导出。|자동 검사에서 게시 기준을 충족하지 못했습니다. 아래 항목을 수정한 후 다시 내보내세요.
21|This preset is not available yet.|此模式暂不可用。|아직 사용할 수 없는 프리셋입니다.
22|Video export failed. Check free disk space and retry.|视频导出失败。请检查磁盘空间后重试。|내보내기에 실패했습니다. 디스크 여유 공간을 확인하고 다시 시도하세요.
23|Cancel the job or wait for completion before deleting.|请先取消任务或等待完成后再删除。|작업을 취소하거나 완료될 때까지 기다린 후 삭제하세요.
24|Close apps using files in this folder and retry. The project has not been deleted.|请关闭正在使用此文件夹文件的应用后重试。项目尚未删除。|폴더의 파일을 사용하는 앱을 닫고 다시 시도하세요. 프로젝트는 삭제되지 않았습니다.
25|Back to {0}|返回{0}|{0}(으)로 돌아가기
26|Show safe areas|显示安全区域|안전 영역 표시
27|Pass|通过|통과
28|Fail|不通过|실패
29|Not run|未执行|미실시
30|Warning|警告|경고
31| · {0}| · {0}| · {0}
32|(Target {0}{1})|（标准 {0}{1}）|(기준 {0}{1})
33|Failed {0}|不通过 {0}|실패 {0}
34|Passed {0}/{1}|通过 {0}/{1}|통과 {0}/{1}
35|Not run {0} (checked on export)|未执行 {0}（导出时检查）|미실시 {0}(내보낼 때 검사)
36|Warnings {0}|警告 {0}|경고 {0}
37|GitHub Copilot (installed CLI)|GitHub Copilot（已安装的 CLI）|GitHub Copilot(설치된 CLI)
38|No AI (select by audio energy)|不使用 AI（根据音频活跃度选择）|AI 없음(음성 에너지로 선택)
39|No AI (configured AI unavailable)|不使用 AI（配置的 AI 不可用）|AI 없음(설정한 AI 사용 불가)
40|No-LLM mode · |不使用 LLM 模式 · |LLM 미사용 모드 · 
41|None|无|없음
42|{0}Transcription: {1} · Selection: {2}|{0}转写：{1} · 选择：{2}|{0}받아쓰기: {1} · 선택: {2}
43|Unavailable: {0}|不可用：{0}|사용 불가: {0}
44|Unable to identify destinations. Check settings before generating.|无法确认发送目标。请在生成前检查设置。|전송 대상을 확인할 수 없습니다. 생성 전에 설정을 확인하세요.
45|No-LLM mode: nothing is sent to an LLM.|不使用 LLM 模式：不会发送至 LLM。|LLM 미사용 모드: LLM에 전송하지 않습니다.
46|Not configured|未设置|미설정
47|LLM destination: {0} (transcript and volume summary).|LLM 发送目标：{0}（转写文本和音量摘要）。|LLM 전송 대상: {0}(받아쓰기 텍스트와 음량 요약).
48|Caption audio is sent to {0}. Not fully offline. Authentication: {1}.|字幕音频将发送至 {0}。并非完全离线。身份验证：{1}。|자막용 음성은 {0}(으)로 전송됩니다. 완전한 오프라인이 아닙니다. 인증: {1}.
49|ASR is not configured. Uncached audio cannot be transcribed.|未设置 ASR。无法转写未缓存的音频。|ASR이 설정되지 않았습니다. 캐시가 없는 음성은 받아쓸 수 없습니다.
50|Not fully offline. Authentication: {0}|并非完全离线。身份验证：{0}|완전한 오프라인이 아닙니다. 인증: {0}
51|Destinations|发送目标|전송 대상
52|Transcript, volume summary and caption audio → {0} ({1})|转写文本、音量摘要及字幕音频 → {0}（{1}）|받아쓰기·음량 요약·자막용 음성 → {0}({1})
53|No-LLM mode: nothing sent to an LLM|不使用 LLM 模式：不会发送至 LLM|LLM 미사용 모드: LLM에 전송하지 않음
54|Transcript and volume summary → {0}|转写文本和音量摘要 → {0}|받아쓰기와 음량 요약 → {0}
55|Caption audio → {0} ({1})|字幕音频 → {0}（{1}）|자막용 음성 → {0}({1})
56|ASR not configured; uncached audio cannot be transcribed|未设置 ASR，无法转写未缓存的音频|ASR 미설정: 캐시가 없는 음성은 받아쓸 수 없음
57|Running in no-LLM mode|正在以不使用 LLM 模式运行|LLM 미사용 모드로 실행 중
58|Recommended. JSON schema keeps candidate output in a reliable format.|推荐。使用 JSON 架构约束输出，候选格式更稳定。|권장. JSON 스키마로 출력 형식을 고정하여 후보 형식이 안정적입니다.
59|Sends transcript and volume summary, not video/audio files. Sign in with Entra ID (az login).|发送转写文本和音量摘要，不发送视频或音频文件。使用 Entra ID（az login）登录。|받아쓰기와 음량 요약만 전송하며 영상·음성 파일은 보내지 않습니다. Entra ID(az login)로 로그인합니다.
60|Azure pay-as-you-go. An estimate is shown before generation.|Azure 按量计费。生成前显示估算费用。|Azure 종량제입니다. 생성 전에 예상 비용을 표시합니다.
61|Endpoint (VideoCreator:Llm:Endpoint) missing. Run scripts/setup.ps1.|未设置端点（VideoCreator:Llm:Endpoint）。请运行 scripts/setup.ps1。|엔드포인트(VideoCreator:Llm:Endpoint)가 없습니다. scripts/setup.ps1을 실행하세요.
62|Default model|默认模型|기본 모델
63|Uses your installed Copilot CLI without file-operation tools.|使用本机已安装的 Copilot CLI，不提供文件操作等工具。|설치된 Copilot CLI를 사용하며 파일 작업 등의 도구는 제공하지 않습니다.
64|Sends transcript and volume summary to GitHub. Included in your Copilot plan.|将转写文本和音量摘要发送至 GitHub。费用包含在 Copilot 套餐中。|받아쓰기와 음량 요약을 GitHub에 보냅니다. 비용은 Copilot 요금제에 포함됩니다.
65|Output formatting cannot be enforced; occasional format errors are possible.|无法强制输出格式，偶尔可能出现格式错误。|출력 형식을 강제할 수 없어 간혹 형식 오류가 발생할 수 있습니다.
66|Copilot CLI not found (winget install GitHub.Copilot)|未找到 Copilot CLI（winget install GitHub.Copilot）|Copilot CLI를 찾을 수 없음(winget install GitHub.Copilot)
67|Do not use an LLM|不使用 LLM|LLM 사용 안 함
68|Cuts around audio peaks. Nothing sent externally for selection; no AI charges.|围绕音量高峰剪辑。选择过程不向外部发送数据，也无 AI 费用。|음량이 높은 구간을 잘라냅니다. 선택을 위해 외부로 전송하지 않으며 AI 요금이 없습니다.
69|Does not consider meaning. AI features such as reaction captions are unavailable.|不考虑对话内容。无法使用吐槽字幕等 AI 功能。|내용은 고려하지 않습니다. 반응 자막 등 AI 기능을 사용할 수 없습니다.
70|Configured provider “{0}” is unavailable. Currently selecting without AI.|配置的“{0}”不可用，目前不使用 AI 进行选择。|설정된 “{0}”을(를) 사용할 수 없어 AI 없이 선택합니다.
71|Not detected|未检测到|감지되지 않음
72|Choose AI for highlight selection. Caption transcription{0} is the same for all options.|请选择用于精彩片段选择的 AI。无论选择哪项，字幕转写{0}都相同。|하이라이트 선택 AI를 고르세요. 자막 받아쓰기{0}는 어느 옵션이든 같습니다.
73|If AI fails to produce candidates, selection automatically continues without AI.|若 AI 调用失败，未获得候选，将自动切换为不使用 AI 的方式继续。|AI 호출로 후보를 얻지 못하면 자동으로 AI 없이 계속합니다.
74|App version|应用版本|앱 버전
75|Build information|构建信息|빌드 정보
76|Missing filters|缺少的滤镜|누락된 필터
77|Preset count|模式数量|프리셋 수
78|Default location|默认位置|기본 위치
79|Set in appsettings|由 appsettings 指定|appsettings에서 지정
80|Set in this app|在此应用中指定|이 앱에서 지정
81|Move everything, including project folders, uploads and exports, and update all app references.|全部移动到新位置，包括项目文件夹、上传素材、导出视频，并更新应用内的引用。|프로젝트 폴더·업로드·내보낸 영상을 모두 이동하고 앱의 참조도 갱신합니다.
82|Do not move. Existing projects stay where they are; only new projects use the new location.|不移动。现有项目继续使用原位置，新项目使用新位置。|이동하지 않습니다. 기존 프로젝트는 유지하고 새 프로젝트만 새 위치에 만듭니다.
83|Project storage location|项目保存位置|프로젝트 저장 위치
84|Current location:|当前位置：|현재 위치:
85|({0}) · {1} projects|（{0}）· {1} 个项目|({0}) · 프로젝트 {1}개
86|New projects, uploaded media, previews and exports are saved inside this folder.|新项目、上传素材、预览及导出视频都保存在此文件夹内。|새 프로젝트·업로드한 미디어·미리 보기·내보내기 파일을 이 폴더에 저장합니다.
87|New folder (full path)|新文件夹（完整路径）|새 폴더(전체 경로)
88|Choose folder…|选择文件夹…|폴더 선택…
89|What should happen to existing folders and files? Choose one option.|如何处理原位置的文件夹和文件？请选择一项。|기존 폴더와 파일을 어떻게 할까요? 하나를 선택하세요.
90|Review changes|确认内容|내용 확인
91|Storage move progress|保存位置迁移进度|저장 위치 이동 진행 상황
92|Select a folder in the folder picker. Check the taskbar if it is hidden.|请在打开的文件夹选择窗口中选择文件夹。若未显示，请检查任务栏。|열린 폴더 선택 창에서 폴더를 고르세요. 창이 보이지 않으면 작업 표시줄을 확인하세요.
93|Selection cancelled. Input unchanged.|已取消选择，输入未更改。|선택을 취소했습니다. 입력은 변경되지 않았습니다.
94|Enter a full folder path or use “Choose folder…”.|请输入新保存位置的完整路径，或点击“选择文件夹…”。|새 저장 폴더 경로를 입력하거나 “폴더 선택…”을 사용하세요.
95|Choose whether to move existing folders and files.|请选择是否移动原有文件夹和文件。|기존 폴더와 파일의 이동 여부를 선택하세요.
96| · {0} files| · {0} 个文件| · 파일 {0}개
97|{0} more items|另有 {0} 项|외 {0}개
98| · {0} other items| · 其他 {0} 项| · 기타 {0}개
99|Same drive: folders are renamed, which is quick.|同一驱动器，只需重命名文件夹，速度较快。|같은 드라이브이므로 폴더 이름만 바꿔 빠르게 이동합니다.
100|Unknown|未知|알 수 없음
101|Copies to another drive, verifies contents, then deletes originals (free: {0}).|复制到另一驱动器并校验内容后删除原文件（可用空间 {0}）。|다른 드라이브로 복사·검증 후 원본을 지웁니다(여유 공간 {0}).
102|Projects|项目|프로젝트
103|Other folders/files (may not belong to this app; these will also move)|其他文件夹及文件（可能并非此应用创建，也将移动）|기타 폴더·파일(앱에서 생성하지 않은 항목도 함께 이동)
104|Nothing to move in the current location.|原位置没有需要移动的项目。|현재 위치에 이동할 항목이 없습니다.
105|Moving: {0} projects{1}, total {2}. {3}|移动内容：{0} 个项目{1}，共 {2}。{3}|이동: 프로젝트 {0}개{1}, 합계 {2}. {3}
106|Files stay in {0}. Deleting a project still moves its folder to the Recycle Bin. Only new projects use the new location.|文件保留在 {0}。删除项目时仍将文件夹移至回收站。仅新项目使用新位置。|파일은 {0}에 유지됩니다. 프로젝트 삭제 시 폴더는 휴지통으로 이동합니다. 새 프로젝트만 새 위치를 사용합니다.
107|{0} projects elsewhere will not move.|其他位置的 {0} 个项目不会移动。|다른 위치의 프로젝트 {0}개는 이동하지 않습니다.
108|Cannot apply these changes|目前无法更改|현재 변경할 수 없음
109|Move all and change location|全部移动并更改位置|모두 이동하고 위치 변경
110|Change location without moving|不移动，仅更改位置|이동 없이 위치 변경
111|{0} / {1} items{2}|{0} / {1} 项{2}|{0} / {1}개{2}
112|Moved {0} items and updated {1} document references.|已移动 {0} 项，更新了 {1} 处文档引用。|{0}개를 이동하고 문서 참조 {1}개를 갱신했습니다.
113|No files were moved.|未移动文件。|파일을 이동하지 않았습니다.
114|New projects will be created in {0}.|新项目将在 {0} 中创建。|새 프로젝트는 {0}에 만듭니다.
115|Items left in the original location because moving failed (still usable)|移动失败，保留在原位置的项目（仍可使用）|이동하지 못해 원래 위치에 둔 항목(계속 사용 가능)
116|Close open files, choose the same destination and move again to continue.|请关闭打开的文件，选择相同目标并再次移动以继续。|열린 파일을 닫고 같은 대상을 선택하여 다시 이동하면 이어서 처리합니다.
117|Copied successfully, but some originals could not be deleted (possibly in use). Delete manually if not needed: {0}|已复制，但部分原文件无法删除（可能正在使用）。若不需要，请手动删除：{0}|복사는 완료했으나 일부 원본을 지우지 못했습니다(사용 중일 수 있음). 불필요하면 직접 삭제하세요: {0}
118|Page not found|未找到页面|페이지를 찾을 수 없음
119|Get started|开始使用|시작하기
120|Add a video using “New project”.|点击“新建项目”添加视频。|“새 프로젝트”에서 동영상을 추가하세요.
121|Delete “{0}”|删除“{0}”|“{0}” 삭제
122|Delete|删除|삭제
123|Created|创建时间|생성일
124|{0} videos|{0} 个视频|동영상 {0}개
125|Latest generation|最近生成|최근 생성
126|Choose a long video and a preset. AI finds highlights and makes captioned vertical shorts.|选择长视频和模式，AI 会寻找精彩片段，生成带字幕的竖屏短视频。|긴 영상과 프리셋을 고르면 AI가 하이라이트를 찾아 자막이 있는 세로형 쇼츠를 만듭니다.
127|Project name|项目名称|프로젝트 이름
128|New project|新建项目|새 프로젝트
129|Generation is in progress. Wait for completion or cancel before deleting.|正在生成。请等待完成或取消后再删除。|생성 중입니다. 완료를 기다리거나 취소한 후 삭제하세요.
130|will be deleted.|将被删除。|을(를) 삭제합니다.
131|The project folder (exports, previews and uploaded media) moves to the|项目文件夹（导出视频、预览和上传素材）将移至|프로젝트 폴더(내보낸 영상·미리 보기·업로드)는
132|Recycle Bin|回收站|휴지통
133|.|。|으로 이동합니다.
134|Candidates, edits, transcripts and analysis data are permanently deleted.|候选、编辑内容、转写及分析数据将删除，无法恢复。|후보·편집 내용·받아쓰기 등 분석 데이터는 삭제되며 되돌릴 수 없습니다.
135|For media added by path,|通过路径添加的素材，|경로로 추가한
136|original video files are not deleted|不会删除原始视频文件|원본 영상 파일은 삭제하지 않습니다
137|Folder: {0}|文件夹：{0}|폴더: {0}
138|Folder moved to the Recycle Bin.|文件夹已移至回收站。|폴더를 휴지통으로 이동했습니다.
139|Folder kept because it is outside app-managed storage ({0}).|文件夹不在应用管理范围内，已保留（{0}）。|앱 관리 영역 밖의 폴더이므로 유지했습니다({0}).
140|Deleted “{0}”. {1}|已删除“{0}”。{1}|“{0}”을(를) 삭제했습니다. {1}
141|Add media first|请先添加素材|미디어를 추가하세요
142|Confirm media rights|请勾选权利确认|사용 권리를 확인하세요
143|Reference (audio source)|基准（音频来源）|기준(음성 원본)
144|Camera A|摄像机 A|카메라 A
145|Camera B|摄像机 B|카메라 B
146|Wide (overview)|远景（全景）|와이드(전체)
147|Audio-only reference|仅音频基准|음성 전용 기준
148|Screen (PC recording)|屏幕（电脑录屏）|화면(PC 녹화)
149|Reference (other media syncs to this audio)|基准（其他素材与此音频同步）|기준(다른 미디어를 이 음성에 맞춤)
150|Manual offset {0} ms|手动偏移 {0} ms|수동 오프셋 {0} ms
151|Audio sync: {0}{1} ms|音频同步：{0}{1} ms|음성 동기화: {0}{1} ms
152| · Clock drift {0}{1} ppm ({2} ms corrected per hour)| · 时钟漂移 {0}{1} ppm（每小时校正 {2} ms）| · 시계 오차 {0}{1} ppm(시간당 {2} ms 보정)
153| · {0} windows matched (residual {1} ms)| · 匹配 {0} 个区间（残差 {1} ms）| · {0}개 구간 일치(잔차 {1} ms)
154|Capture-time sync: {0} s (about ±1 s; verify)|拍摄时间同步：{0} 秒（误差约 ±1 秒，请确认）|촬영 시각 동기화: {0}초(약 ±1초 오차, 확인 필요)
155|Unable to sync; enter an offset in seconds.|无法同步，可手动指定偏移秒数。|동기화하지 못했습니다. 오프셋(초)을 직접 지정하세요.
156|Sync confidence {0} (measured again next generation)|同步可信度 {0}（下次生成时重新测量）|동기화 신뢰도 {0}(다음 생성 시 재측정)
157|Automatic audio sync during generation|生成时通过音频自动同步|생성 시 음성으로 자동 동기화
158|Economy|节约|절약
159|Send only high-energy segments to AI (fast, low cost)|仅向 AI 发送活跃片段（快速、低成本）|에너지가 높은 구간만 AI에 전송(빠르고 저렴)
160|Balanced|标准|표준
161|Send roughly half to AI|筛选约一半后发送至 AI|약 절반으로 추려 AI에 전송
162|Full transcript|全文|전체
163|Send the full transcript to AI (fewest missed moments)|将完整转写发送至 AI（最不易遗漏）|전체 받아쓰기를 AI에 전송(누락 최소)
164|View previous results|查看上次生成结果|이전 생성 결과 보기
165|Audio present|有音频|음성 있음
166|No audio|无音频|음성 없음
167|Role|角色|역할
168|Speaker|说话人|화자
169|Automatic|自动|자동
170|Offset (s)|偏移（秒）|오프셋(초)
171|Confirmed|已确认|확인됨
172|Needs review|需确认|확인 필요
173|{0}–{1} s|{0}～{1} 秒|{0}~{1}초
174|Coming soon|准备中|준비 중
175|Opening|开头|도입
176|Pace|节奏|템포
177|Text overlays|字幕特效|텍스트 자막
178|Sound|声音|소리
179|Examples: {0}|示例：{0}|예: {0}
180|Split into a series (first, middle, last…)|拆分为系列（上、中、下篇…）|시리즈로 나누기(전편·중편·후편…)
181|Split long media into a continuous series|将长视频拆分为连续观看的系列|긴 영상을 이어 보는 시리즈로 나누기
182|Split by|拆分方式|분할 방식
183|Number of videos|指定数量|동영상 수 지정
184|Video count|数量|동영상 수
185|videos|个|개
186|Length per video|指定每段时长|동영상 길이 지정
187|Minutes per video|每段分钟数|동영상당 분
188|min|分钟|분
189|How splitting works|拆分原理|분할 방식 설명
190|Boundaries follow topics, not fixed times, so lengths vary. Each part starts with a teaser; later parts recap the previous one and end with a next-part preview. The full transcript is sent to AI.|按话题切换而非固定时间拆分，各篇时长会有差异。每篇先展示看点，第二篇起加入回顾，末尾加入后续预告。向 AI 发送完整转写。|시간이 아닌 주제 경계로 나누므로 길이가 달라집니다. 각 편에 도입 하이라이트, 2편부터 이전 편 요약과 다음 편 예고가 들어갑니다. 전체 받아쓰기를 AI에 전송합니다.
191|Filter long videos|长视频筛选|긴 영상 필터링
192|About {0}% sent to AI|发送至 AI 约 {0}%|AI 전송량 약 {0}%
193|About {0}|约 {0}|약 {0}
194|For videos over 30 minutes, this option changes how much is sent to AI.|超过 30 分钟的视频将根据所选项调整发送至 AI 的内容量。|30분 초과 영상은 선택에 따라 AI 전송량이 달라집니다.
195|At this length, all options send the full transcript and produce the same result.|此时长下所有选项都发送全文，结果相同。|이 길이에서는 모두 전체를 전송하므로 결과가 같습니다.
196|Add media to see a cost estimate.|添加素材后显示费用估算。|미디어를 추가하면 예상 비용을 표시합니다.
197|Long-video filtering (speed/cost vs. coverage)|长视频筛选（速度、费用与遗漏程度）|긴 영상 필터링(속도·비용과 누락 정도)
198|your {0} tracks|已添加的 {0} 首曲目|등록한 {0}곡
199|3 built-in tracks|内置 3 首曲目|기본 3곡
200|(Your music)|（自有曲目）|(내 음악)
201|Use throughout: {0}{1}{2}|全程使用：{0}{1}{2}|전체에 이 곡 사용: {0}{1}{2}
202|Sound effects (pop, chime, boom, etc.)|音效（弹跳、提示音、冲击音等）|효과음(팝·차임·쿵 등)
203|Let AI suggest reactions (P3 only; approval required before export)|让 AI 建议吐槽内容（仅 P3，导出前需批准）|AI 반응 제안(P3만 지원, 내보내기 전 승인 필요)
204|Automatic (recommended): pause cards at speech boundaries, overlays elsewhere|自动（推荐）：仅在发言间隙使用暂停卡片，其他位置叠加字幕|자동(권장): 발언 경계는 정지 카드, 나머지는 겹침 자막
205|Reaction overlay: text without pausing video|吐槽字幕：不暂停视频，叠加文字|반응 자막: 영상을 멈추지 않고 글자 표시
206|Card: pause video for 2–3 s with image, text and BGM|卡片：暂停视频 2～3 秒，显示图片、文字及 BGM|카드: 영상 2~3초 정지, 이미지·글자·BGM
207|{0} tracks registered|已添加 {0} 首曲目|{0}곡 등록됨
208|Add multiple files together|可批量添加|여러 파일을 함께 추가 가능
209|{0} BPM · |{0} BPM · |{0} BPM · 
210|{0} LUFS · {1}Energy {2} ({3}) · Region {4}–{5}|{0} LUFS · {1}活跃度 {2}（{3}）· 使用范围 {4}–{5}|{0} LUFS · {1}에너지 {2}({3}) · 사용 구간 {4}–{5}
211|Not measured (next generation)|未测量（下次生成时测量）|미측정(다음 생성 시 측정)
212|Remove {0}|移除 {0}|{0} 제거
213|Remove|移除|제거
214|I own the rights or have permission to use this music.|我拥有此曲目的使用权或已获得许可。|이 음악의 사용 권리를 보유하거나 허가받았습니다.
215|Follow preset|按模式选择|프리셋에 맡기기
216|Select for each scene ({0})|按场景自动选择（{0}）|장면에 맞게 자동 선택({0})
217|BGM is automatically lowered during speech (normally at least 14 LU below voices; varies by preset). Review and adjust music and levels per scene before exporting.|讲话时自动降低 BGM 音量（通常比人声低至少 14 LU，依模式而异）。导出前可逐场景调整曲目和音量。|발화 중 BGM이 자동으로 작아집니다(프리셋별 기준, 기본 14 LU 이상 차이). 내보내기 전 장면별 곡과 음량을 조절할 수 있습니다.
218|Reaction presentation|吐槽显示方式|반응 표시 방식
219|Mask words (comma-separated). Replaced by ● in captions/overlays and a beep in audio.|屏蔽词（逗号分隔）。字幕显示为 ●，音频替换为提示音。|가릴 단어(쉼표 구분). 자막은 ●, 음성은 삐 소리로 바뀝니다.
220|Example: shop name, person name|示例：店名、人名|예: 가게 이름, 사람 이름
221|Your BGM ({0})|自有 BGM（{0}）|내 BGM({0})
222|BGM files (multiple selection)|BGM 文件（可多选）|BGM 파일(여러 개 선택)
223|Add BGM|添加 BGM|BGM 추가
224|Measures each track’s loudness, tempo, onsets and brightness to match scene energy.|添加后测量各曲目的响度、节拍、起音和明亮度，用于匹配场景活跃度。|각 곡의 음량·템포·어택·밝기를 측정해 장면 에너지에 맞춥니다.
225|Opening, closing and transitions|片头、片尾与转场|오프닝·클로징·장면 전환
226|Measured on this video: VMAF {0} · XPSNR {1} dB|此视频实测：VMAF {0} · XPSNR {1} dB|이 영상 실측: VMAF {0} · XPSNR {1} dB
227|→ Enhanced to {0}×{1} ({2}, {3} s. {4})|→ 已增强至 {0}×{1}（{2}，{3} 秒。{4}）|→ {0}×{1} 향상 완료({2}, {3}초. {4})
228| · Upscaled {0}×| · 放大 {0} 倍| · {0}배 확대
229|{0} ({1}×{2}, {3} s{4}){5}|{0}（{1}×{2}，{3} 秒{4}）{5}|{0}({1}×{2}, {3}초{4}){5}
230|Not registered|未注册|미등록
231|{0} images|{0} 张|{0}장
232|None (text reaction cards)|无（吐槽文字卡片）|없음(반응 글자 카드)
233|Videos below 1080×1920 are enlarged on export and may look slightly soft.|低于 1080×1920 的视频导出时会放大，可能略显模糊。|1080×1920 미만 영상은 내보낼 때 확대되어 약간 흐려질 수 있습니다.
234|Enhance now|立即增强画质|지금 화질 향상
235|Opening: {0}|片头：{0}|오프닝: {0}
236|Closing: {0}|片尾：{0}|클로징: {0}
237|Card images: {0}|卡片图片：{0}|카드 이미지: {0}
238|Include opening (after teaser)|插入片头（先展示看点后）|오프닝 삽입(미리 보는 하이라이트 뒤)
239|Include closing (at end)|插入片尾（末尾）|클로징 삽입(마지막)
240|Add 2–3 s transition reaction cards (image, text, BGM; AI text requires approval)|场景切换处添加 2～3 秒吐槽卡片（图片、文字、BGM，AI 文字需批准）|전환 시 2~3초 반응 카드 삽입(이미지·글자·BGM, AI 문구 승인 필요)
241|Replace or unregister|重新注册或取消注册|다시 등록·해제
242|Register opening/closing|注册片头及片尾|오프닝·클로징 등록
243|Enhance videos below 1080×1920 once at registration. AI super-resolution is used only when measurements confirm fidelity to the original (1–2 min/video on GPU).|注册时一次性增强低于 1080×1920 的视频。仅在实测确认更忠于原片时使用 AI 超分辨率（GPU 每段约 1～2 分钟）。|등록 시 1080×1920 미만 영상을 한 번 향상합니다. 실측으로 원본 충실도가 확인될 때만 AI 초해상도를 사용합니다(GPU 영상당 1~2분).
244|I own or have permission to use these videos/images, including logos, music and illustrations.|我拥有这些视频、图片（含标志、音乐、插图）的使用权或已获许可。|로고·음악·일러스트를 포함한 이 영상·이미지의 사용 권리를 보유하거나 허가받았습니다.
245|Unregister|取消注册|등록 해제
246|Registered card images will be used.|将使用已注册的卡片图片。|등록된 카드 이미지를 사용합니다.
247|{0} s|{0} 秒|{0}초
248|Source video|原视频|원본 영상
249|Create|创建|만들기
250|Do not create|不创建|만들지 않기
251|Keep the {0} existing images, but do not use them in candidates.|保留已创建的 {0} 张图片，但不用于候选。|기존 이미지 {0}장은 유지하되 후보에 사용하지 않습니다.
252|Created {0} images. Used in sequence for transitions; existing images are not overwritten.|已创建 {0} 张，转场时依次使用，不覆盖现有图片。|이미지 {0}장 생성됨. 전환에 순서대로 사용하며 기존 이미지는 덮어쓰지 않습니다.
253|(Rights unconfirmed)|（权利未确认）|(권리 미확인)
254|This project’s source videos|此项目的原视频|이 프로젝트의 원본 영상
255|Opening|片头|오프닝
256|Closing|片尾|클로징
257|{0}: {1} ({2}×{3}, {4} s)|{0}：{1}（{2}×{3}，{4} 秒）|{0}: {1}({2}×{3}, {4}초)
258|Registered openings/closings|已注册的片头与片尾|등록된 오프닝·클로징
259|Add source video or register an opening/closing first.|请先添加原视频或注册片头及片尾。|원본 영상을 추가하거나 오프닝·클로징을 먼저 등록하세요.
260|Video to extract images from|提取图片的源视频|이미지를 추출할 영상
261|Card image source video|卡片图片源视频|카드 이미지 원본 영상
262|Maximum image count|图片数量（最多）|최대 이미지 수
263|Create card images from video|从视频创建卡片图片|영상에서 카드 이미지 만들기
264|{0}Transitions will use text reaction cards.|{0}转场将使用吐槽文字卡片。|{0}장면 전환에 반응 글자 카드를 사용합니다.
265|Enter a full folder path to load videos named Opening/Closing and PNG/JPG card images. If both portrait (9:16) and landscape (16:9) exist, the matching version is used automatically. Registration is shared across projects.|指定文件夹完整路径，读取名称含 Opening/Closing 的视频和 PNG/JPG 卡片图片。若同时有竖屏（9:16）及横屏（16:9），自动匹配输出比例。注册内容供所有项目共用。|폴더 전체 경로를 지정하면 이름에 Opening/Closing이 있는 영상과 PNG/JPG 카드를 읽습니다. 세로(9:16)·가로(16:9)가 모두 있으면 출력에 맞게 선택합니다. 모든 프로젝트에서 공통으로 사용합니다.
266|Opening/closing folder|片头及片尾文件夹|오프닝·클로징 폴더
267|Card duration (s)|卡片时长（秒）|카드 길이(초)
268|Register|注册|등록
269|Create transition images from video|从视频创建转场图片|영상에서 전환 이미지 만들기
270|How images are selected|图片生成方式|이미지 선택 방식
271|Used when no card images are registered. Selects representative frames by visual change, brightness and time coverage, removing black or similar frames. No video is sent to AI. Images stay in this project; meaningful highlights are not guaranteed. Generate candidates afterward to use them.|无注册卡片图时使用。按画面变化、亮度和时间分布提取代表帧，排除黑屏及相似图。不会向 AI 发送视频，仅保存在此项目中，不保证语义上的看点。创建后重新生成候选即可应用。|등록된 카드가 없을 때 사용합니다. 장면 변화·밝기·시간 분포로 대표 프레임을 고르고 검은 화면과 유사 이미지는 제외합니다. AI에 영상을 보내지 않고 이 프로젝트에만 저장하며 의미상 하이라이트는 보장하지 않습니다. 이후 후보를 생성하면 반영됩니다.
272|Previous analysis (from other presets)|之前的分析结果（其他模式）|이전 분석 결과(다른 프리셋)
273|Reuse (recommended, faster)|复用（推荐，快速）|재사용(권장, 빠름)
274|Reanalyze from scratch (for example after updating the app)|不复用，从头分析（如应用更新后）|재사용하지 않고 처음부터 분석(앱 업데이트 후 등)
275|Cost breakdown|费用明细|비용 내역
276|Rename|重命名|이름 변경
277|Save|保存|저장
278|Cancel|取消|취소
279|The storage folder name stays unchanged. New exports use this name in their filenames.|保存文件夹名称不变。此名称用于今后导出的视频文件名。|저장 폴더 이름은 바뀌지 않습니다. 이후 내보내는 영상 파일명에 사용합니다.
280|Saved in: {0}|保存位置：{0}|저장 위치: {0}
281|1. Choose media|1. 选择素材|1. 미디어 선택
282|Drag and drop video files here|将视频文件拖放到此处|여기에 동영상 파일 끌어놓기
283|MP4 / MOV / MKV / WebM, 10 s–3 h. For multicamera (P7) or screen explanation (P10: PC screen + speaker camera), add recordings of the same scene. Audio automatically aligns start offsets.|MP4 / MOV / MKV / WebM，10 秒～3 小时。多机位（P7）或屏幕讲解（P10：电脑录屏＋讲解者镜头）请添加同一场景的多个视频，通过音频自动对齐起始时间。|MP4 / MOV / MKV / WebM, 10초~3시간. 멀티카메라(P7)·화면 해설(P10: PC 화면+화자 카메라)은 같은 장면의 영상을 여러 개 추가하세요. 음성으로 시작 시각을 자동 정렬합니다.
284|Choose file|选择文件|파일 선택
285|Enter full path|输入完整路径|전체 경로 지정
286|Full video file path|视频文件完整路径|동영상 파일 전체 경로
287|Add|添加|추가
288|2. Choose preset|2. 选择模式|2. 프리셋 선택
289|Editing preset|编辑模式|편집 프리셋
290|3. Generate|3. 生成|3. 생성
291|Create 3 candidates|创建 3 个候选|후보 3개 만들기
292|Generated variations|已创建的方案|생성한 변형
293|items)|项）|개)
294|Each time you change settings and generate, a new variation is added here. Earlier variations remain.|每次更改设置并生成候选，都会新增一个方案。以前的方案保留在此处。|설정을 바꾸고 후보를 생성할 때마다 새 변형이 추가됩니다. 이전 변형은 여기에 남습니다.
295|Proper nouns in captions/overlays|字幕中的专有名词|자막의 고유 명사
296|Register display spellings, readings and recognition variants for people, places or companies. Used as hints in future transcription and for overlay matching. Existing transcripts/timing stay unchanged. Review recognition results in the review screen.|登记人名、地名、公司名的显示写法、读音及识别变体。用于下次转写提示及字幕词语匹配，不改变已有转写或时间。请在检查页面确认及修正识别结果。|인명·지명·회사명의 표시 표기·발음·인식 변형을 등록합니다. 이후 받아쓰기 힌트와 자막 단어 대조에 사용합니다. 기존 받아쓰기와 시각은 바꾸지 않으므로 검토 화면에서 인식 결과를 확인·수정하세요.
297|Add proper noun|添加专有名词|고유 명사 추가
298|Person|人名|인명
299|Place|地名|지명
300|Company/organization|企业或组织|기업·단체
301|Product/work|商品或作品|상품·작품
302|Other|其他|기타
303|Proper noun {0}|专有名词 {0}|고유 명사 {0}
304|Type|类型|종류
305|Caption/overlay spelling|字幕显示写法|자막 표기
306|Example: Alex Smith|例如：张小明|예: 김민수
307|Reading (optional)|读音（可选）|발음(선택)
308|Example: alex smith|例如：zhāng xiǎo míng|예: 김민수
309|Recognition variants (optional, comma-separated)|识别变体（可选，逗号分隔）|음성 인식 변형(선택, 쉼표 구분)
310|Example: Alec Smith, Alex Smyth|例如：张晓明、章小明|예: 김민수, 김민쑤
311|Delete proper noun {0}|删除专有名词 {0}|고유 명사 {0} 삭제
312|Nothing registered yet.|尚未注册。|아직 등록된 항목이 없습니다.
313|Proper nouns saved. ASR hints apply to transcription during the next candidate generation.|已保存专有名词。ASR 提示从下次候选生成时的转写开始生效。|고유 명사를 저장했습니다. 다음 후보 생성 시 받아쓰기부터 ASR 힌트가 적용됩니다.
314|Saving settings…|正在保存设置…|설정 저장 중…
315|Settings saved|设置已保存|설정 저장됨
316|Off (3 short candidates)|关闭（3 个短视频候选）|끔(쇼츠 후보 3개)
317|On · Split into {0} videos|开启 · 拆分为 {0} 段|켬 · {0}개로 분할
318|On · About {0} min per video|开启 · 每段约 {0} 分钟|켬 · 동영상당 약 {0}분
319|Series sends the full transcript|系列模式发送全文|시리즈는 전체 받아쓰기 전송
320|Automatic per scene|按场景自动|장면별 자동
321|Specified|指定|지정
322|On|开启|켬
323|Off|关闭|끔
324|Sound effects {0}|音效 {0}|효과음 {0}
325|Card|卡片|카드
326|Reactions {0}|吐槽 {0}|반응 {0}
327|Reactions off|吐槽关闭|반응 끔
328|{0} masked words|屏蔽词 {0} 个|가릴 단어 {0}개
329| · | · | · 
330|Included|有|있음
331|Opening {0}|片头 {0}|오프닝 {0}
332|Closing {0}|片尾 {0}|클로징 {0}
333|Transition cards {0}|转场卡片 {0}|전환 카드 {0}
334|Reuse|复用|재사용
335|Start over|从头开始|처음부터 다시
336|Media has not been analyzed yet. Either option starts analysis from scratch.|素材尚未分析，无论选择哪项都会从头分析。|아직 분석하지 않은 미디어이므로 어느 쪽이든 처음부터 분석합니다.
337|Analyzed on {0}. Reuses proxies, transcript, shot/volume/silence, laughter/face/screen detection and camera sync. AI selection, edit planning and previews run again.|此素材已于 {0} 分析。复用代理、转写、镜头/音量/静音、笑声/人脸/屏幕检测及机位同步。每次仍执行 AI 选择、编辑计划和预览。|{0}에 분석했습니다. 프록시·받아쓰기·장면/음량/무음·웃음/얼굴/화면 감지·카메라 동기화를 재사용합니다. AI 선택·편집 계획·미리 보기는 매번 수행합니다.
338|; adds about ¥{0} for transcription|，转写费用约增加 ¥{0}|. 받아쓰기 비용 약 ¥{0} 추가
339|Transcript corrections will be replaced by new recognition results.|检查页面中修正的转写将被新结果替换。|수정한 받아쓰기는 새 받아쓰기로 대체됩니다.
340|Rebuilds analysis from {0} (takes longer{1}). {2}Manual offset and speaker settings are preserved.|不使用 {0} 的分析，全部重做（较耗时{1}）。{2}保留手动偏移及说话人设置。|{0} 분석을 사용하지 않고 다시 만듭니다(시간 소요{1}). {2}수동 오프셋과 화자 설정은 유지합니다.
341|Estimated cost: about {0}|预计费用约 {0}|예상 비용 약 {0}
342| · Reuse previous result| · 复用上次结果| · 이전 결과 재사용
343|Estimated cost: about {0} (transcription {1} min{2}, AI input about {3} tokens) — {4}|预计费用约 {0}（转写 {1} 分钟{2}，AI 输入约 {3} 个令牌）— {4}|예상 비용 약 {0}(받아쓰기 {1}분{2}, AI 입력 약 {3}토큰) — {4}
344|Part 1|上篇|전편
345|Final part|下篇|후편
346|Middle part|中篇|중편
347|Part {0}|第 {0} 集|제{0}편
348|When off, generates 3 short highlight candidates.|关闭时生成 3 个精彩短视频候选。|끄면 하이라이트 쇼츠 후보 3개를 만듭니다.
349|Create {0} videos ({1}{2})|创建 {0} 段（{1}{2}）|{0}개 만들기({1}{2})
350|Create two parts|创建上、下篇|전편·후편 만들기
351|Add media to see the estimated length per video.|添加素材后显示每段时长估算。|미디어를 추가하면 동영상당 예상 길이를 표시합니다.
352|Estimated after silence cuts: {0}|去除静音后预计 {0}|무음 제거 후 예상 {0}
353|Original length {0} (shorter after silence cuts)|原时长 {0}（去除静音后会缩短）|원본 길이 {0}(무음 제거 시 단축)
354|Enter a whole number from 2 to 20 videos.|数量须为 2～20 的整数。|동영상 수를 2~20 사이 정수로 지정하세요.
355|This video ({0}) is too short to split (each part needs at least 1 minute).|此视频（{0}）太短，无法拆分（每段至少 1 分钟）。|이 영상({0})은 짧아 나눌 수 없습니다(편당 1분 이상 필요).
356|{0} parts would be under 1 minute each. This video ({1}) supports at most {2} parts.|分为 {0} 段时每段不足 1 分钟。此视频（{1}）最多可分 {2} 段。|{0}개로 나누면 편당 1분 미만입니다. 이 영상({1})은 최대 {2}개로 나눌 수 있습니다.
357|{0} videos ({1}), about {2} each (estimate: {3} split into {4}).|{0} 段（{1}），每段约 {2}（将 {3} 等分为 {4} 段的估算）。|{0}개({1}), 편당 약 {2}({3}을 {4}등분한 예상).
358|Enter a length from 1 to 60 minutes.|每段时长须为 1～60 分钟。|편당 길이를 1~60분으로 지정하세요.
359|This video ({0}) is too short for two {1}-minute parts. Reduce the length or specify the part count.|此视频（{0}）不足以分成两段 {1} 分钟。请缩短每段时长或指定数量。|이 영상({0})은 {1}분짜리 두 편에 부족합니다. 길이를 줄이거나 편수를 지정하세요.
360|About {0} videos ({1}), about {2} each (from {3}).|约 {0} 段（{1}），每段约 {2}（源于 {3}）。|약 {0}개({1}), 편당 약 {2}({3} 기준).
361|Folder selected. Confirm rights and select Register.|已选择文件夹。请确认权利并点击“注册”。|폴더를 선택했습니다. 사용 권리를 확인하고 “등록”을 누르세요.
362|Create: use video-derived transition images|创建：使用从视频提取的转场图片|만들기: 영상에서 만든 이미지를 전환에 사용
363|Do not create: next generation uses text transition cards|不创建：下次生成候选起使用文字转场卡片|만들지 않기: 다음 후보 생성부터 글자 카드 사용
364|Confirm rights to the source video.|请勾选原视频的权利确认。|원본 영상의 사용 권리를 확인하세요.
365|Enter a whole number of images from 2 to 6.|图片数量须为 2～6 的整数。|이미지 수를 2~6 사이 정수로 지정하세요.
366|Opening/closing|片头及片尾|오프닝·클로징
367|Extracting representative images from {0}…|正在从{0}提取代表图片…|{0}에서 대표 이미지 추출 중…
368|Check the usage-rights confirmation.|请勾选使用权确认。|사용 권리 확인란을 체크하세요.
369|Loading and measuring volume…|正在读取并测量音量…|읽고 음량을 측정하는 중…
370|Enhancing: {0}|正在增强画质：{0}|화질 향상 중: {0}
371|Enhancement progress|画质增强进度|화질 향상 진행 상황
372|Choose an audio file.|请选择音频文件。|음성 파일을 선택하세요.
373|Adding {0} tracks and measuring volume and energy…|正在添加 {0} 首曲目并测量音量及活跃度…|{0}곡 추가 및 음량·에너지 측정 중…
374|. Tracks not added: {0}|。未能添加的曲目：{0}|. 추가하지 못한 곡: {0}
375|Added {0} tracks; BGM set to automatic scene selection{1}|已添加 {0} 首曲目，BGM 已设为按场景自动选择{1}|{0}곡을 추가하고 BGM을 장면별 자동 선택으로 설정했습니다{1}
376|Loading {0}…|正在读取 {0}…|{0} 읽는 중…
377|Upload failed|上传失败|업로드 실패
378|Complete|完成|완료
379|Failed|失败|실패
380|Cancelled|已取消|취소됨
381|Filtering: {0}|筛选：{0}|필터링: {0}
382|Split into {0} parts|拆分为 {0} 段|{0}개로 분할
383|Split into about {0}-minute parts|每段约 {0} 分钟|약 {0}분씩 분할
384|Transition card|转场卡片|전환 카드
385|Reactions ({0})|吐槽（{0}）|반응({0})
386|{0} masked words|屏蔽词 {0} 个|가릴 단어 {0}개
387|Extra instruction: {0}|附加指示：{0}|추가 지시: {0}
388|Reanalyze from scratch|从头重新分析|처음부터 재분석
389|Generating|正在生成|생성 중
390|(From variation {0})|（源于 {0} 方案）|({0} 변형에서 파생)
391|View candidates|查看候选|후보 보기
392|View progress|查看进度|진행 상황 보기
393|Candidates {0} · Approved {1} · Exported {2}|候选 {0} · 已批准 {1} · 已导出 {2}|후보 {0} · 승인 {1} · 내보냄 {2}
394|Create another variation using {0} settings|使用 {0} 的设置创建新方案|{0} 설정으로 다른 변형 만들기
395|Create from these settings|从此设置重新创建|이 설정으로 다시 만들기
396|Delete variation {0}|删除 {0} 方案|{0} 변형 삭제
397|No variations yet. Configure above and generate candidates.|暂无方案。请在上方设置并生成候选。|아직 없습니다. 위에서 설정하고 후보를 생성하세요.
398|Settings loaded. Make changes and generate to add a new variation; the current one is kept.|已读取此方案设置。更改后生成候选即可添加新方案，现有方案保留。|설정을 불러왔습니다. 변경 후 후보를 생성하면 새 변형이 추가되며 현재 변형은 유지됩니다.
399|Deleting variation “{0}”. Its candidates, edits and exported videos will be|删除“{0}”方案。其候选、编辑内容及导出视频将被|“{0}” 변형의 후보·편집·내보낸 영상을
400|permanently (video files go to Recycle Bin). Other variations and source media remain.|，无法恢复（视频文件移至回收站）。其他方案及原素材保留。|합니다(영상 파일은 휴지통으로). 되돌릴 수 없으며 다른 변형과 원본은 유지됩니다.
401|Delete|删除|삭제
402|Keep|放弃|그만두기
403|Deleted variation “{0}”.|已删除“{0}”方案。|“{0}” 변형을 삭제했습니다.
404|Overall|整体|전체
405|Overall progress|整体进度|전체 진행 상황
406|Current task progress|当前任务进度|현재 작업 진행 상황
407| · Estimated cost about ¥{0}| · 预计费用约 ¥{0}| · 예상 비용 약 ¥{0}
408|Reanalyzing from scratch|正在从头重新分析|처음부터 재분석 중
409|Reusing previous analysis if available|若有之前的分析则复用|이전 분석이 있으면 재사용
410|Back to project|返回项目|프로젝트로 돌아가기
411|Generating videos|正在生成|생성 중
412|Elapsed|已用时|경과
413|What happens during generation|生成过程中执行的操作|생성 중 수행하는 작업
414|Azure AI Speech transcribes words with timestamps and identifies speakers.|Azure AI Speech 生成带单词时间戳的转写并区分说话人。|Azure AI Speech로 단어별 시각을 기록하고 화자를 구분합니다.
415|FFmpeg measures shot changes, volume and silence.|FFmpeg 测量镜头变化、音量和静音。|FFmpeg로 장면 전환·음량·무음을 측정합니다.
416|AI scores hook, standalone clarity, payoff and emotion, then ranks using audio energy.|AI 评估开头吸引力、独立可理解性、笑点和情感，结合音频活跃度排序。|AI가 도입·독립 이해도·마무리·감정을 평가하고 음성 에너지와 함께 순위를 매깁니다.
417|Aligns cuts to sentences, trims gaps and adds captions and emphasis overlays.|按句子边界确定片段，缩短间隔并添加字幕和强调文字。|문장 경계에 맞춰 자르고 간격을 줄여 자막과 강조 글자를 추가합니다.
418|Syncs cameras by audio, switches to the active speaker and tracks faces for portrait framing.|通过音频同步机位，切换到发言者画面，检测人脸并跟踪竖屏构图。|음성으로 카메라를 맞추고 발화자 화면으로 전환하며 얼굴을 추적해 세로로 구성합니다.
419|Adds BGM (lowered during speech) and sound effects.|添加 BGM（讲话时自动降低）和音效。|BGM(발화 시 자동 감소)과 효과음을 추가합니다.
420|Checks every candidate for duration, resolution, caption safe areas, cut-off speech and factual overlays.|自动检查所有候选的时长、分辨率、字幕安全区、发言截断和文字事实依据。|모든 후보의 길이·해상도·자막 안전 영역·발언 잘림·텍스트 근거를 자동 검사합니다.
421| · About {0} remaining for this task| · 当前任务预计剩余 {0}| · 이 작업 약 {0} 남음
422|Running {0}%|执行中 {0}%|실행 중 {0}%
423|Running|执行中|실행 중
424|Preview/QA predates edits|预览及 QA 为修改前结果|미리 보기·QA는 편집 전 결과
425|Signal-based|信号驱动|신호 기반
426|No preview|无预览|미리 보기 없음
427|(No headline)|（无标题）|(제목 없음)
428|Hook|开头吸引力|도입 흡인력
429|Standalone clarity|独立可理解性|독립 이해도
430|Payoff|笑点/结尾|마무리
431|Emotion|情感|감정
432|Audio energy|音频活跃度|음성 에너지
433|Overall|综合|종합
434|Adjustment log ({0})|调整记录（{0}）|조정 기록({0})
435|Candidate #{0}|候选 #{0}|후보 #{0}
436|Candidate {0}|候选 {0}|후보 {0}
437|{0} s of source|素材 {0} 秒|원본 {0}초
438|Review and edit this candidate|检查并修改此候选|이 후보 검토·수정
439|Make it shorter|更短一些|더 짧게
440|Use a different scene|换个场景|다른 장면
441|Find funnier moments|更有趣的片段|더 재미있는 부분
442|Find a clearer payoff|结尾更鲜明的片段|마무리가 뚜렷한 부분
443|Candidates ({0})|候选（{0} 个）|후보({0}개)
444|Back to media/preset|返回素材及模式|미디어·프리셋으로 돌아가기
445|Selection: {0} · Prompt {1} · Actual cost about ¥{2} · Time {3} s|选择：{0} · 提示词 {1} · 实际费用约 ¥{2} · 耗时 {3} 秒|선택: {0} · 프롬프트 {1} · 실제 비용 약 ¥{2} · 소요 {3}초
446|Generate other candidates|生成其他候选|다른 후보 생성
447|Additional instruction (optional)|附加指示（可选）|추가 지시(선택)
448|Additional instruction|附加指示|추가 지시
449|Regenerate|重新生成|다시 생성
450|Requested {0} videos (about {1} min each)|指定 {0} 段（每段约 {1} 分钟）|{0}개 지정(편당 약 {1}분)
451|Requested about {0} min per video|指定每段约 {0} 分钟|편당 약 {0}분 지정
452|Transcript vocabulary changes (no AI)|转写词汇变化（无 AI）|받아쓰기 어휘 변화(AI 없음)
453|Boundary: {0}|分界：{0}|분할 경계: {0}
454|Last part|最后一篇|마지막 편
455|Structure ({0})|结构（{0}）|구성({0})
456|Editing log ({0})|编辑记录（{0}）|편집 기록({0})
457|Source {0}–{1} (main content)|原视频 {0}–{1}（正文）|원본 {0}–{1}(본편)
458|Review and edit {0}|检查并修改{0}|{0} 검토·수정
459|Series ({0} videos){1}|系列（{0} 段）{1}|시리즈({0}개){1}
460|{0} · Total {1} · Chapters: {2} · Actual cost about ¥{3} · Time {4} s|{0} · 总计 {1} · 分章：{2} · 实际费用约 ¥{3} · 耗时 {4} 秒|{0} · 합계 {1} · 챕터: {2} · 실제 비용 약 ¥{3} · 소요 {4}초
461|To change boundaries, return to the project and regenerate with a different count or length. Approve parts separately and export approved parts together; metadata includes chapters and series link fields.|要调整分界，请返回项目修改数量或时长并重新生成。各篇单独批准，可批量导出已批准篇目；发布元数据包含章节及系列链接字段。|경계를 바꾸려면 프로젝트에서 편수나 길이를 바꿔 다시 생성하세요. 편별 승인 후 함께 내보낼 수 있으며 메타데이터에 챕터와 시리즈 링크가 포함됩니다.
462|Exporting|正在导出|내보내는 중
463|Reapproval needed|需重新批准|재승인 필요
464|Exported|已导出|내보냄
465|Approved|已批准|승인됨
466|Not reviewed|未检查|미검토
467|Review next unapproved video ({0})|检查下一个未批准视频（{0}）|다음 미승인 영상({0}) 검토
468| ({0} approved)|（已批准 {0} 段）| (승인 {0}개)
469|Continue to export{0}|前往导出{0}|내보내기로 이동{0}
470|Review and export progress|检查与导出进度|검토·내보내기 진행 상황
471|Review progress: {0} / {1} approved · {2} / {3} exported|检查进度：批准 {0} / {1} · 导出 {2} / {3}|검토 진행: 승인 {0} / {1}개 · 내보냄 {2} / {3}개
472|Review and approve each video in “Review & edit”, then export approved videos together.|逐个在“检查与修改”中确认并批准，再批量导出已批准视频。|“검토 및 수정”에서 하나씩 확인·승인한 후 함께 내보내세요.
473|Not approved|未批准|미승인
474|Edited after approval (reapproval required)|批准后有修改（需重新批准）|승인 후 수정됨(재승인 필요)
475|Export|导出|내보내기
476|Export {0}|导出 {0}|{0} 내보내기
477|Export progress for {0}|{0} 的导出进度|{0} 내보내기 진행 상황
478|Export approved videos|导出已批准视频|승인한 영상 내보내기
479|Check content, approve it and select videos to export (one or more). Edits invalidate approval. If any selected video is blocked, none will export. Videos export sequentially at 1080×1920.|确认内容并批准，勾选要导出的视频（可单个或多个）。批准后修改需重新批准。若任一所选视频无法导出，则全部不导出。按顺序逐个导出（1080×1920）。|내용을 확인·승인하고 내보낼 영상을 선택하세요(하나 이상). 수정하면 재승인이 필요합니다. 선택한 영상 중 하나라도 막히면 모두 내보내지 않습니다. 1080×1920으로 순서대로 처리합니다.
480|Select all approved|选择全部已批准项|승인된 항목 모두 선택
481|Export selected videos|导出所选视频|선택한 영상 내보내기
482|Batch export overall progress|批量导出整体进度|일괄 내보내기 전체 진행 상황
483|Export {0} selected videos|导出所选 {0} 段|선택한 {0}개 내보내기
484|Revoke approval|撤销批准|승인 취소
485|Approve again|重新批准|다시 승인
486|Approve this content|批准此内容|이 내용으로 승인
487|Queued for export|等待导出|내보내기 대기
488|Open exported video|打开已导出视频|내보낸 영상 열기
489|Waiting in queue.|正在排队。|순서를 기다리고 있습니다.
490|Cancel|取消|취소
491|Running: {0}|执行中：{0}|실행 중: {0}
492| · Previous {0}| · 上次的 {0}| · 이전 {0}
493|Could not export.|无法导出。|내보내지 못했습니다.
494|View reason|查看原因|이유 보기
495| Previous {0}| 上次的 {0}| 이전 {0}
496|Export cancelled. {0}|已取消导出。{0}|내보내기를 취소했습니다. {0}
497|Final QA and export results are invalidated by edits.|修改后，最终 QA 和导出结果已失效。|편집 후 최종 QA·내보내기 결과가 무효화되었습니다.
498|Exported: {0}|已导出：{0}|내보냄: {0}
499|Failed {0}|失败 {0}|실패 {0}
500|Cancelled {0}|取消 {0}|취소 {0}
501|Overall: {0} / {1} videos complete{2}|整体：已完成 {0} / {1} 段{2}|전체: {0} / {1}개 완료{2}
502|Started exporting {0} videos. Check progress here; export continues if you navigate away.|已开始导出 {0} 段。可在此查看进度，切换页面后仍会继续导出。|{0}개 내보내기를 시작했습니다. 여기에서 진행 상황을 확인하세요. 다른 화면으로 이동해도 계속됩니다.
503|Teaser|先导片段|미리 보기
504|Reaction|反应|리액션
505|Wide|远景|와이드
506|Recap|回顾|요약
507|Next-part preview|下集预告|다음 편 예고
508|Caption|字幕|자막
509|Emphasis|强调|강조
510|Hook|开头吸引点|도입
511|Headline|标题|제목
512|Reaction|吐槽|반응
513|Onomatopoeia|拟声词|의성어
514|Counter|计数器|카운터
515|Pause card|暂停卡片|정지 카드
516|Full-screen image {0}|全屏图片 {0}|전체 화면 이미지 {0}
517|Pause for {0} s|暂停 {0} 秒|{0}초 정지
518|AI suggestion|AI 建议|AI 제안
519|{0} s|{0} 秒|{0}초
520|Review {2} from {0} to {1} s (seek to start)|检查 {0}～{1} 秒的{2}（跳到起始位置）|{0}~{1}초의 {2} 확인(시작 위치로 이동)
521|(Not used)|（不使用）|(사용 안 함)
522|(No text)|（无文字）|(글자 없음)
523|Text for {0} {1}|{0} {1} 的文字|{0} {1} 글자
524|Approve|批准|승인
525|Use {0} {1}|使用 {0} {1}|{0} {1} 사용
526|Sound effects|音效|효과음
527|is automatically lowered during speech|会在讲话时自动降低音量|은(는) 발화 중 자동으로 작아집니다
528|{0} sound effects{1}. This is an older video format. Regenerate to enable per-scene music and volume controls.|{0} 个音效{1}。此视频使用旧格式，重新生成后可逐场景选曲和调节音量。|효과음 {0}개{1}. 이전 형식 영상입니다. 다시 생성하면 장면별 곡·음량 조절을 사용할 수 있습니다.
529|{0}–{1} s: lower by {2} dB|{0}～{1} 秒降低 {2} dB|{0}~{1}초: {2} dB 감소
530|Use throughout: {0}|全程使用：{0}|전체에 이 곡 사용: {0}
531|Beat-synced presets cut on musical beats, so tracks cannot be changed (volume can).|节拍同步模式按音乐节拍剪辑，不能更换曲目（可调整音量）。|비트 동기화 프리셋은 음악 박자에 맞춰 자르므로 곡은 바꿀 수 없습니다(음량 조절 가능).
532|No BGM|无 BGM|BGM 없음
533|From {0} s: {1}|从 {0} 秒起：{1}|{0}초부터 {1}
534| (recommended)|（推荐）| (권장)
535| (minimum)|（下限）| (최솟값)
536|{0} LU below voice{1}|比人声低 {0} LU{1}|음성보다 {0} LU 작게{1}
537|Same level as voice|与人声音量相同|음성과 같은 크기
538|{0} LU below voice|比人声低 {0} LU|음성보다 {0} LU 작게
539|Preview|预览|미리 보기
540|Measured ({0}, ebur128): during speech {1}|实测（{0}，ebur128）：讲话时 {1}|실측({0}, ebur128): 발화 중 {1}
541|Update preview to measure the voice/BGM difference in rendered audio.|更新预览后，将实测合成音频的人声与 BGM 音量差。|미리 보기를 갱신하면 생성한 음성에서 음성과 BGM의 차이를 실측합니다.
542|Automatically selected|自动选曲|자동 선곡
543|Your selection|您选择的曲目|직접 선택
544|Restore automatic selection|恢复自动选曲|자동 선곡으로 복원
545|Play scene from {0} s|从 {0} 秒播放场景|{0}초부터 장면 재생
546|{0} scene|{0}场景|{0} 장면
547|Track|曲目|곡
548|Track from {0}|从 {0} 开始的曲目|{0}부터의 곡
549|No BGM for this scene|此场景不使用 BGM|이 장면은 BGM 없음
550|Volume|音量|음량
551|Track volume from {0}|从 {0} 开始的曲目音量|{0}부터의 곡 음량
552|Reason: {0}|原因：{0}|이유: {0}
553|BGM timeline: {0}. Lower bands mark speech ducking.|BGM 时间线：{0}。下方色带表示讲话时降低 BGM 的位置。|BGM 흐름: {0}. 아래 띠는 발화로 BGM을 낮추는 구간입니다.
554|During speech (below voice)|讲话时（与人声的差值）|발화 중(음성과 차이)
555|Without speech|无讲话时|음성 없는 곳
556|For {0} speech regions, lowers BGM by{1} starting 0.25 s before words and keeps it lowered across gaps under 0.8 s (more for quiet voices or energetic music). At least 10 LU is a listening guideline (Torcoli et al., JAES 2019).|针对 {0} 处发言，从单词前 0.25 秒起将 BGM 降低{1}，不足 0.8 秒的间隔保持降低（低声或高能音乐处降低更多）。建议音量差至少 10 LU（Torcoli 等，JAES 2019）。|발화 {0}곳에 맞춰 단어 0.25초 전부터 BGM을{1} 낮추며 0.8초 미만 간격은 유지합니다(작은 목소리·강한 음악은 더 감소). 10 LU 이상 차이를 권장합니다(Torcoli 외, JAES 2019).
557|BGM per scene|各场景 BGM|장면별 BGM
558|Sound & BGM|声音与 BGM|소리·BGM
559|Select per scene (automatic)|按场景选择（自动选曲）|장면별 선택(자동)
560|{0} sound effects. Update preview to hear changes.|{0} 个音效。更改后更新预览即可试听。|효과음 {0}개. 변경 후 미리 보기를 갱신해 들어보세요.
561|Automatic repair available|可自动修复|자동 복구 가능
562|AI repair available|可用 AI 修复|AI 복구 가능
563|Manual correction|手动修正|수동 수정
564|Cannot repair automatically|无法自动修复|자동 복구 불가
565|Cut {0}|剪辑 {0}|컷 {0}
566|Seek to {0} {1}|跳转至 {0} {1}|{0} {1} 위치로 이동
567|Repaired {0} findings|已修复 {0} 项|{0}개 복구 완료
568|No findings could be repaired|未能修复任何项目|복구한 항목이 없습니다
569| ({0} not repaired)|（未修复 {0} 项）| (복구 못함 {0}개)
570|Undo|撤销|되돌리기
571|Rule|规则|규칙
572|Needs approval|需批准|승인 필요
573|AI-rewritten text is not approved. Review and approve it.|AI 改写内容尚未批准，请检查后批准。|AI가 다시 쓴 문구는 미승인입니다. 내용을 확인하고 승인하세요.
574|{0} findings|{0} 项|{0}개
575|Why some findings were not repaired|未修复的原因|복구하지 못한 이유
576|{0} unrepaired findings|未修复的 {0} 项|복구하지 못한 항목 {0}개
577|Captions preserve spoken words, hesitations, endings and repetition; only timing, splitting and merging change (except configured masks). AI repairs presentation text/cards and requires approval. Checks rerun after each change, retaining only changes that do not worsen other checks.|字幕保留原话、停顿、语尾及重复，仅调整显示时间、拆分与合并（已配置屏蔽词除外）。AI 优化特效字幕和卡片，修改后需批准。每次更改都重新检查，仅保留不使其他项目变差的改动。|자막은 말한 단어·머뭇거림·어미·반복을 유지하고 시간·분할·결합만 고칩니다(설정한 가림 제외). 연출 자막·카드는 AI가 수정하고 승인이 필요합니다. 매번 재검사하여 다른 항목을 악화하지 않는 변경만 남깁니다.
578|Approved · {0}|已批准 · {0}|승인됨 · {0}
579|Repairing…|正在修复…|복구 중…
580|Repair this check ({0} findings)|修复此项（涉及 {0} 处）|이 항목 복구({0}개 대상)
581|Repair this check|修复此项|이 항목 복구
582|Repair progress for this check|此项修复进度|이 항목의 복구 진행 상황
583|Recommended correction|推荐修正|권장 수정
584|to preserve wording; |为保留原话，|말을 바꾸지 않기 위해 
585|{0} cannot be repaired automatically ({1}remain warnings)|{0} 项无法自动修复（{1}仍保留警告）|{0}개 자동 복구 불가({1}경고 유지)
586|{0} (target {1}{2})|{0}（标准 {1}{2}）|{0}(기준 {1}{2})
587| ({0} checks)|（{0} 项检查）| ({0}개 항목)
588|Auto-repair with Copilot|使用 Copilot 自动修复|Copilot으로 자동 복구
589|Automatic repair progress|自动修复进度|자동 복구 진행 상황
590|{0} checks, {1} findings targeted. |涉及 {0} 项检查、{1} 处问题。|{0}개 검사·{1}개 문제 대상. 
591|No automatically repairable findings. |没有可自动修复的项目。|자동 복구 가능한 항목이 없습니다. 
592|AI correction sends overlay text and surrounding transcript to {0}.|AI 修正会将字幕文字及前后转写发送至 {0}。|AI 수정 시 자막 글자와 전후 받아쓰기를 {0}(으)로 전송합니다.
593|LLM not configured: repairing only rule-based findings.|未设置 LLM，仅修复可按规则处理的项目。|LLM 미설정으로 규칙으로 고칠 수 있는 항목만 복구합니다.
594|All automatic checks pass.|所有自动检查均已通过。|모든 자동 검사를 통과했습니다.
595|Passed / not run: {0}|通过及未执行 {0} 项|통과·미실시 {0}개
596|Automatic checks|自动检查|자동 검사
597|Passed {0}|通过 {0}|통과 {0}
598|Approve all{0}|批量批准{0}|일괄 승인{0}
599|Approved {0} / {1} checks|已批准 {0} / {1} 项|승인 {0} / {1}개
600|Approval records that results were reviewed. Failures/warnings remain; export requirements and text/card approvals do not change. Edits or changed results require reapproval. Checks not run are excluded.|批准仅记录已检查结果，失败及警告仍保留，不改变导出条件或字幕、卡片批准。编辑或结果改变后需重新批准。未执行检查不在此范围。|승인은 결과를 확인했다는 기록입니다. 실패·경고는 남으며 내보내기 조건과 자막·카드 승인은 바뀌지 않습니다. 편집·결과 변경 후 재승인이 필요하고 미실시는 제외합니다.
601|Finding positions (video time)|问题位置（视频时间）|문제 위치(영상 시간)
602|approval saved|批准已保存|승인을 저장했습니다
603|approval revoked|批准已撤销|승인을 취소했습니다
604|{0} checks: {1}.|{0} 项检查：{1}。|{0}개 항목의 {1}.
605|‹ Previous video|‹ 上一个视频|‹ 이전 영상
606|Next video ›|下一个视频 ›|다음 영상 ›
607|Switch video|切换视频|영상 전환
608|Video {0} / {1}|视频 {0} / {1}|영상 {0} / {1}
609|Choose video to review|选择要检查的视频|검토할 영상 선택
610|Preview is outdated because content changed.|内容已修改，预览已过时。|내용이 변경되어 미리 보기가 오래되었습니다.
611|Main {0} · Total {1}|正文 {0} · 总计 {1}|본편 {0} · 전체 {1}
612|Structure / chapters ({0})|结构与章节（{0}）|구성·챕터({0})
613| ×{0} speed| ×{0} 倍速| ×{0}배속
614| · Face tracking| · 跟踪人脸| · 얼굴 추적
615| · Screen (zoom to action)| · 屏幕（放大操作处）| · 화면(조작 위치 확대)
616| · Screen (full)| · 屏幕（全景）| · 화면(전체)
617| · Picture-in-picture: {0}| · 画中画：{0}| · 화면 속 화면: {0}
618|{0} {1}{2} {3} s{4}{5}{6}{7}|{0} {1}{2} {3} 秒{4}{5}{6}{7}|{0} {1}{2} {3}초{4}{5}{6}{7}
619|Word {0}|单词 {0}|단어 {0}
620|Series boundaries link adjacent parts and cannot be moved here. Regenerate with a different count or length.|系列分界与相邻篇目关联，无法在此移动。请修改数量或时长后重新生成。|시리즈 경계는 앞뒤 편과 연결되어 여기서 옮길 수 없습니다. 편수나 길이를 바꿔 다시 만드세요.
621|Adjust segment (by sentence)|调整区间（按句子）|구간 조정(문장 단위)
622|Start one sentence earlier|开始提前一句|시작을 한 문장 앞으로
623|Start one sentence later|开始延后一句|시작을 한 문장 뒤로
624|End one sentence earlier|结束提前一句|끝을 한 문장 앞으로
625|End one sentence later|结束延后一句|끝을 한 문장 뒤로
626|Edits invalidated final QA and exports. Reapprove and export the current content.|修改使最终 QA 及导出结果失效。请重新批准并导出当前内容。|편집으로 최종 QA·내보내기 결과가 무효화되었습니다. 현재 내용으로 재승인·내보내기 하세요.
627|Approve and review next ({0})|批准并检查下一个（{0}）|승인 후 다음 영상({0})
628|Approve and continue to export|批准并前往导出|승인 후 내보내기로
629|Approve and return to candidates|批准此内容并返回候选|이 내용으로 승인 후 후보로
630|Export only this video now (1080×1920)|立即仅导出此视频（1080×1920）|이 영상만 지금 내보내기(1080×1920)
631|Export approved videos together in step 6. Editing after approval requires reapproval.|可在“6 导出”中批量导出已批准视频。批准后修改需重新批准。|승인한 영상은 “6 내보내기”에서 함께 내보낼 수 있습니다. 승인 후 수정하면 재승인이 필요합니다.
632|Approve only and return to candidates|仅批准并返回候选|승인만 하고 후보로
633|Approve and export this content (1080×1920)|批准并导出此内容（1080×1920）|승인 후 내보내기(1080×1920)
634|Fix failing checks to export (Automatic checks on the right)|修复未通过项目后可导出（右侧“自动检查”）|실패 항목을 고치면 내보낼 수 있습니다(오른쪽 자동 검사)
635|Approve all reactions|批准全部吐槽|모든 반응 승인
636|Approve all cards|批准全部卡片|모든 카드 승인
637| ({0} caption lines)|（字幕 {0} 行）| (자막 {0}줄)
638|Speaker {0} · {1}–{2} s “{3}”|说话人 {0} · {1}～{2} 秒“{3}”|화자 {0} · {1}~{2}초 “{3}”
639|No source speech specified|未指定依据发言|근거 발언 미지정
640|Speaker {0} · {1} s {2}|说话人 {0} · {1} 秒 {2}|화자 {0} · {1}초 {2}
641|Source speech|依据发言|근거 발언
642|Source speech {0}|依据发言 {0}|근거 발언 {0}
643|Select…|请选择|선택하세요
644|Review {0}–{1} s (seek to start)|检查 {0}～{1} 秒（跳到开始）|{0}~{1}초 확인(시작으로 이동)
645|Overlay {0}|字幕 {0}|자막 {0}
646|Visible|显示|표시
647|Back to candidates|返回候选|후보로 돌아가기
648|Update preview|更新预览|미리 보기 갱신
649|{0}Length {1} · Cuts {2} · Overlays {3}|{0}时长 {1} · 剪辑 {2} · 字幕 {3}|{0}길이 {1} · 컷 {2} · 자막 {3}
650|Cut list (video / speed)|剪辑列表（画面与速度）|컷 목록(영상·속도)
651|Correct transcription|修正转写（识别错误）|받아쓰기 수정(인식 오류)
652|Words used in this video. Changes rebuild captions without changing timing; manual caption edits will be replaced.|这些是此视频所用范围的单词。修改后重建字幕，时间不变，手动修改过的字幕将被替换。|이 영상에 쓰인 단어입니다. 수정하면 시각은 유지하고 자막을 다시 만듭니다. 수동 자막 수정은 대체됩니다.
653|Text overlays & transitions|字幕与转场|자막·장면 전환
654|Editing text reruns length, speed, line-break and safe-area checks. Aim for {0} full-width characters per line{1} and {2} caption characters/s. Cards pause full-screen for 2–3 s with text readable in 1 s (up to 20 characters). AI text needs approval before export. Update preview to check card edits.|修改文字后立即重新检查字数、显示速度、换行禁则及安全区。每行建议不超过 {0} 个全角字{1}，字幕每秒不超过 {2} 字。卡片全屏暂停 2～3 秒，文字须能在 1 秒内读完（最多 20 字）。AI 文字须批准后才能导出。更新预览以检查卡片修改。|글자 수정 시 길이·속도·줄바꿈·안전 영역을 재검사합니다. 줄당 전각 {0}자{1}, 자막 초당 {2}자가 기준입니다. 카드는 2~3초 전체 화면 정지, 1초에 읽을 수 있는 20자 이내로 합니다. AI 글자는 내보내기 전 승인이 필요합니다. 카드 수정은 미리 보기 갱신으로 확인하세요.
655|Time|时间|시각
656|Text|文本|텍스트
657|Characters|字数|글자 수
658|Speed|速度|속도
659|Creating preview|正在创建预览|미리 보기 생성 중
660|Preview creation progress|预览创建进度|미리 보기 생성 진행 상황
661| · Preview/QA predates edits| · 预览及 QA 为修改前结果| · 미리 보기·QA는 편집 전 결과
662|Export ({0} videos)|导出（{0} 段）|내보내기({0}개)
663|Select approved videos for batch export. Review and approve remaining videos in “Review & edit”.|勾选已批准的视频进行批量导出。其余视频请先在“检查与修改”中确认并批准。|승인한 영상을 선택해 함께 내보내세요. 미승인 영상은 “검토 및 수정”에서 확인·승인하세요.
664|Copied. Press Ctrl+V at the destination.|已复制。请在目标位置按 Ctrl+V 粘贴。|복사했습니다. 붙여넣을 곳에서 Ctrl+V를 누르세요.
665|Clipboard unavailable. Select the text and press Ctrl+C.|无法复制到剪贴板。请选择文本并按 Ctrl+C。|클립보드에 복사하지 못했습니다. 텍스트를 선택하고 Ctrl+C를 누르세요.
666|Checking exported files…|正在检查导出文件…|내보낸 파일 확인 중…
667|Reload exported files|重新读取成果文件|결과 파일 다시 읽기
668|Final video|完成视频|완성 영상
669|Captions (SRT)|字幕（SRT）|자막(SRT)
670|Captions/effects (ASS)|字幕及特效（ASS）|자막·연출(ASS)
671|Edit plan (JSON)|编辑计划（JSON）|편집 계획(JSON)
672|Posting metadata|发布元数据|게시 메타데이터
673|Post text data (JSON)|发布文案数据（JSON）|게시 글 데이터(JSON)
674|Video file not found. Check the storage location.|未找到视频文件，请检查保存位置。|영상 파일이 없습니다. 저장 위치를 확인하세요.
675|Thumbnail candidate {0}|缩略图候选 {0}|썸네일 후보 {0}
676|Open|打开|열기
677|File not found|未找到文件|파일을 찾을 수 없음
678|Preview contents|预览内容|내용 미리 보기
679|Use the video player above.|可在上方视频播放器中查看。|위 영상 플레이어에서 확인할 수 있습니다.
680|No preview for this format. Use Open or Save.|此格式不支持预览，请使用“打开”或“保存”。|이 형식은 미리 보기가 없습니다. “열기” 또는 “저장”을 사용하세요.
681|Exported video preview|导出视频预览|내보낸 영상 미리 보기
682|This is the saved final video, not the low-resolution editing preview.|这是已保存的完成视频，并非低分辨率编辑预览。|편집용 저해상도 미리 보기가 아니라 저장된 완성 영상입니다.
683|Video file location|视频保存位置|영상 파일 저장 위치
684|Copy path|复制路径|경로 복사
685|Open folder|打开文件夹|폴더 열기
686|Posting text (copy and paste)|发布文案（复制粘贴）|게시용 글(복사·붙여넣기)
687|Other exported files ({0}) and previews|其他成果文件（{0} 项）及预览|기타 결과 파일({0}개)·미리 보기
688|Requested File Explorer to open the output folder.|已请求在资源管理器中打开保存位置。|파일 탐색기에 저장 폴더 열기를 요청했습니다.
689|Exported final video|已导出的完成视频|내보낸 완성 영상
690|Video playback failed. Check file availability/format, then reload exported files.|视频播放失败。请检查文件是否存在及格式是否支持，然后重新读取成果文件。|영상을 재생하지 못했습니다. 파일·지원 형식을 확인하고 결과 파일을 다시 읽으세요.
691|Image failed to load. Use Open to inspect the saved file.|图片读取失败。请点击“打开”检查保存的文件。|이미지를 읽지 못했습니다. “열기”로 저장된 파일을 확인하세요.
692|Loading…|正在读取…|읽는 중…
693|Showing the first 65,536 characters. Use Open or Save for the full file.|仅显示前 65,536 个字符。全文请使用“打开”或“保存”查看。|처음 65,536자만 표시합니다. 전체는 “열기” 또는 “저장”으로 확인하세요.
694|Post text has not been generated, or its file is missing.|发布文案尚未生成，或文件不存在。|게시 글이 아직 생성되지 않았거나 파일이 없습니다.
695|Create posting text|生成发布文案|게시용 글 만들기
696|Loading posting text…|正在读取发布文案…|게시용 글 읽는 중…
697|Copy|复制|복사
698|AI draft based on the transcript; numbers/names absent from speech are removed automatically. |AI 根据转写生成的草稿，自动排除发言中没有的数字和名称。|받아쓰기를 바탕으로 AI가 만든 초안이며 발언에 없는 숫자·이름은 자동 제거합니다. 
699|Template-based draft. |基于模板生成的草稿。|서식으로 만든 초안입니다. 
700|YouTube description|YouTube 说明|YouTube 설명
701|Paste into the description. The first 1–2 lines appear before “Show more”.|可直接粘贴到说明栏。前 1～2 行显示在“展开”之前。|설명란에 붙여넣으세요. 처음 1~2줄은 “더보기” 앞에 표시됩니다.
702|Viva Engage post|Viva Engage 帖子|Viva Engage 게시 글
703|Internal post text. Attach the video and paste.|面向公司内部的文案，请附上视频后粘贴。|사내 게시용 글입니다. 영상을 첨부하고 붙여넣으세요.
704|Chapters (timecodes)|章节（时间码）|챕터(타임코드)
705|Actual exported-video times, including openings and cards. Paste into YouTube’s description to create chapters.|使用导出视频的实际时间，包含片头及卡片。放入 YouTube 说明栏可创建章节。|오프닝·카드를 포함한 실제 내보낸 영상 시각입니다. YouTube 설명란에 넣으면 챕터가 됩니다.
706|No chapters: YouTube requirements are not met (start at 00:00, at least 3 chapters, at least 10 s each).|不添加章节：未满足 YouTube 条件（从 00:00 开始，至少 3 章，每章至少 10 秒）。|챕터 없음: YouTube 조건(00:00 시작·3개 이상·각 10초 이상)을 충족하지 않습니다.
707|{0}Showing saved text. Review before posting and make corrections at the destination.|{0}显示已保存文案。发布前请确认内容，在粘贴目标处修改。|{0}저장된 글을 표시합니다. 게시 전에 확인하고 붙여넣을 곳에서 수정하세요.
708|Create another draft|生成其他草稿|다른 초안 만들기
709|Open post text data (JSON)|打开发布文案数据（JSON）|게시 글 데이터(JSON) 열기
710|{0} characters{1}|{0} 字符{1}|{0}자{1}
711|Creating another draft…|正在生成其他草稿…|다른 초안 생성 중…
712|Back to candidates (review other videos)|返回候选（检查其他视频）|후보로 돌아가기(다른 영상 검토)
713|Export list|导出列表|내보내기 목록
714|Automatic checks: {0}/{1} hard constraints passed (ffprobe / loudnorm / measurements)|自动检查：硬性约束通过 {0}/{1}（ffprobe / loudnorm / 实测验证）|자동 검사: 필수 제약 {0}/{1} 통과(ffprobe / loudnorm / 실측 검증)
715|Export complete|导出完成|내보내기 완료
716|Automatic checks (QA)|自动检查（QA）|자동 검사(QA)
717|Export cancelled|已取消导出|내보내기 취소됨
718|Back to review & edit|返回检查与修改|검토 및 수정으로 돌아가기
719|The edit plan changed. Approval, final QA and exports no longer apply. Reapprove and export again.|编辑计划已更改，批准、最终 QA 及导出结果不再适用。请重新批准并导出。|편집 계획이 바뀌어 승인·최종 QA·내보내기 결과를 사용할 수 없습니다. 재승인 후 다시 내보내세요.
720|Export result invalidated|导出结果已失效|내보내기 결과 무효
721|Export failed|无法导出|내보내기 실패
722|Exporting video|正在导出|내보내는 중
723|Export progress|导出进度|내보내기 진행 상황
724|1080×1920 / H.264 / AAC 48 kHz. Two-pass volume normalization to −14 LUFS; automatic checks after export.|1080×1920 / H.264 / AAC 48kHz。使用两遍处理将音量调整至 −14 LUFS，导出后自动检查。|1080×1920 / H.264 / AAC 48kHz. 2패스로 −14 LUFS 정규화 후 내보낸 결과를 자동 검사합니다.
`;
