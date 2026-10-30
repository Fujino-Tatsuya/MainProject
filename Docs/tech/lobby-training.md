# 로비 훈련장 (할 일 86)

> 로비(3.LobbyScene) 훈련실에서 하는 TRAIN_01~03 진행. 이 문서는 T8 에서 흐름·단계별 내용으로 확장한다 — 지금은 대사 출처·로비 무전·브리핑·TRAIN_01 체크 시점·TRAIN_03 상대·진행기 연결·NavMesh 절만 있다.
> 흐름·출격·접속 승인은 [lobby-demo-flow.md](lobby-demo-flow.md) §1.

## 대사 문구 출처 (T6a)

튜토리얼·23호 레이드 자막 문구는 Strings 일반 키 **`Tutorial.Guide.<id>`** 로 조회하고, 시트에 행이 없으면 `TutorialGuideTexts` SO 문구를 쓴다(지속시간·효과음은 SO) —
[tutorial-guide-ui.md](tutorial-guide-ui.md#문구-출처--strings-시트-우선-so-폴백-할-일-86-t6a). 로비 무전(T6b)·브리핑(T6c)의 새 대사도 같은 키 접두로 시트에만 넣어도 `TutorialGuideTextSource` 가 찾는다(SO 행이 없으면 지속시간 = 큐 기본값).

## 로비 무전 (T6b)

명세 v0.2 §4(무전 공통 규칙)·§14(대사 초안), PLAN-86 §3.6. 코드: `Assets/1.Scripts/Lobby/LobbyRadio.cs`(NGO) · `LobbyRadioRules.cs`(순수 — `LobbyRadioIds`·`LobbyRadioSchedule`·`LobbyRadioModel`·`LobbyRadioReceiver`).
테스트: `Assets/Tests/EditMode/Lobby/Editor/LobbyRadioTests.cs`.

- **배치**: `3.LobbyScene` 루트에 `TutorialGuideUI` 프리팹 인스턴스 1개(MapScene 과 같은 프리팹 — 목표 패널·자막·강조). `LobbyRadio` 는 `LobbyTraining/LobbyTrainingDirector`
  (씬 상주 NetworkObject, 진행기·무기 점검·일반 전투와 같은 오브젝트)에 붙는다 — 새 NetworkObject 없음.
- **재생 결정 = 서버만**: `LobbyTrainingDirector.ServerFlowChanged` 구독 → 아래 표의 단계 진입 대사를 `PlayClientRpc(id, 단계 번호)` 로 전 클라에 1회 →
  각 클라 `TutorialGuideUI.ShowSubtitle(id)`. 파티 공통 1회, 클라 로컬 판정 없음. 대상 = 접속자 전원(훈련·브리핑 중 접속해 로비에서 기다리는 사람 포함).
- **늦은 클라·재접속**: 복제 상태 없이 RPC 만 — 지난 대사는 다시 오지 않고 다음 대사부터 받는다. 서버 쪽도 처음 본 상태(스폰 직후 Lobby·늦은 구독)는 기준만 잡고 틀지 않는다.
- **회차당 1회**: 같은 id 는 한 회차에 한 번(단계 진입 대사 + `ServerPlay` 기본값). 판 끝 로비 복귀 = 로비 씬 새로 로드 = 새 `LobbyRadio` 라 다시 나온다.
- 진행기 `LobbyTrainingDirector.cs` 는 **고치지 않았다**(구독만).

### 큐·폐기 규칙

- 일반 무전은 `TutorialSubtitleQueue`(기존) 그대로 — 한 번에 한 줄, 들어온 순서대로.
- **폐기 = 단계 번호 방식**: 서버는 단계(`LobbyTrainingStage`)가 바뀔 때마다 번호를 1 올리고 `DiscardPendingClientRpc(번호)` 를 먼저 보낸다. 대사 RPC 에도 번호를 싣는다.
  클라(`LobbyRadioReceiver`)는 더 큰 번호를 받으면 **대기 줄만** 버리고(`TutorialGuideUI.ClearPendingSubtitles` — 지금 뜬 줄은 끝까지 보인다), 더 작은 번호의 대사는 버린다.
  하위 상태(`LobbyTrainingPhase`)만 바뀐 건 같은 단계라 버리지 않는다.
- **대사 종료 ≠ 목표 완료**: 진행기는 대사를 기다리지 않는다. 대사는 진행을 막지 않고, 목표를 먼저 끝내면 다음 단계에서 남은 대기 줄이 버려진다.
- **브리핑·강제 연출 시작 때 기존 무전 종료**: `LobbyRadio.ServerClear()` — 지금 뜬 줄 + 대기 줄을 즉시 끈다(`TutorialGuideUI.ClearSubtitles`, 목표 패널·대기 줄·강조는 그대로). 호출 = 브리핑 시작(`LobbyBriefing`, 아래 "브리핑" 절).

### 단계 → 대사 표

앞으로만 가는 단계라 (단계, 하위 상태) 진입 = 전이 하나다. 명세 이벤트와 진행기 전이를 이렇게 붙였다.

| 대사 ID | 명세 이벤트 | 붙인 전이(진행기) |
|---|---|---|
| `LOBBY_TRAIN_ROUTE` | 입구 전원 집결 | 입구 집결 확정 → `Train01/Gathering`. 입구 텔레포트(할 일 111)로 이미 첫 위치 안이라 "표적 앞으로 이동해" 문구가 어색할 수 있다 — 문구 미수정(보고만) |
| `TRAIN_01_START` | 첫 훈련 시작 | 첫 위치 집결 확정 → `Train01/InProgress` |
| `TRAIN_01_END` | 개인 목표 전원 완료 | Train01 완료 보고 → `Train02/Gathering` |
| `TRAIN_02_START` | 일반 전투 시작 | 두 번째 위치 집결 확정 → `Train02/InProgress` |
| `TRAIN_02_END` | 일반 전투 완료 | Train02 완료 보고 → `Train03A/Gathering` |
| `TRAIN_03_START` | 간파 연습 시작 | 세 번째 위치 집결 확정 → `Train03A/InProgress` |
| `TRAIN_03_CC` | B 소개 | A 완료 뒤 영역 안 확인 → `Train03B/InProgress` |
| `TRAIN_03_RESET` → `TRAIN_03_WALL_GUIDE` | B→C 초기화 / C 시작 | B 완료 뒤 영역 안 확인 → `Train03C/InProgress`(명세 §9.4 초기화 순간). 같은 진입에 두 줄 — 순서대로 |
| `TRAIN_03_SUPPRESS` | D 제압 진입 | C 완료 보고(제압 시작 순간) → `Train03D/InProgress` |
| `TRAIN_END` | 훈련 종료 | D 완료 보고 → `TrainEnd/Gathering` |
| ~~`BRIEF_ROUTE`~~ | 종료 집결 | **재생 안 함**(할 일 111) — 종료 텔레포트가 이미 브리핑 구역 안에 두므로 이동 안내 불필요. 상수·Strings 키·문구 행은 남겨 둠 |
| ~~`BRIEF_01`~`04`~~ | 브리핑 | **재생 안 함**(할 일 157 ⑨ — 대사 행 `LOBBY_BRIEF_*` 로 대체). 상수·Strings 키·문구 행은 남겨 둠 |

`Lobby → Sortie`(훈련 없이 출격)·`Brief/Gathering`·`Brief/InProgress`·`Brief/Completed`·`Sortie` 진입 대사는 없다(단계가 바뀌면 대기 줄 폐기만).

### 단계 안 사건 대사 — 공개 API

```
LobbyRadio.Active.ServerPlay(string id, bool oncePerRound = true) : bool   // [서버] 큐에 한 줄. 이번 회차에 이미 튼 id·서버 아님·스폰 전·빈 id = false
LobbyRadio.Active.ServerHasPlayed(string id) : bool                       // [서버] 이번 회차에 틀었나(단계 진입 대사 포함)
LobbyRadio.Active.ServerClear()                                           // [서버] 무전 전체 종료(T6c 브리핑·강제 연출)
```

`ServerPlay` 대사도 지금 단계 번호를 달고 가므로, 다음 단계가 시작될 때 아직 안 떴으면 버려진다.

#### Work2 T5c 연결 대상 (레인 B `feature/LobbyTrainingSparring`)

**연결 완료** — `LobbyTrainingSparring` 이 아래 시점의 서버 이벤트(`ServerFirstCounterSucceeded`·`ServerFirstValidWallImpact`·`ServerFinishStarted`)를 낼 때 같은 자리에서 `LobbyRadio.Active.ServerPlay(id)` 를 직접 부른다
(무전이 없으면 경고 한 번 뒤 건너뜀). id 상수는 `LobbyRadioIds`. 문구·지속시간 행은 이미 `TutorialGuideTexts` 에 있다. 세 줄은 `LobbyRadioRules` 단계 진입 표에 넣지 않는다(중복 금지).

| id | 상수 | 호출 시점(서버) | 메모 |
|---|---|---|---|
| `TRAIN_03_COUNTER` | `LobbyRadioIds.Train03Counter` | A(`Train03A/InProgress`)에서 **첫 간파 성공**이 확정된 순간 — 단계 완료 보고(`ServerReportStageCompleted(Train03A)`) **전에** 부른다 | 완료 보고 뒤엔 같은 단계(A/Completed)라 폐기되지 않지만, B 진입이 바로 이어지면 대기 줄이 버려질 수 있다 — 먼저 부르면 지금 뜬 줄이 된다 |
| `TRAIN_03_WALL` | `LobbyRadioIds.Train03Wall` | C(`Train03C/InProgress`)에서 **첫 유효 벽 충돌**(칸 감소 동반, 명세 §9.5)이 기록된 순간 | 같은 넉백이 마지막 칸 감소 → 제압이면 벽 기록 → `ServerPlay` → C 완료 보고 순서(D 진입 때 폐기돼도 `TRAIN_03_SUPPRESS` 가 이어진다) |
| `TRAIN_03_COMBAT` | `LobbyRadioIds.Train03Combat` | D(`Train03D/InProgress`)에서 **제압 중 유효 피해 경험 + 제압 종료**(마무리 체력 설정 직후) | 진행기에 D 제압 종료 시점이 없어 단계 진입 대사로 두지 않았다. 판정 = 상대 모델 `IsFinishing` 이 처음 true 가 된 다음 프레임(`ServerFinishStarted`). 피해 없이 끝난 제압은 무전 없음 |

세 줄 모두 `oncePerRound` 기본값(true)으로 부르면 재시도·중복 이벤트에도 회차당 1번만 나온다.

### 문구·지속시간

- ko 폴백 = `TutorialGuideTexts` SO 행(19줄, 명세 §14 초안 그대로). 추가 = 메뉴 **Tools/UI/로비 무전 문구 추가**(`TutorialGuideUIAuthoring.LobbyRadioTexts`, 빠진 행만 — 고친 문구는 안 건드림).
- Strings 키 = `Tutorial.Guide.<id>` — Export 수집기(`TutorialGuideStringKeys`)가 SO 행에서 자동으로 모은다(`#설명` = "TutorialGuideTexts · 로비 무전"). xlsx 행 추가는 SVN 작업(사람).
- 지속시간 = 기존 행과 같은 폭: 짧은 한 문장 4초(`TRAIN_03_SUPPRESS`·`BRIEF_04`) · 30자 안팎 5초 · 두세 문장 6초 · 45자 넘는 긴 줄 7초(`TRAIN_03_CC`·`BRIEF_01`).
  참고 = `TUT_Z3_*` 3~7초, `BOSS_RAID_*` 3~6초, 기본 6초.
- 목표 패널 문구(`훈련장으로 이동하세요` 등)는 이번 범위가 아니다 — 무전 자막만. 예외 = 브리핑 두 줄(T6c, 아래).

## 브리핑 BRIEF_01 (T6c)

> ⚠ **할 일 157 ⑨(2026-10-30)로 바뀜** — 아래 T6c 흐름(BRIEF_01~04 무전을 한 번에 서버 시간으로 재생·`LobbyBriefingModel`·목표 '작전 설명 중')은 폐기.
> 지금: L30 중앙 집결 0.3초 → 대사 행 L31~L34(`LOBBY_BRIEF_EVAL`·`_FACTORY`(배경 `BRIEF_BG_FACTORY`)·`_REBELLION`(`BRIEF_BG_REBELLION`)·`_SORTIE`)를
> 진행기 공용 대사 스케줄러가 한 행씩 서버 시각으로 넘기고 L34 끝에 L40. `LobbyBriefing` 은 묶음(L31~L34) 입력 잠금(`LobbyBriefingLockSession`+장부, 모든 해제 경로)과 카메라만.
> 목표 패널은 대사 동안 파사드가 숨긴다(`LOBBY_OBJ_BRIEFING` 표시 안 함 — 키 보존). BRIEF_01~04 는 재생 안 함(키 보존). 표시 계약: [handoff-lobby-tutorial-contract.md](handoff-lobby-tutorial-contract.md).
> 아래 잠금 대상·장부·카메라·복구 설명은 그대로 유효하다.

명세 v0.2 §4(강제 연출 규칙)·§10, PLAN-86 §3.6. 코드: `Assets/1.Scripts/Lobby/LobbyBriefing.cs`(NGO) · `LobbyBriefingRules.cs`
(순수 — `LobbyBriefingModel`·`LobbyBriefingLockLedger`·`LobbyBriefingRules`·`LobbyObjectiveIds`). 테스트: `Assets/Tests/EditMode/Lobby/Editor/LobbyBriefingTests.cs`.
`LobbyBriefing` 은 `LobbyTraining/LobbyTrainingDirector` 오브젝트에 붙는다(새 NetworkObject 없음). 진행기 `LobbyTrainingDirector.cs` 는 **고치지 않았다**(구독 + `ServerReportStageCompleted` 호출만).

### 흐름

1. 종료 집결 확정 → `Brief/Gathering`(진입 무전 없음 — 할 일 111) → 브리핑 위치(`Gather_Brief`) 훈련 명단 전원 집결(`gatherHoldSeconds` 3초 — 종료 텔레포트가 이미 구역 안에 둔다, "훈련 종료" 절) → `Brief/InProgress`.
2. **[서버] 시작**(`ServerFlowChanged` 로 `Brief/InProgress` 를 본 순간, 회차당 1회): `LobbyRadio.ServerClear()`(기존 무전 종료) → 접속자 전원 조작 제한 →
   시작 지연 `leadSeconds`(0.5초) 뒤 `BRIEF_01` → 앞 줄 표시 시간이 지날 때마다 다음 줄(`LobbyRadio.ServerPlay`, 단계 번호 그대로라 폐기되지 않는다).
3. **시간 = 서버가 잰다**: 지연 + 대사 표시 시간 합(`TutorialGuideTexts` SO 행 `durationSeconds` — 7+5+6+4초) + 끝 여유 `tailSeconds`(1초) = 23.5초.
   클라 자막 종료 보고를 기다리지 않는다. SO 행이 없거나 표가 비면 줄마다 6초(자막 큐 기본값).
4. **[서버] 끝**: 조작 제한 해제 → `ServerReportStageCompleted(Brief)` 1회 → `Brief/Completed` → 출격 집결 활성.
5. 대표 상호작용·개인 건너뛰기 없음(명세 §4·§10 추천).

### 조작 제한 — 보스 등장 연출 잠금 재사용

- 새 차단 계통 없음. `PlayerEncounterLock.BeginCinematicServer()`/`EndCinematicServer()`(보스 등장 연출용 서버 잠금)를 그대로 쓴다:
  FSM `Cinematic`(이동·기본 공격·스킬 서버 승인 닫힘) · 대시 차단 · 오너 입력 = `PlayerLifeInputPolicy` 가 합쳐 끔 · 진행 중 스킬/공격 정상 종료 · 디버프 해제 · 무적 토큰(`Cinematic`).
  무적·디버프 해제는 로비라 무해해서 그대로 둔다.
- **상호작용(F)**: 이 잠금은 입력 차단 토큰이 아니어서 `LobbyInteractor.IsBlocked` 에 연출 잠금 판정(`PlayerLifeInputPolicy.BlocksForCinematic`)을 더했다 — 캐릭터 선택 프롬프트·F 도 막힌다.
- **대상 = 접속자 전원**: 훈련 명단 밖 늦은 참가자(로비 대기)도 같이 잠근다 — 브리핑 동안 혼자 돌아다니며 출격 구역·상호작용을 쓰지 않게.
  **브리핑 중 새로 스폰된 플레이어**(늦은 접속·재접속)도 0.25초 주기로 찾아 잠근다. 잠금은 복제 값이라 늦게 붙은 클라에도 그대로 적용된다.
- **내가 건 것만 푼다**: `LobbyBriefingLockLedger` 장부. 시작 때 이미 다른 계통(보스 등장 연출 등)이 잠근 사람은 장부에 넣지 않아 브리핑이 끝나도 풀지 않는다
  (`PlayerEncounterLock` 의 연출 잠금은 bool 하나라 장부로 구분한다 — 로비엔 보스 연출이 없어 실제로 겹치지 않는다).

### 복구 경로

| 경우 | 처리 |
|---|---|
| 정상 완료 | `Tick` 완료 신호 → 장부 전원 `EndCinematicServer` → 완료 보고. 목표 패널 '출격 지점에 집결'·카메라 복귀 |
| 단계 이탈(회차 초기화 등 — `Brief/InProgress` 가 아닌 상태를 봄) | 모델 `Abort` → 장부 전원 해제. 남은 대사·완료 보고 없음 |
| 판 끝 로비 복귀·씬 언로드·호스트 셧다운(`OnNetworkDespawn`) | `Abort` + 장부 전원 해제(이미 디스폰된 플레이어는 잠금도 함께 사라졌다). 클라 쪽 목표 패널·카메라도 `OnNetworkDespawn`·`OnDestroy` 에서 되돌린다 |
| 브리핑 중 접속 끊김 | 장부에서만 뺀다(플레이어 오브젝트와 잠금이 함께 사라진다). 남은 사람으로 계속 — 시간은 사람 수와 무관 |
| 브리핑 중 접속 | 주기 검사로 잠근다(위). 지난 대사는 받지 않고 다음 줄부터(로비 무전 규칙) |

- **메인 진입 때 잠금이 남지 않는다**: 출격 집결은 `Brief/Completed` 뒤에만 켜져 브리핑 중 메인 진입이 일어나지 않는다. 또 로비 플레이어 오브젝트는 넘어가지 않는다 —
  로비를 떠날 때 `NetworkLoadingFlowController.UnloadNetworkSourceScene` 이 로비 플레이어를 포함한 동적 객체를 디스폰하고(`LobbyPlayerSpawner` 주석), MapScene 이 새로 스폰한다.

### 표시 — 목표 패널·카메라 (전 피어, 복제된 진행기 단계로)

- **목표 패널**(로비의 `TutorialGuideUI`): `Brief/InProgress` = `LOBBY_OBJ_BRIEFING`('작전 설명 중'), `Brief/Completed` = `LOBBY_OBJ_SORTIE`('출격 지점에 집결'), 그 밖 단계 = 비움
  (이 컴포넌트가 띄운 것만 지운다). 문구 = `TutorialGuideTexts` SO 행 + Strings 키 `Tutorial.Guide.LOBBY_OBJ_*`(Export `#설명` = "TutorialGuideTexts · 로비 목표"). 행 추가 = 같은 메뉴 **Tools/UI/로비 무전 문구 추가**.
- **카메라**: 새 카메라·영상 없음. 보스 엘리베이터 연출이 쓰는 `CameraTargetSwitcher.SetTemporaryFollow(target)` 를 대상만 바꿔 쓴다 —
  브리핑 동안 플레이어 vcam 이 `cameraFocus`(= `Gather_Brief`, 브리핑 위치) 를 따라가고(컷), 끝나거나 중단·디스폰되면 `ClearTemporaryFollow`(현재 추적 대상으로 컷 복귀).
  `cameraFocus` 를 비우면 카메라는 건드리지 않는다. 플레이어가 이미 브리핑 위치에 모여 있어 화면 변화는 작다(위치·연출 강화는 배치 조정 몫).

### 실전 초기화 — 여기서 하지 않는다

명세 §10 "실전 초기화"(체력 최대·쿨타임·보정 제거·암살자 변신 해제·분노 0·거너 과열 0)는 **구현하지 않는다** — 메인 진입 때 MapScene 이 플레이어를 새로 스폰하므로
(로비 오브젝트는 디스폰) 새 오브젝트의 기본값이 곧 실전 초기값이다(메인 결정 10-22). 훈련 보정 체력 하한도 브리핑에선 이미 0 이다(진행기).

## 튜토리얼 클리어 플래그 (할 일 112, 로비 Alt+1)

QA·부스 운영용 예외 키(키 표·게이트 = [debug-hotkeys.md](debug-hotkeys.md) §3). 규칙 원본 = `LobbyTrainingFlow.SetTrainingCleared`(EditMode `LobbyTrainingFlowTests`).

- **누른 사람만**, 서버 확정 → 진행기 복제 목록(`LobbyTrainingDirector.IsTrainingCleared`). 결과는 누른 사람에게만 토스트.
- **켜면**: 입구 집결 인원·훈련 명단(입구 확정 때 고정)·훈련 내부 집결(Train01~TrainEnd·브리핑 위치)에서 빠진다 = 늦은 참가자와 같은 "로비 대기".
  **출격 집결 인원엔 남는다**(접속자 전원). 브리핑 조작 제한은 접속자 전원이라 이 사람도 잠긴다. 캐릭터 변경은 늦은 참가자처럼 허용.
- **훈련 중에 켜면** 즉시 명단에서 빠진다 — 진행 중 집결 구역 명단·체력 하한·무기 점검(TRAIN_01 개인 체크 장부, `ServerRosterMemberRemoved`)이 그 사람을 기다리지 않는다.
  그래서 **명단이 비면** 훈련·브리핑을 마친 것으로 보고 `Brief/Completed`(출격 집결 활성)로 넘긴다 — 아무도 없는 집결에서 멈추지 않게.
- **끄면**: 플래그만 지운다. 이미 고정된 이번 회차 명단엔 다시 넣지 않고, **다음 입구 확정(판 끝 로비 복귀·Alt+3 리로드 뒤 새 회차)부터** 다시 훈련 명단.
  Lobby 단계(입구 확정 전)에 끄면 이번 입구 확정부터 포함된다.
- **유지·정리**: 진행기는 로비 씬과 함께 새로 생기므로 서버 세션 정적 장부에 적어 다음 진행기로 잇는다. 연결 종료(재접속 = 새 clientId 라 꺼진 채로 시작)·호스트 셧다운 때 지운다.
- Lobby 단계에서 전원이 켜면 입구 집결 인원 0명 = 훈련이 시작되지 않는다(출격만).
- ~~훈련을 끝까지 하면 자동으로 켜진다(할 일 111)~~ — **폐지**(할 일 157 Q3 A): 훈련 완료 자동 부여 없음, 매 회차(판 끝 로비 복귀 = 새 진행기·runId +1) L00 부터. 플래그는 Alt+1 로만 켜고 끈다.
- **재접속(할 일 157 ⑩)**: 같은 playerId 로 새 clientId 가 접속하면 훈련 명단 소속·라커 방문·T10 체크 마스크를 이어 간다(명단이 훈련장 안인 단계면 훈련장 안으로 텔레포트). 매핑이 없으면 늦은 참가자(로비 대기, 출격 집결엔 포함). 클리어 플래그는 이어 가지 않는다.

## 훈련 종료 — 클리어 플래그·텔레포트 (할 일 111)

**원인(10-27)**: 안내 스텝 ③(출격)은 `TrainingCleared`(진행기 복제 `trainingCleared` = Alt+1 과 같은 목록) 또는 `Brief/Completed` 로 켜진다.
그런데 이 목록을 켜는 곳이 **Alt+1(`ServerSetTrainingCleared`) 하나뿐**이었다 — 훈련을 정상 완료해도 플래그가 안 켜져,
판 끝 로비 복귀·Alt+3 리로드(새 진행기 = Lobby 단계, 명단 비움) 뒤엔 다시 ②(튜토리얼)로 안내했다. 스텝 판정은 맞는 상태를 읽고 있었다(판정 변경 없음).

**수정 — 메커니즘 하나**: 종료 집결 확정(`TrainEnd` 확정 → `Brief/Gathering`, `LobbyTrainingFlow.OnGatherConfirmed`)에서 **훈련 명단 전원에게 같은 클리어 플래그**를 켠다.
- 새 상태 없음 — 같은 `_trainingCleared` → 같은 복제 목록 → 같은 서버 세션 장부(`ServerClearedStore`, 로비 복귀·리로드를 넘어 남고 연결 종료 때 지움). 늦은 접속도 NetworkList 로 그대로 받는다.
- Alt+1 과 다른 점 하나: **명단에서 빼지 않는다** — 이번 회차 브리핑 집결 인원·캐릭터 잠금은 그대로(Alt+1 처럼 빼면 명단이 비어 브리핑을 건너뛴다).
- 늦은 참가자(명단 밖)는 훈련을 안 했으니 켜지지 않는다.
- 결과(스텝 판정 표 그대로): 브리핑 집결·진행 중 = 없음(줄 4) → 브리핑 완료 = ③ → 다음 회차 Lobby = ③(줄 8, 입구 집결 인원에서도 빠짐 — 훈련을 다시 하려면 Alt+1 로 끈다).

**종료 텔레포트 — 자리 = 브리핑 구역(`Gather_Brief`) 안 (10-27 결정)**: 같은 순간(종료 집결 확정) 서버가 훈련 명단 전원을
진행기가 이미 들고 있는 `briefZone`(PartyGatherZone) 박스 안으로 옮긴다 — `LobbyTrainingDirector.TeleportRosterInto`(입구 텔레포트와 공용, 아래 절).
- 흐름: 종료 집결 확정 → `Brief/Gathering`(구역 켜기는 다음 진행기 Update 의 `ApplyZones`) → 명단 전원이 이미 구역 안 → 브리핑 집결 n/m 이 바로 차고
  유지 `LobbyRules.gatherHoldSeconds`(3초) 뒤 확정 → `Brief/InProgress`. 원격 클라는 오너 RPC → 위치 복제가 돌아온 뒤에 안으로 세므로 왕복 지연만큼 늦게 찬다(호스트는 즉시).
  집결 판정 조건(살아 있음·Idle/Move)은 그대로 — 텔레포트 순간 행동 중이면 끝난 뒤 센다.
- 자리 = **`LobbyTrainingEndPlacement`**(순수 규칙, `Assets/1.Scripts/Lobby/LobbyTrainingEndPlacement.cs`, EditMode `LobbyTrainingEndPlacementTests`):
  박스 로컬 XZ, 가장자리 여백 1m 안쪽, 간격 1.5m 격자. 한 줄 = 로컬 X 방향(지금 박스 6×11.5 → 한 줄 3명, 7줄 = 21자리), 묶음·마지막 줄은 가운데 정렬(혼자면 정중앙).
  자리보다 많으면 처음 자리부터 순환(겹침), 박스가 여백보다 좁으면 그 축은 중심 한 줄. 박스 회전·스케일 반영(여백·간격은 월드 m).
- 높이 = 구역 원점 바닥면(로컬 y 0 을 박스 높이 범위 안으로 자름) + 0.5m(스폰·멈춤 탈출과 같은 보정). 구역 원점이 바닥에 있다고 본다(지금 `Gather_Brief` y 0 = 바닥 y 0).
- 이동 = 기존 서버 텔레포트 `PlayerFallRecovery.RelocateServer`(서버 → 오너 RPC → 모터 `TeleportAuthoritative`, 로비 추락 복귀·전멸 리스폰과 같은 경로). 새 RPC·상태 없음.
  **바라보는 방향은 바꾸지 않는다**(이 경로에 회전 인자가 없다 — 돌리려면 RPC 를 새로 만들어야 해서 보류).
- `briefZone` 이 비면 경고 1회(지점마다) 후 텔레포트 생략(훈련 흐름은 그대로).
- 옛 방식(별도 점 `LobbyTrainingEndTeleport` + `LobbyGuideSteps.prefab` 자식 `TrainingEnd/TrainingEndTeleport_1~3`)은 메커니즘 하나로 지웠다.
  점을 직접 두는 방식이 다시 필요하면 커밋 `7313de77` 의 `LobbyTrainingEndTeleport.cs`(+`.meta`, guid 4daf9530…)·프리팹 `TrainingEnd` 자식을 되살리고 진행기 목적지를 그쪽으로 돌린다.

## 입구 텔레포트 — 자리 = 첫 위치(`train01StartZone`) 안 (할 일 111, 10-27)

훈련장 문이 막혀 있으므로(아래 절) 입구 집결 확정 순간 서버가 **훈련 명단 전원**을 훈련장 안으로 옮긴다.
- 대상 규칙 = `LobbyTrainingFlow.TeleportTargetOnConfirm`(순수, EditMode `LobbyTrainingFlowTests`): 입구(`Entrance`) → `Train01Start`, 종료(`TrainEnd`) → `Brief`, 그 밖 확정 = 없음.
  둘 다 **확정 직후 켜지는 다음 집결 구역**이다(입구 확정 → `Train01/Gathering` → 켜지는 구역 = `Train01Start` 하나).
- 실행 = 종료 텔레포트와 같은 `LobbyTrainingDirector.TeleportRosterInto(대상, 확정 지점)` 하나 — 자리 `LobbyTrainingEndPlacement`(여백 1m·간격 1.5m·가운데 정렬·순환),
  높이 = 구역 원점 바닥면 + 0.5m, 이동 `PlayerFallRecovery.RelocateServer`, 새 RPC·상태 없음. 이름의 "End" 는 처음 용도 그대로 남겼다(.cs 이름·guid 변경 회피).
- 명단 = 입구 확정 때 고정된 훈련 명단(클리어 플래그 켠 사람 제외). 늦은 참가자·명단 밖은 옮기지 않는다. `train01StartZone` 이 비면 경고 1회 후 생략.
- 이어지는 흐름: `Train01/Gathering` 구역이 켜지면(다음 진행기 Update) 명단 전원이 이미 안 → `gatherHoldSeconds`(3초) 뒤 확정 → `Train01/InProgress`(무기 점검).
  무전은 확정 순간 `LOBBY_TRAIN_ROUTE`("전원 확인. 표적 앞으로 이동해. 먼저 각자의 무기를 점검하겠다.", 6초)가 뜨고, 3초 + 왕복 지연 뒤 `TRAIN_01_START` 가 큐에 이어진다.
  이미 표적 앞이라 "이동해" 가 어색할 수 있다 — 문구는 고치지 않았다(기획 판단).

## 훈련장 문 — 막힘 (할 일 111, 10-27)

- **훈련장 문(`Door_Training`) 하나만** 여닫힘을 없애고 **막았다** — 훈련장 출입은 입구 텔레포트(위 절)로만. 나머지 로비 문 3개(`Door_Administrator`·`Door_Locker`·`Door_Operations`)는
  근접 자동 문(`LobbyProximityDoor`·`LobbyProximityDoorRules`, 할 일 78·99) 그대로다 — 공용 문 프리팹 `PF_Room_DoorSingle_01_v003.prefab` 은 손대지 않는다.
- 적용 위치 = 중첩 프리팹 **`PF_Lobby_Props_v003.prefab` 안 `Door_Training` 인스턴스 오버라이드** 세 개:
  `LobbyProximityDoor` **컴포넌트 제거**(`m_RemovedComponents` — 끄기만 하면 `Awake` 가 문짝에 키네마틱 Rigidbody 를 붙이므로 제거로 했다) +
  문짝 콜라이더(비볼록 MeshCollider, 레이어 7 `Wall`) **`m_Enabled: 0`** +
  **추가 `BoxCollider`**(트리거 아님, 문짝 메시 bounds 그대로 — 로컬 center (-0.63, 0.95, -0.08)·size (1.27, 1.88, 0.28), 같은 오브젝트라 레이어 `Wall`). 문틀 개구부 전체를 닫힌 문짝 모양으로 막는다.
- `PF_Lobby_Complete_v016` 과 `3.LobbyScene` 에는 이 문을 덮어쓰는 오버라이드가 없다(에디터에서 읽어 확인: 세 곳 모두 `Door_Training` 만 근접 문 컴포넌트 없음·MeshCollider 꺼짐·BoxCollider 있음).
  런타임에 이름으로 문을 찾아 끄는 코드는 두지 않는다.

## 바닥 안내 화살표 (할 일 111)

다음 갈 곳을 바닥에 흐르는 화살표로 알려 준다. **씬 배치는 사용자가 직접**(프리팹만 제공, 씬 YAML 은 건드리지 않았다).
두 종류를 **같이 둔다** — 꺾이는 길 안내 = **경로형**(기본 권장), 짧은 직선에 흐르는 띠 = **직선 띠**.

### 경로형 — `LobbyFloorArrowPath.prefab` (점선 + 삼각형, 꺾은선)

- **프리팹**: `Assets/2.Prefabs/Lobby/FloorArrow/LobbyFloorArrowPath.prefab`
  - 루트 = `LobbyFloorArrowPath`. 켜고 끄기는 부모 `LobbyGuideSteps` 가 이 루트를 통째로 한다(아래 "안내 스텝").
  - 자식 `Start`·`Waypoint1`·`End` = 경로 점(빈 오브젝트), `Pieces` = **구운 조각**(내장 Quad + 공유 머티리얼 `M_LobbyFloorArrowPath`).
  - 셰이더 `Lobby/FloorArrowPath`(`Assets/1.Scripts/Lobby/Shaders/LobbyFloorArrowPath.shader`, URP Unlit 반투명), 텍스처 `50.Art/UI/Navigate/ArrowLineSet.png` **참조만**.
- **세팅법**: 프리팹을 놓고 `Start`·`Waypoint*`·`End` 자식을 바닥 위로 옮긴다. 꺾이는 점이 더 필요하면 빈 자식을 만들어 `waypoints` 목록에 순서대로 넣는다.
  점은 루트의 **직속 자식**으로 두는 게 좋다(로컬 좌표를 그대로 써서 어디 놓아도 다시 굽지 않는다. 다른 곳의 Transform 도 되지만 루트 기준으로 환산).
- **굽기**: 에디터에서 값을 바꾸거나 점을 옮겨 놓으면(드래그가 끝난 뒤) **자동으로 다시 굽는다**. 인스펙터 아래 **`다시 굽기`** 버튼·컴포넌트 ⋮ 메뉴 `다시 굽기` 로 바로 굽는다.
  기즈모 선이 **빨간색이면 구운 결과가 낡음**. 프리팹 모드·씬 인스턴스 둘 다 Undo 된다. Play 중·디스크의 프리팹 에셋(열지 않은 상태)에선 굽지 않는다.
  - 씬 인스턴스에서 조각이 줄면 프리팹에서 온 조각은 지울 수 없어 **꺼 둔다**(오버라이드). 깨끗이 하려면 프리팹 모드에서 굽는다.
  - `Pieces` 아래에 다른 오브젝트를 넣지 말 것 — 굽기가 순서대로 재사용한다.
- **런타임 비용**: Play·빌드에선 아무것도 만들지 않는다. 켜질 때 조각마다 `MaterialPropertyBlock`(UV 영역·경로 거리·색·흐름)만 넣는다. 머티리얼 복제·`new Mesh`·`new Texture2D` 없음.
  흐름은 셰이더가 **조각별 경로 거리 + `_Time`** 으로 불투명도 물결을 앞으로 민다(조각 단위로 밝아졌다 어두워짐) — 매 프레임 스크립트 없음.
- **배치 규칙**(`LobbyFloorArrowPathLayout`, EditMode `LobbyFloorArrowPathLayoutTests`):
  - 구간마다 따로 등간격 — 양 끝 여백을 뺀 길이를 `spacing` 에 가장 가까운 간격으로 나눈다(구간마다 실제 간격이 조금 다를 수 있다).
  - 꺾임 여백 = max(`spacing`/2, (가장 긴 조각 길이 + 가장 넓은 조각 폭)/2) — 직각에서 양쪽 조각이 모서리를 넘거나 겹치지 않는다. 예각(직각보다 좁은 꺾임)은 겹칠 수 있다.
  - 구간이 여백보다 짧으면 그 구간엔 조각이 없다. 같은 위치의 점(길이 0 구간)은 건너뛴다.
  - 시작 조각은 앞 끝이 시작점에 닿게, 큰 삼각형은 **중심 = 도착점**. 작은 삼각형 = 경로 전체 순서로 `triangleEvery` 번째마다.
  - 점선은 긴 쪽, 삼각형은 꼭짓점이 그 구간 진행 방향.
- **SVN 슬라이스 의존 없음**: 점선·삼각형 영역은 컴포넌트 `dashUv`·`triangleUv`(UV 0~1, 왼쪽 아래 기준)에 직접 들고 있다.
  기본값 = 사용자가 잘라 둔 `ArrowLineSet_0`(x317 y556 w280 h125)·`ArrowLineSet_1`(x726 y540 w211 h188) 과 같은 픽셀 사각형 / 1254. `.meta` 슬라이스가 커밋되지 않은 PC 에서도 같게 보인다.
  텍스처 그림이 바뀌면 이 두 값만 고친다.

#### 인스펙터 — `LobbyFloorArrowPath`

| 필드 | 기본 | 뜻 |
|---|---|---|
| `start` / `waypoints` / `end` | 프리팹 자식 | 경로 점. 순서대로 잇는다 |
| `material` | `M_LobbyFloorArrowPath` | 모든 조각이 공유 |
| `spacing` | 0.8 | 조각 중심 간격(m) |
| `dashLength` / `dashWidth` | 0.45 / 0.2 | 점선 조각 길이·폭(m) |
| `triangleEvery` | 4 | 몇 조각마다 작은 삼각형(0 = 없음) |
| `smallTriangleSize` | 0.35 | 작은 삼각형 폭(m), 길이는 텍스처 비율 |
| `endTriangleSize` | 0.9 | 도착점 큰 삼각형 폭(m) |
| `heightOffset` | 0.02 | 바닥에서 띄우는 높이 |
| `color` | 노랑 α0.9 (HDR) | 색·투명도 |
| `flowSpeed` | 2 | 물결 속도(m/초), 0 = 정지, 음수 = 뒤로 |
| `flowWavelength` | 3 | 물결 한 주기(m) |
| `flowMinAlpha` | 0.35 | 물결 골 불투명도 배율(1 = 물결 없음) |
| `dashUv` / `triangleUv` | ArrowLineSet 영역 | 아틀라스 안 점선·삼각형(꼭짓점 위) 영역 |
| `piecesRoot` | `Pieces` | 구운 조각 부모 — 손대지 말 것 |

색·흐름은 다시 굽지 않고 바로 반영된다. 나머지(모양·점)는 다시 굽는다.

**혼자 놓으면 늘 켜져 있다**. 스텝에 따라 켜고 끄려면 `LobbyGuideSteps.prefab` 의 자식으로 쓴다(아래).

### 직선 띠 — `LobbyFloorArrow.prefab` (병행 유지)

- **프리팹**: `Assets/2.Prefabs/Lobby/FloorArrow/LobbyFloorArrow.prefab`
  - 루트 = `LobbyFloorArrowStrip`, 자식 `Visual` = 내장 Quad + `M_LobbyFloorArrow`. 늘 켜져 있다(스텝 켜고 끄기 없음).
  - 셰이더 `Lobby/FloorArrow`(`Assets/1.Scripts/Lobby/Shaders/LobbyFloorArrow.shader`, URP Unlit 반투명) — 텍스처는 사용자가 `50.Art/UI/Navigate/ArrowLineSet.png` 로 바꿨다(10-27, 처음엔 `arrow2.png`). **참조만**(SVN 파일 수정 없음).
  - 흐름은 **셰이더가 `_Time` 으로 UV 를 민다** — 매 프레임 스크립트 없음. 수치는 `MaterialPropertyBlock` 으로 넣어 머티리얼 복제·런타임 텍스처/메시 생성이 없다.
- **배치**: 루트 위치 = 띠 **시작점**, 루트 **+Z(파란 축) = 화살표 진행 방향**. 바닥 위에 놓고 Y 축으로 돌려 방향을 맞춘다. 길이는 앞(+Z)으로 뻗는다.
  꺾이는 길·스텝 안내는 위 경로형 + 아래 `LobbyGuideSteps` 를 쓴다.

#### 인스펙터 — `LobbyFloorArrowStrip` (값을 바꾸면 편집 중에도 바로 보인다)

| 필드 | 기본 | 뜻 |
|---|---|---|
| `length` | 4 | 띠 길이(m), 루트에서 +Z 로 |
| `width` | 1 | 띠 폭(m) |
| `arrowSpacing` | 1 | 화살표 하나가 차지하는 길이(m). 폭과 같으면 원본 비율 |
| `heightOffset` | 0.02 | 바닥에서 띄우는 높이(겹침 깜빡임 방지) |
| `color` | 하늘색 α0.9 (HDR) | 화살표 색·투명도 |
| `flowSpeed` | 1.5 | 흐름 속도(m/초), 음수면 뒤로 |
| `endFade` | 0.15 | 양 끝을 지우는 구간(길이 비율 0~0.5) |

`length`·`width`·`heightOffset` 은 `Visual` 자식의 위치·스케일로 들어간다 — `Visual` 트랜스폼을 손으로 고치지 말 것(값이 덮인다).

### 안내 스텝 — `LobbyGuideSteps.prefab` (경로 3개 중 지금 것 하나만)

타이틀 → 로비에 들어온 플레이어를 순서대로 **① 캐릭터 선택 방 → ② 훈련장 입구 집결 → ③ 출격 집결** 로 안내한다. 스텝마다 경로형 화살표 하나, **지금 스텝 하나만 켠다**.

- **프리팹**: `Assets/2.Prefabs/Lobby/FloorArrow/LobbyGuideSteps.prefab`
  - 루트 = `LobbyGuideSteps`(`Assets/1.Scripts/Lobby/LobbyGuideSteps.cs`).
  - 자식 3개 = `LobbyFloorArrowPath.prefab` 중첩 인스턴스 `Step1_CharacterSelect` / `Step2_Tutorial` / `Step3_Sortie`. 각자 `Start`·`Waypoint1`·`End` 가 임시 자리(서로 안 겹치게 대략 배치).
- **배치**: 씬에 이 프리팹 **하나**를 놓고, 스텝마다 `Start`(로비 들어오는 쪽/앞 스텝 끝)·`Waypoint*`·`End`(목적지 — 캐릭터 선택 오브젝트 앞, 입구 집결 박스 안, 출격 집결 박스 안)를 바닥 위로 옮긴다.
  옮기면 그 경로가 자동으로 다시 구워진다(경로형과 같음). 씬 참조 필드는 없다 — 진행기·구역·캐릭터 선택 오브젝트는 Play 때 찾는다.
- **편집 중 보기**: 편집 모드에선 세 경로가 다 보이고 기즈모 글자(`1 캐릭터 선택` 등)가 경로 시작에 뜬다. 인스펙터 `editorPreview` 로 한 스텝만 볼 수 있다
  (씬 뷰 보기 숨김 `SceneVisibilityManager` — 씬·프리팹에 저장되지 않고 Play·빌드에 영향 없음). `All` 로 되돌리면 다 보인다.
- **판정**: 전 피어 로컬, **내 화면만**. 매 프레임 복제된 진행기(`LobbyTrainingDirector.Active` 의 단계·하위 상태·훈련 명단·클리어 플래그·출격 카운트다운)와 내 플레이어 위치를 읽는다.
  새 NetworkObject·NetworkVariable·RPC·콜라이더·타이머 없음. 규칙 원본 = `LobbyGuideStepRules`(EditMode `LobbyGuideStepRulesTests`).
- **완료 조건(기존 것 재사용)**:
  - ① 캐릭터 = 캐릭터 선택 `LobbyInteractable` 의 **F 사정거리 안에 한 번 들어감**(프롬프트 뜨는 범위 그대로 — 할 일 161부터 반경이 아니라 라커룸 `Area_Locker` 박스, `area` 칸). **세션 래치** — 로비 리로드(Alt+3)·판 끝 로비 복귀를 넘어 남고, 연결이 끊기면(타이틀로 나감) 지운다.
    캐릭터를 바꿀 수 없는 상태(훈련 명단 잠금·출격 카운트다운)면 래치와 무관하게 건너뛴다(`LobbyTrainingFlow.CharacterChangeBlock` 재사용).
  - ② 튜토리얼 = 진행기 입구 구역(`LobbyGatherPoint.Entrance` 의 `PartyGatherZone`) **박스 안**이면 끔(집결 대기), 입구 확정(= 훈련 시작, 단계가 Lobby 를 벗어남)이면 끝.
  - ③ 출격 = 진행기 출격 구역 박스 안이면 전부 끔. 출격 확정(Sortie 단계)이면 끝.

#### 판정 표 (위에서부터 처음 맞는 줄)

| # | 입력 상태 | 켜는 스텝 |
|---|---|---|
| 1 | 진행기 없음·미스폰 / 내 플레이어 없음 / 네트워크 꺼짐 | 없음 |
| 2 | 단계 = Sortie (메인 진입 중) | 없음 |
| 3 | 내가 출격 집결 구역 박스 안 | 없음 |
| 4 | 훈련 명단 + 단계 ≠ Lobby + (Brief/Completed 아님) = 훈련·브리핑 진행 중 | 없음 (훈련 안 집결 구역이 안내) |
| 5 | 캐릭터 래치 꺼짐 + 캐릭터 변경 가능(`CharacterChangeBlock` = None) | ① 캐릭터 선택 |
| 6 | 단계 = Lobby + 클리어 플래그 꺼짐 + 입구 구역 박스 안 | 없음 (집결 대기) |
| 7 | 단계 = Lobby + 클리어 플래그 꺼짐 | ② 튜토리얼 |
| 8 | 그 밖 — 클리어 플래그 켜짐(훈련 완료·Alt+1) / 늦은 참가자·명단에서 빠진 사람(훈련 중) / Brief/Completed | ③ 출격 |

**건너뛰기 예**

| 상황 | 결과 |
|---|---|
| 처음 로비 입장 | ① → (F 반경 도착) ② → (입구 박스 안) 없음 → (훈련 시작) 없음 → (브리핑 완료) ③ → (출격 박스 안) 없음 |
| 훈련 없이 바로 출격 구역으로 감 | 출격 박스 안에선 없음(줄 3), 나오면 다시 ②(줄 7) |
| 튜토리얼 클리어 플래그(Alt+1) | 캐릭터 래치 있으면 바로 ③, 없으면 ① → ③ |
| Alt+3 로비 리로드 / 판 끝 로비 복귀 | 캐릭터 래치가 남아 ① 건너뜀 → 새 회차 Lobby 이면 ② (클리어 플래그면 ③ — 훈련을 끝까지 했으면 켜져 있다) |
| 이미 훈련 진행 중에 들어온 늦은 참가자 | 캐릭터 변경 가능하므로 ① → ③(출격 구역에서 대기, 브리핑 완료 뒤 출격 집결이 켜짐) |
| 훈련 명단인데 브리핑 완료(Brief/Completed) | 캐릭터 잠금이라 ① 건너뛰고 ③ |
| 출격 카운트다운 중 | ① 건너뜀(캐릭터 잠금). 카운트다운은 전원이 박스 안일 때만이라 보통 없음 |

**메커니즘 정리(할 일 111 후속)**: 처음 만든 단계 체크 컴포넌트(`LobbyTrainingStageVisibility`, 미병합)는 **삭제**하고 바닥 경로 표시는 `LobbyGuideSteps` 가 전담한다.
단계 판정은 스텝 규칙(줄 2·4·7·8)에 흡수됐다. `LobbyFloorArrowPath.prefab`·`LobbyFloorArrow.prefab` 에서도 그 컴포넌트를 뺐다 — **혼자 놓은 경로형·직선 띠는 Play 에서도 늘 켜져 있다**.

전이 원본 = `LobbyTrainingFlow`(`IsPointActive`·`OnGatherConfirmed`·`ReportStageCompleted`). 튜토리얼 클리어(Alt+1)로 명단이 비면 바로 `Brief/Completed` 로 넘어간다.

## TRAIN_01 무기 점검 — 체크 시점

개인 6항목은 존1 서버 훅을 그대로 재사용해 센다(`LobbyTrainingWeaponCheck` 구독 → `LobbyWeaponCheckRules`/`LobbyWeaponCheckModel` 판정).
행동한 플레이어 본인만, 항목별 최초 1회. 거부된 요청·조준 취소는 어느 훅도 나지 않아 체크되지 않는다.
**체크 시점은 지금 구현을 유지한다**(사용자 확정 2026-10-22, PLAN-86 §7) — 명세 v0.2 §7 의 "실제 효과 시점"으로 옮기지 않는다.

| 항목 | 팔라딘(가붕이) | 거너 | 암살자(일반 / 변신) | 훅 · 시점 | 명세와 차이(사용자 확정 10-22 유지) |
|---|---|---|---|---|---|
| 기본 공격 | 콤보 한 타 시작 | 한 발 발사 | 한 타 시작(강타 포함, 변신 공통) | `Player.ServerBasicAttackStarted` — 한 타 실제 시작 | **적중 무관**(허공도 체크). R 대상 확정 좌클릭은 이 경로가 아니다 |
| Q | `FirstMeleeMainSkill` 승인 | `GunnerChargeLaserSkill` **발사** | `AssassinDashStrikeSkill` 승인(일반·변신 같은 Main 슬롯) | 거너 = `GunnerChargeLaserSkill.ServerFired`(충전 시작 승인은 무시), 나머지 = `PlayerSkillController.ServerSkillUsed` 승인 | — |
| E | `FirstMeleeSubSkill` 승인 | `GunnerCoolBackstepSkill` 승인 | 일반 `AssassinEnhanceSkill` / 변신 `AssassinCircleStrikeSkill`(좌클릭 확정) 승인 | `ServerSkillUsed` 승인 | **팔라딘 E = 방어 자세 시작 승인** — 클립 Custom0 방어 창이 열리기 전 |
| R | `FirstMeleeUltimateSkill` 승인 | `GunnerTrackingLaserSkill` 대상 확정 승인 | `AssassinTransformSkill` 승인 | `ServerSkillUsed` 승인 | **거너 R = 대상 확정 승인** — Custom0 레이저 생성보다 약간 이르다 |
| 우클릭 스킬 | `FirstMeleeInterruptSkill` 승인 | `GunnerInterruptSkill` 승인 | `AssassinInterruptSkill` 승인(일반·변신 공통) | `ServerSkillUsed` 승인 | 명세의 '간파' 단독 체크 대신 우클릭 스킬 사용 |
| 대시 | 대시 승인 | 대시 승인 | 대시 승인 | `PlayerDashController.ServerDashApproved` — 승인되고 실제 진행(거부·즉시 중단 제외, 대시 캔슬 포함) | — |

- 승인 = `PlayerSkillController.StartSkillServer` 끝의 `ServerSkillUsed` — 쿨타임을 수동 커밋하는 스킬(암살자 E 등)도 승인 시점에 난다.
- 발사 신호로 세는 스킬은 `GunnerChargeLaserSkill` 하나뿐이다(`TryGetApprovalItem(countsOnFire)`). 다른 스킬을 발사·효과 시점으로 바꾸려면 이 규칙과 훅을 같이 고친다.

## TRAIN_03 훈련 상대 `TrainingSparringBot` (T5b) · 진행기 연결 `LobbyTrainingSparring` (T5c)

- **프리팹**: `Assets/2.Prefabs/Lobby/TrainingSparringBot.prefab` = `SpinnerBot.prefab` 의 **Variant** 에서 `SpinnerBot` 컴포넌트를 지우고 `TrainingSparringBot` 을 얹었다
  (외형·애니메이터·`MonsterCounterWindow` 창 길이·`SpinnerBotData` 는 SpinnerBot 저작값을 따라간다). `DefaultNetworkPrefabs` 등록.
- **규칙 값 = 23호 데이터** `counterRules` = `No23_A.asset`(TwentyThree.prefab 의 data) — 그로기·취약·넉백·반동·칸 감소·제압 시간·배율·정면 각도. 훈련 쪽 값은 `finishHealth`(D 마무리 체력, 임시 60) 하나.
- **판정**: 단계 = `TrainingSparringModel`(T5a), 게이지·벽·처치 방지 = `TrainingSparringRules`. 23호 코드(`TwentyThreeBoss`)는 읽고 같은 규칙을 이 상대 안에 다시 썼다 — 23호 규칙이 바뀌면 같이 맞춘다(PLAN-86 R3).
- **이동 없음**: 배치 위치에서 돌아보고 사거리(`SpinnerBotData.attackRange`) 안이면 간파 창 → 채찍. 넉백으로 밀린 자리에서 계속한다(벽마다 원위치 복귀 없음, 명세 §9.5).
  base 추격·리쉬 복귀(체력 회복)를 쓰지 않아 C 위치와 지정 벽 거리가 설계대로 유지된다.
- **벽**: 넉백·Q 밀림 틱마다 `UnitWallImpacts.ServerProbe`(넉백 1회 = 1회). 결과 콜라이더가 `designatedWalls` 에 있을 때만 유효 — 씬 배치(T5c, 아래)가 `ServerConfigureArena(C 위치, 지정 벽)` 로 넣는다.
- **간파 성공 쿨 감소**: 할 일 90 으로 게임 전체에서 제거 — 훈련 상대 별도 처리 없음.
- **칸 UI**: 머리 위 중간보스 바(`MonsterHealthBar_MidBoss`)를 이 상대의 `IOverheadHealthBarRule` 로 C·D 에만 켠다. 게이지 칸은 바 이름 자리 `칸 n/m`, D 제압 중엔 `제압된 상대를 공격하세요 n초`(임시 — 문구 `LobbyUiStrings.Train03*`). 바 채움은 체력.
  - **D 마무리**: 제압 중 유효 피해가 있었고 제압이 끝나 마무리로 넘어가면(마무리 체력 60 적용·처치 방지 해제) 바 이름이 `마무리하세요`(`UI.Lobby.Train03Finish`)로 바뀌고 칸 n/m 은 숨긴다 — 체력 바(60→0)는 그대로. 게이지는 23호 규칙대로 제압 종료 때 가득 복원(표시만 숨김).
    판단 = `TrainingSparringModel.IsFinishing`(D ∧ 마무리, 사망 전까지), 표시 = 서버 쓰기 복제 bool `_finishPrompt` = `IsFinishing ∧ 제압 아님`(늦은 참가 동일, RPC 없음). 스폰·B→C 초기화·사망은 모델에서 자동 false, 마무리 중 다시 제압되면 제압 안내가 우선.
    피해 없이 끝난 제압은 지금처럼 칸 5/5 로 다시 시작.
- **이벤트(서버)**: 간파 성공·CC 적용/종료·지정 벽 유효 충돌·칸 감소·제압 진입/종료·제압 중 피해·사망·단계 완료(`ServerStageCompleted`)·B→C 초기화 완료(`ServerResetExecuted` — 플레이어 쪽 `TrainingResetOptions.PracticeReset` 을 진행기가 이어 건다).

### 진행기 연결 (T5c)

`LobbyTrainingSparring` — `LobbyTrainingDirector` 와 같은 오브젝트(`LobbyTraining/LobbyTrainingDirector`)에 붙어 `ServerFlowChanged` 를 구독한다(T3·T4 와 같은 방식).
"언제 한 번만"은 순수 `LobbySparringLink`(EditMode `LobbySparringLinkTests`), 단계·CC 판정은 상대 쪽 그대로.

| 시점 | 하는 일 |
|---|---|
| 진행기 Train03A 진행(시작 집결 확정 — 집결 대기 중엔 상대 없음, 사용자 10-22) | 상대를 `SpawnAB` 에 스폰(이미 있으면 재사용) → `ServerConfigureArena(PointC, 지정 벽)` → 같은 평가에서 Train03A 진행 알림. 판단 = `LobbySparringLink.BotPresent(단계, 하위 상태)` |
| Train03A~D 가 진행 중이 될 때 | `ServerNotifyFlowStageStarted(단계)` 1회(앞으로만). Train03C = 상대 B→C 초기화(C 위치 재배치) |
| 상대 `ServerResetExecuted` | 훈련 명단 `ServerResetTrainingRoster(PracticeReset)` — 회차당 1회(체력·Q/E/R/우클릭 쿨·대시, 분노 재지급 없음) |
| 상대 `ServerStageCompleted` | **다음 프레임** `ServerReportStageCompleted(Train03A~D)` 1회 — 진행기가 그 단계를 진행 중일 때만, 어긋남·중복·역순은 경고 후 무시 |
| Train03 밖(TrainEnd 등)·진행기 디스폰(판 끝 로비 복귀) | 살아 있는 상대 디스폰·회차 끝(다음 회차는 기록을 새로). D 에서 처치된 상대는 사망 연출 뒤 스스로 디스폰 |

- A→B·B→C 전이는 진행기 쪽 영역 안 확인(`Gather_Train03Area`, 0초)을 거친다. C→D 는 제압 시작 순간 바로.
- **무전 연결(서버 C# 이벤트, `LobbyTrainingSparring.Active` 에서 구독 가능)** — Train03 회차당 1회, 판정 콜백 밖(다음 프레임)에서 낸다. 낼 때 `LobbyRadio.ServerPlay` 로 해당 무전을 바로 튼다(연결 완료).
  `Update` 순서 = 간파 → 벽 → 마무리 무전 → 대기 중인 단계 완료 보고.
  - `ServerFirstCounterSucceeded` — A 첫 간파 성공(명세 §14 `TRAIN_03_COUNTER`). C·D 의 간파는 아니다.
  - `ServerFirstValidWallImpact` — C 첫 유효 벽 충돌 = 칸 감소를 동반한 지정 벽 충돌(상대 모델 `HasRecordedWall`, `TRAIN_03_WALL`). A/B 의 벽·0칸 접촉·장식 벽은 아니다.
  - `ServerFinishStarted` — D 첫 마무리 진입 = 제압 중 유효 피해 + 제압 종료(마무리 체력 설정 직후, 상대 모델 `IsFinishing`, `TRAIN_03_COMBAT`). 규칙 = `LobbySparringLink.TryRaiseFirstFinish`. 피해 없이 끝난 제압은 아니다.
- **씬 배치(임시 좌표 — 최종 위치는 사용자)**: `LobbyTraining/Train03Arena`(`LobbyTrainingSparringArena`, Gizmo 는 선택 안 해도 보인다 — A/B 노랑·C 하늘색·지정 벽 주황 + 라벨).
  - `SpawnAB` (-16, 0, 4), 방향 +z(집결 구역 중심 쪽) — 시작 집결 구역 `Gather_Train03Area`(중심 (-16, 5), 7×5 → x -19.5~-12.5, z 2.5~7.5) **안쪽**, 중심에서 1 m.
    상대는 이동이 없고 사거리는 `SpinnerBotData.attackRange` 2 m 라 처음엔 구역 밖 z 1.5 였을 때 가운데 선 플레이어(3.5 m)에게 공격·간파 창이 없었다
    (2026-10-22 Play 피드백 '시작 직후 멀뚱멀뚱'). 10-22 사용자 결정(a)로 구역 안으로 옮겨 해결 — 구역 중심 1 m, 남쪽 가장자리 1.5 m, 동·서·북 가장자리 3.5 m, 모서리 3.8~4.9 m(가장자리 플레이어는 1.5~2.9 m 다가가야 한다).
    2026-10-22 확인(읽기 전용, NavMesh.Raycast·반경 0.5 SphereCast 8방향 3 m): 간파 넉백(3 m)은 어느 방향이든 NavMesh·콜라이더에 막히지 않는다(끝점 x -19~-13, z 1~7).
    가장 가까운 NavMesh 끝 3.33 m(동쪽 x -12.67), 주변엔 벽·바닥 외 소품 콜라이더 없음. 북쪽으로 밀려도 z 7 = 구역 안, 남쪽 z 1 = 구역 밖이지만 A/B 의 영역 확인은 플레이어 위치만 본다(`PartyGatherZone` = 트리거, 상대 무관).
  - `PointC` (-17.5, 0, 5), 방향 +x(동쪽 = 플레이어 쪽) — 지정 벽 면까지 2.2 m, 간파 넉백(No23_A `vulnerableKnockbackDistance` 3 m)으로 닿는다. NavMesh 끝 x -19.33 에서 막히면 판정 거리 1.6 m 로 잡힌다.
  - `DesignatedWall_West` — Wall 레이어 BoxCollider(보이는 메시 없음), 중심 (-19.8, 1.1, 5) 크기 (0.2, 2.2, 5.5). 서쪽 아트 벽(x -20, 두께 0.22) 바로 앞에 겹쳐 스윕이 이 상자를 먼저 맞힌다.
    아트 벽 콜라이더를 직접 넣지 않은 이유: SVN 아트가 바뀌면 참조가 조용히 끊긴다. 위치를 옮길 땐 상자를 아트 벽 면에 붙여 두고 NavMesh 걷는 면(벽에서 0.5 m 안쪽)을 침범하지 않게 둔다.
    2026-10-22 확인(Physics.OverlapBox·바운드 비교): 서쪽 아트 벽 = `PF_Lobby_Architecture_v015` 의 `SB_WallBay_004/005_body`(x -20, z 0.25~7.75, 안쪽 면 x -19.89)·`SB_WallNode_004_I`(z 4)·모서리 `SB_WallNode_005_L`(-19.93, 7.93), 모두 Wall 레이어 MeshCollider.
    지정 벽 상자(x -19.9~-19.7, z 2.25~7.75)는 아트 벽과 0.01 m 겹치고 안쪽 면이 아트 면보다 0.19 m 앞 → 스윕이 지정 벽을 먼저 맞힌다. 상대(반경 0.5)가 NavMesh 끝(x -19.33)에서 멈추면 몸 가장자리 ↔ 지정 벽 면 0.87 m < 판정 거리 1.6 m. 옮길 필요 없음.
  - **지정 벽 Game 뷰 표시 `DesignatedWall_West_Marker` = 임시 비주얼, 아트 교체 대상.** Gizmo 는 Scene 뷰 전용이라 Game 뷰엔 주황 반투명 띠(얇은 큐브, 콜라이더 없음, 그림자 cast/receive off)를 지정 벽 면에 붙였다 —
    중심 (-19.67, 1.05, 5) 크기 (0.06, 2, 5.5), 머티리얼 `Assets/3.Materials/Lobby/MA_Train03DesignatedWall.mat`(URP Unlit Transparent, `MA_AoeTelegraph_Red` 복사본, 주황 α 0.45).
    **Train03C·D 동안만 켜진다**(하위 상태 무관, `LobbySparringLink.WallMarkerVisible`). A·B·TrainEnd 이후·로비 복귀·진행기 없음은 꺼짐. `LobbyTrainingSparringArena.wallMarkers` 가 전 피어에서 진행기 복제 단계(`LobbyTrainingDirector.Active.Stage`)를 매 프레임 읽어 켜고 끈다 — 새 RPC 없음, 늦게 붙은 클라도 같다. 씬엔 꺼진 채 저장.
- **남은 것**: (무전 COUNTER·WALL·COMBAT 연결은 완료 — 위), 제압 중 쿨타임 가속 = 할 일 91 `GrantServer(sourceId, duration)` 로 연결(지금 보류 플래그 꺼짐, 코드 없음),
  마무리 체력 = 프리팹 `finishHealth` 임시 60, 명세 §9.4 '변신 해제·강화·잔류 공격 종료'(`PracticeReset` 주석 참고), 명세 §9.7 경계 밖 갇힘 복구(상대는 이동이 없고 넉백은 NavMesh 안에서만 밀린다 — 따로 없음). Play·MPPM 확인은 사용자.

## NavMesh 사전 베이크

TRAIN_02 일반 전투 몬스터(ChompBot·HumanoidBot·MortarBot, NavMeshAgent 타입 `Humanoid`)가 걸을 NavMesh 는
**에디터에서 미리 구워 git 에 넣는다.** 로비에는 런타임 베이크 코드가 없다(MapScene 의 `MapNavMeshBaker` 와 별개).

- **오브젝트**: `3.LobbyScene` › `LobbyTraining/TrainingNavMesh` 의 `NavMeshSurface`.
- **범위**: Collect Objects = **Volume**(훈련실만) — 중심 (-16.15, 0.75, 0), 크기 (8.3, 2.5, 16.6) → x -20.3~-12.0, z -8.3~8.3.
  벽(x -20·-12, z ±8)을 걸쳐 넣어 장애물로 깎이게 했고, 동쪽 문 밖 복도는 넣지 않아 몹이 훈련실을 나가지 않는다.
  Agent Type = Humanoid(0), Use Geometry = Physics Colliders, Include Layers = **Ground + Wall**(바닥 = Ground, 벽·소품 = Wall).
  Default 는 뺐다 — 집결 구역 트리거(BoxCollider, Default)가 바닥처럼 구워지지 않게.
- **결과**(2026-10-22): 걷는 면 x -19.33~-12.67, z -7.33~7.33(벽에서 에이전트 반경 0.5 만큼 깎임). 정점 19, 넓이 약 96 m².
  빈 곳은 `Training_Storage` 소품 자리뿐.
- **이 볼륨은 끼임 복구의 훈련실 경계로도 쓴다**(`LobbyTrainingCombat.roomNavMesh`). 볼륨을 바꾸면 경계도 같이 바뀐다.
- **결과 에셋**: `Assets/0.Scenes/MainFlow/3.LobbyScene/NavMesh-TrainingNavMesh.asset`(+ `.meta`, 폴더 `3.LobbyScene.meta`).
  바이너리 에셋이라 **git 에 커밋해야** 다른 PC·빌드에 들어간다. 커밋하지 않으면 몹이 서 있기만 한다.

### 다시 구워야 할 때

로비 아트 중 **훈련실 바닥·벽·소품 배치가 바뀌거나 로비 전체가 회전·이동되면** 다시 굽는다(SVN 아트 교체 포함).
볼륨은 월드 좌표로 고정돼 있어 아트만 옮겨 가면 따라가지 않는다 — 그때는 볼륨 중심·크기도 같이 맞춘다.

1. Play 모드가 아닌 상태에서 `3.LobbyScene` 을 연다.
2. Hierarchy 에서 `LobbyTraining/TrainingNavMesh` 선택 → 인스펙터 `NavMeshSurface` 의 **Bake** 버튼.
   같은 경로(`3.LobbyScene/NavMesh-TrainingNavMesh.asset`)에 다시 만들어지고 씬 참조도 바뀐다. 다른 서피스를 고르지 말 것.
3. Scene 뷰에서 파란 NavMesh 가 훈련실 바닥을 덮는지, `Train02Spawns` Gizmo 의 초록 상자 9개가 그 위에 있는지 본다.
4. 씬 저장 → `NavMesh-TrainingNavMesh.asset` 과 `3.LobbyScene.unity` 를 함께 커밋.
