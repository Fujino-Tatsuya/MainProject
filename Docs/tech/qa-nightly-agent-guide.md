# 야간 QA 에이전트 가이드 — 팀원 PC 공유 양식 (할 일 120 후속)

> **대상: 팀원 PC 에서 야간 QA 를 맡은 AI 에이전트.** 이 문서만 읽고 위에서부터 차례로 따라 한다.
> 결과물 = 공유 날짜 폴더의 **하이라이트 클립**(코드가 저장) + **종합 리포트 `report-<이름>.md`**(네가 쓴다).
> 세부 동작·설정 목록은 [qa-nightly-runbook.md](qa-nightly-runbook.md)(런북), OBS 사실은 [qa-nightly-recording-research.md](qa-nightly-recording-research.md).
> 사용자와는 **한국어**로 이야기한다.

## 0. 용어
| 말 | 뜻 |
|---|---|
| 이름 | 팀원 이름 = 시드 파일 `QANightly/nightly-seed.json` 의 `name`(영문·숫자·`-`·`_`). 공유 파일 이름 앞에 붙는다. 칸은 이것 하나다. |
| 공유 루트 | `<Team-Vault>\MainProject-Vault\QA_LogAndVideo` — Syncthing 으로 팀 전체에 동기화되는 폴더. PC 마다 Team-Vault 위치가 다르다. |
| 날짜 폴더 | `<공유 루트>\<MM-dd>\`(연도 없음). 날짜 = **QA 배치를 시작한 날**(자정을 넘겨도 한 폴더). |
| 배치 폴더 | 로컬 `C:/UnityProject/QA/N108-<yyyyMMdd_HHmm>-<이름>/` — 원본 로그·CSV·report.html. **공유하지 않는다.** |
| progress.txt | 배치 폴더의 진행 파일(`key=value` 줄). 끝나면 완료 키 `end=…` 가 생긴다. |

## 1. 공유 루트 경로 받기 (처음 한 번)
1. 사용자에게 묻는다: "Team-Vault 폴더가 어디 있나요? (예: `C:\Team-Vault`) — 그 아래 `MainProject-Vault\QA_LogAndVideo` 를 공유 루트로 씁니다."
2. 받은 경로로 `<Team-Vault>\MainProject-Vault\QA_LogAndVideo` 가 **있는지 확인**한다. 없으면 Syncthing 동기화가 안 된 것 — 사용자에게 알리고 멈춘다(마음대로 만들지 않는다).
3. 저장 — 둘 중 하나(둘 다 있으면 환경변수가 이긴다):
   - **파일(권장)**: 레포의 `UserSettings/QANightly.local.json`(gitignore 경로 — 커밋되지 않는다).
     없으면 Unity 메뉴 `Tools/QA/Nightly/Settings/Create Local Settings File` 로 만들거나 아래 서식으로 직접 쓴다. 칸:
     ```json
     {
       "obsEnabled": true,
       "obsHost": "127.0.0.1",
       "obsPort": 4455,
       "obsPassword": "",
       "shareRoot": "D:\\Team-Vault\\MainProject-Vault\\QA_LogAndVideo",
       "videoKeepDays": 7,
       "replayOnly": true,
       "replaySeconds": 0,
       "replayAfterSeconds": 10,
       "replayCooldownSeconds": 30,
       "clipEvents": ""
     }
     ```
     (JSON 이라 `\` 는 `\\`. 이미 파일이 있으면 `shareRoot`·`obsEnabled` 만 바꾸고 나머지 칸은 그대로 둔다.)
   - **환경변수**: `setx QA_NIGHTLY_SHARE_ROOT "D:\Team-Vault\MainProject-Vault\QA_LogAndVideo"` 와 `setx QA_NIGHTLY_OBS 1`.
4. OBS 비밀번호는 파일보다 환경변수 `setx QA_OBS_PASSWORD "<비밀번호>"` 를 권한다(사용자에게 받는다. 대화·로그·커밋에 남기지 않는다).
5. 확인: Unity 메뉴 `Tools/QA/Nightly/Status` → 콘솔 `PC 설정 — 녹화 리플레이 버퍼(클립만) … 공유 루트 <경로> (공유 모드 — …) · 지금 시작하면 공유 날짜 폴더 <루트>\<MM-dd>`.
   - 공유 루트가 있으면 **공유 모드**: 녹화는 리플레이 클립만으로 고정된다(판 전체 녹화 안 함).

## 2. 사전 준비 (밤마다 시작 전 — 사람 손이 필요한 것은 사용자에게 부탁한다)
1. **OBS 설정(처음 한 번, 사용자 손)** — OBS 를 켠 채 아래를 바꾼 뒤 **OBS 를 재시작**해야 먹는다:
   - 설정 → 출력 → 출력 모드 **단순** → 녹화 품질 **"스트림과 같음"**, 방송 비트레이트 **2500 Kbps**, 인코더 하드웨어.
   - 설정 → 출력 → **리플레이 버퍼 사용** 체크, 최대 리플레이 시간 60초 이상(이벤트 뒤 10초 + 쿨다운 30초보다 길게).
   - 도구 → WebSocket 서버 설정 → **WebSocket 서버 사용**, 포트 4455.
   - 🔴 **녹화 품질·리플레이 버퍼 사용 체크는 OBS 를 켠 채 바꾸면 반영이 안 되거나 출력이 깨진다** → 바꿨으면 반드시 OBS 재시작.
2. **캡처 소스 = 게임이 뜨는 모니터** — 장면에 디스플레이 캡처(메인 에디터 Game 뷰와 가상 플레이어 창 2개가 보이는 모니터).
3. **OBS 설정 창은 닫아 둔다** — 열려 있으면 리플레이 버퍼 시작 요청이 성공만 하고 실제로 안 켜진다.
4. **OBS 녹화·리플레이 버퍼를 손으로 켜 두지 않는다** — 켜져 있으면 코드가 녹화 경로를 날짜 폴더로 못 바꾼다(그래도 클립은 날짜 폴더로 옮겨지지만 경고가 남는다).
5. 절전·화면 보호기 끄기, 에디터 끄기 → SVN 업데이트(`--non-interactive --accept postpone`) → GameData.xlsx 리비전 적어 두기 → 에디터 켜기 — 런북 §0~§4 그대로.
6. 메뉴 `Tools/QA/Nightly/Settings/Test OBS Connection` → `OBS 연결 OK`.

## 3. QA 시작
1. `Tools/QA/Nightly/Settings/Validate Seed File` → `시드 파일 OK — <이름> …`(이름이 사용자 이름인지 본다).
2. **`Tools/QA/Nightly/Start Night`**(MCP: `EditorApplication.ExecuteMenuItem("Tools/QA/Nightly/Start Night")`). 나머지는 코드가 자동:
   - 날짜 폴더 `<공유 루트>\<MM-dd>\` 만들기(있으면 그대로) · 지난 내 클립 정리(`videoKeepDays`).
   - OBS 녹화 경로를 날짜 폴더로(`SetRecordDirectory` — 원래 경로는 PC 파일에 보관, 배치 끝에 되돌린다).
   - 리플레이 버퍼 시작 → **실제로 켜졌는지 확인**.
3. 시작 확인 — 배치 폴더 `logs/loop.log`(또는 콘솔 `[QA108/Nightly]`)에서:
   - `OBS 녹화 경로 → 공유 날짜 폴더 <경로>` 또는 `OBS 녹화 경로 = 공유 날짜 폴더(이미)`.
   - `리플레이 버퍼 시작·확인(active)`.
   - `⚠ 리플레이 버퍼가 실제로 안 켜졌다` 가 보이면: OBS 설정 창이 열려 있거나 리플레이 버퍼 사용이 꺼져 있다 → 사용자에게 알린다(설정 창 닫기 / 체크 후 OBS 재시작). QA 는 계속 돌고 판 시작마다 다시 시도한다.
   - `⚠ OBS … 중이라 녹화 경로를 바꾸지 않았다` — OBS 출력이 미리 켜져 있었다. 클립은 날짜 폴더로 옮겨지니 그대로 둔다(리포트에 적는다).
4. 🔴 돌고 있는 동안 스크립트 수정·Refresh·`isPlaying` 직접 조작 금지.

## 4. 완료 감시 (progress.txt)
- 경로: Start Night 콘솔 줄 `폴더 C:/UnityProject/QA/N108-…` 의 `progress.txt`. 모르면 `C:/UnityProject/QA/` 에서 `N108-*-<이름>` 중 이름순 마지막.
- **폴링: 5분 간격**으로 파일을 읽는다(읽기만 — 쓰지 않는다).
- 끝 판정: `end=done` 또는 `end=aborted` 줄이 있으면 끝. 이 키는 루프가 정리(남은 클립 저장·리플레이 버퍼 정지·OBS 경로 되돌리기)를 **다 마친 뒤 한 번에** 쓰므로, 보이면 바로 리포트를 써도 된다.
  | 키 | 쓰는 곳 |
  |---|---|
  | `end` · `end_reason` · `end_at` | 결과 요약(완료/중단과 사유) |
  | `batch_dir` | 로그 분석 원본 위치 |
  | `share_dir` | 리포트를 쓸 날짜 폴더 |
  | `nightly_name` | 이름 |
  | `runs_done` · `runs_total` · `runs_error` | 판 수 |
  | `report_name` | 제안 리포트 이름(§5 규칙으로 다시 확인) |
- 아직 아니면 `state`·`run`(지금 판)·`updated`(마지막 변경 시각)를 본다. 한 판은 보통 15~45분.
- **타임아웃**: `updated` 가 **60분 넘게 안 바뀌면** 루프가 멈췄거나 에디터가 죽은 것 — 사용자에게 알린다(에디터 프로세스 확인, 대화상자 확인). 전체 상한은 `runs_total × 50분 + 30분`; 넘으면 끝난 판까지로 리포트를 쓰고 "미완료"로 적는다.
- `end=aborted` 사유가 `에디터 재시작` 이면 OBS 정리(버퍼 정지·경로 되돌리기)를 못 한 것 — 리포트에 적고 사용자에게 OBS 녹화 경로 확인을 부탁한다.

## 5. 로그 분석 → 리포트 작성
### 5.1 읽을 것 (전부 로컬 배치 폴더 `batch_dir`)
| 파일 | 무엇 |
|---|---|
| `batch.txt` | 머리 — `git_branch`·`git_commit`·`xlsx_svn_rev`·`data_source_mode`·`started_at`·`nightly_*`·`record`·`share_dir` |
| `runs.csv` | 판 결과(판·재실행·결과·데이터 출처 `data_source`·오류 사유) |
| `nightly-summary.md` | 코드가 만든 로컬 요약(판 결과·클립·이벤트 시각표) — 출발점으로 좋다 |
| `events.tsv` | 이벤트 시각표(탭: 판·재실행·이벤트·시각·게임 시각·영상 타임코드·상세) |
| `clips.txt` | 클립(탭: 판·재실행·경로·이벤트/메모) — 경로가 날짜 폴더의 `<이름>_<HHmmss>_r<판>_<이벤트>.mp4` |
| `logs/loop.log` | 루프 로그. `⚠` 줄 = 경고(OBS·클립·요약) |
| `logs/notes.txt` | 리포트 루프 메모(연속 오류 휴식·OBS 경고) |
| `C:/UnityProject/QA/48-*/report.md` | 판마다 시나리오 리포트(맵 직행·자리별 저장 id·발견 항목 — 할 일 130 부터 로비 점검 없음) — `started_at` 이후 폴더 |
- 지난 리포트 대비: 공유 루트의 이전 날짜 폴더에서 **내 이름** 리포트(`report-<이름>*.md`) 중 가장 최근 것.

### 5.2 파일 이름 (번호 매기기)
- 날짜 폴더 = `share_dir`(바로 아래, 하위 폴더 만들지 않음).
- `report-<이름>.md`. 이미 있으면 `report-<이름>-2.md`, 그것도 있으면 `-3` … (비어 있는 가장 작은 번호). `report_name` 은 제안값 — **쓰기 직전에 날짜 폴더를 다시 보고** 정한다.
- 이미 있는 리포트는 **덮어쓰지 않는다**(같은 날 두 번째 배치 = 새 번호).
- `share_dir` 가 비어 있으면(공유 안 함) 배치 폴더에 `report-<이름>.md` 로 쓰고 사용자에게 알린다.

### 5.3 템플릿
```markdown
# 야간 QA 리포트 — <이름> · <MM-dd>

| 항목 | 값 |
|---|---|
| 날짜(배치 시작) | <started_at> |
| 끝 | <end> · <end_at> · <end_reason> |
| 이름 | <이름> |
| 브랜치 / 커밋 | <git_branch> / <git_commit> |
| GameData.xlsx SVN 리비전 | <xlsx_svn_rev> (사전 준비에서 적은 값과 같은가: 예/아니오) |
| 데이터 출처 | 툴바 <data_source_mode> · 런타임 테이블 <n>/<판 수>판 |
| 판 수 | 완료 <runs_done> / 계획 <runs_total> · 최종 오류 <runs_error> |
| 녹화 | 리플레이 클립 <개수>개 · OBS 경고 <있음/없음> |

## 결과 요약
| 판 | 재실행 | 결과 | 데이터 출처 | 이벤트 |
|---|---|---|---|---|
| 1 | 0 | Clear | table | death×1 |

## 실패·오류 판
- 판 <n>(재실행 <0/1>): <결과/오류 사유> — 원인 추정: <로그 근거 한 줄(loop.log·events.tsv 줄 인용)>

## 하이라이트 클립
| 파일 | 판 | 이벤트 | 시각 |
|---|---|---|---|
| <이름>_<HHmmss>_r<판>_<이벤트>.mp4 | <판> | <이벤트> | <HH:mm:ss> |

## 발견 이슈
| # | 내용 | 재현(판·시각·클립) | 심각도 |
|---|---|---|---|
| 1 | <무엇이 잘못됐나> | 판 3 · 23:41:05 · <클립 파일명> | 높음/중간/낮음 |

## 지난 리포트 대비
- 비교 대상: <지난 리포트 파일명 또는 "없음(첫 리포트)">
- 바뀐 점: <클리어율·오류 판·새로 생긴/사라진 이슈>

## 원본 로그 (로컬 — 공유 안 함)
- 배치 폴더: <batch_dir>
- 48 리포트: C:/UnityProject/QA/48-… (판별)
```
- 심각도: **높음** = 크래시·예외 폭주·판 진행 불가·데이터 출처 틀림 / **중간** = 끼임·정체·비정상 전멸 / **낮음** = 표시·연출.
- 판정은 로그 근거로만. 추측은 "추정" 이라고 쓴다. 클립이 없는 이벤트는 "클립 없음".

## 6. 끝나고 보고
- 사용자에게: 리포트 파일 경로, 판 결과 한 줄, 높음 이슈, OBS 경고(있으면).
- OBS 녹화 경로가 되돌려졌는지: `loop.log` 의 `OBS 녹화 경로 되돌림 — <원래 경로>`. 실패 경고면 사용자에게 OBS 설정 → 출력 → 녹화 경로 확인을 부탁한다.

## 7. 하지 말 것
- 🔴 **다른 사람 파일을 만들거나 고치거나 지우지 않는다** — 날짜 폴더에서는 `<내 이름>_…` 클립과 `report-<내 이름>[-N].md` 만 쓴다(Syncthing 충돌).
- 🔴 **원본 로그·CSV·report.html·nightly-summary.md·Editor.log 를 공유 폴더로 복사하지 않는다** — 공유 폴더 = 클립 + 리포트만.
- 🔴 **push·병합 금지.** `qa/*` 브랜치는 `development` 로 병합하지 않는다. 팀원 브랜치 `qa/Nightly-<이름>` 은 `qa/NightlyBase` 에서 만들고 시드 파일만 바꾼다(런북 §8, 지시 있을 때만).
- 공유 루트·날짜 폴더 구조를 바꾸지 않는다(하위 폴더 만들기 금지). 지난 날짜 폴더 정리는 코드(`videoKeepDays`)에 맡긴다.
- 돌고 있는 동안 에디터 스크립트 수정·Refresh·Play 조작 금지. OBS 설정(특히 녹화 품질)을 OBS 켠 채 바꾸지 않는다.
- 비밀번호를 리포트·대화·커밋에 쓰지 않는다.
