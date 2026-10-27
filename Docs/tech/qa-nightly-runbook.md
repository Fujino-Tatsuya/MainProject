# 야간 QA 실행 런북 — 팀원 에이전트용 (할 일 108)

> 이 문서만 보고 팀원의 에이전트(또는 사람)가 밤새 QA 를 돌리고 아침에 결과를 찾을 수 있게 쓴다. 위에서부터 차례로 따라 한다.
> 한 판 = **로비 점검 → 캐릭터 선택·출격 → 맵(특성 4라운드) → 보스전 결과**(시나리오 `N108`, MPPM 3인 = 호스트 + 가상 2·3).
> 팀원마다 다른 것은 **시드 파일 하나**(`QANightly/nightly-seed.json`)다. 녹화·공유 폴더는 PC 설정(레포 밖).
> 관련: 녹화 리서치 [qa-nightly-recording-research.md](qa-nightly-recording-research.md) · 103 보스전 QA [handoff-qa103-notify.md](handoff-qa103-notify.md) · 데이터 출처 [data-table.md](data-table.md) · 로비 [lobby-demo-flow.md](lobby-demo-flow.md).

## 0. 준비물 (처음 한 번)
- Unity **6000.3.16f1**(`C:/UnityEditors/6000.3.16f1/Editor/Unity.exe` 기준 — 경로가 다르면 바꿔 읽는다).
- 이 레포의 팀원 브랜치 작업 트리(예: `C:/UnityProject/MainProject`, 브랜치 `qa/Nightly-<이름>`, §6).
- SVN 작업 사본이 같은 폴더에 연결돼 있어야 한다(`Assets/50.Art/` — `GameData.xlsx` 포함).
- (선택) OBS Studio 28 이상.
- Windows 절전·화면 보호기 끄기(녹화가 검게 나오거나 에디터가 느려진다): 설정 → 시스템 → 전원 → 화면·절전 "안 함".

## 1. 에디터 끄기
- Unity 가 켜져 있으면 Play 를 먼저 멈춘다(`Tools/QA/Stop Active Scenario`). 그 뒤 에디터를 닫는다.
- 에이전트: `taskkill /IM Unity.exe`(**/F 없이** — 저장 확인을 받는다). 닫혔는지 확인:
  - `tasklist /FI "IMAGENAME eq Unity.exe"` 에 프로세스가 없고,
  - `<프로젝트>/Temp/UnityLockfile` 이 지워졌거나 잠겨 있지 않다.
- 🔴 에디터가 켜진 채 SVN 아트를 갈면 교체가 실패하거나 낡은 임포트가 남는다(CLAUDE.md §6).

## 2. SVN 업데이트 (특히 GameData.xlsx 최신)
```
cd <프로젝트 폴더>
svn update --non-interactive --accept postpone
svn info --show-item last-changed-revision "Assets/50.Art/DataTable~/GameData.xlsx"
```
- `--non-interactive --accept postpone` 은 **필수**다(충돌 프롬프트에서 무한 대기하는 사고가 있었다).
- 마지막 줄의 숫자(예: `528`)를 적어 둔다 — 아침 리포트의 "GameData.xlsx SVN 리비전" 과 같아야 한다. 리포트에 `unknown` 이 찍히면 svn 이 PATH 에 없거나 작업 사본이 아닌 것이다.
- 충돌(`C`) 이 나오면 QA 를 시작하지 말고 사람에게 알린다.
- git 은 팀원 브랜치를 최신으로(필요할 때만): `git pull`(🔴 에디터를 끈 상태에서).

## 3. 에디터 실행
```
"C:/UnityEditors/6000.3.16f1/Editor/Unity.exe" -projectPath "<프로젝트 폴더>"
```
- 여러 에디터를 켤 때는 하나씩, 5초 이상 간격. 켠 뒤 `Temp/UnityLockfile` 과 `Unity.exe` 프로세스로 떴는지 확인.
- 첫 로드(패키지 설치·임포트)는 길다. 콘솔에 컴파일 오류가 없는지 본다(MCP 가 있으면 `Unity_GetConsoleLogs`).
- 씬 저장 확인 같은 **대화상자가 떠 있으면 루프가 멈춘다** — 닫고 시작한다.

## 4. 툴바 '데이터: 테이블' 확인
- 에디터 위쪽 툴바의 `[ 데이터: 테이블 ▾ ]` (피치색). `인스펙터`(연두)면 눌러서 **테이블**로 바꾼다.
- 야간 루프는 시작할 때·판마다 테이블로 되돌린다. 그래도 사람이 보는 확인 단계로 남긴다. 원래 값은 batch.txt `data_source_toolbar_before` 에 남는다.
- 판마다 호스트가 **런타임 적용 경로**로 출처를 다시 확인한다(103 의 데이터 출처 확인 그대로). 테이블이 아니면 그 판은 `Error(data_source)` → 끝에서 1회 재실행.

## 5. (선택) OBS 녹화 켜기
1. OBS 실행 → **도구 → WebSocket 서버 설정** → "WebSocket 서버 사용" 체크, 포트 `4455`, 인증 사용 시 비밀번호 확인("연결 정보 표시").
2. 장면에 **디스플레이 캡처**(모니터 전체)를 넣고 메인 에디터와 가상 플레이어 창 2개가 한 화면에 보이게 둔다. 녹화 형식 mkv(또는 hybrid mp4), 하드웨어 인코더 권장(리서치 문서 §3.3).
3. PC 설정을 넣는다(둘 중 하나, **레포에 커밋하지 않는다**):
   - 환경변수(권장 — 에디터를 다시 켜지 않아도 사용자 범위 값을 읽는다):
     ```
     setx QA_NIGHTLY_OBS 1
     setx QA_OBS_PASSWORD "<OBS 비밀번호>"
     setx QA_OBS_PORT 4455
     ```
   - 또는 메뉴 `Tools/QA/Nightly/Settings/Create Local Settings File` → `<프로젝트>/UserSettings/QANightly.local.json` 의 `obsEnabled` 를 `true` 로(비밀번호는 환경변수로).
4. 메뉴 `Tools/QA/Nightly/Settings/Test OBS Connection` → 콘솔에 `[QA108/Nightly] OBS 연결 OK` 가 나오면 된다. 실패해도 QA 는 녹화 없이 돈다(경고만).

## 5.1 (선택) 공유 폴더 루트
- 날짜별 결과를 모을 폴더(Tailscale 공유 폴더 아래 — 예: `C:\Team-Vault\QA-Nightly`. 실제 경로는 팀장이 정한 공유 폴더로):
  ```
  setx QA_NIGHTLY_SHARE_ROOT "C:\Team-Vault\QA-Nightly"
  ```
  또는 로컬 설정 파일의 `shareRoot`. 비워 두면 복사하지 않는다.
- 복사 위치: `<루트>/<yyyy-MM-dd>/<시드 이름>/` (날짜 = **배치 시작 날** — 자정을 넘겨도 한 밤 = 한 폴더).

> 할 일 120 — 녹화 용량 줄이기는 두 단계다. **지금은 1단계**(판 전체 녹화 + 저화질 + N일 삭제 + 이벤트 시각표). 이벤트 목록이 확정되면 스위치 하나로 **2단계**(리플레이 버퍼 클립만)로 바꾼다.
> 모든 값은 비워 두면 **아무것도 바꾸지 않는다**(108 과 같다). 설정 목록 전체: 리서치 문서 §4.

## 5.2 (선택) 1단계 — 저화질·저비트레이트
배치 시작(`Start Night`·`Resume Last`) 때 1회, OBS WebSocket 으로 적용한다(로그 `logs/loop.log` 에 `OBS 화질 —`·`OBS 프로필 … =` 줄).
```
setx QA_OBS_VIDEO_WIDTH 1280
setx QA_OBS_VIDEO_HEIGHT 720
setx QA_OBS_VIDEO_FPS 24
setx QA_OBS_BITRATE_KBPS 2500
```
(로컬 설정 파일이면 `videoWidth`·`videoHeight`·`videoFps`·`videoBitrateKbps`.)
- 해상도·fps = `SetVideoSettings`(출력(스케일) 해상도만 바꾸고 기본 캔버스는 그대로). **녹화·리플레이 버퍼가 켜져 있으면 건너뛰고 경고**한다 — OBS 를 켠 뒤 손으로 녹화를 시작해 두지 말 것.
- 비트레이트 = `SetProfileParameter`. OBS 출력 모드를 먼저 묻는다(`Output/Mode`):
  - **단순(Simple)**: `SimpleOutput/VBitrate` 에 쓴다. ✅(2026-10-27, OBS 32.0.1) 녹화 품질이 "스트림과 같음"일 때 녹화가 이 값을 쓴다 — 2500 으로 10초 녹화 = 약 2.6Mbps(오디오 포함). "작은 파일 크기" 등 다른 품질이면 이 값은 무시된다.
  - **고급(Advanced)**: 녹화 인코더 비트레이트는 프로필 ini 가 아니라 인코더 설정 파일에 있어 **못 바꾼다 → 경고만**. 손으로 맞추거나, 키를 안다면 `QA_OBS_BITRATE_PARAM=<카테고리>/<키>`(파일 `obsBitrateParam`)로 직접 지정.
  - ✅(2026-10-27) **녹화 품질(`SimpleOutput/RecQuality`)은 OBS 가 켜진 채로 바꾸면 안 된다** — 바꾼 직후 녹화 인코더를 못 만들어 `StartRecord` 가 응답만 성공하고 녹화가 시작되지 않는다(로그 `Cannot apply a new video_t object after the encoder has been initialized`). OBS 재시작으로 풀린다. 그래서 QA 코드는 품질을 건드리지 않고, 사람이 설정 창에서 한 번 맞춘다.
  - ✅(2026-10-27) **OBS 설정 창이 열려 있으면** `StartRecord`·`StartReplayBuffer` 가 응답만 성공하고 출력이 켜지지 않는다. 밤 배치 전에 설정 창을 닫아 둘 것.
  - 🔶 [확인 필요] `VBitrate` 만 바꾼 값이 다음 `StartRecord` 에 바로 먹는지(이번 확인은 재시작 뒤 값). 첫 밤 아침에 영상 비트레이트를 확인한다.
- **손으로 OBS 프로필 만들기(권장 — 확실한 방법)**: OBS → 프로필 → 새로 만들기 `QA-Nightly` →
  설정 → 출력 → 출력 모드 **단순** → 녹화 품질 **"스트림과 같음"**, 녹화 형식 mkv, 인코더 하드웨어, 방송 비트레이트 **2500 Kbps** →
  설정 → 비디오 → 출력(스케일) 해상도 **1280x720**, FPS **24**. 이 프로필을 켜 둔 채 밤을 돌리면 위 환경변수는 비워도 된다.

## 5.3 (선택) 2단계 — 리플레이 버퍼 클립만 (스위치, 기본 꺼짐)
켜면 **판 전체 녹화를 하지 않는다**. 배치 시작에 `StartReplayBuffer`, 배치 끝에 `StopReplayBuffer`. 이벤트가 나면 M초 뒤 `SaveReplayBuffer` → 클립 하나.
```
setx QA_NIGHTLY_REPLAY 1
setx QA_OBS_REPLAY_SECONDS 60
setx QA_OBS_REPLAY_AFTER 10
setx QA_OBS_REPLAY_COOLDOWN 30
setx QA_NIGHTLY_CLIP_EVENTS ""
```
(파일이면 `replayOnly`·`replaySeconds`·`replayAfterSeconds`·`replayCooldownSeconds`·`clipEvents`. `QA_NIGHTLY_OBS=1` 도 켜져 있어야 한다.)
- 되돌리기(1단계로): `setx QA_NIGHTLY_REPLAY 0`. 모드는 **배치 시작 때 고정**(batch.txt `record=obs|replay|off`) — 돌던 밤 중간에는 안 바뀐다.
- **OBS 쪽 준비(손으로 한 번)**: 설정 → 출력 → **리플레이 버퍼 사용** 체크, 최대 리플레이 시간 = N초(예 60). 메모리 한도는 기본값.
  `QA_OBS_REPLAY_SECONDS` 를 넣으면 배치 시작에 `…/RecRB=true`·`…/RecRBTime=N` 을 프로필 파라미터로 써 본다(단순 `SimpleOutput`, 고급 `AdvOut`) — ✅(2026-10-27) 키 이름은 맞다. 단 **즉시 반영되지 않는다** — `RecRB=true` 를 웹소켓으로 쓴 뒤 `StartReplayBuffer` 는 604(사용할 수 없음), OBS 재시작 뒤 시작·저장·정지 정상. 그래서 리플레이 버퍼 사용 체크는 손으로 해 두는 게 정답이다.
- 이벤트(이벤트 표 — `Assets/1.Scripts/QA/Nightly/Editor/QANightlyEvents.cs` 의 `Table` 한 곳): `error`(에러·예외 로그, QA 발견 Error/Critical, 판 Error, 루프 판 오류) · `stuck`(20초 끼임 재배치, 벽 관통·벽 타기·Softlock 발견) · `stall`(정체 감시 90초) · `death`(사망) · `wipe`(전멸) · `timeout`(30분·조기 시간초과).
  일부만 클립으로: `QA_NIGHTLY_CLIP_EVENTS=stall,wipe`(비움 = 전부). 시각표에는 늘 전부 남는다. 새 이벤트는 `Table` 에 한 줄 더한다.
- 합치기: 저장 대기(M초) 중에 오는 이벤트, 지난 저장 뒤 쿨다운 안에 오는 이벤트는 **새 클립을 만들지 않고 다음 클립 하나에 모은다**(이름에 `stall+death` 처럼). 쿨다운 안 이벤트의 저장 시각 = max(이벤트 + M, 지난 저장 + 쿨다운).
  → **버퍼 N 은 M + 쿨다운보다 길게**(기본 10 + 30 → 60초 이상) 둬야 첫 이벤트 앞 구간이 남는다.
- 클립 파일: OBS 가 저장한 경로(`ReplayBufferSaved` 이벤트의 `savedReplayPath`, 못 받으면 `GetLastReplayBufferReplay`)를 `<yyyy-MM-dd>_<시드>_r<판>[re]_<이벤트+이벤트>.<확장자>` 로 바꾼다(재실행 판 = `r3re`, 겹치면 `_2`). 이름 바꾸기 실패는 경고 후 원래 이름. 공유 폴더 `video/` 로 robocopy, `clips.txt`·리포트 "리플레이 클립" 줄.
- Play 중 저장은 스레드 풀에서 한다(게임을 멈추지 않게). 판이 끝나 Play 가 멈추는 도메인 리로드와 저장이 겹치면 그 클립의 이름 바꾸기·기록이 빠질 수 있다(클립 파일 자체는 OBS 폴더에 남는다).
- OBS 가 없거나 연결·저장 실패면 경고만, QA 는 계속(108 원칙). 버퍼 시작 실패면 판마다 다시 시도한다.

## 5.4 (선택) 공유 폴더 영상 보관 일수
```
setx QA_NIGHTLY_VIDEO_KEEP_DAYS 7
```
- 배치 시작 때 1회, `<루트>/<yyyy-MM-dd>/<내 시드 이름>/video/` 중 **날짜가 오늘보다 N일 넘게 지난** 폴더의 **영상 파일**(mkv·mp4·mov·flv·ts·m4v·webm·avi)만 지운다.
- 리포트·CSV·로그·`robocopy.log` 는 지우지 않는다. 이름이 정확히 `yyyy-MM-dd` 가 아닌 폴더는 건드리지 않는다. **다른 팀원 시드 폴더는 건드리지 않는다**(각자 PC 가 자기 것만).
- 0·비움 = 지우지 않음. 실패는 경고만(`⚠ 지난 영상 삭제 실패`). OBS 녹화 폴더의 원본은 이 기능이 지우지 않는다 — 원본 폴더는 OBS·디스크 정리로 따로 관리.

## 6. 야간 QA 시작
1. (확인) `Tools/QA/Nightly/Settings/Validate Seed File` → `시드 파일 OK — <이름> · 기준 시드 … · N판`.
2. (확인) `Tools/QA/Nightly/Status` → 시드·PC 설정(녹화 켜짐/꺼짐·공유 루트)·루프 상태가 콘솔에 나온다.
3. **`Tools/QA/Nightly/Start Night`** — 끝. 이후는 무인:
   - 가상 플레이어 2·3 을 켜고(판마다 껐다 켜서 접속 거절 예방) → 판마다 설정 파일을 호스트·클론에 쓰고 → `N108` 3인 Play → 판 기록을 보면 Play 정지 → 녹화 정지·수집 → 다음 판.
   - 오류 판(심장박동 끊김·시작 8분·Play 진입 실패 등)은 기록 후 다음 판, 끝에서 1회 재실행.
   - 🔴 돌고 있는 동안 스크립트를 고치거나 Refresh 하지 않는다. `isPlaying` 을 직접 만지지 않는다.
- 에이전트가 MCP 로 시작할 때: `EditorApplication.ExecuteMenuItem("Tools/QA/Nightly/Start Night")`.
- 중간에 멈추기: `Tools/QA/Nightly/Stop After Current Run`(지금 판이 끝나면 멈추고 가상 플레이어 비활성·시나리오 모드 해제까지 한다).
- 에디터가 꺼졌다 켜지면 저절로 이어 가지 않는다 → `Tools/QA/Nightly/Resume Last`.

## 7. 아침 결과 확인
1. `Tools/QA/Nightly/Status` → `멈춤 · … 상태 done`(완료) 또는 `stopped`. 진행 `N/N`·오류 확정 수.
2. 로컬 배치 폴더 `C:/UnityProject/QA/N108-<yyyyMMdd_HHmm>-<시드 이름>/`:
   - `nightly-summary.md` — **먼저 이것**: 시드 이름·데이터 출처·GameData.xlsx 리비전·브랜치/커밋·판 결과·녹화 목록.
   - `report.html` — 103 리포트(머리말에 같은 정보 + 통계·판 목록). `Tools/QA/Nightly/Open Report` 로 연다.
   - `batch.txt`(배치 머리) · `runs.csv` 외 CSV · `logs/loop.log`(루프 로그, `⚠` = 녹화·복사 경고) · `recordings.txt`.
   - 할 일 120 `events.tsv`(이벤트 시각표 원본 — 탭: 판·재실행·이벤트·시각·게임 시각(초)·영상 타임코드(초)·상세) · `clips.txt`(2단계 클립) · `clip-pending.txt`(저장 대기 — 비면 정상).
     `nightly-summary.md` 끝의 **이벤트 시각표** 표 — "영상 타임코드"(`H:MM:SS`)로 그 판 영상을 바로 찾아간다. 같은 이벤트가 5초 안에 반복되면 한 줄(판당 300줄 상한).
     타임코드 = 녹화 시작 직후 `GetRecordStatus` 의 녹화 경과(`outputDuration`) + 그 뒤 경과. 기준을 못 받았으면(상태 요청 실패) 녹화 시작 시각 기준(수 초 오차).
   - 로비 점검 항목(108 로비 카메라·스킬·에러 0)과 판 진행 근거는 판마다 생기는 `C:/UnityProject/QA/48-*/report.md`.
3. 공유 폴더 `<루트>/<yyyy-MM-dd>/<시드 이름>/`:
   - `N108-…/`(배치 폴더 사본, 심장박동 hb/ 제외) · `48/`(그 밤 48 리포트) · `logs/Editor-*.log` · `video/`(영상 + `robocopy.log`).
4. 영상: 리포트 "녹화" 줄의 경로(OBS 녹화 폴더 원본) 또는 공유 사본. 주간에 사람이 보고 이벤트를 적는다.
5. 확인할 것: 리포트 xlsx 리비전 = §2 에서 적은 숫자 · 데이터 출처 런타임 테이블 n/n · 브랜치가 내 `qa/Nightly-<이름>`.

## 8. 팀원 브랜치 만들기 (시드만 다르게)
> 브랜치는 팀장/조율이 정한 뒤 만든다. 이 문서 작성 시점에는 만들지 않았다.

- 이름: `qa/Nightly-<이름>`(영문, 예 `qa/Nightly-eunhee`). 기반 = `qa/NightlyBase`.
- **바꾸는 파일은 `QANightly/nightly-seed.json` 하나**:
  ```json
  {
    "name": "eunhee",
    "baseSeed": 2108,
    "traitSeed": 0,
    "botTier": "auto",
    "characterOrder": ["Gunner", "Paladin", "Assassin"],
    "runs": 10,
    "note": "은희 PC 야간"
  }
  ```
  | 칸 | 뜻 | 값 |
  |---|---|---|
  | `name` | 시드 이름 = 공유 폴더 이름 | 영문·숫자·`-`·`_` 1~32자 |
  | `baseSeed` | 판 시드(봇 전투 난수)의 원천 | 0 이 아닌 정수, 팀원마다 다르게 |
  | `traitSeed` | 특성 제시·선택 시드 | 0 = baseSeed 와 같이 씀 |
  | `botTier` | 봇 단계 | `auto`(판 번호로 보통→숙련→초보) · `Beginner` · `Normal` · `Skilled` |
  | `characterOrder` | 자리 0(호스트)·1·2 의 캐릭터 | `Paladin`·`Assassin`·`Gunner` 를 한 번씩 |
  | `runs` | 한 밤 판 수 | 1~90 (판당 최대 약 45분 — 10판 ≈ 하룻밤) |
- 절차(에디터를 끈 상태):
  ```
  git switch qa/NightlyBase && git pull
  git switch -c qa/Nightly-<이름>
  (QANightly/nightly-seed.json 수정)
  git add -- QANightly/nightly-seed.json
  git commit -m "qa(108): 야간 시드 <이름>"
  ```
  push 는 지시가 있을 때만. 🔴 `qa/*` 는 `development` 로 병합하지 않는다.
- 기반 하네스가 바뀌면 팀원 브랜치에 `qa/NightlyBase` 를 병합한다(시드 파일만 다르니 충돌은 그 파일뿐).

## 9. 알려진 한계
- 맵 구간의 복도 이동·전투는 103 과 같이 **건너뛴다**: 특성 4라운드를 서버에서 바로 열고, G10 까지 개방한 뒤 파티를 보스 발판으로 옮긴다(103 판 흐름 — `QABoss103Match.RunHost`). 로비·캐릭터 선택·출격·맵 진입·보스전은 정식 흐름이다.
- 자리 순서는 세 캐릭터 1명씩만 된다(같은 캐릭터 2명 불가 — 계측이 캐릭터당 1명 전제).
- 녹화는 화면 전체 1개 파일/판(1단계). 2단계는 이벤트 클립만(§5.3).
- 이벤트는 **호스트(메인 에디터) 로그만** 본다 — 가상 플레이어(클론) 프로세스의 에러 로그는 시각표에 안 들어간다(클론 봇의 끼임은 호스트 재배치 `qa_relocate` 로 잡힌다).
- 게임 시각 = Play 의 `Time.time`(간파 슬로우 등 시간 배율을 탄다). 영상 위치는 "영상 타임코드" 열을 본다.
- 108 하네스·120 은 작성 시점에 **컴파일·EditMode 테스트·Play 를 이 세션에서 못 돌렸다**(권한 거부) — 첫 실행 전에 `QANightlyTests` 를 돌린다. OBS 실연동(화질 적용·리플레이 저장·이름 바꾸기)도 미검증.
