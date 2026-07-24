# Boss Encounter Intro Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 생존 플레이어 1~3인의 보스룸 이동 완료부터 No.23 등장 연출, 만장일치 ESC 스킵, 안전한 동시 전투 시작, 충전기 재사용과 보스룸 추락 방지까지 서버 권한으로 완성한다.

**Architecture:** `BossTeleportManager`는 도착 ACK까지만 담당하고, 새 `BossEncounterDirector`가 참가자·연출 단계·보스 스폰·스킵·전투 시작을 단독 소유한다. 플레이어는 `PlayerEncounterLock`으로 클라이언트 입력과 서버 gameplay 진입점을 함께 막고, No.23은 전투 공격 판정을 끈 전용 intro presenter로 착지한 뒤 BT를 연다.

**Tech Stack:** Unity 6000.3.16f1, Netcode for GameObjects, Multiplayer Play Mode, Cinemachine 3.1.6, AI Navigation/NavMeshSurface, TextMeshPro

## Global Constraints

- `Assets/50.Art/**`, `Assets/51.Audio/**`와 해당 `.meta`는 SVN 소유이므로 수정·이동·재생성하지 않는다.
- 현재 작업 트리의 `No.23.asset`, `CommonMeleeRobot.asset`, `EditorBuildSettings.asset` 변경은 보존하고 어떤 구현 커밋에도 섞지 않는다.
- 보스 스폰과 `EnemyBTActivator.OpenBT()` 호출 소유자는 `BossEncounterDirector` 하나로 제한한다.
- 연출 중 입력 차단은 로컬 input disable만으로 끝내지 않고 서버의 이동·공격·스킬·피해·상태이상 진입점에서도 검증한다.
- 정상 완료, ESC 스킵, 참가자 감소, 오류 복구는 각각 별도 해제 코드를 만들지 않고 멱등적인 `BeginCombatServer()` 또는 `AbortEncounterServer()`로 수렴한다.
- 새 Asset Store 의존성을 추가하지 않는다.
- Unity MCP의 종료/menu 실행 경로는 사용하지 않는다. 컴파일·상태 조회는 제한 시간 내 read-only 명령만 사용하고, MPPM 실행은 Unity 에디터에서 수행한다.

---

## Task 1: 텔레포트 완료 계약과 참가자 전달

**Files**

- Modify: `Assets/1.Scripts/Map/BossTeleportManager.cs`
- Modify: `Assets/1.Scripts/Map/BossEnterTrigger.cs`
- Modify: `Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab`
- Modify: `Assets/0.Scenes/MapScene.unity`

- [ ] `bossroom.prefab`에 `PlayerArrivalPoints/Player1..Player3`와 `BossLandingPoint` Transform을 배치한다. 각 플레이어 지점은 서로 최소 플레이어 캡슐 지름 이상 떨어뜨리고 경계·착지점과 분리한다.
- [ ] `BossTeleportManager`의 황금각 계산을 직렬화된 도착 지점 배열로 교체한다. `ConnectedClientsList`의 생존 PlayerObject를 `ClientId` 오름차순으로 정렬해 최대 3개 슬롯에 결정적으로 배정한다.
- [ ] 텔레포트 시도마다 증가하는 `uint encounterSequence`와 `_awaitingArrivalClientIds`를 서버에 둔다.
- [ ] 대상 RPC가 Rigidbody 속도, Transform, owner-authoritative NetworkTransform을 적용한 뒤 `ArrivalAppliedServerRpc(sequence)`를 호출하게 한다.
- [ ] 서버 RPC는 sender가 현재 대기 집합에 있고 sequence가 일치할 때만 ACK를 수락한다. 중복 ACK는 무시한다.
- [ ] 모든 유효 참가자가 ACK하면 `AlivePlayersArrived` 서버 이벤트로 `IReadOnlyList<ulong>`을 한 번 전달한다.
- [ ] `OnClientDisconnectCallback`과 생존 검사에서 대상이 사라지면 ACK 대기 집합에서 제거한다. 집합이 0명이면 `Idle`로 복귀한다.
- [ ] 5초 ACK 타임아웃에서는 전투를 강행하지 말고 실패 이벤트를 내보내며 페이드와 카운트다운을 복구한다.
- [ ] `BossEnterTrigger`가 연출 진행 중 재진입 카운트다운을 만들지 않도록 Manager의 busy 상태를 확인한다.
- [ ] Unity 컴파일 후 1·2·3인 각각 로그에 슬롯 번호, ClientId, ACK 완료가 정확히 한 번씩 찍히는지 확인한다.
- [ ] Commit: `feat(map): acknowledge safe boss-room arrivals`

Expected result: 마지막 생존 참가자의 실제 로컬 위치 적용 전에는 보스 등장 시퀀스가 시작될 수 없다.

---

## Task 2: 플레이어 연출 잠금과 전투 상태 초기화

**Files**

- Create: `Assets/1.Scripts/Player/PlayerEncounterLock.cs`
- Modify: `Assets/1.Scripts/Player/PlayerInputReader.cs`
- Modify: `Assets/1.Scripts/Player/PlayerStateController.cs`
- Modify: `Assets/1.Scripts/Player/PlayerMovement.cs`
- Modify: `Assets/1.Scripts/Player/DefaultAttackController.cs`
- Modify: `Assets/1.Scripts/Player/Skill/PlayerSkillController.cs`
- Modify: `Assets/1.Scripts/Unit/StatusEffectController.cs`
- Modify: `Assets/1.Scripts/Player/Player.cs`
- Modify: `Assets/2.Prefabs/Player/Player.prefab`
- Modify: `Assets/2.Prefabs/Player/Paladin/Paladin.prefab`

- [ ] `PlayerEncounterLock : NetworkBehaviour`에 server-write `NetworkVariable<bool> IsCinematicLocked`를 추가한다.
- [ ] `BeginCinematicServer()`를 멱등적으로 구현한다. 먼저 lock을 올리고 Rigidbody 속도 정지, 기본 공격 취소, active skill 종료, 상태이상 전체 제거, FSM cinematic lock, 오너 input disable 순서로 실행한다.
- [ ] `StatusEffectController.ClearAllServer()`를 추가해 서버 권한에서 복제 리스트를 뒤에서부터 비운다. 무한 지속 버프와 디버프도 모두 제거한다.
- [ ] `DefaultAttackController`가 시작 요청과 콤보 연계 요청 전에 encounter lock을 확인한다. `CancelCurrentAttack()`이 combo index, queued input, root motion runtime까지 0으로 만드는지 확인하고 부족한 값은 같은 reset 경로에 포함한다.
- [ ] `PlayerSkillController`가 시작·홀드 요청 전에 lock을 확인하고 `EndActiveSkillServer(SkillEndReason.Cancelled)`로 진행 중 스킬을 종료한다.
- [ ] `PlayerMovement`의 오너 입력 적용과 서버/권한 이동 적용 모두 lock 중에는 속도 0으로 끝나게 한다.
- [ ] `PlayerStateController`에 `Cinematic` 상태를 추가해 Idle/Move/Attack/Skill 전이를 막는다.
- [ ] `Player.ReceiveAttack`과 `TakeDamage`가 서버 lock 중 피해를 무시하게 한다. `StatusEffectController`의 적용 진입점도 owner Player의 lock을 확인해 새 상태이상을 거부한다.
- [ ] `EndCinematicServer()`는 FSM을 Idle로 복구하고 속도를 다시 0으로 만든 뒤 lock 해제, 오너 input enable 순서로 수행한다.
- [ ] 두 플레이어 프리팹에 동일한 `PlayerEncounterLock` 참조를 연결한다.
- [ ] 오프라인/호스트에서 lock 시작 전 공격 콤보, 홀드 스킬, 버프·디버프를 만든 뒤 모두 종료·제거되는지 확인한다.
- [ ] MPPM 클라이언트가 lock 중 공격/스킬 RPC를 보내도 서버 상태와 HP가 변하지 않는지 로그로 확인한다.
- [ ] Commit: `feat(player): add server-authoritative encounter lock`

Expected result: 연출 시작 전에 날아온 요청을 포함해 모든 gameplay 변화가 서버에서 차단되며 ESC 전용 입력만 살아 있다.

---

## Task 3: BossEncounterDirector와 단일 전투 전환

**Files**

- Create: `Assets/1.Scripts/Map/BossEncounterDirector.cs`
- Create: `Assets/1.Scripts/Map/BossEncounterPhase.cs`
- Modify: `Assets/1.Scripts/Map/BossTeleportManager.cs`
- Modify: `Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeArenaContext.cs`
- Modify: `Assets/0.Scenes/MapScene.unity`

- [ ] `BossEncounterPhase`에 `Idle`, `AwaitingArrival`, `Preparing`, `Descending`, `Impact`, `Dialogue`, `Combat`, `FailedSafe`를 정의한다.
- [ ] Director에 server-write phase, phase start time, dialogue page index, eligible count, vote count NetworkVariable을 둔다.
- [ ] `OnNetworkSpawn`/`OnNetworkDespawn`에서 텔레포트 완료, disconnect 이벤트를 대칭 구독·해제한다.
- [ ] 텔레포트 완료 ClientId를 다시 생존·연결·PlayerObject 존재 조건으로 검증해 `_eligibleClientIds`에 스냅샷한다. 후발 접속자는 추가하지 않는다.
- [ ] 각 참가자의 `PlayerEncounterLock.BeginCinematicServer()`를 완료한 뒤 No.23을 상공에 한 번만 NetworkSpawn한다.
- [ ] boss prefab의 `NetworkObject`, `EnemyBTActivator`, `Animator`, `TwentyThreeIntroPresentation`, `ChargeController`와 landing point를 시작 전에 검증한다.
- [ ] `TwentyThreeArenaContext`의 `OnNetworkSpawn()` 즉시 보스 Spawn/OpenBT를 제거하고, 보스 프리팹·충전기 참조만 제공하는 scene configuration component로 축소한다.
- [ ] `BeginCombatServer()`에 re-entry guard를 두고 보스 landing snap/NavMesh Warp, intro guard 해제, 모든 현재 생존 참가자 unlock, HUD 종료 상태 복제, `OpenBT()`를 한 번만 실행한다.
- [ ] `AbortEncounterServer()`는 참가자 unlock, HUD 종료, 이미 스폰된 보스 Despawn, 집합·투표 초기화 후 `Idle`로 복구한다.
- [ ] disconnect callback과 연출 중 서버 `Update`의 생존 재검사로 사망 참가자를 즉시 eligible/vote 집합에서 제거한다. 0명이면 Abort, 남은 수와 표 수가 같으면 BeginCombat을 호출한다.
- [ ] 모든 phase 전환에 sequence, 이전/다음 phase, eligible/vote를 포함한 서버 로그를 남긴다.
- [ ] Unity 컴파일 후 MapScene에서 No.23이 한 번만 스폰되고 BT가 intro 전에는 닫혀 있는지 확인한다.
- [ ] Commit: `feat(boss): orchestrate encounter phases on server`

Expected result: 보스 스폰, 연출 수명 주기와 BT 시작에 두 번째 소유자가 없고 모든 종료 경로가 한 전투 전환으로 수렴한다.

---

## Task 4: No.23 낙하·착지 연출과 로컬 피드백

**Files**

- Create: `Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeIntroPresentation.cs`
- Modify: `Assets/1.Scripts/Enemy/Boss/JumpController.cs`
- Create: `Assets/1.Scripts/Camera/BossImpactFeedback.cs`
- Modify: `Assets/2.Prefabs/Wells&No.23/TwentyThree.prefab`
- Modify: `Assets/2.Prefabs/Camera/PlayerFollowCamera.prefab`
- Create: `Assets/5.VFX/Boss/BossLandingImpact.prefab`
- Modify: `Assets/0.Scenes/MapScene.unity`

- [ ] `TwentyThreeIntroPresentation`에 intro mode, spawn height, descend duration, AnimationCurve, landing point를 입력받는 서버 API를 만든다.
- [ ] 시작 시 No.23 Animator의 기존 `JumpAttack`/`Leap` 상태를 cross-fade하고 서버가 곡선에 따라 root NetworkTransform을 landing point까지 이동한다.
- [ ] `JumpController`에 `SetCinematicLandingMode(bool)`을 추가하고 intro mode 동안 애니메이션 이벤트의 피해, 넉백, 장판 생성 경로를 즉시 반환시킨다.
- [ ] landing 완료는 한 번만 발생시키고 정확한 point로 스냅한다. Director가 이를 받아 `Impact` phase로 전환한다.
- [ ] 낙하 중 스킵은 presentation의 `CompleteImmediatelyServer()`로 landing snap과 combat hit guard 정리 후 전투 전환한다.
- [ ] `BossImpactFeedback`이 phase 변화에서 착지 sequence당 한 번만 로컬 Cinemachine impulse를 발생시키게 한다.
- [ ] `PlayerFollowCamera.prefab`의 실제 Cinemachine 카메라에 impulse listener를 연결하고 강도·지속 시간을 인스펙터에서 조정 가능하게 한다.
- [ ] 기존 Git 추적 VFX만 사용해 `BossLandingImpact.prefab`에 낮은 먼지 ring과 작은 돌 입자를 구성한다. loop off, 짧은 lifetime, 충돌·NetworkObject 없음으로 설정한다.
- [ ] 각 클라이언트가 landing point에 VFX를 로컬 한 번 생성하고 완료 후 자동 정리하도록 한다.
- [ ] 1·3인 MPPM에서 모든 화면 shake/VFX가 한 번씩 보이고 No.23의 착지 피해가 발생하지 않는지 확인한다.
- [ ] Commit: `feat(boss): present networked landing intro`

Expected result: 기존 JumpAttack 시각은 재사용하지만 연출 착지는 전투 공격이나 장판을 만들지 않는다.

---

## Task 5: 페이지 대사 HUD와 만장일치 ESC 스킵

**Files**

- Create: `Assets/1.Scripts/UI/Combat/BossEncounterHUD.cs`
- Modify: `Assets/1.Scripts/Map/BossEncounterDirector.cs`
- Create: `Assets/2.Prefabs/UI/BossEncounterHUD.prefab`
- Modify: `Assets/0.Scenes/MapScene.unity`

- [ ] Director에 `[Serializable] BossDialoguePage { string text; float durationSeconds; }` 목록을 추가하고 첫 원소를 `보스 등장.`으로 직렬화한다.
- [ ] HUD prefab에 우측 중앙~하단 anchored panel, TMP 대사 텍스트, `ESC 건너뛰기` 텍스트와 선택적 분수 텍스트를 만든다.
- [ ] HUD는 phase, page index, server end time, vote/eligible NetworkVariable을 구독해 표시만 담당한다.
- [ ] 1명일 때 분수 GameObject를 숨기고, 2명 이상일 때 `votes/eligible`을 표시한다.
- [ ] gameplay PlayerInput과 별개로 `Keyboard.current.escapeKey.wasPressedThisFrame`를 읽되, 자신이 eligible이고 아직 투표하지 않았을 때만 `RequestSkipServerRpc()`를 한 번 보낸다.
- [ ] 서버 RPC는 sender ClientId, phase가 cinematic인지, eligible 포함 여부를 검증하고 HashSet에 추가한다. 중복 표는 무시한다.
- [ ] 사망·disconnect 제거 직후 vote/eligible 수를 갱신하고 `eligible > 0 && votes == eligible`이면 즉시 `BeginCombatServer()`를 호출한다.
- [ ] 자동 페이지 종료는 서버 시간으로 진행하고 마지막 페이지 뒤 같은 `BeginCombatServer()`를 호출한다.
- [ ] 정상 종료, Dialogue 중 스킵, Descending 중 스킵 모두 HUD가 남지 않고 플레이어와 보스가 같은 최종 상태인지 확인한다.
- [ ] MPPM 1인 `건너뛰기`, 2인 `0/2 → 1/2 → 2/2`, 3인 disconnect 후 분모 감소를 검증한다.
- [ ] Commit: `feat(ui): add unanimous boss-intro skip`

Expected result: 스킵 표시는 인원수 규칙과 일치하고, 사망·연결 해제가 연출을 영구 정지시키지 않는다.

---

## Task 6: 충전기 4개의 네트워크 피격·재사용

**Files**

- Modify: `Assets/1.Scripts/Enemy/Boss/ChargingObject.cs`
- Modify: `Assets/1.Scripts/Enemy/Boss/ChargeController.cs`
- Modify: `Assets/1.Scripts/Map/BossEncounterDirector.cs`
- Modify: `Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab`
- Modify: `Assets/0.Scenes/MapScene.unity`

- [ ] bossroom의 `Env_Mv_bosscharger_upper` 4개를 각각 Git 소유 `ChargingObject` 루트 아래 시각 자식으로 정리한다. FBX와 FBX `.meta`는 변경하지 않는다.
- [ ] 각 root에 기존 Unit 요구 컴포넌트, Collider, NetworkObject/NetworkTransform을 구성하고 scene NetworkObject로 정상 등록되는지 확인한다.
- [ ] `ChargingObject`의 절대 `_minY/_maxY`를 `hiddenLocalPosition/activeLocalPosition`으로 교체한다. 시작 시 hidden에 스냅하고 collider·피격을 끈다.
- [ ] 상태를 `Hidden`, `Rising`, `Active`, `Lowering`으로 명시하고 서버만 전환한다. NetworkTransform이 위치를 복제한다.
- [ ] `StartCharge()`가 체력, destroyed/reached 1회 플래그, collider를 초기화하고 rising을 시작하게 한다. 피격은 Rising 시작 순간부터 허용한다.
- [ ] 파괴 시 DestroyEvent를 한 번만 보내고 Lowering으로 전환한다. hidden 도달 후 다음 StartCharge를 받을 수 있게 한다.
- [ ] `EndCharge()`는 Hidden에서도 안전한 멱등 메서드로 만들고 Rising/Active 모두 Lowering으로 보낸다.
- [ ] `ChargeController.SetList()`가 기존 이벤트를 먼저 해제하고 정확히 4개인지 검사한 뒤 재구독하게 한다.
- [ ] `StartCharge(playerCount)`는 생존 참가자 수를 1~3으로 clamp하고 설정된 개수만 활성화하며 목록 범위를 넘지 않게 한다.
- [ ] Director가 No.23 Spawn 직후 `ChargeController.SetList()`를 한 번 호출한다.
- [ ] 1·2·3인별 활성 개수와 파괴/강제 종료 후 hidden 복귀를 확인한다. 같은 세션에서 패턴을 두 번 실행해 두 번째 체력·이벤트가 정상인지 확인한다.
- [ ] Commit: `fix(boss): make charge pillars reusable`

Expected result: 4개의 외형은 평상시 이동을 방해하지 않고, 패턴 때만 서버 권한 피격 대상이 되며 반복 사용된다.

---

## Task 7: 보스룸 추락 방지와 NavMesh 포함

**Files**

- Modify: `Assets/1.Scripts/Map/MapNavMeshBaker.cs`
- Modify: `Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab`
- Modify: `Assets/0.Scenes/MapScene.unity`
- Modify only if collision matrix is wrong: `ProjectSettings/DynamicsManager.asset`

- [ ] bossroom 실제 보행 면적을 덮는 `BossFloorCollider`를 만들고 Default/walkable layer에 둔다.
- [ ] 네 변과 모서리 틈을 막는 `InvisibleBoundaries` BoxCollider를 Wall layer로 만든다. 렌더러와 trigger는 사용하지 않는다.
- [ ] Player와 Enemy/Boss가 Wall layer와 충돌하는 기존 Physics matrix를 확인하고, 틀린 경우에만 `DynamicsManager.asset`을 최소 변경한다.
- [ ] `MapNavMeshBaker`에 `[SerializeField] LayerMask walkableLayerMask`를 추가하고 Awake가 인스펙터 값을 매번 Default로 덮어쓰지 않게 한다.
- [ ] NavMeshSurface는 PhysicsColliders와 CollectObjects.All을 유지하되 walkable mask에는 바닥의 Default 레이어만 포함하고 Wall 레이어는 제외한다.
- [ ] MapGenerator 완료 후 bossroom floor가 포함된 NavMesh가 생성되는지 Scene 뷰에서 확인한다.
- [ ] Director의 전투 전환 직전 `NavMesh.SamplePosition(landingPoint)`과 No.23 agent `Warp()`가 성공해야 BT를 열게 한다.
- [ ] 플레이어 3명을 네 벽·네 모서리로 밀고 이동해 추락이 없는지 확인한다.
- [ ] No.23을 방 가장자리의 플레이어에게 유도해 `isOnNavMesh` 유지와 방 밖 경로 미생성을 확인한다.
- [ ] Commit: `fix(map): secure boss-room bounds and navigation`

Expected result: 플레이어는 어떤 경계에서도 낙하하지 않고 No.23은 보스룸 walkable surface 안에서 이동한다.

---

## Task 8: 통합 검증과 변경 경계 확인

**Files**

- Modify if required by verified wiring only: files from Tasks 1–7
- Create: `Docs/04-report/boss-encounter-intro-validation.report.md`

- [ ] `git status --short`와 `svn status`를 기록하고 SVN 소유 아트 또는 기존 무관 변경이 구현 diff에 섞이지 않았는지 확인한다.
- [ ] Unity 6000.3.16f1에서 전체 recompile 후 Console error 0을 확인한다.
- [ ] Host 1인: 정상 자동 완료, Descending ESC, Dialogue ESC를 각각 새 세션에서 확인한다.
- [ ] MPPM 2인: 도착 ACK, 위치 분리, `0/2 → 1/2 → 2/2`, 동시 input/BT 시작을 확인한다.
- [ ] MPPM 3인: 위치 분리, 각 화면 impact, `0/3 → 3/3`, 보스 이동을 확인한다.
- [ ] 3인 연출 중 한 클라이언트를 연결 해제해 분모 감소와 남은 표로 즉시 전환되는지 확인한다.
- [ ] 연출 시작 직전 지속 피해, 버프·디버프, 기본 공격 콤보, active skill을 각각 걸어 초기화와 연출 중 HP 고정을 확인한다.
- [ ] No.23 스폰 1회, BT Open 1회, impact 1회, HUD hide 1회를 로그로 확인한다.
- [ ] 충전 패턴을 같은 세션에서 2회 실행하고 인원수별 활성 개수, 피격, 파괴/종료 후 하강을 확인한다.
- [ ] 보스룸 벽·모서리 추락 방지와 No.23 NavMesh agent 부착을 확인한다.
- [ ] 결과, 재현 절차, Console 핵심 로그, 미해결 위험을 validation report에 적는다.
- [ ] `git diff --check`와 변경 파일별 diff를 검토한다.
- [ ] Commit: `test(boss): validate encounter intro flow`

Expected result: 1·2·3인과 disconnect, 정상 종료, 스킵, 상태 초기화, 충전기 반복, 경계/NavMesh가 모두 문서의 수용 기준을 충족한다.

---

## Final Verification Commands

```powershell
git status --short --branch
git diff --check
git diff --name-status HEAD
svn status
```

Expected:

- Git diff에 승인된 코드·씬·프리팹·문서만 존재한다.
- `Assets/50.Art/**`, `Assets/51.Audio/**` 수정이 없다.
- 기존 무관 변경은 별도 미커밋 상태로 보존된다.
- Unity Console error 0.
- MPPM 1·2·3인 검증 보고서가 실제 결과로 채워져 있다.

## Implementation Stop Condition

Task 8의 모든 시나리오가 통과하고, No.23 BT와 플레이어 입력이 정상 완료·스킵 모두에서 한 번의 서버 전환으로 활성화되며, SVN 소유 파일이 구현 커밋에 포함되지 않았을 때 완료한다.
