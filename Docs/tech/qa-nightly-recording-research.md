# 야간 QA 녹화 — 외부 녹화 프로그램 제어 리서치 (할 일 108)

> 사양 원본: `_lane-artifacts/common/108/108-결정.md` §4 — **전체 녹화**(주간에 사람이 보고 이벤트 수집) 구현, **하이라이트(상시 버퍼 → 이벤트 구간 저장)는 리서치만**.
> 작성 2026-10-27, claude-alt. ⚠ 이 세션은 웹 접근이 막혀 있어 아래 사실은 작성자 지식 기준이다. **[확인 필요]** 표시는 공식 문서로 다시 확인할 것.
> 확인처: obs-websocket 프로토콜 문서 `github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md`.
> **2026-10-27 갱신(할 일 120)**: 조율이 위 공식 protocol.md(rpcVersion 1)로 §3.1 의 요청·응답·이벤트 칸을 확인했다(✅ 표시). 프로필 파라미터 키 이름과 출력 중 SetVideoSettings 가능 여부는 문서에 없어 [확인 필요]로 남는다.
> 120 사양: `_lane-artifacts/common/120/120-결정.md` — 1단계(판 전체 + 저화질 + N일 삭제 + 이벤트 시각표) 구현, 2단계(리플레이 버퍼 클립) 스위치로 구현.

## 1. 결론(추천안)

**OBS Studio 28+ 의 내장 WebSocket 서버(obs-websocket v5)를 에디터에서 직접 부른다.** 추가 설치 없음, 이미 구현했다.

- 판 시작 전 `StartRecord`, 판이 끝나 Play 가 멈춘 뒤 `StopRecord` → 응답의 `outputPath`(영상 경로)를 리포트에 남긴다.
- 코드: `Assets/1.Scripts/QA/Nightly/Editor/QAObsWebSocket.cs`(프로토콜·클라이언트), `QANightlyHooks.cs`(판 앞뒤 호출).
- 기본 **꺼짐**. PC 설정 `UserSettings/QANightly.local.json`(gitignore) 또는 환경변수로 켠다(§4). 비밀번호는 레포에 넣지 않는다.
- OBS 가 없거나 연결·인증에 실패하면 **경고만** 남기고 QA 는 계속 간다(5초 상한, 판마다 다시 시도).
- 사람·에이전트가 손으로 확인할 때는 메뉴 `Tools/QA/Nightly/Settings/Test OBS Connection` 을 쓴다. 터미널에서 확인하려면 `obs-cmd` 를 보조로 쓸 수 있다(§3.2).

## 2. 후보 비교

| 후보 | 외부 제어 | 영상 경로 얻기 | 리플레이 버퍼(하이라이트) | 설치 | 판정 |
|---|---|---|---|---|---|
| **OBS + 내장 WebSocket v5** | WebSocket(JSON-RPC). 시작/정지/상태/장면/프로필 | `StopRecord` 응답 `outputPath` | `StartReplayBuffer`·`SaveReplayBuffer`·`GetLastReplayBufferReplay` | OBS 만(28+ 내장) | **추천** |
| OBS + `obs-cmd` (Rust CLI) | 같은 WebSocket 을 CLI 로 감쌈 | 출력에 표시 [확인 필요] | `replay save` 류 명령 [확인 필요] | 실행 파일 1개 | 보조(수동 확인용) |
| OBS + `obs-cli` (Go, muesli) | 같은 WebSocket | [확인 필요] | [확인 필요] | 실행 파일 1개 | 보조. v4 시절 도구라 v5 지원 버전 확인 필요 |
| OBS 실행 인자 | `obs64.exe --startrecording --minimize-to-tray` (켤 때만) | 없음 | `--startreplaybuffer` | OBS | 켤 때만 가능. 정지는 못 함 → WebSocket 과 함께 쓴다 |
| OBS MCP 서버 | 커뮤니티 MCP 서버가 obs-websocket 을 감싼 것이 있다(예: `obs-mcp`) [확인 필요] | WebSocket 과 같음 | WebSocket 과 같음 | Node 등 + MCP 등록 | 에이전트가 대화형으로 만질 때만 의미가 있다. 무인 루프는 에디터가 직접 부르는 편이 단순하다 |
| ffmpeg `gdigrab`/`ddagrab` | 프로세스 시작, 정지 = stdin 에 `q` | 우리가 정함 | 없음(직접 구현) | ffmpeg | 대안. 다만 도메인 리로드를 넘겨 프로세스를 들고 있어야 하고, 인코더 설정을 직접 맞춰야 한다 |
| ShareX | CLI `-ScreenRecorder` 는 토글뿐(내부 ffmpeg) | 없음 | 없음 | ShareX | 상태·경로를 모름 → 부적합 |
| NVIDIA App(ShadowPlay)·Xbox Game Bar | 단축키뿐, 공식 CLI/API 없음 | 없음 | 인스턴트 리플레이(단축키) | 드라이버 | 무인 제어 불가 → 부적합 |

## 3. OBS WebSocket v5 요약

### 3.1 프로토콜 (구현에 쓴 부분)
- 켜기: OBS 메뉴 **도구 → WebSocket 서버 설정 → "WebSocket 서버 사용"**. 기본 포트 **4455**. 인증 켜면 비밀번호(같은 창 "연결 정보 표시"에서 확인).
- 흐름: 접속 → `Hello`(op 0, 인증이 있으면 `authentication.challenge`·`salt`) → `Identify`(op 1, `rpcVersion: 1`, `authentication`, `eventSubscriptions`) → `Identified`(op 2) → `Request`(op 6) → `RequestResponse`(op 7).
- 인증 문자열 = `base64(sha256(base64(sha256(비밀번호 + salt)) + challenge))`.
- 쓰는 요청(✅ 공식 문서 확인 — 120):
  | 요청/이벤트 | 요청 칸 | 응답·이벤트 칸 | 쓰는 곳 |
  |---|---|---|---|
  | `StartRecord` | 없음 | 없음 | 1단계 판 시작 |
  | `StopRecord` | 없음 | `outputPath` | 1단계 판 끝 |
  | `GetRecordStatus` | 없음 | `outputActive`·`outputPaused`·`outputTimecode`·`outputDuration`(ms)·`outputBytes` | 타임코드 기준·출력 중 확인 |
  | `StartReplayBuffer`·`StopReplayBuffer`·`SaveReplayBuffer` | 없음 | 없음 | 2단계 배치 시작·끝·이벤트 |
  | `GetReplayBufferStatus` | 없음 | `outputActive` | 출력 중 확인 |
  | `GetLastReplayBufferReplay` | 없음 | `savedReplayPath` | 클립 경로(이벤트를 못 받았을 때) |
  | 이벤트 `ReplayBufferSaved` | (구독 Outputs = `1<<6`) | `savedReplayPath` | 클립 경로(우선) |
  | 이벤트 `RecordStateChanged` | (구독 Outputs) | `outputActive`·`outputState`·`outputPath` | 쓰지 않음 |
  | `SetVideoSettings` | `fpsNumerator`·`fpsDenominator`·`baseWidth`·`baseHeight`·`outputWidth`·`outputHeight`(모두 선택, **짝으로**) | 없음 | 1단계 화질(출력 해상도·fps 만) |
  | `SetProfileParameter` | `parameterCategory`·`parameterName`·`parameterValue` | 없음 | 비트레이트·버퍼 길이 |
  | `GetProfileParameter` | `parameterCategory`·`parameterName` | `parameterValue`·`defaultParameterValue` | 출력 모드(`Output/Mode`) |
- 상태 코드: 500 = OutputRunning(이미 녹화 중), 501 = OutputNotRunning(녹화 중 아님). 둘 다 QA 에서는 경고 없이 넘어간다(리플레이 버퍼 시작·정지도 같게 본다).
- 🔶 [확인 필요] `SetVideoSettings` 를 **출력 중**(녹화·리플레이 버퍼)에 불러도 되는지 — 문서에 없다. 구현은 보수적으로 둘 다 꺼져 있을 때만 부르고, 켜져 있으면 건너뛰고 경고한다.
- 🔶 [확인 필요] 프로필 파라미터 키(OBS 프로필 `basic.ini` 기준, 작성자 지식): 출력 모드 `Output/Mode` = `Simple`·`Advanced`. 단순 모드 비트레이트 `SimpleOutput/VBitrate`(녹화 품질 "스트림과 같음"일 때 녹화에 쓰임),
  리플레이 버퍼 `SimpleOutput/RecRB`·`SimpleOutput/RecRBTime`(고급 `AdvOut/RecRB`·`AdvOut/RecRBTime`). 고급 모드 녹화 인코더 비트레이트는 `recordEncoder.json` 이라 프로필 파라미터로 못 바꾼다.
  바꾼 값이 다음 출력 시작에 바로 먹는지도 미확인 — 안 먹으면 런북 §5.2·§5.3 의 손 설정이 정답.
- 녹화 폴더를 판 폴더로 바꾸는 `SetRecordDirectory` 는 v5.3(OBS 30)부터다 [확인 필요]. 지금은 쓰지 않는다 — OBS 기본 녹화 폴더에 두고 공유 폴더로 복사한다.

### 3.2 obs-cmd (수동 확인용, 선택)
- 예: `obs-cmd --websocket obsws://127.0.0.1:4455/<비밀번호> recording start` / `recording stop` / `replay save` [확인 필요 — 인자 형식은 `obs-cmd --help`].
- 비밀번호가 명령줄에 남으니 셸 기록에 주의한다. 무인 루프에는 쓰지 않는다.

### 3.3 녹화 설정 권장값 (각 PC 의 OBS)
- 소스: **디스플레이 캡처**(모니터 전체) — MPPM 가상 플레이어 창 2개와 메인 에디터 Game 뷰가 한 화면에 보이게 배치. 창 캡처는 가상 플레이어 창을 놓친다.
- 녹화 형식: **mkv** 또는 OBS 30.2+ 의 hybrid mp4(크래시에도 파일이 살아남는 형식). mp4(일반)는 OBS 가 비정상 종료되면 파일이 깨진다.
- 인코더: 하드웨어(NVENC/AMF/QSV). 해상도 1080p, 30fps, 품질 낮게(밤새 10판이면 판당 수백 MB~수 GB). 디스크 여유를 먼저 확인.
- OBS 설정 **일반 → "녹화 중 종료 확인" 끔**, 시작할 때 자동 업데이트 확인 대화상자가 막지 않게 한 번 켜서 넘겨 둔다.

## 4. 이 레포의 연동 (구현됨)

| 설정 | 파일 키(`UserSettings/QANightly.local.json`) | 환경변수(우선) | 기본 |
|---|---|---|---|
| 녹화 켜기 | `obsEnabled` | `QA_NIGHTLY_OBS` = `1`/`0` | 꺼짐 |
| 주소 | `obsHost` | `QA_OBS_HOST` | `127.0.0.1` |
| 포트 | `obsPort` | `QA_OBS_PORT` | `4455` |
| 비밀번호 | `obsPassword`(권하지 않음) | `QA_OBS_PASSWORD`(권함) | 없음 |
| 공유 폴더 루트 | `shareRoot` | `QA_NIGHTLY_SHARE_ROOT` | 없음(복사 안 함) |
| (120) 출력 해상도 | `videoWidth`·`videoHeight` | `QA_OBS_VIDEO_WIDTH`·`QA_OBS_VIDEO_HEIGHT` | 0 = 그대로 |
| (120) fps | `videoFps` | `QA_OBS_VIDEO_FPS` | 0 = 그대로 |
| (120) 녹화 비트레이트(kbps) | `videoBitrateKbps` | `QA_OBS_BITRATE_KBPS` | 0 = 그대로 |
| (120) 비트레이트 키 직접 지정 | `obsBitrateParam`(`카테고리/키`) | `QA_OBS_BITRATE_PARAM` | 비움 = 출력 모드로 자동 |
| (120) 공유 폴더 영상 보관 일수 | `videoKeepDays` | `QA_NIGHTLY_VIDEO_KEEP_DAYS` | 0 = 안 지움 |
| (120) 2단계 스위치 | `replayOnly` | `QA_NIGHTLY_REPLAY` = `1`/`0` | 꺼짐(판 전체 녹화) |
| (120) 리플레이 버퍼 길이 N(초) | `replaySeconds` | `QA_OBS_REPLAY_SECONDS` | 0 = OBS 설정 그대로 |
| (120) 이벤트 뒤 기다릴 M(초) | `replayAfterSeconds` | `QA_OBS_REPLAY_AFTER` | 10 |
| (120) 클립 쿨다운(초) | `replayCooldownSeconds` | `QA_OBS_REPLAY_COOLDOWN` | 30 |
| (120) 클립 이벤트 | `clipEvents`(쉼표) | `QA_NIGHTLY_CLIP_EVENTS` | 비움 = 이벤트 표 전체 |

- 환경변수는 에디터 프로세스 → **사용자 범위(레지스트리)** 순으로 읽는다. `setx` 로 넣은 값은 에디터를 어디서 띄웠든 읽힌다.
- 녹화 모드는 **배치 시작 때 고정**(batch.txt `record=obs|replay|off`). 주소·비밀번호는 판마다 다시 읽는다.
- (120) 이벤트 시각표 `events.tsv` + `nightly-summary.md` "이벤트 시각표" + `report.html` "이벤트 시각표" 줄. 2단계 클립 `clips.txt` + 리포트 "리플레이 클립" 줄.
- (120) 코드: 순수 로직 `QANightlyEvents.cs`(이벤트 표·시각표·타임코드·클립 계획·파일명·보관), 연동 `QANightlyHooks.cs`, 프로토콜 `QAObsWebSocket.cs`(`QAObsProfileParams` 포함). 테스트 `QANightlyTests`.
- 판 끝 영상은 `robocopy` 를 따로 띄워 `<공유 폴더>/video/` 로 복사한다(다음 판 Play 의 도메인 리로드에 끊기지 않게). 결과는 `<공유 폴더>/video/robocopy.log`.
- 리포트: `report.html` 머리말 "녹화" 줄, `nightly-summary.md` "녹화" 절, 원본 `recordings.txt`(탭 구분: 판·재실행·영상 경로·공유 사본·메모).

## 5. 하이라이트

> **120 에서 구현**: 아래 2(전체 녹화 + 이벤트 시각) = 1단계(기본), 1(리플레이 버퍼 + 이벤트 저장) = 2단계(스위치). 설정·전환은 런북 §5.2~5.4.
> 챕터 표시(`CreateRecordChapter`)·ffmpeg 자르기는 구현하지 않았다.

이벤트 목록(무엇을 잡을지)은 주간 영상 확인 뒤 따로 grill 한다(108-결정 §6). 방식 후보:

1. **리플레이 버퍼 + 이벤트 저장** — OBS 리플레이 버퍼(예: 최근 60초)를 켜 두고, QA 가 이벤트(전멸·끼임 재배치·stall·예외 로그·보스 패턴 실패)를 볼 때 `SaveReplayBuffer` → `GetLastReplayBufferReplay`(또는 `ReplayBufferSaved` 이벤트의 `savedReplayPath`)로 파일 경로를 받아 리포트에 링크. 장점: 즉시 짧은 클립. 단점: 이벤트 **뒤** 구간을 담으려면 저장을 몇 초 늦춰야 하고, 버퍼가 RAM 을 쓴다.
2. **전체 녹화 + 시각 표시 후 잘라내기** — 지금 구현(전체 녹화)에 이벤트 시각(녹화 시작 기준 초)을 CSV 로 남기고, 아침에 `ffmpeg -ss <t-20> -to <t+10> -i 영상 -c copy 클립.mkv` 로 잘라낸다. 장점: 앞뒤 구간을 마음대로, 놓친 이벤트도 나중에 다시. 단점: 큰 원본이 남는다. OBS 30.2+ hybrid mp4 의 챕터 표시(`CreateRecordChapter`) [확인 필요] 를 쓰면 플레이어에서 바로 점프할 수 있다.
3. 권장 순서: 먼저 2(지금 녹화에 이벤트 시각만 더함) → 클립이 자주 필요하면 1 을 더한다.

## 6. 알려진 한계
- 녹화는 PC 화면 전체다. 에디터가 최소화되거나 화면 보호기·절전이 걸리면 검은 화면이 녹화된다 → 런북의 절전 끄기 단계.
- `StopRecord` 직후 OBS 가 파일을 마무리하는 동안 복사가 잠깐 실패할 수 있다 → robocopy 재시도(10회·15초).
- 웹 접근 없이 작성 — [확인 필요] 항목은 첫 실사용 전에 공식 문서로 확인한다.
