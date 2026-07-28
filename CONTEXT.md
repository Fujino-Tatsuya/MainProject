# CONTEXT.md - Shared Project Language

This file defines the shared vocabulary for the project. Keep it concise. It is not a full spec and should not contain implementation plans.

Update this file when a term becomes important enough that future agents or teammates must use it consistently.

## 현재 인수인계 (2026-07-28 · SVN r235 커밋 완료)

작업 세션: **경석(Claude)**. 브랜치 `feature/map-player-merge` (`30cf1df`, origin보다 1 앞).

### 이번 세션 완료 — SVN 최신화

**커밋 r235** (225 → 235). 신규 242 / 수정 104.

- 신규: `MapObj/mesh/level`(131) · `material`(48) · `texture`(32) · `mesh/object`(28)
- **⚠️ r234 GUID 사고 처리** — 팀원이 "누락된 meta 커밋"으로 MapObj meta 87개의 GUID를 새로
  발급해 올렸다. 그대로 update 했으면 git 쪽 41개 파일(존 프리팹 12·벽 10·머티리얼 17·씬 1)이
  전부 미싱 레퍼런스가 된다. `svn merge -c -234`로 GUID 재발급분만 역머지해 원본 유지.
  경위·재발 방지 = `Docs/_local/lessons.md` #14
- **콜라이더 플래그 복구** — floor/wall/urethane fbx meta 39개가 7/28 아트 교체로 `addColliders`·
  `isReadable` 0으로 리셋돼 있던 것을 r225 값(1/1)으로 되돌림.
- 검증: 컴파일 0에러/0경고, GUID 80/80 원본 일치, 이번 작업발 미싱 레퍼런스 0.
  (기존 미싱 5건 — MapScene FoW `maskTexture`(미사용) · `MA_Wall_basic.mat` 1 · `Stage1.prefab` 3 — 은 별개)
- git 정리 커밋 `30cf1df`: Tree/TutorialInfo 템플릿 잔재 + `all_mesh.unity` 제거
  (빌드 세팅 미등록·코드 참조 0 확인).

### 이어서 완료 — 경사로 콜라이더 + dash-soul 머지

- **`caaef90`** 경사로·계단 17개 MeshCollider 부착. Play에서 slope를 밟으면 낙하하던 문제.
  프리팹 안 언팩 사본이라 fbx `addColliders`가 전파되지 않는데 `MapColliderAuthoring`
  이름 필터에 slope/stair가 없었다. 키워드 추가로 해결(소품 88개는 통행 방해 우려로 제외).
- **`7a5db51`** `feature/dash-soul` 머지(59커밋). 씬이 `0.Scenes/MainFlow/`로 재편돼
  rename+양쪽수정 충돌 9건 발생, 전부 해소. 상세 = 머지 커밋 메시지.
  - MapScene은 **UnityYAMLMerge 3-way로 충돌 0** — 우리(맵 시스템 7)와 그쪽(옵션 UI 11)이
    겹치지 않았다. 병합 후 18개 오브젝트·참조·스크립트 전수 확인.
  - 자동 해소에 맡겼으면 조용히 깨졌을 3건을 수동 처리: **BossHudTarget 유실**(보스 체력바
    미표시), **레이어 슬롯 14 이중 점유**(EnemyHurtBox↔Corpse), **HasAimGroundPoint 게이트**
    (생성맵에서 지면 스킬 전면 차단).
- 검증: Unity 배치모드 컴파일 **error CS 0**, MapScene 댕글링 참조 0 / 미싱 스크립트 0.

### 이어서 완료 — Play 검증 피드백 반영 (`dec1eb3`·`f990c73`·`35aa703`)

**★ 이번 세션 최대 교훈: dash-soul 시스템들은 "씬마다 하나씩 배치"를 전제하고, 없으면
예외 없이 조용히 비활성화된다.** 증상만 보면 기능 버그로 보인다. MapScene에 배치한 것:
`FallBoundarySettings`(추락 감지) · `PlayerDashValidationManager`(대시 서버 검증) ·
`Temp_MultiGameRule`(부활 규칙) · `PartyWipeWatcher` · `SessionStatsTracker`.

- **콜라이더**: 바닥 82개(머티리얼 이름 판정 추가 — 아트가 `Cube.209` 식으로 내보내 이름
  필터로 안 잡혔다). **계단은 경사면 BoxCollider로 대체** — 계단 형상 콜라이더는 Rigidbody
  캡슐이 턱에 걸린다(`stepOffset` 보정 없음). 보이는 건 계단 그대로.
- **경사 등판**: `PlayerMovement`가 수평 벡터를 그대로 `MovePosition`에 넣어 경사면에 파고들었다.
  접지 중이면 지면 노멀 평면에 투영하도록 수정.
- **Soul**: `Soul` 레이어 신설(16) + `Soul`/`Corpse` 충돌을 `Default`/`Ground`/`Wall`/`Env`로
  제한. 어비스 위에서는 `useGravity`를 꺼 부유(속도만 0으로는 중력이 재가속시킨다).
- **낙사**: `ServerFallDeath` 구독자가 0이어서 사망 시 몸이 추락 지점에 남았다 → 안전지점 복귀.
- **사이클**: 전멸(=전원 `PermanentDead`) → Result → 로비. 결과 = `SessionResult` +
  `SessionStatsTracker`(생존 시간·처치 수) + `ResultStatsView`. 처치 수는
  `MonsterBase`/`BossBase` 사망 지점의 `MonsterDeathEvents`로 집계.
- **시체 자홍색**: 빌트인 `Default-Material`은 URP 미지원 → URP Lit로 교체.
- `GameManager.prefab`의 씬 이름이 리네임 전 값이었다(BootStrap 인스턴스는 이미 정상이라
  정식 플로우는 무영향, MapScene 직접 Play만 실패). 프리팹 최신화.

### 이어서 완료 — 보스룸 경계 + 도착 ACK (`3b626a1`·`93f65b8`·`a3d284e`)

승인 계획의 **A단계(투명벽) + Task 1**까지. 계획 사본 = `C:\Users\user\.claude\plans\synchronous-pondering-coral.md`

- `BossRoomAuthoring` 저작 도구로 `bossroom.prefab`에 생성(재실행 가능, 렌더러 바운즈 실측):
  `BossFloorCollider`(21×1×21, Default, 상단 Y 0.61) · `InvisibleBoundaries` 4면(Wall, 높이 8,
  트리거·렌더러 없음) · `PlayerArrivalPoints/Player1..3`(2m 삼각, 착지점 응시) · `BossLandingPoint`
- `BossTeleportManager`: 황금각 산개 → 도착 지점 배열 + ACK 계약. `encounterSequence` +
  대기 집합, sender·sequence 일치만 수락(중복 무시), 전원 ACK → `AlivePlayersArrived` 1회,
  5초 타임아웃 → `ArrivalAborted`, disconnect 처리, `IsEncounterBusy`로 재진입 차단
- ⚠️ 함정: `renderer.bounds`는 월드 좌표이고 `LoadPrefabContents`는 프리팹을 원점이 아닌 프리뷰
  씬에 올린다. 로컬인 `BoxCollider.center`에 그대로 넣으면 전체가 밀린다(1차 실행에서 Z≈109).
  (로컬 로그 `Docs/_local/lessons.md` #18 — 팀 공유 대상 아님)

### 이어서 완료 — Task 2 연출 잠금 (`c35d7bd`)

`PlayerEncounterLock` 신설. **잠금 = 새 계통이 아니라 기존 게이트 재사용**:
`PlayerActionState.Cinematic`(기존 `PlayerLockedState`) 하나로 이동·공격·스킬의 서버 승인
게이트가 함께 닫히고, 피해 무시는 `PlayerInvulnerability`의 `Cinematic` 토큰이 담당한다.
dash-soul 계통(대시 예측+서버 스냅샷 / 추락 감지 / 생명주기 전이)도 잠금 중 차단.

- ⚠️ **함정**: `Unit.Died`는 `_deathNotified`로 래치돼 재발행되지 않는다. 잠금 중 사망 신호를
  버리면 HP 0인데 Alive로 남는다 → `PlayerLifeCycleController`가 보류 후 해제 시 처리.
- 오너 입력은 `PlayerLifeInputPolicy` 한 곳에서만 적용(두 계통이 `SetInputEnabled`를 각자
  부르면 나중 호출이 앞선 차단을 지운다).
- `Player.prefab` 배선은 YAML 직접 편집(+21줄). **Unity가 포커스를 받아야 재임포트된다** —
  인스펙터에 컴포넌트가 보이는지 확인 필요.
- 미검증: Play/MPPM에서 잠금 중 공격·스킬·대시·추락·부활 무효 + 해제 후 복귀.
- 이탈 기록·검증 경계 = `IMPLEMENTATION_NOTES.md`(로컬, gitignore).
- ⚠️ MCP 주의: `unity_recompile_scripts` 직후 상태 조회를 부르면 도메인 리로드 중이라 응답이
  없다(무한대기). 컴파일 확인은 `Library/ScriptAssemblies/*.dll` 타임스탬프 + 콘솔 Error로.

### 이어서 완료 — Task 3 Director + 충전 기둥 + 존 스폰 마커 (`3c3833d`·`77a6458`)

**MapScene에 보스가 등장하는 경로가 처음으로 연결됐다.** `BossEncounterDirector`(씬 상주
NetworkObject)가 등장·전투 전환의 단일 소유자다: 도착 ACK → 참가자 잠금 → 보스 1회 스폰(+18m)
→ 곡선 하강 → 착지 → `BeginCombatServer()`(NavMesh Warp + 잠금 해제 + `OpenBT` 한 번에)
→ 보스 `Unit.Died` → `Capture(cleared: true)` → 결과 화면.

- ⚠️ **순서 함정**: `RunningOnlyOnServer`가 `OnNetworkSpawn`에서 `navMeshAgent.enabled = IsServer`로
  되돌린다. **에이전트 차단은 반드시 `Spawn()` 이후** — 아니면 상공의 보스를 NavMesh로 끌어내린다.
- **충전 기둥 4개**: `bossroom`의 `Env_Mv_bosscharger_upper`에 NetworkObject·NetworkTransform·
  BoxCollider·`ChargingObject`를 저작 도구로 부착(`Tools/Map/Authoring/Setup Boss Charge Pillars`).
  Director가 스폰 직후 `ChargeController.SetList`로 주입 → BT의 `SetChargingStateAction`이 그대로 동작.
- ⚠️ **기둥 레이어는 `Enemy(8)`** — KMKScene은 `EnemyHurtBox(14)`지만 플레이어 공격 마스크가
  `m_Bits=256`(Enemy)뿐이라 14에 두면 기둥을 때릴 수 없다. **민경 님과 정리 필요**(마스크를
  넓힐지, 기둥을 8에 둘지).
- `ChargingObject` 절대 Y → 로컬 숨김/활성 위치 + Hidden/Rising/Active/Lowering 상태로 재작성.
  같은 세션 반복 사용 가능.
- **존 스폰 마커 복구**: 존 프리팹 12개 중 `ZoneLayout`이 3개만 남아 있었고 마커는 0개였다
  (v11 재작업·아트 교체 때 유실). `Author ZoneLayouts (from Catalog)` 재실행 → 11존 복구,
  마커 27개. `ZoneL_typeC` = **GauntletBot 중간보스**. 마커 위치는 러프값 → Play 후 조정.
- 배선 도구: `Tools/Map/Authoring/Wire Boss Encounter (MapScene)`(MapScene 열고 실행, 재실행 안전).
- 미검증: Play/MPPM 전체 흐름. 미구현: 대사 HUD·ESC 만장일치 스킵(Task 5), 착지 카메라
  흔들림·먼지 VFX(Task 4 나머지).

### 진행 중 (2026-07-28 이어서) — 승인 계획서 전제 갱신 완료

`Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md`에 **Revised Premises 9항** 추가.
Task 1·Task 7(경계 부분) 완료 표시, 씬 경로(`MainFlow/4.MapScene`) 전면 수정, Task 2를
"기존 `GameplayAccess` 게이트 확장 + dash/fall/life 차단"으로 재작성, Task 3에 클리어 판정
(`Unit.Died` → `Capture(cleared: true)`)과 `PartyWipeWatcher` 경합 항목 추가.

~~다음 = Task 2~~ **완료**. 다음 = **Task 3**(`BossEncounterDirector`). Task 2가 수정한 파일 = `1.Scripts/Player/{PlayerEncounterLock.cs(신규),
PlayerInputReader, PlayerStateController, PlayerMovement, DefaultAttackController, Player,
PlayerDashController, Skill/PlayerSkillController, Fall/PlayerFallController,
Life/PlayerLifeCycleController, Life/PlayerLifeInputPolicy}`, `1.Scripts/Unit/StatusEffectController.cs`,
`2.Prefabs/Player/Player.prefab`. ⚠️ 플레이어 계통은 은희 담당 영역 — 동시 수정 주의.

### ▶ 다음 세션 시작점

**증상: 보스룸으로 이동은 되는데 보스가 안 나온다 — 정상이다.** MapScene에는 보스를 스폰하는
주체가 없다. 보스를 스폰하는 `TwentyThreeArenaContext`(`OnNetworkSpawn`에서 `boss.Spawn()`)는
`KMKScene`·`PlayerBossTest`에만 배치돼 있고, **MapScene의 `TwentyThree.prefab` 참조는 0건**이다.
그게 **Task 3 `BossEncounterDirector`**의 일이고 이번 범위는 "도착까지"였다.

1. **승인 계획서에 "달라진 전제" 반영** — 착수 첫 단계. 씬 경로(`MainFlow/4.MapScene`),
   플레이어 프리팹 1개, 연출 잠금 대상 확대(dash·fall·revive·soul), `PartyWipeWatcher` 오발 억제,
   카메라 우선순위(Float 뷰), 클리어 판정 연결, 담당 경계, DynamicsManager 재수정 불필요.
2. **Task 2~8** 순서대로. Task 3에서 보스 스폰 소유자를 Director 하나로 정리한다
   (`TwentyThreeArenaContext`는 민경 님 영역 — 중복 스폰이 실제로 생기면 그때 수정, 팀장 승인 받음).
3. 보스 격파 시 `SessionStatsTracker.Active.Capture(cleared: true)` 연결 → 결과 클리어 판정 완성.
4. **미구현 확인분**: dash HUD 위젯(충전 개수·재충전 게이지), 로딩바 보간,
   RMB 스킬(`FirstMeleeInterruptSkill` 구현체 자체가 없음), Result UI 서체·배치.
5. push 안 된 상태. 롤백 지점 = `backup/pre-dash-soul-merge`(`caaef90`).

### 확인만 하고 넘긴 것

- **로비 Ready**: 호스트는 자동 Ready + GameStart 버튼, 클라이언트만 Ready — **의도된 설계**.
- `ProjectSettings/DynamicsManager.asset`이 serializedVersion 13 → 23으로 포맷 마이그레이션됨
  (Unity가 저장 시 재작성). 신규 필드는 전부 기본값이지만 팀원 pull 시 통째로 바뀐 diff를 본다.
- 몬스터 루트 레이어가 제각각(ChompBot=19 이름없는 레이어, SpinnerBot·WallBot=0). 콜라이더는
  전부 Enemy(8)라 현재 증상은 없으나 레이어 마스크 로직에서 물릴 수 있다.

### 주의 — SVN meta

`50.Art/Char/Boss/bomb.fbx.meta`가 로컬 미버전 상태다. r233에서 fbx만 meta 없이 올라와
우리 Unity가 GUID를 새로 발급한 것 — **커밋하지 말 것**(r234와 같은 사고가 된다).
boss 담당(민경)이 자기 프로젝트에서 커밋해야 한다.

### 이전 세션 완료

1. **벽 투명화 per-pixel 재설계** — 오브젝트당 스칼라 불투명도(MPB) → 프래그먼트 월드좌표 기반
   셰이더 계산. 물리 쿼리 0, MPB 0, 약 1,600줄 → 473줄. Play 검증 통과.
   설계·검증·한계 = [Docs/tech/wall-occlusion-implementation.md](Docs/tech/wall-occlusion-implementation.md)
   - 삭제된 타입: `WallOcclusionUnit/Proxy/Manager/VisibilityContributor/Core/RuntimeBinder/ProjectBridge`
   - 현행 타입: `WallOcclusionDriver`(Assembly-CSharp) + `Globals`/`MaterialBinder`/`Settings`(Occlusion asmdef)
2. **맵 프리팹 콜라이더 복구** — 아트 교체로 12개 프리팹 콜라이더 0개였던 것 복구.
3. **`Assets/level` 아트팩 145개 재배치** — GUID 참조 그래프로 사용/미사용 판정 후 이전.
   `AssetDatabase.MoveAsset`만 사용, **GUID 145/145 보존, 미싱 레퍼런스 0**.
   - `50.Art/MapGen/MapObj/{mesh/level, material, texture}` ← FBX·머티리얼·셰이더그래프·텍스처 (**SVN**)
   - `2.Prefabs/Map/Props` ← 프롭 프리팹 38개 (git)
   - `99.Settings` ← `PP.asset`, `PP_Renderer.asset` (미참조 URP 파이프라인, 위치만 잡아둠)
   - 이름 충돌 3건은 `_level` 접미사로 개명 (`MA_prop03_level` 등 — 기존 50.Art와 이름만 같고 별개 에셋)
4. **오클루전 머티리얼 매핑 5쌍 → 14쌍** — level 폴더 머티리얼 9종이 매핑에서 빠져 프롭들이
   디더 셰이더를 못 달고 있던 문제 수정.

### 다음 작업 (이어서)

1. **SVN 최신화** — `50.Art/MapGen/MapObj` 아래 신규 파일들을 SVN에 add/commit. git에는 안 보인다
   (`.gitignore:83`이 `50.Art/` 제외, `Assets/50.Art.meta` 1개만 추적).
2. **머지** — SVN 상태 맞춘 뒤 진행.

### 주의

- **`8.BehaviorTreeGraph/**` 는 다른 담당자 작업물이다. 수정·커밋하지 말 것.** (현재 워킹트리에
  수정 상태로 있으나 의도적으로 커밋에서 제외했다.)
- 미커밋으로 남긴 것: `TitleScene`·`ProjectSettings`(줄바꿈만 변경, 내용 0), `all_mesh.unity`,
  `FogProfile.asset`, BT 에셋 2종.
- 아트(FBX·텍스처·머티리얼)는 SVN, 코드·씬·프리팹은 git. 이 경계를 넘기지 말 것.

## 이전 인수인계 (2026-07-21 → Codex)

작업 세션: **경석(Claude)** — MapScene 몬스터/보스입장 통합 완료(컴파일 0, 1차 플레이 검증). 다음 작업자 = Codex.

- **상세 현황·남은작업·조사항목 = [Docs/tech/map-monster-boss-handoff.md](Docs/tech/map-monster-boss-handoff.md)** (이 세션 산출물 전체 + 우선순위 목록).
- 계획 잠금: `PLAN.md` §"MapScene 몬스터 통합" + §6(보스 입장).
- 최근 수정 파일(동시수정 주의): `1.Scripts/{Map/*, Monster/MonsterBase.cs, Player/PlayerMovement.cs·PlayerAimIndicator.cs, Unit/Weapon/BaseAttack.cs, Player/Skill/FirstMeleeMainSkill.cs}`, `MapScene.unity`, 존 프리팹 12개.
- **아직 push/커밋 안 됨.** git + SV( 50.Art meta·MapGenConfig) 분리 커밋 예정 — 핸드오프 문서 §4.
- 즉시 다음 후보: 패드 y 가림 조치 / 멀티(MPPM) 텔레포트 검증 / 터렛 스폰 재확인 / **MortarBot 복귀 후 간헐 Idle 회귀 조사**(핸드오프 §3).

## Project Summary

A top-down cooperative action game inspired by Ravenswatch-style structure.

Current near-term target:
- Start game
- Boss intro sequence
- Boss combat
- Listen-server network vertical slice

Later scope:
- Map expansion
- Growth systems
- General mobs
- Additional content

## Core Terms

- Player: A human-controlled networked unit.
- Host: The player running the listen server.
- Client: A connected player that is not the host.
- Server authority: Logic owned and decided by the server/host, then replicated.
- Owner authority: Logic controlled by the owning client, usually player input and movement.
- Unit: A gameplay actor with common state and snapshot behavior.
- UnitBase: The common base for shared unit state and snapshot only. Movement, abilities, status effects, and networking behavior should be composed with components where possible.
- Boss: A server-authoritative enemy with encounter flow, patterns, state, and network-visible presentation.
- Boss intro: The sequence before combat begins, including presentation and state transition into battle.
- State abnormality: Status effect or condition applied to a unit.
- Build: A player growth or ability configuration concept.
- Skill: A player or boss action/pattern defined by data and executed by runtime logic.
- ScriptableObject data: Authoring-time gameplay data for skills, builds, bosses, patterns, and tuning values.
- Vertical slice: A thin but complete path through gameplay, networking, UI/presentation, and verification.

## Networking Language

- Player input: Usually owner-authoritative.
- Player movement: Usually owner-authoritative unless a specific anti-cheat or server correction rule is chosen.
- Boss state: Server-authoritative.
- Enemy state: Server-authoritative.
- Damage: Server-authoritative.
- Drops/rewards: Server-authoritative.
- Scene progression: Server-authoritative.
- Snapshot: A compact representation of state needed for synchronization, save, debug, or replay-like inspection.

## Design Preferences

- Prefer composition over deep inheritance.
- Prefer data-driven tuning for gameplay content.
- Prefer small vertical slices over broad unfinished systems.
- Prefer clear module interfaces that hide meaningful implementation.
- Prefer names from this file and `Docs/` over ad hoc synonyms.

## Open Vocabulary To Resolve

Add definitions when these become concrete:
- Exact boss encounter phase names
- Player class names
- Ability categories
- Build/growth terminology
- State abnormality taxonomy
- Scene/session flow terms
- Network room/lobby terms

## Resolved Terms (2026-07-21)

- Boss enter pad: BossRoom 역할 존 중앙의 진입 패드(트리거+테두리 표시). 생존 플레이어 점유 시 카운트다운(3·2·1), 전원 이탈 시 취소. 완주 시 생존자 전원 보스룸으로 텔레포트. 튜닝은 BossTeleportManager 인스펙터.
- RangedTurret: 고정 포탑 몬스터 아키타입(PeekABot·TeslaBot). 넉백 면역, 경직만 적용.
- Knockback direction: 공격이 AttackInfo.knockbackDirection으로 명시(방향성 공격). zero면 수신측이 방사형(대상-공격자)으로 폴백(장판/폭발형).