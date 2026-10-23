# 로비 훈련장 (할 일 86)

> 로비(3.LobbyScene) 훈련실에서 하는 TRAIN_01~03 진행. 이 문서는 T8 에서 흐름·단계별 내용으로 확장한다 — 지금은 TRAIN_01 체크 시점·TRAIN_03 상대·진행기 연결·NavMesh 절만 있다.
> 흐름·출격·접속 승인은 [lobby-demo-flow.md](lobby-demo-flow.md) §1.

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
