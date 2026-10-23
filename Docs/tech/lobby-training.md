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
| `LOBBY_TRAIN_ROUTE` | 입구 전원 집결 | 입구 집결 확정 → `Train01/Gathering` |
| `TRAIN_01_START` | 첫 훈련 시작 | 첫 위치 집결 확정 → `Train01/InProgress` |
| `TRAIN_01_END` | 개인 목표 전원 완료 | Train01 완료 보고 → `Train02/Gathering` |
| `TRAIN_02_START` | 일반 전투 시작 | 두 번째 위치 집결 확정 → `Train02/InProgress` |
| `TRAIN_02_END` | 일반 전투 완료 | Train02 완료 보고 → `Train03A/Gathering` |
| `TRAIN_03_START` | 간파 연습 시작 | 세 번째 위치 집결 확정 → `Train03A/InProgress` |
| `TRAIN_03_CC` | B 소개 | A 완료 뒤 영역 안 확인 → `Train03B/InProgress` |
| `TRAIN_03_RESET` → `TRAIN_03_WALL_GUIDE` | B→C 초기화 / C 시작 | B 완료 뒤 영역 안 확인 → `Train03C/InProgress`(명세 §9.4 초기화 순간). 같은 진입에 두 줄 — 순서대로 |
| `TRAIN_03_SUPPRESS` | D 제압 진입 | C 완료 보고(제압 시작 순간) → `Train03D/InProgress` |
| `TRAIN_END` | 훈련 종료 | D 완료 보고 → `TrainEnd/Gathering` |
| `BRIEF_ROUTE` | 종료 집결 | 종료 집결 확정 → `Brief/Gathering` |
| `BRIEF_01`~`04` | 브리핑 | 단계 진입 대사 아님 — `LobbyBriefing` 이 `Brief/InProgress` 동안 서버 시간으로 한 줄씩 `ServerPlay`(아래 "브리핑" 절) |

`Lobby → Sortie`(훈련 없이 출격)·`Brief/InProgress`·`Brief/Completed`·`Sortie` 진입 대사는 없다(단계가 바뀌면 대기 줄 폐기만).

### 단계 안 사건 대사 — 공개 API

```
LobbyRadio.Active.ServerPlay(string id, bool oncePerRound = true) : bool   // [서버] 큐에 한 줄. 이번 회차에 이미 튼 id·서버 아님·스폰 전·빈 id = false
LobbyRadio.Active.ServerHasPlayed(string id) : bool                       // [서버] 이번 회차에 틀었나(단계 진입 대사 포함)
LobbyRadio.Active.ServerClear()                                           // [서버] 무전 전체 종료(T6c 브리핑·강제 연출)
```

`ServerPlay` 대사도 지금 단계 번호를 달고 가므로, 다음 단계가 시작될 때 아직 안 떴으면 버려진다.

#### Work2 T5c 연결 대상 (레인 B `feature/LobbyTrainingSparring`)

훈련 상대(`TrainingSparringBot`) 서버 이벤트에서 아래처럼 부른다. id 상수는 `LobbyRadioIds`. 문구·지속시간 행은 이미 `TutorialGuideTexts` 에 있다.

| id | 상수 | 호출 시점(서버) | 메모 |
|---|---|---|---|
| `TRAIN_03_COUNTER` | `LobbyRadioIds.Train03Counter` | A(`Train03A/InProgress`)에서 **첫 간파 성공**이 확정된 순간 — 단계 완료 보고(`ServerReportStageCompleted(Train03A)`) **전에** 부른다 | 완료 보고 뒤엔 같은 단계(A/Completed)라 폐기되지 않지만, B 진입이 바로 이어지면 대기 줄이 버려질 수 있다 — 먼저 부르면 지금 뜬 줄이 된다 |
| `TRAIN_03_WALL` | `LobbyRadioIds.Train03Wall` | C(`Train03C/InProgress`)에서 **첫 유효 벽 충돌**(칸 감소 동반, 명세 §9.5)이 기록된 순간 | 같은 넉백이 마지막 칸 감소 → 제압이면 벽 기록 → `ServerPlay` → C 완료 보고 순서(D 진입 때 폐기돼도 `TRAIN_03_SUPPRESS` 가 이어진다) |
| `TRAIN_03_COMBAT` | `LobbyRadioIds.Train03Combat` | D(`Train03D/InProgress`)에서 **제압 중 유효 피해 경험 + 제압 종료**(마무리 체력 설정 직후) | 진행기에 D 제압 종료 시점이 없어 단계 진입 대사로 두지 않았다 |

세 줄 모두 `oncePerRound` 기본값(true)으로 부르면 재시도·중복 이벤트에도 회차당 1번만 나온다.

### 문구·지속시간

- ko 폴백 = `TutorialGuideTexts` SO 행(19줄, 명세 §14 초안 그대로). 추가 = 메뉴 **Tools/UI/로비 무전 문구 추가**(`TutorialGuideUIAuthoring.LobbyRadioTexts`, 빠진 행만 — 고친 문구는 안 건드림).
- Strings 키 = `Tutorial.Guide.<id>` — Export 수집기(`TutorialGuideStringKeys`)가 SO 행에서 자동으로 모은다(`#설명` = "TutorialGuideTexts · 로비 무전"). xlsx 행 추가는 SVN 작업(사람).
- 지속시간 = 기존 행과 같은 폭: 짧은 한 문장 4초(`TRAIN_03_SUPPRESS`·`BRIEF_04`) · 30자 안팎 5초 · 두세 문장 6초 · 45자 넘는 긴 줄 7초(`TRAIN_03_CC`·`BRIEF_01`).
  참고 = `TUT_Z3_*` 3~7초, `BOSS_RAID_*` 3~6초, 기본 6초.
- 목표 패널 문구(`훈련장으로 이동하세요` 등)는 이번 범위가 아니다 — 무전 자막만. 예외 = 브리핑 두 줄(T6c, 아래).

## 브리핑 BRIEF_01 (T6c)

명세 v0.2 §4(강제 연출 규칙)·§10, PLAN-86 §3.6. 코드: `Assets/1.Scripts/Lobby/LobbyBriefing.cs`(NGO) · `LobbyBriefingRules.cs`
(순수 — `LobbyBriefingModel`·`LobbyBriefingLockLedger`·`LobbyBriefingRules`·`LobbyObjectiveIds`). 테스트: `Assets/Tests/EditMode/Lobby/Editor/LobbyBriefingTests.cs`.
`LobbyBriefing` 은 `LobbyTraining/LobbyTrainingDirector` 오브젝트에 붙는다(새 NetworkObject 없음). 진행기 `LobbyTrainingDirector.cs` 는 **고치지 않았다**(구독 + `ServerReportStageCompleted` 호출만).

### 흐름

1. 종료 집결 확정 → `Brief/Gathering`(`BRIEF_ROUTE` 무전) → 브리핑 위치(`Gather_Brief`) 훈련 명단 전원 5초 집결 → `Brief/InProgress`.
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
- **무전 연결 지점(서버 C# 이벤트, `LobbyTrainingSparring.Active` 에서 구독)** — Train03 회차당 1회, 판정 콜백 밖(다음 프레임)에서 낸다.
  - `ServerFirstCounterSucceeded` — A 첫 간파 성공(명세 §14 `TRAIN_03_COUNTER`). C·D 의 간파는 아니다.
  - `ServerFirstValidWallImpact` — C 첫 유효 벽 충돌 = 칸 감소를 동반한 지정 벽 충돌(상대 모델 `HasRecordedWall`, `TRAIN_03_WALL`). A/B 의 벽·0칸 접촉·장식 벽은 아니다.
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
- **남은 것**: 무전 구독 = Work1 T6(위 두 이벤트), 제압 중 쿨타임 가속 = 할 일 91 `GrantServer(sourceId, duration)` 로 연결(지금 보류 플래그 꺼짐, 코드 없음),
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
