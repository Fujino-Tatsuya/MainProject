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

### 이어서 완료 — Play 피드백 5건 (2026-07-29, `703988f`~`4482d0b`)

- **인게임에서만 바닥에 구멍** — 시드 차이(TestGenerate=TickCount / 인게임=Random.Range).
  `AssignSlotRoles`는 BossRoom·PlayerSpawn 후보를 **크기 무관**하게 뽑는데 역할 디자인은 Small만
  저작돼 있다 → `GetRoleLayout`이 null → `MapContentSpawner`가 **로그 없이 continue** → 구멍.
  → 역할 디자인 없으면 같은 크기 전투 풀 폴백 + 경고, 스포너는 에러 로그. **조용한 구멍 금지.**
- **보스만 피격 빨간 틴트 없음** — `Enemy.OnNetworkSpawn`이 `base` 호출 없이 `if (!IsServer) return;`
  으로 시작했다. `Unit.OnNetworkSpawn`의 HP 복제 구독 + `HitFlash` 자동 부착이 통째로 건너뛰어졌다.
  Unit 파생 5종 중 Enemy만 누락. **★파생 클래스에서 base 누락은 "그 타입만" 조용히 기능이 빠진다.**
- **충전 중 보스가 맵 밖으로** — BT가 복귀 위치로 읽는 `SpawnPointer.SpawnPoint`가 프리팹 기본값
  `(0,0,0)`이고 코드에서 아무도 채우지 않는다. BossScene은 아레나가 원점이라 우연히 맞았다.
  → Director가 스폰 직후 착지점(방 중앙)으로 채운다.
- **보스 진입로가 막혔다(직전 세션 회귀)** — 레이저 프리팹에 심은 차단벽이 Stage1 통로 26곳을
  전부 막았다. 어느 슬롯이 Quest가 되는지는 시드마다 달라 정적 배치로는 불가.
  → 레이저 벽 제거, `MapContentSpawner`가 역할 확정 시점에 **Quest 존만** 네 변으로 감싼다.
- **중간보스 감축** — 마커 수 = 스폰 수. 엘리트 그룹(5)은 마커 1개 제한 + 초과 마커 정리
  → `ZoneL_typeC` 4 → 1. 위치 수동 조정은 재실행해도 보존(앞쪽 마커 유지).

### 이어서 완료 — Play 피드백 4건 (`60f3862`·`66ac555`)

**★ 교훈: 민경 님 보스 코드는 아레나 바닥이 `Ground` 레이어에 Y=0이라고 가정한다(BossScene 기준).
생성맵 보스룸 바닥은 `Default` 레이어에 Y≈0.61이라 바닥을 찾는 모든 레이캐스트가 조용히 빗나간다.**
증상은 제각각으로 보이지만 뿌리가 하나였다.

- **보스를 때릴 수 없었다** — 보스 몸 콜라이더는 `HurtBox(EnemyHurtBox=14)` 하나뿐이고 루트에는
  콜라이더가 없다. 플레이어 공격 마스크는 `1280`(Enemy|Projectile)이라 14를 못 봤다.
  → 기본공격·Q `17664`, 궁극기 타겟팅 `16640`. **프로젝트 관례 = 적 대상 마스크는 Enemy|EnemyHurtBox**
  (폭탄의 `enemy=16640`이 그 근거).
- **폭탄이 공중에 떴다** — `BombLauncher.groundMask`/`BombController.ground`가 `Ground(8)`뿐 →
  착지 Y 탐색·낙하 판정·장판 스냅 전부 실패. → `9`(Ground|Default).
- **장판과 몸체가 어긋났다** — `JumpController`가 바닥 레이어를 `"Ground"` 하드코딩 + 착지 Y를
  `0f` 고정. → `groundMask` 인스펙터화(비면 Default+Ground 폴백) + `hit.point.y` 사용.
- **Quest 통로 차단** — `layprefab`(레이저)에 `LaserBlockWall`(3.17×8×0.6, Wall) 생성.
  `Level_wall_hallway`에 26개 들어 있어 **26곳이 모두 막힌다**. 열어야 할 곳은 인스턴스에서 끄면 된다.
  도구 = `Tools/Map/Authoring/Setup Quest Laser Blockers`.

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
- ⚠️ **기둥 레이어는 `Enemy(8)`** — BossScene은 `EnemyHurtBox(14)`지만 플레이어 공격 마스크가
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

### ▶▶ 다음 세션 시작점 (2026-07-29 마감 · 팀장 지시 = **분석 먼저, 수정 나중**)

팀장 지시 원문 취지: "지금 너무 수정만 하는데 달라지는 게 아니니 **분석부터 제대로** 해야 한다.
다음 세션에서 작업하자." → 다음 세션은 **코드를 고치기 전에 아래 4건의 분석을 먼저 끝내고
결과를 보고한 뒤** 지시를 받는다. 상세 경위·교훈은 `Docs/_local/lessons.md` #20~#26 +
「⏳ 미해결」 절.

**분석 1 — `Env_Wall_doorframe` 콜라이더 부재 (확인됨, 원인 미확정)**
- 사실: 문틀은 `MeshFilter`+`MeshRenderer`만 있고 콜라이더가 없다(인스펙터 확인). 그래서 통과된다.
- 벽 투명화와 무관함은 확정: `WallOcclusionDriver`에 `Collider`/`Physics`/`SetActive`/`enabled` 참조 **0건**
  (셰이더 전용). 투명해 보이는 것과 콜라이더 부재는 별개 사건이다.
- 분석할 것: `MapColliderAuthoring`의 이름 필터에 doorframe 계열이 포함되는지, 문틀 26개(+`Env_laser`)의
  콜라이더 유무 전수, 문틀이 통행을 막아야 하는지(문이므로 열려 있어야 할 수도) — **디자인 의도 확인 필요**.

**분석 2 — M키 지도 ↔ 우측하단 미니맵 탐사 동기화 (미구현)**
- 사실: 미니맵은 `_MaskTex`(R=explored, G=visible)로 점점 밝아진다. M키 지도(`MapOverviewUI`)는
  슬롯 사각형만 그리고 탐사 개념이 없다.
- 요구: 미니맵에서 밝아진 영역이 M키 지도에도 같이 그려져야 한다.
- 분석할 것: `MinimapController.GetExploredBits()`(이미 존재) + `_worldRect` 좌표계를 `MapOverviewUI`가
  어떻게 소비할지. 존 사각형 위에 탐사 마스크를 덮는 방식 vs 베이크 텍스처를 그대로 확대 표시하는 방식 —
  둘 중 어느 쪽이 지금 UI 구조에 맞는지 먼저 결정.

**분석 3 — `BossArea` ✅ 해결(2026-07-29): BT가 태그로 스스로 찾는다, 주입 불필요**
- No.23 BT는 `FindObjectWithTagAction`(Tag=문자열 `"BossArea"`)으로 씬에서 직접 찾아 `BossArea`
  GameObject 블랙보드 변수를 채우고, 같은 부모 아래 `SetEnableBoxColliderAction(Enable=true)`로 켠다
  (`No.23.asset` rid 1567581773390152037·038 — 읽기만 함). **Director 주입 불필요.**
- MapScene 현황은 이미 완비: `bossroom.prefab`에 tag `BossArea` 트리거(20.98×2×20.98, center y=1),
  충전 기둥 4개 = `Env_Mv_bosscharger_upper` + BoxCollider + ChargingObject + NetworkObject +
  NetworkTransform(maxHp 5·defense 0·riseHeight 1·moveSpeed 1 = KMK와 동일),
  `BossEncounterDirector.chargingObjects`에 4개 전부 연결.
- ⚠️ **BossScene의 BossArea를 MapScene으로 복사해 오면 안 된다** — 태그 2개가 되어 `FindObjectWithTag`가
  어느 것을 잡을지 보장되지 않고, 기둥 8개 중 4개만 동작하며, `TwentyThreeArenaContext`가 함께 오면
  보스 이중 스폰이 된다. `BossEncounterDirector.ValidateBossAreaTag()`가 태그 0개/2개 이상을 에러로 잡는다.
- 부수 확인: `HomePoint`는 코드·No.23 BT 어디서도 참조 0건(미사용 마커). `8.BehaviorTreeGraph/Boss/
  BossArea.asset` 그래프는 어느 씬·프리팹도 참조하지 않는 고아(예제 빌더 산출물).

**분석 4 — ✅ 해결(2026-07-29): 존 저작은 이미 맞았고, 검증 도구가 잘못된 대상을 읽었다**
- **거짓 경보였다.** `Save Placements`는 **씬의 Stage1 인스턴스**에 쓰고 씬을 dirty 처리한다(프리팹에
  Apply하지 않음). 그런데 `Validate Slot Authoring`은 `Stage1.prefab` **에셋**을 읽어 씬 오버라이드를
  못 봤다 → 재저작을 마친 뒤에도 "미저작 9건"을 계속 보고했다.
- 프리팹+씬 오버라이드(슬롯 대상 61건)를 병합한 실제 상태 = **실질 미저작 0건**. 도구가 세던 3건은
  전부 도달 불가 조합이었다: Slot 5·7 × `ZoneS_typeA`(둘 다 Boss/Spawn 후보 2곳뿐 → 절대 Combat 안 됨),
  Slot 8 × `Quest01`(`QuestPrefab=Quest02` 지정 시 카탈로그 Quest 풀 미조회).
- Quest는 의도대로 고정됨: Slot 4 `IsQuestCandidate` 0으로 내리고, Slot 8만 후보 + `QuestPrefab=Quest02`
  → 시드 무관 항상 Slot 8 / Quest02. 부작용으로 카탈로그 Quest 풀 2종은 사실상 미사용.
- 셔플은 카탈로그대로 돈다: Large 0·1·2 ↔ A/B/C 1:1 순열 / Medium은 Slot 3 `FixedPrefab=ZoneM_typeC`가
  pinned 제외되어 남은 풀 {A,B} 2개 ↔ Slot 4·9 2곳 1:1 / Small 전투는 Slot 6 하나 ↔ `ZoneS_typeA`.
  총 조합 = 6 × 2 × (Boss/Spawn 스왑 2) = 24가지. **Medium은 여유 0** — Medium 슬롯을 늘리거나 Slot 4를
  Quest 후보로 되살리면 즉시 풀 부족(재사용)으로 넘어간다.
- 남은 잔재: 옛 GUID 참조 9건(Slot 3:3, 4:3, 8:2, 9:1) + 끊긴 `QuestPrefab` 2건. 런타임 무해(null은
  프리팹 비교에 안 걸림), 리포트만 오염. 청소 도구 = `Tools/Map/Authoring/Cleanup Slot Authoring (dead refs)`.
- ⚠️ **구조적 취약점**: 저작 정본이 씬 오버라이드에만 있다 → Stage1 인스턴스에서 Revert 한 번이면
  61건이 날아간다(복구선 = 커밋된 MapScene). Stage1을 다른 씬에 인스턴스화하면 저작 0 상태로 떨어진다.
  근본 해소는 씬 오버라이드를 Stage1.prefab에 Apply해 정본을 프리팹으로 옮기는 것(씬 수술 리스크, 미착수).

**참고 — 이번 세션에 실제로 검증된 것**
- 보스 등장 흐름 정상: 로그 `SpawnPoint를 방 중앙 (500.49, 0.61, 0.49)으로 설정` → 하강 → `전투 시작 — BT 개방`.
  스크린샷의 보스 좌표 `(13.33, 0.08, -4.83)`은 그 수정 **이전** Play다.
- 미니맵 베이크 자체는 정상(`중앙 샘플 평균 밝기 0.308`). 남았던 원인은 실루엣 마스크였다.
- 레이저 통로 차단(26곳)은 **의도된 것**이며 유지한다. 내 판단으로 제거했다가 원복했다(`5dee39d`).
- ⚠️ 보스룸 저작 도구(`Rebuild Boss Room Bounds`)는 `PlayerArrivalPoints`·`BossLandingPoint`를 **재생성**한다
  → 실행 후 `Wire Boss Encounter (MapScene)`를 반드시 재실행(참조 끊김). 손으로 옮긴 지점도 초기화된다.

### 이번 세션 완료 (2026-07-29 · 아레나 자립화 + 유령 타겟팅)

- **`BossArenaContext` 신설 + bossroom.prefab에 부착·저장** — 아레나가 자기 부품(착지점·BossArea·
  충전 기둥 4개·도착 지점 3개)을 프리팹 내부 참조로 들고 있다. 참조가 전부 프리팹 내부 fileID라
  **절대좌표가 개입하지 않는다**. `BossEncounterDirector`는 `arena` 하나만 물어보고, 씬 배선이
  비어 있으면 여기서 채운다 → 다른 씬에 인스턴스화해도 동작. 저작 도구가 기준점을 재생성해
  참조가 끊기는 사고도 사라진다. 부착 도구 = `Tools/Map/Authoring/Wire Boss Arena Context (bossroom)`.
  - **위치 검증 결과: 착지점 localPos (0.49, 0.61, 0.49) == BossArea localPos, 간격 0.000m.**
    즉 "아레나 중앙으로 안 간다"의 원인은 절대좌표 하드코딩이 아니다(코드 전수 검색에서도 0건).
    `Spawn Point`·`ArrivePoint` 블랙보드 Vector3는 둘 다 런타임에 채워진다
    (Director→`SpawnPointer`→`GetSpawnPointAction` / `JumpController.SetTarget`). 남은 유일한 위험은
    `GameObject.Find("BossLandingPoint")` 이름 폴백 → 이제 arena를 먼저 보므로 최후 폴백으로만 남았다.
- **유령(Soul) 상태에서 몬스터가 더 이상 반응하지 않는다** — `MonsterTargeting.IsAttackable`(신설)이
  단일 기준. 기존 `IsTargetValid`가 `null`+`activeInHierarchy`만 봐서 Soul이 통과했고, 사망 직전에
  잡힌 타겟이 유지되어 몬스터가 유령을 쫓고 공격 모션까지 냈다(데미지는 Soul에서 hurtbox가 꺼져
  안 들어갔으므로 **행동만 남은 상태**였다). 적용: `MonsterBase.IsTargetValid`·`FindNearestTarget`,
  `BossBase` 동일 2곳, `GauntletBot.CountNearbyPlayers`(유령이 스매시 단계 인원수에 잡히던 것).
  - 판정 기준은 `ShouldEnableHurtbox`가 아니라 `PlayerLifeState.Alive`다 — 무적 프레임(대시 회피)이
    hurtbox를 끌 때 "맞지 않는다"를 "노리지 않는다"로 해석하면 대시 한 번에 타겟이 풀린다.
  - ⚠️ **No.23 보스는 미적용**: 타겟 선정이 BT 노드(`FindClosestWithTagAction` 등, 민경 님 영역)라
    코드에서 못 막는다. 같은 증상이 보스전에서 재현되면 BT 쪽 조건 추가가 필요하다.

### ▶ 진행 중 — "23호가 landing 직후 (0,0,0) 근처로 이동" (2026-07-29, 원인 2개 후보 모두 차단·검증 대기)

증상: 하강·착지까지 정상, **착지 직후** `TwentyThree(Clone)` position이 x≈1.9(맵 중앙)로 이동.
아레나는 x≈500이므로 500m 순간이동이다. 절대좌표 하드코딩은 코드 전수 검색 **0건**이었고,
착지점·BossArea localPos는 간격 **0.000m**로 정합했다. 그래서 원인은 다음 둘 중 하나다.

**후보 A — NavMeshAgent를 메시 밖에서 켰다 (착지 시점에 정확히 실행됨 = 가장 유력)**
- `SnapBossToNavMesh`가 `_bossAgent.enabled = true`를 **샘플링보다 먼저** 했다. NavMesh 밖에서
  에이전트를 켜면 Unity가 내부 위치를 가장 가까운 메시에 맞추는데, 아레나에 메시가 없으면
  그 "가장 가까운 곳"이 맵 본체(원점 근처)다 → 보스가 끌려간다.
- 수정: **샘플 먼저 → 실패하거나 착지점에서 2.5m 이상 떨어진 메시를 잡으면 에이전트를 켜지 않고
  에러 로그**. 아레나 안에서 못 움직이는 게 원점으로 날아가는 것보다 낫다(lessons #26 원칙).
- ⚠️ 이 경우 보스가 아레나에서 이동하지 못한다 = **NavMesh 베이크를 고쳐야 하는 진짜 문제**가 드러난다.
  `MapNavMeshBaker`는 `useGeometry=PhysicsColliders` + `layerMask=Default만` + `collectObjects=All`이고,
  bossroom의 `BossFloorCollider`(BoxCollider)는 layer 0(Default)이라 **수집 대상은 맞다**. 경계 5개는
  layer 7(Wall)로 올바르게 벽 취급. 즉 배선상 bossroom이 특별 취급되는 곳은 없다 — 남은 의심은
  아트 바닥 콜라이더 유실/unreadable mesh다(베이커에 `UnreadableMeshColliderBakeScope` 폴백이 있는 이유).

**후보 B — BT 절대 위치 블랙보드가 (0,0,0)으로 시작한다 (구조는 확인, 발동 여부 미확인)**
- No.23 루트가 `ParallelAllComposite` + `Start` 8브랜치 **병렬**이다. 브랜치[1]에
  `NavigateToLocationAction(Location="Spawn Point")`가, 브랜치[4]에 그 값을 **쓰는**
  `GetSpawnPointAction`이 있다 → 쓰기가 먼저라는 보장이 없다.
- `ArrivePoint`도 같다. `JumpController.SetTarget`이 채우는데 연출 착지 중엔 `_isCinematicLanding`으로
  **조기 반환해 한 번도 안 채워진다**. 그 값을 `SetPositionThroughRaycastAction`·`MoveForDurationAction`이
  **위치에 직접 쓴다**(파인딩 아님) → (0,0,0) 순간이동. 단 이들은 `SwitchComposite`(상태 스위치) 아래라
  첫 틱 무조건 실행은 아니다 — 그래서 "구조는 확실, 발동은 미확인"이다.
- 수정: `BossEncounterDirector.SeedArenaPositionBlackboard`가 스폰 직후 `Spawn Point`·`ArrivePoint`를
  **방 중앙으로 미리 덮는다**. BT 그래프는 보스 담당 영역이라 손대지 않고 최악값만 없앴다.

**✅ 판별 완료 (Play 1회, Editor.log 확인)** — 원점 이동 **해결**됨:
- `[BossEncounter] 보스 NavMesh 부착 완료 — (500.49, 0.67, 0.49) (착지점 오차 0.06m)`
  → **아레나에 NavMesh 정상 존재. 후보 A 기각.** bossroom 베이크는 문제없다.
- `[BossEncounter] BT 위치 블랙보드 초기화 — Spawn Point, ArrivePoint = (500.49, 0.61, 0.49)` → 시딩 동작.
- 즉 실제로 들었던 원인은 **후보 B(블랙보드 (0,0,0))** 쪽이다.

### ▶ 후속 — "보스가 공격만 하고 걷지 않는다" = 이동 배선이 아니라 **거리창** 문제 (2026-07-29 분석 완료)

- **로그 증거: 전체 런에 `Walk`/추격 상태 전환 0건.** Idle→Upper/LeftHook/RightHook/Grab/Jump→Idle 반복,
  마지막에 Charging→Groggy→Dead. 걷는 상태에 들어간 적이 없다.
- 원인 = `TwentyThreeBasicAttackChoice` 거리창이 거의 전 구간을 덮는다(프리팹 값):
  hook 0~3 / upper 0~3 / grab 0~1 / jump 5~10 / dash 10~20, 가중치 50/50/100/100/100.
  **빈 구간은 3~5m와 20m 초과뿐**이고 `GetRandomAttack`은 그때만 `None`을 반환한다 → BT가 Walk로 가는 건
  그 두 구간에서만이다. 플레이어가 붙어 있으면 항상 공격이 뽑히므로 **걷지 않는 게 설계상 정상**이다.
  → **팀장 확인(2026-07-29): 3~5m Walk 밴드 하나는 의도된 설계다.** 그 안쪽은 hook/upper/grab이 처리한다.
  남은 쟁점은 수치를 bossroom 크기에 맞추는 것뿐이다(아래).

**아레나 스케일 실측 — 튜닝 전제가 바뀌었다 (2026-07-29)**

| | 벽 안쪽 전투 구역 | 최대 거리(대각선) | BossArea 트리거 |
|---|---|---|---|
| BossScene(튜닝 환경) | Ground scale 5 → 약 48×48m, 벽 ±24m | ~68m | 10×10, 중심에서 **x+2.79 어긋남** |
| bossroom(실전) | **21.0×21.0m** | **29.67m** | 20.98×20.98, 방 전체·정중앙 |

- bossroom이 KMK 테스트장보다 **약 2.3배 작다.** 현재 거리창은 48m 방 기준으로 잡힌 값이다.
- ⚠️ **사각지대**: dash가 20에서 끊기는데 bossroom 최대 거리는 29.67m → **20~29.67m 구간(코너 대각)**에서
  `GetRandomAttack`이 `None`을 반환해 Walk로 간다. WalkSpeed 2로는 10m를 5초 걸어온다.
- 원칙: **공격 리치는 아레나 크기와 무관, 이동 공격과 사각지대만 아레나에 의존한다.**
  hook/upper/grab(0~3, 0~3, 0~1)은 애니메이션 리치이므로 스케일하면 안 된다(팔이 안 닿는 거리에서 때린다).
  그리고 **가장 먼 공격의 max는 항상 아레나 대각선 이상**이어야 한다.
- 권고 2안 (프리팹 값이라 팀장/민경 승인 후 변경, 아직 미적용):
  - **안 1(최소 변경, 권장)**: dash max **20 → 30**. 나머지 그대로 → 사각지대 0.
  - **안 2(방 크기에 맞춤)**: jump 5~9 / dash 9~30. 대시를 더 일찍 쓰게 하고 사각지대 0.
  - 별건: **WalkSpeed 2 → 3.5 이상** 검토(NavMeshAgent 기본값보다 낮아 걸어도 티가 안 난다).
- 참고: KMK의 BossArea는 10×10에 x+2.79 어긋난 반면 bossroom은 방 전체·정중앙이다. BT가 켜고 끄는
  그 콜라이더의 범위가 두 환경에서 크게 달라, KMK에서 "구역 밖"이던 거리가 bossroom에선 전부 "구역 안"이다.

**⚠️⚠️ 위 권고는 무효 — `origin/feature/Boss` 3커밋(민경, 미머지)이 거리창을 SO로 옮기고 값을 바꿨다 (2026-07-29 확인)**

`89bc9e1`(페이즈별 Page SO) → `e61999d`(SO 리스트 통합) → `9a2cad0`(넉백 SO + 씬 리네임).
거리창은 이제 프리팹 필드가 아니라 `9.ScriptableObject/Enemy/Boss/Wells&No.23/TwentyThreePage {0,1,2}.asset`이고,
`PageEventAction` BT 노드가 페이즈 전환 시 `PageEvent(page)`로 교체한다. 실측값:

| Page | hook | upper | grab | jump | dash |
|---|---|---|---|---|---|
| 0 | 0~4 (50) | 0~4 (35) | 0~4 (15) | 5~10 (60) | **5~10** (40) |
| 1 | 0~4 (40) | 0~4 (40) | 0~4 (20) | 5~10 (45) | **5~10** (55) |
| 2 | 0~4 (35) | 0~4 (40) | 0~4 (25) | 5~10 (25) | **5~10** (75) |

- **세 페이지 모두 0~4와 5~10만 덮는다. 10m 초과를 덮는 밴드가 하나도 없다.** bossroom 최대 거리는
  대각선 **29.67m** → **10~29.67m 전체가 Walk 구간**(가능 거리의 약 2/3). 이전(dash 10~20)과 성격이 반대다:
  "사각지대가 좁아 안 걷는다" → **"멀면 거의 항상 걷는다"**.
- 따라서 **`WalkSpeed`(=`Enemy.moveSpeed`)가 2인 것이 이제 치명적이다.** 20m를 10초 걸어온다.
  이전엔 코너 케이스였지만 지금은 상시 경로다. → **최우선 조정 대상은 dash max가 아니라 WalkSpeed.**
  권고: `moveSpeed` 2 → **5~7**(NavMeshAgent 기본 3.5보다도 낮은 현재값은 21m 방에 안 맞는다).
  대안(더 침습적): dash max를 대각선까지(예: 5~30). 단 페이즈별 jump/dash 비중 설계(40→55→75)를 깨뜨린다.
- ⚠️ **팀장이 확정한 "3~5m Walk 밴드"가 이미 4~5m로 절반이 됐다** — hook/upper/grab max가 3→4로 올랐다.
  의도 재확인 필요(민경과).
- 새 구조 자체는 개선이다: `GetRandomAttack`의 `None`→Walk 로직은 그대로이고, 수치만 SO로 빠졌다.

**⚠️ 머지 리스크 (가장 큰 이슈) — 분기점 `0aba7b3`(7/20), 미머지**

- 🔴 **`No.23.asset`이 양쪽에서 변경.** 민경 쪽 11,191줄(+6041/−5546). 그런데 **내 워킹트리도 이미 dirty
  (+3373/−3951)** — 이 세션 전부터 그랬고 내가 만든 변경이 아니다. 내용은 rid 재번호 + `Name: Self` 같은
  블랙보드 변수 삭제 = **Unity 재직렬화 churn**이다. `CommonMeleeRobot.asset`도 같은 성격(+516/−516).
  → **이 churn을 커밋하면 민경의 11k줄 작업을 덮어쓴다.** 머지 전에 반드시 `git checkout --`으로 폐기해야 한다
  (메모리 규칙 "8.BehaviorTreeGraph 수정금지"와도 일치). 거대 생성 YAML은 3-way 머지가 사실상 불가능하다.
- 🟡 `ProjectSettings/EditorBuildSettings.asset` 양쪽 변경(내 쪽 dirty + 민경 씬 리네임). union 성격이라 수동 병합 가능.
- 🟢 **소스 파일 충돌 0.** 민경이 건드린 것 = `BaseAttackChoice`, `TwentyThreeBasicAttackChoice`, `Unit.cs`,
  `KnockbackAttack`, `TwentyThreeWells_*`, `TwentyThree.prefab`, `PageEventAction`(신규), `IKnockbackSettable`(신규).
  내가 건드린 것과 **겹치는 파일이 없다.** 특히 `Enemy.cs`는 민경이 안 건드려 내 `ApplyOptionalSpeed`가 그대로 산다.

**씬 리네임 파급 — `KMKScene.unity` → `BossScene.unity`**

- `.meta`를 유지해 **GUID 보존 → 에셋 참조는 안 깨진다** ✅. `TwentyThreeArenaContext`·BossArea·Cylinder1~4·
  HomePoint 모두 BossScene에 잔존 → MapScene과 분리 유지, 이중 스폰 위험 없음(내 `ValidateBossAreaTag` 가드도 유효).
- 하지만 **이름 문자열 참조는 깨진다.** 갱신 대상: `Map/BossArenaContext.cs`, `Map/BossEncounterDirector.cs`,
  `Map/Editor/BossEncounterWiring.cs`, `Map/Editor/BossRoomAuthoring.cs`(주석), `CONTEXT.md`,
  `Docs/tech/game-structure-uml.md`, `Docs/tech/script-inventory.md`, `Docs/_local/lessons.md`.
  그리고 **`Assets/1.Scripts/Scene/BossScene.cs`는 스크립트 이름이 그대로다** — 씬만 리네임됐다.
- 그 커밋에서 FMOD `Sound`/`BGM` 오브젝트가 **제거**됐다(164줄 삭제). 보스 테스트 씬에서 BGM이 사라진 것 —
  사운드 담당(민경) 의도인지 확인 필요.

**부수 — `Unit.cs`의 `////`**

`// 방어력 경감률 적용` → `//// 방어력 경감률 적용`으로 바뀌었는데 **바로 아래 계산 코드는 그대로 살아 있다.**
경감률을 끄려던 시도였다면 실패했다(여전히 적용 중). 커밋 메시지는 "주석 정리"라 의도일 수도 있으나
데미지 밸런스에 직결되므로 확인 필요.

**부수 — 넉백**

`KnockbackAttack`에 `SetKnockbackStrength`(+`IKnockbackSettable`)가 생겨 세기를 SO로 주입하게 됐지만,
내가 보고한 **"Floor 넉백 매 프레임 재적중 + 피해 0"은 해결되지 않았다** — 중복 방지 로직은 없다.
이제 "세기 0"과 "피해 0"을 구분해서 봐야 한다.

**✅ `ChaseSpeed` 경고 정리 완료** — `Enemy.ApplyOptionalSpeed`로 교체했다. 부재는 **조용히 통과**(그래프마다
선택적으로 쓰는 변수이므로 정상), 반대로 **그래프가 쓰는데 넣을 값이 0**이면 그때 경고한다. 이유:
값 0인 프리팹에 이름만 맞추는 잘못된 수정을 경고가 유도하고 있었다.
- **이동 배선 자체는 정상**: NavMesh 부착 OK / 추격 노드 `NavigateToTargetAction(Speed→WalkSpeed)` /
  `Enemy`가 `WalkSpeed = moveSpeed = 2` 기록 성공(루트 `BehaviorGraphAgent`는 1개뿐 — 프리팹에 보이는
  두 번째 항목은 `m_GameObject: 0`인 고아 직렬화라 `GetComponent`가 올바른 쪽을 잡는다).
  단 **WalkSpeed 2는 보스치고 매우 느리다**(NavMeshAgent 기본 3.5보다 낮음) — 걸어도 티가 안 난다.
- ⚠️ **함정: `ChaseSpeed` 죽은 배선.** `[Enemy] ChaseSpeed 변수를 얻어오는 것에 실패` 경고가 뜨는데,
  No.23 그래프에 `ChaseSpeed`가 **없다**(가진 그래프는 `Enemy/CommonMeleeRobot.asset`뿐). 게다가
  `TwentyThree.prefab`의 `chaseSpeed = 0`이다. 지금은 무해하지만 다음 사람이 경고를 보고 이름을 맞추면
  **추격 속도 0이 되어 보스가 진짜로 안 움직인다.** 이름을 손대기 전에 값을 먼저 넣어야 한다.

### ▶ 후속 — 대시 2회차부터 이동 없음 = **서버 거부 후 즉시 EndDash** (원인 사슬 확정, 사유는 로그 대기)

- `PlayerDashController.RespondDashClientRpc`가 거부/중단 시 곧바로 `stateController.EndDash()`를 호출한다.
  호스트에서는 ServerRpc→ClientRpc 왕복이 사실상 같은 프레임이라 **`PlayerDashState.Tick`이 변위를 한 번도
  적용하기 전에 상태가 끝난다.** `PlayerDashState.Enter`가 `SetAnimatorMoving(false)`를 호출하므로 증상이
  "가만히 있는 애니메이션만 순간 출력 + 이동 0"으로 정확히 나타난다.
- 왜 1회차만 통과하는지는 서버 `PlayerDashValidationManager.ValidateRequest`의 거부 사유에 달렸다.
  유력 후보 = `NoFreshSnapshot`(clientLocalTime↔serverNow 시계 도메인 + `SnapshotFreshnessTolerance`) 또는
  충전 장부 epoch/revision 불일치.
- **거부가 로그 0줄이었다**(조용한 실패). 경고를 추가했다 → 다음 Play에서
  `[Dash] 서버가 대시를 취소했습니다 — approved=… / reason=… / 남은시간=… / 권한충전=…` 한 줄로 확정된다.
- ⏩ **후속 결론은 아래 「3. 대시 — 쿨타임 결함 3건 수정 완료」 참조.** 여기 적힌 후보 중
  `NoFreshSnapshot`은 확정되지 않았고, 실제로 잡힌 것은 "거부 시 충전 미환불 + 스냅샷 기준 충전 판정 +
  오프라인 멈춘 시계" 3건이다.

### ▶ 부수 발견 (미처리, 보고만)

- `[No.23] Floor 넉백 공격 적중: Player(Clone) (피해 0)`이 **19회 이상 연속**으로 찍힌다.
  장판 넉백이 매 프레임 재적중하면서 데미지는 0 — 중복 방지 누락과 데미지 값 둘 다 의심된다.

### ▶▶ 다음 세션 시작점 (2026-07-29 마감 · 팀장 지시)

**1. 몬스터가 NavMesh 없는 공중을 걸어서 건너온다 (확정 — 원인 미규명)**
- 팀장 확인: 몹이 순간이동한 게 아니라 **걸어서** 고립 플랫폼으로 건너왔다. 즉 NavMesh가 **틈 위 공중에
  깔려 있다.** `ReattachAgents` Warp는 원인이 아니다(그 건은 별개로 1.5m 제한으로 수정 완료 — lessons #31).
- 조사할 것: `NavMeshSurface` 설정(`agentClimb`/`agentRadius`/voxel size). 낮은 단차를 이어붙이는
  `agentClimb`가 크면 플랫폼 사이 틈이 walkable로 연결된다. `MapNavMeshBaker.Awake`가 강제하는 값은
  `useGeometry`·`collectObjects`·`layerMask`뿐이고 **에이전트 파라미터는 씬 세팅 그대로**다 — 거기부터 본다.
- ⚠️ 이번 세션에 내가 바꾼 것도 용의자다: NavMesh를 **다리가 열린 상태로** 굽게 했다(`BakeOpenScope`).
  카브(`ZoneBridgeGate` 의 `BridgeGapCarve`)가 안 먹으면 다리가 물러난 구간이 walkable로 남는다.
  Play에서 그 오브젝트가 생성되는지, 크기가 다리 구간을 덮는지 먼저 확인할 것.

**2. 민경 팀원 커밋 받기 (`origin/feature/Boss`) — 커밋 추가 도착, 총 13개 ahead**
- 기존 3개(`89bc9e1` 페이즈별 Page SO · `e61999d` SO 리스트 통합 · `9a2cad0` 넉백 SO + BossScene 리네임)에
  더해 `465a934`(몬스터 시간 제어 HitStop/SlowMotion + `WaitForAnimState` BT 노드) ·
  `01fd648`(`SetNumberWithTag` onlyCountRoot) · `677ffc4`(No.23 BT 그래프) · `63242b2`(maxRageCount 2→5).
- 팀장 방침(2026-07-29): **feature/Boss 쪽을 권위로 받고 내 로컬 수정본은 폐기**한다.
- 상세 분석·리스크는 이 문서 위쪽 「⚠️⚠️ 위 권고는 무효」 절 참조.

**2-b. ✅ 머지 완료 (2026-07-29 · `3ef3cab`, 컴파일 0에러 0경고 / Play 검증 대기)**

- 충돌 10건 해결: 보스 소유 에셋(BossScene·TwentyThree·Wells·No.23) = theirs / TagManager·Unit.cs =
  ours / BombLauncher = theirs / ChargeController·JumpController = 수동 병합 / Player.prefab = 충돌
  블록만 ours.
- ⚠️ **Player.prefab에서 살린 저쪽 clean hunk**: `PlayerDefaultAttack.targetLayer` ·
  `DefaultAttack.hittableLayers` 가 **Enemy(8) → EnemyHurtBox(14)** 로 전환됐다. 보스와 일반몹
  (`ModularRobots_R1`) 모두 레이어 14 노드가 있어 정합. `--ours`로 파일 전체를 되돌리면 256으로
  회귀해 **보스를 못 때리게 된다** — 다음에 이 파일 충돌 시 주의.
- 머지 후 컴파일 수정 1건: `BossBasicAttackChoice.PageEvent(int)` 구현 추가(`BaseAttackChoice`에
  추상 멤버 신설). 코드 FSM 보스는 Page SO 체계가 없어 의도적 no-op.
- **의도적으로 안 받은 것**: Player.prefab의 `AudioListener` + `FMODUnity.StudioListener`.
  이 머지는 FMOD 파일·참조 코드를 하나도 안 가져오고(`51adcf1`에서 사운드 분리) 이 워킹카피에 FMOD가
  미설치라 받으면 Missing Script가 된다. AudioListener를 Player 프리팹에 두면 멀티에서 리스너가
  여러 개가 되는 문제도 있다 → `feature/Sound` 머지 때 함께 검토.
- 머지 직후 Unity가 BT 에셋 3개(`No.23` +4598/-5033 등)를 재직렬화했다 → 규칙대로 폐기했다.
  커밋하면 민경 저작 바이트를 덮어쓴다.
- 후속 커밋 `c273ad8`: `EditorBuildSettings` 경로를 BossScene으로 갱신.

**2-a. 🔴 머지로 유실된 내 작업 — 다시 해야 함 (팀장 지시로 기록)**

프리팹 YAML은 수동 머지하지 않는다(GUID/fileID 깨짐 위험). Boss 쪽을 통째로 받고 아래를 재작업한다.

| 대상 | 유실되는 내 작업 | 근거 커밋 |
|---|---|---|
| `Bomb.prefab` | 폭탄 비주얼을 아트 모델로 교체 | `1b13d6e` |
| `TwentyThree.prefab` | **생성맵에서 보스 피격 가능 + 지면 인식** (맞으면 빨갛게 되는 처리 포함) | `60f3862` |
| `TwentyThree.prefab` | 미사용 `maxShield` 직렬화 제거(재저장) | `1271b85` |
| `Wells.prefab` | 루트에 `NetworkObject` 추가 (dirty, 커밋 안 됨) | — |
| `BossScene.unity` | 보스 테스트 씬 작업분 (리네임 전 `KMKScene`) | `8e0215b` |
| `Player.prefab` | — (유실 아님) 오디오 2개는 `feature/Sound` 머지 때 재검토 | 위 2-b 참조 |

- Boss 쪽도 같은 파일을 만졌다: `18befc0`·`89bc9e1`·`e61999d`·`9dbdf8c`·`9a2cad0`·`465a934`.
  → `Bomb.prefab`/`TwentyThree.prefab`은 **양쪽 커밋 충돌**이므로 "theirs" 채택 시 위 3건이 사라진다.
- `Wells.prefab`의 `NetworkObject`는 **HEAD에도 Boss에도 없다.** 그런데 Wells는 오래전부터
  `DefaultNetworkPrefabs.asset`에 등록돼 있다(`6e2c783`) → 지금 레포는 "네트워크 프리팹으로 등록됐지만
  `NetworkObject`가 없는" 무효 상태다(`TwentyThree.prefab`은 갖고 있다). 머지 후 인스펙터로 재부착하고
  별도 커밋할 것. 민경과 담당 경계 확인 필요.
- `CommonMeleeRobot.asset` dirty(내 로컬 1032줄)는 BT 리세이브 churn → `git checkout --`으로 폐기하고
  Boss 쪽 1828줄을 받는다. `0.BootStrapScene.unity` dirty는 **내용 차이 0**(개행 변환뿐), 팀장 지시로 보존.
- 🔴 **머지 전 필수**: `git status`로 `Assets/8.BehaviorTreeGraph/*` dirty 확인 → dirty면 `git checkout --`으로
  폐기. Unity가 리컴파일마다 재직렬화하므로 계속 되살아난다. 커밋하면 민경의 11k줄을 덮어쓴다.
- 🔴 **`git add -A` 금지** — 위 BT 에셋이 섞여 들어간다(이번 세션에 실제로 한 번 섞여 amend로 제거).

**3. 대시 — 쿨타임 결함 3건 수정 완료 (2026-07-29 후속, Play 검증 대기)**

팀장 판단("1회 제한이 아니라 쿨타임이 안 돈다")이 맞았다. `DashChargeLedger`의 회복 계산 자체는
정상이고(별도 콘솔 하네스로 실행 검증), 문제는 **쿨타임이 리셋/동결되는 경로**였다.

- **(1) 거부 시 예측 충전이 환불되지 않았다** — 오너는 입력 순간 소비로 `Revision`을 올리고, 거부한
  서버는 소비를 안 해 `Revision`이 더 낮다. 그래서 응답의 권한 충전값이 `SyncToAuthoritative`의
  과거-리비전 가드에 걸려 **조용히 버려졌다**. 결과: 거부 1회 = 대시 안 나가고 재충전 2초는 통째 손실.
  → `DashChargeLedger.ForceAdoptAuthoritative`(리비전 무시 채택) 추가, 거부 경로에서만 사용.
  잔여시간은 응답의 `nextChargeReadyServerTime`을 오너 도메인으로 환산해 이식한다.
- **(2) 충전 유무를 과거 스냅샷으로 판정했다** — `snapshot.ChargeCount`는 마지막 물리 tick 값이라
  회복 경계 직후에는 아직 0이다. 충전은 서버만 바꾸는 자원이라 지연보정할 이유가 없다(오탐만 생긴다).
  → `DashValidationPolicy.Validate`에 `authoritativeChargeCount` 파라미터 추가, 현재 서버 장부로 판정.
- **(3) 오프라인 Play에서 시계가 멈춰 충전이 영구히 회복되지 않았다** — `NetworkClock`은 세션이 안 돌면
  `LocalNow`/`ServerNow`를 **상수 0**으로 돌려준다. 그런데 `OwnerNow()`는 `Instance != null`만 보고
  폴백을 결정했다 → 프리팹은 씬에 있고 세션은 안 켠 상태(예: `PlayerDashTest` 단독 Play)에서
  `Advance(0.0)`만 반복 → **대시 딱 1회**. → `NetworkClock.IsRunning` 추가하고 그걸로 폴백 판단.
- 부수: 서버 충전 소비 시각을 RPC 도착시각 → **추정 입력시각**으로 옮겼다(오너/서버 회복 시점 정렬).

⚠️ **반증된 가설 (기록용)**: "원격 클라는 오너가 서버보다 먼저 회복해서 경계 입력이 구조적으로
NoCharge 거부된다"— 12초 시뮬레이션으로 반증됐다. 승인 응답의 `SyncToAuthoritative`가 오너 타이머를
**응답 도착 시점**으로 재시작하기 때문에 오너는 항상 서버보다 RTT만큼 **늦다**. 부작용으로 오너
체감 쿨타임 = `rechargeDuration + RTT`(100ms RTT면 2.1초)다. 지금은 안전한 방향이라 그대로 뒀다.

- 남은 검증: Play 1회. 성공 경로에도 로그를 넣었으므로
  `[Dash] 시작 — 남은충전 n/1, 재충전 2.00s, now=…, 시계=NetworkClock|Time.timeAsDouble` 한 줄이 뜬다.
  **`시계=Time.timeAsDouble`로 찍히면 (3)의 상황**이고, 간격이 2초보다 훨씬 길면 아직 다른 원인이 있다.
- EditMode 테스트: 정책 2건·장부 3건 추가(`DashValidationPolicyTests`·`DashChargeLedgerTests`).
  에디터가 열려 있어 Test Runner 배치 실행은 못 했고, 대신 순수 로직 파일을 `dotnet`으로 떼어
  같은 단정을 실제 실행해 전부 통과 확인했다. **Test Runner 실행은 아직 안 했다.**

**3-a. 보스룸 진입 경사(`Env_object_bossroomenter`) 콜라이더 — 완료 (2026-07-29)**

- 증상: 보스룸 진입 4방향 경사를 못 올라간다. 콜라이더가 없었다(lessons #29 재발).
- 원인 = **두 저작 경로가 모두 놓치는 사각지대**. 이 오브젝트는 존 프리팹 안의 **fbx 모델 프리팹
  인스턴스**다. ① fbx 임포터 `addColliders: 0`이라 모델 쪽에서 안 붙고, ② `MapColliderAuthoring`의
  `AddFloorWallColliders`는 `IsPartOfPrefabInstance`면 건너뛴다(원본 프리팹에서 1회 붙이는 전제인데,
  여기서 원본은 fbx라 컴포넌트를 붙일 수 없다). 이름 필터(`floor/wall/hallway/slope/stair`)에도
  `Env_object_*`는 소품으로 분류돼 안 걸린다.
- 조치: 새 메뉴 `Tools/Map/Authoring/Add MeshColliders to Walkable Model Instances`.
  허용목록(`bossroomenter`) − 제외목록(`_mv_`)으로만 동작한다. **엘리베이터
  `Env_object_MV_bossroomenter`는 이동 플랫폼이라 의도적으로 제외**(팀장 확인).
  fbx `.meta`는 SVN 관리라 임포터 설정을 못 건드리므로 git 쪽(존 프리팹)에 인스턴스 오버라이드로 붙였다.
- 형상 실측(면적 가중, 삼각형 법선): 위쪽 면이 **0~10도 7% / 20~30도 84% / 30~40도 8% → 전부 60도 이하.**
  완경사라 MeshCollider(비볼록)로 충분하고 계단식 램프 박스 대체는 불필요하다.
  ⚠️ bounds(rise/run)로 각도를 추정하면 헛값이 나온다(첫 시도 65.8도 — 중앙 구조물 높이를 경사로 착각).
  도구가 이제 각도 분포를 로그로 남긴다.
- ⚠️ **부수효과 주의**: 이 계열 도구는 `Assets/2.Prefabs/Map` 전체를 `LoadPrefabContents`로 순회하는데,
  그 과정에서 **중첩 프리팹 에셋의 루트 위치가 0으로 정규화**되는 일이 있다(이번에 `bossroom.prefab`의
  루트가 `(6.199, 0, 108.774)` → `(0,0,0)`으로 바뀌어 되돌렸다). MapScene 인스턴스는 위치 오버라이드를
  3축 다 갖고 있어 영향은 없었지만, **도구 실행 후 `git status`로 의도 외 프리팹 변경을 확인할 것.**

**4. 미착수 (오후 목표 잔여)**
- 벤트에서 증기 나옴
- 이동 플랫폼 바닥 `Env_MV_floor_typeA` 컨베이어 — 은희와 협의 후 추가

**완료된 것 (이번 세션 후반)**
- 다리 개통 F 상호작용 **동작 확인**(패널 4개 → 링 4개 → 다리 lerp 이동). 링 각도 버그 수정
  (원을 로컬 XZ에 그리고 또 90° 돌려 벽면이 됐던 것 → 로컬 XY로 그림).
- 다리 조각에 MeshCollider 자동 부착(없으면 NavMesh에 안 올라간다 — lessons #29).
- 열림 위치 저작 완료(팀장 확인: 의도 맞음). `Record Bridge CLOSED/OPEN Positions` 도구 2개.

### ▶ 이전 세션 시작점(참고용)

**증상: 보스룸으로 이동은 되는데 보스가 안 나온다 — 정상이다.** MapScene에는 보스를 스폰하는
주체가 없다. 보스를 스폰하는 `TwentyThreeArenaContext`(`OnNetworkSpawn`에서 `boss.Spawn()`)는
`BossScene`·`PlayerBossTest`에만 배치돼 있고, **MapScene의 `TwentyThree.prefab` 참조는 0건**이다.
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