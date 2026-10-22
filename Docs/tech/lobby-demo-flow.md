# 로비 데모 흐름 — 부스 운영 (할 일 48)

> 대상: 부스 스태프·팀원. **PC 를 켜고 설정 파일을 두는 법, 화면 문구의 뜻, 막혔을 때 볼 것**만 적는다.
> 설계·규칙 원본: [networking.md "Lobby Demo"](networking.md#lobby-demo-target-2026-10-19) · [SceneFlow.md "Target Flow"](../design/SceneFlow.md#-target-flow--lobby-demo-2026-10-19-implementation-in-progress) · 결과 패널 [lobby-result-panel.md](lobby-result-panel.md) · 계획 `_lane-artifacts/common/48-lobby/PLAN-48-lobby-demo-flow-v2.md`.
> 기준: `feature/LobbySession` C13(2026-10-19). 문서와 코드가 다르면 코드가 맞다.

## 1. 흐름
```text
exe 실행 → 타이틀(1.TitleScene) — 아무 키 → 모니터 메뉴 → START
  → (호스트) 바로 호스팅 / (클라) 자동 접속 — 접속 버튼 없음
  → 걷는 로비(3.LobbyScene) → (원하면 캐릭터 선택 오브젝트 앞에서 F → 패널에서 교체) → 준비 오브젝트 앞에서 F
  → 접속자 1~3명 전원 준비 → 5초 카운트다운 → 4.MapScene
  → 판 끝(클리어·전멸·나가기) → 로비 복귀 + 결과 패널(180초 뒤 자동 닫힘, 닫기·ESC)
  → 다시 준비 → … (exe 재실행 없이 반복)
```
- **호스트·클라 모두 부팅 직후 타이틀을 한 번 거친다**(할 일 70, 2026-10-21). 타이틀 UI·입력은 평소 그대로(SETTING·EXIT 도 그대로)이고, **START 의 시작 연출이 끝나면** 수동 로비 대신 대기 화면 → 호스팅/자동 접속으로 간다. START 를 누르기 전엔 접속을 시도하지 않는다 — 클라 PC 도 **각자 START 를 눌러야** 붙는다.
- 판 끝 로비 복귀·끊김 뒤 재접속 대기(6.DemoConnectScene)는 **타이틀을 다시 거치지 않는다**(세션 유지·판 도중 재합류를 지키려고). 타이틀은 exe 실행당 한 번뿐이다.
- 5.ResultScene 은 없다(Build Settings 에서도 뺐다 — 결과는 로비 패널).
- 판이 끝나도 연결과 고른 캐릭터는 그대로, **준비 상태만 풀린다**.
- 새 사람이 들어오면 전원의 준비가 풀린다. 한 명이라도 준비를 풀거나 나가면 카운트다운이 취소된다.
- 수치(카운트다운 5초·상호작용 반경 2m) = `Assets/9.ScriptableObject/Lobby/LobbyRules.asset`.

## 2. 설정 파일 — 모든 PC 에 같은 파일 하나 (호스트 1대 + 클라 최대 2대)
1. **모든 PC 에 같은 빌드**를 깐다(§3).
2. 빌드 폴더(exe 옆)에 샘플이 들어 있다 — 빌드 메뉴가 넣는다([BuildWindowsPlayer.cs](../../Assets/1.Scripts/Editor/BuildWindowsPlayer.cs), 원본 [BuildSamples/](../../BuildSamples/)).
   - **`demo-connection.sample.json` — 모든 PC 공용 샘플 하나.** `role` 이 `auto` 다(호스트·클라 명시 샘플은 2026-10-21 지웠다 — 필요하면 아래 5 처럼 `role` 만 고친다).
3. 한 PC 에서 공용 샘플을 `demo-connection.json` 으로 이름을 바꾸고 **`hostAddress` 를 호스트로 쓸 PC 의 IPv4** 로 고친다(호스트 PC 에서 `ipconfig`). 그 **빌드 폴더를 통째로 모든 PC 에 복사**한다 — PC 마다 고칠 것이 없다.
   - 실행하면 이 PC 의 IPv4(켜진 NIC 전부) 중 하나가 `hostAddress` 와 같으면 **호스트**, 아니면 `hostAddress` 로 접속하는 **클라**가 된다. `hostAddress` 가 비었거나 `127.x`·`localhost` 면 호스트.
   - 판정 결과는 부팅 안내(3초)·대기 화면 제목 아래에 한 줄 뜬다 — `자동: 호스트 (100.79.198.77 일치)` / `자동: 클라 (이 PC 에 100.79.198.77 없음)`. 로그(`[DemoSession] role …`)에는 비교한 이 PC 주소 목록도 남는다.
   - **`playerId` 는 쓰지 않는다** — 비우면 PC 이름이라 PC 마다 저절로 다르다. 같은 PC 이름 두 대(복제 이미지)라면 그 PC 만 `playerId` 를 넣는다. 같으면 "같은 플레이어 ID 가 이미 접속 중" 으로 거절된다. 재접속해도 캐릭터를 이어 받는 열쇠라서 **행사 중엔 바꾸지 않는다.**
4. 파일이 없으면 **호스트**(127.0.0.1 · 7777)로 뜨고 "설정 파일 없음 — 호스트 모드" 가 3초 뜬다. 클라 PC 에서 이 문구가 보이면 파일 이름·위치가 틀린 것이다.
5. **`role` 에 `host`/`client` 를 쓰면 자동 판정보다 우선한다**(예전 PC 별 파일도 그대로 동작 — 공용 샘플의 `role` 만 고치면 된다). 호스트 PC 의 IP 가 바뀌어 클라로 뜨면 `hostAddress` 를 고치거나 그 PC 만 `host` 로 둔다.

| 필드 | 뜻 | 공용 샘플 값 |
|---|---|---|
| `role` | `auto`(비거나 없어도 auto) / `host` / `client`. 모르는 값은 경고 후 auto | `auto` |
| `hostAddress` | 호스트 PC 의 IPv4 — 자동 판정 기준이자 클라가 붙을 주소(호스트는 0.0.0.0 에서 듣는다) | `192.168.0.10`(자리표시) |
| `port` | 모든 PC 가 같아야 한다. 방화벽에서 UDP 허용 | `7777` |
| `playerId` | PC 고유 ID. 비우면 PC 이름 | (없음) |
| `retryIntervalSeconds` | 접속 실패 뒤 다시 시도까지 | `3` (0 초과 60 이하) |
| `connectTimeoutSeconds` | 한 번 시도의 제한 시간 | `5` (0 초과 60 이하) |
| `disconnectTimeoutSeconds` | 응답이 끊긴 뒤 "끊김" 으로 볼 때까지 | `10` (0 초과 300 이하) |

- 틀린 값은 기본값으로 바꾸고 계속 돈다(JSON 이 깨졌으면 통째로 호스트). 파서·자동 판정: [DemoConnectionConfig.cs](../../Assets/1.Scripts/Network/Session/DemoConnectionConfig.cs)(`ResolveAutoRole`).
- **R5 끊김 타임아웃:** 랜선이 빠지면 `disconnectTimeoutSeconds`(10초) 안에 대기 화면으로 간다. 너무 짧으면 순간 렉에도 끊기고, 길면 복구가 늦다 — **부스 리허설에서 조정**한다.
- 에디터는 **프로젝트 루트**의 `demo-connection.json` 을 읽는다. 기본값(`host` 명시)으로 git 에 들어 있다 — 클라로 시험할 때만 로컬에서 고치고 그 변경은 커밋하지 않는다.

## 3. 같은 빌드 필수 (R6)
- 접속 승인을 켜서, 빌드가 다르면(NetworkConfig 해시 불일치) 호스트가 사유 없이 끊고 클라는 **"버전 불일치 — 호스트와 같은 빌드인지 확인"** 을 띄우며 재시도한다.
- 대기 화면 맨 아래 줄 `v{버전} · cfg {해시}` 로 비교한다. 해시는 NetworkManager 를 한 번 시작한 뒤에야 나온다(그 전엔 `?`). 접속·호스팅이 되면 대기 화면이 사라지므로 **호스트는 부팅 직후 잠깐만** 보인다 — 의심되면 그냥 같은 폴더를 다시 복사한다.
- A 보스 빌드(`BOSS_A_BUILD`)와 기본(B 보스) 빌드도 섞으면 안 된다.

## 4. 대기 화면(`6.DemoConnectScene`) 문구
| 문구 | 뜻 · 할 일 |
|---|---|
| 호스트 대기 중 / 접속 시도 중 (n회째) | 클라가 붙는 중. 호스트가 켜지면 자동 입장 |
| n초 후 재시도 (n회째) | 직전 시도 실패. 그 아래 줄이 사유 |
| 응답 없음 — 호스트를 기다리는 중 | 호스트가 꺼졌거나 `hostAddress`·`port`·방화벽이 틀림 |
| 게임 진행 중 — 판이 끝나면 자동 입장 | 호스트가 판 진행 중이거나 카운트다운 중이고, 이 PC 는 그 판 시작 때 없었다. **기다리면 된다**(판 도중 새 사람은 못 들어간다) |
| 판에 다시 합류하는 중 — 잠시 기다리세요 | 이 판에 있던 PC 가 다시 붙는 중인데 호스트가 씬을 바꾸는 중이거나 이전 연결을 아직 정리 중(최대 `disconnectTimeoutSeconds`). **기다리면 들어간다** |
| 판에 합류하는 중… | 맵에 들어왔지만 내 캐릭터가 아직 안 나왔다 — 엘리베이터·보스 등장 연출·슬로모션·전멸 유예가 끝나면 나온다 |
| 인원 가득 (3/3) | 이미 3명. 4번째 PC 는 들어갈 수 없다 |
| 버전 불일치 — 호스트와 같은 빌드인지 확인 | §3 |
| 같은 플레이어 ID 가 이미 접속 중 | 두 PC 의 `playerId` 가 같다. 끊긴 직후라면 서버가 이전 연결을 정리한 뒤 들어간다 |
| 호스트 시작 중 | 호스트 PC — 포트를 못 열면 재시도한다(같은 PC 에서 exe 두 개를 켰는지 확인) |
| 연결 끊김 — 정리 중 | 접속 중 끊김. 곧 재시도로 넘어간다 |
| 자동: 호스트 (… 일치) / 자동: 클라 (이 PC 에 … 없음) | `role` auto 판정 근거(§2). 호스트 PC 인데 "클라" 면 `hostAddress` 가 그 PC IP 와 다르다 — `ipconfig` 로 확인 |

- 문구 키·원본: §8. 판단: [DemoSessionRules.cs](../../Assets/1.Scripts/Network/Session/DemoSessionRules.cs) · [DemoJoinRule.cs](../../Assets/1.Scripts/Network/Session/DemoJoinRule.cs).

## 5. 재접속
- **판 도중 재합류:** 판 시작 때 있던 PC(`playerId`)가 끊기면 대기 화면에서 계속 재시도하다 **그 판으로 다시 들어간다**. 맵 첫 스폰 지점에 나오고, 보스 룸이면 네 모서리 중 다른 플레이어·보스에서 가장 먼 곳에 나온다.
  - 유지되는 것: 캐릭터 · 특성(리셋 팝업이 떠 있었으면 그 팝업도) · 생명 상태(살아 있었으면 체력 비율, 영혼이면 영혼, 완전 사망이면 관전) · 결과 패널 행(같은 사람 1행). 스킬 쿨다운은 0부터.
  - 나가 있는 동안 전멸 리스폰이 일어났으면 돌아올 때 살아 있는 만피 상태다.
  - 그 판 시작 때 없던 PC 는 "게임 진행 중" 으로 기다리다 **호스트가 로비로 돌아오면 자동 입장**한다(백필 없음).
- 다시 들어오면 **마지막 캐릭터가 유지**된다(`playerId` 기준). 상세: [networking.md](networking.md#mid-game-rejoin-c14).
- 호스트가 꺼지면 클라는 전부 "호스트 대기 중" 으로 간다. 호스트 exe 를 다시 켜고 **타이틀에서 START** 를 누르면 전원 자동 재입장(클라는 대기 화면에서 계속 재시도 중이라 타이틀로 돌아가지 않는다).

## 6. MPPM 에서 검증 (에디터 1개)
- 데모 흐름은 **0.BootStrapScene 에서 Play 할 때만** 켜진다. Dev 부팅·디버그 씬 Play 는 예전 흐름(타이틀 → 로비)이다.
- 메인 에디터 = 호스트(루트에 `demo-connection.json` 이 없거나 host, 또는 auto 판정이 호스트).
- `Client` 태그 덮어쓰기는 자동 판정보다 우선한다 — 공용 파일(auto · 이 PC 주소)을 둬도 가상 플레이어는 클라 · 127.0.0.1 이다.
- 가상 플레이어는 MPPM 태그 **`Client`** 를 줘야 데모 흐름이 켜진다 → 클라 · 127.0.0.1 로 덮어쓰고 `playerId` 뒤에 인스턴스 이름을 붙인다. 태그가 없으면 예전 수동 흐름이다(같은 포트로 호스트를 시도해 무한 재시도하는 걸 막으려는 것).
- 메인 에디터·`Client` 태그 가상 플레이어 **모두 타이틀에서 멈춘다** — 각 창에서 아무 키 → START 를 눌러야 호스팅/접속이 시작된다. 무인 QA 는 타이틀 입력 대신 `DemoSessionDirector.TryHandleTitleStart()` 를 불러도 된다(true = 데모 흐름이 시작/처리함).
- 시나리오 시작 씬은 `0.BootStrapScene`. 체크 목록은 PLAN §7(솔로·MPPM 3인·끊김).

## 7. 알려진 한계 (2026-10-19)
- **캐릭터 선택(C12)** — 로비의 캐릭터 선택 오브젝트(`Environment/LobbyInteractables/CharacterSelectStation`, 스폰 지점 왼쪽) 앞에서 F → 선택 패널 → 고르면 그 자리에서 캐릭터가 바뀐다(디스폰 → 같은 자리 새 Variant 스폰, 교체 VFX 없음 — 1~2 프레임 깜빡일 수 있다).
  - **준비 중·카운트다운 중에는 바꿀 수 없다**(패널은 열리지만 잠금 문구). 교체 뒤 0.5초 쿨다운(`LobbyRules.asset`). 오브젝트에서 멀어진 채 보낸 요청·연타는 서버가 거부하고 **로그만** 남긴다(화면 피드백 없음).
  - 고른 캐릭터는 다음 판 스폰·판 사이·재접속(`playerId`)에 유지된다. 코드: [LobbyInteractor.cs](../../Assets/1.Scripts/Lobby/LobbyInteractor.cs)(패널 연결) · [LobbySession.cs](../../Assets/1.Scripts/Lobby/LobbySession.cs)(`RequestCharacterServerRpc` 검증) · [LobbyPlayerSpawner.cs](../../Assets/1.Scripts/Lobby/LobbyPlayerSpawner.cs)(`TrySwapCharacter`).
- **증강(특성)은 판 도중 재합류에만 유지된다**(C14). 판 사이(로비로 돌아갔다 다음 판)에는 새로 시작한다. [networking.md "Mid-game rejoin"](networking.md#mid-game-rejoin-c14).
- **판 도중 재합류(C14)는 사람이 MPPM·빌드 2대로 아직 확인하지 않았다** — 영혼으로 돌아온 사람은 튜토리얼 밖에서는 기존 부활 규칙(현재 사실상 보스 강제 개시 부활·전멸 리스폰)까지 영혼이다.
- 해시는 대기 화면에서만 보인다(§3).
- 무입력 리셋·스태프 키·코스 스킵·전멸 상한·관람객 디버그 키 차단은 범위 밖(별도 작업). 디버그 단축키(`DEV_HOTKEYS`)는 빌드에 들어 있다 — [debug-hotkeys.md](debug-hotkeys.md).
- Unity Relay 는 쓰지 않는다. **유선 직접 IPv4 만.**

## 8. 화면 문구 키 (Strings)
> 문구 원본 = `GameData.xlsx`(SVN) **Strings 시트** ko·en. 키·ko 폴백은 아래 코드 파일 한 곳이고 `UiStrings.All` 이 모은다 — 규칙 [localization.md](localization.md) §6·§8.
> 시트에 행이 없으면 화면은 코드 ko 폴백이다(`#키` 가 안 나온다). 기준 2026-10-22(할 일 48 후속).

| 접두사 | 개수 | 쓰는 곳 | 키 원본 |
|---|---|---|---|
| `UI.Lobby.*` | 7 | 로비 상호작용 프롬프트(`InteractReady`·`InteractUnready`·`InteractCharacterSelect`) · 준비 `ReadyCount` · 카운트다운 `Countdown`·`CountdownStarting` · 지난 판 결과 버튼 `ShowLastResult` | [LobbyUiStrings.cs](../../Assets/1.Scripts/Lobby/LobbyUiStrings.cs) · `ShowLastResult` 만 [UiStrings.cs](../../Assets/1.Scripts/Localization/UiStrings.cs) |
| `UI.CharacterSelect.*` | 4 | 캐릭터 선택 패널 `LockedNotice`·`CurrentTag`·`Unavailable`·`Hint` | [CharacterSelectUiStrings.cs](../../Assets/1.Scripts/UI/CharacterSelect/CharacterSelectUiStrings.cs) |
| `UI.DemoConnect.*` | 20 | 대기 화면(§4) — 제목·주소·진행 9 + 재합류 `RejoinWaiting`·`JoiningMatch` + 자동 판정 3 + **거절 사유 7**(`GameInProgress`·`LobbyFull`·`HashMismatch`·`Timeout`·`DuplicatePlayerId`·`InvalidPlayerId`·`UnknownReason`) | [DemoConnectionOverlayText.cs](../../Assets/1.Scripts/Network/Session/DemoConnectionOverlayText.cs)(`DemoConnectUiStrings`) — 사유 → 키는 [DemoJoinRejectReason.cs](../../Assets/1.Scripts/Network/Session/DemoJoinRejectReason.cs) |
| `UI.LobbyResult.*` | 10 | 결과 패널 정적 라벨 `Title`·`PlayTimeLabel`·`Close`·`Header.Rank/Player/Damage/Healing/Counters/DamageTaken/Deaths`(프리팹 `LocalizedText`) | [LobbyResultUiStrings.cs](../../Assets/1.Scripts/UI/LobbyResult/LobbyResultUiStrings.cs) |
| `UI.Result.*` | 10 | 결과 라벨 CLEAR/FAILED/ABORTED 등 — 5.ResultScene 과 공용(이미 ko·en 있음) | [UiStrings.cs](../../Assets/1.Scripts/Localization/UiStrings.cs) |

- 🔸 거절 사유 7키·`UI.LobbyResult.*` 10키는 2026-10-22 새로 뺐다(예전엔 코드·프리팹 하드코딩). `LobbyFull` 은 `{max}` 자리표시자(= 3).
- 결과 패널 열 제목은 5.ResultScene 열 제목(`UI.Result.Damage` 등)과 문구가 달라(`가한피해`·`간파 성공`) 키를 나눴다.
- 로비·네트워크 **오류**(`LOBBY_*`·`NET_*`)는 Strings 가 아니라 ErrorCodes 시트 — [localization.md §7](localization.md#7-l4--로비네트워크-오류-코드).
- 번역하지 않는 것: 설정 파일 경고(`DemoConnectionConfig` issues — 로그 전용), 서버 거부 사유(로그 전용), 키 기호 `F`.
- en 추천 번역·행 상태표: `C:/UnityProject/_lane-artifacts/MainProject-WorkTree/48-strings/strings-rows-to-add.tsv`(xlsx 반영 전 초안).
