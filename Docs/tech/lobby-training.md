# 로비 훈련장 (할 일 86)

> 로비(3.LobbyScene) 훈련실에서 하는 TRAIN_01~03 진행. 이 문서는 T8 에서 흐름·단계별 내용으로 확장한다 — 지금은 대사 출처·로비 무전·TRAIN_01 체크 시점·NavMesh 절만 있다.
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
- **브리핑·강제 연출 시작 때 기존 무전 종료**: `LobbyRadio.ServerClear()` — 지금 뜬 줄 + 대기 줄을 즉시 끈다(`TutorialGuideUI.ClearSubtitles`, 목표 패널·대기 줄·강조는 그대로). **API 만, 호출은 T6c.**

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
| `BRIEF_01`~`04` | 브리핑 | (정의만 — 재생은 T6c) |

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
- 목표 패널 문구(`훈련장으로 이동하세요` 등)는 이번 범위가 아니다 — 무전 자막만.

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
