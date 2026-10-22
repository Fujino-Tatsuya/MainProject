# 로비 훈련장 (할 일 86)

> 로비(3.LobbyScene) 훈련실에서 하는 TRAIN_01~03 진행. 이 문서는 T8 에서 흐름·단계별 내용으로 확장한다 — 지금은 TRAIN_01 체크 시점·NavMesh 절만 있다.
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

## TRAIN_03 훈련 상대 `TrainingSparringBot` (T5b — 진행기 연결·씬 배치는 T5c)

- **프리팹**: `Assets/2.Prefabs/Lobby/TrainingSparringBot.prefab` = `SpinnerBot.prefab` 의 **Variant** 에서 `SpinnerBot` 컴포넌트를 지우고 `TrainingSparringBot` 을 얹었다
  (외형·애니메이터·`MonsterCounterWindow` 창 길이·`SpinnerBotData` 는 SpinnerBot 저작값을 따라간다). `DefaultNetworkPrefabs` 등록.
- **규칙 값 = 23호 데이터** `counterRules` = `No23_A.asset`(TwentyThree.prefab 의 data) — 그로기·취약·넉백·반동·칸 감소·제압 시간·배율·정면 각도. 훈련 쪽 값은 `finishHealth`(D 마무리 체력, 임시 60) 하나.
- **판정**: 단계 = `TrainingSparringModel`(T5a), 게이지·벽·처치 방지 = `TrainingSparringRules`. 23호 코드(`TwentyThreeBoss`)는 읽고 같은 규칙을 이 상대 안에 다시 썼다 — 23호 규칙이 바뀌면 같이 맞춘다(PLAN-86 R3).
- **이동 없음**: 배치 위치에서 돌아보고 사거리(`SpinnerBotData.attackRange`) 안이면 간파 창 → 채찍. 넉백으로 밀린 자리에서 계속한다(벽마다 원위치 복귀 없음, 명세 §9.5).
  base 추격·리쉬 복귀(체력 회복)를 쓰지 않아 C 위치와 지정 벽 거리가 설계대로 유지된다.
- **벽**: 넉백·Q 밀림 틱마다 `UnitWallImpacts.ServerProbe`(넉백 1회 = 1회). 결과 콜라이더가 `designatedWalls` 에 있을 때만 유효 — 씬 배치(T5c)가 `ServerConfigureArena(C 위치, 지정 벽)` 로 넣는다.
- **간파 성공 쿨 감소**: 할 일 90 으로 게임 전체에서 제거 — 훈련 상대 별도 처리 없음.
- **칸 UI**: 머리 위 중간보스 바(`MonsterHealthBar_MidBoss`)를 이 상대의 `IOverheadHealthBarRule` 로 C·D 에만 켠다. 게이지 칸은 바 이름 자리 `칸 n/m`, D 제압 중엔 `제압된 상대를 공격하세요 n초`(임시 — 문구 `LobbyUiStrings.Train03*`). 바 채움은 체력.
- **이벤트(서버)**: 간파 성공·CC 적용/종료·지정 벽 유효 충돌·칸 감소·제압 진입/종료·제압 중 피해·사망·단계 완료(`ServerStageCompleted`)·B→C 초기화 완료(`ServerResetExecuted` — 플레이어 쪽 `TrainingResetOptions.PracticeReset` 을 진행기가 이어 건다).

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
