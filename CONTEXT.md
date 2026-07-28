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
  `(0,0,0)`이고 코드에서 아무도 채우지 않는다. KMKScene은 아레나가 원점이라 우연히 맞았다.
  → Director가 스폰 직후 착지점(방 중앙)으로 채운다.
- **보스 진입로가 막혔다(직전 세션 회귀)** — 레이저 프리팹에 심은 차단벽이 Stage1 통로 26곳을
  전부 막았다. 어느 슬롯이 Quest가 되는지는 시드마다 달라 정적 배치로는 불가.
  → 레이저 벽 제거, `MapContentSpawner`가 역할 확정 시점에 **Quest 존만** 네 변으로 감싼다.
- **중간보스 감축** — 마커 수 = 스폰 수. 엘리트 그룹(5)은 마커 1개 제한 + 초과 마커 정리
  → `ZoneL_typeC` 4 → 1. 위치 수동 조정은 재실행해도 보존(앞쪽 마커 유지).

### 이어서 완료 — Play 피드백 4건 (`60f3862`·`66ac555`)

**★ 교훈: 민경 님 보스 코드는 아레나 바닥이 `Ground` 레이어에 Y=0이라고 가정한다(KMKScene 기준).
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

**분석 3 — `BossArea`가 실제로 쓰이는지 (내가 만들었지만 검증 안 됨)**
- 사실: KMKScene·PlayerBossTest에만 있었고 MapScene엔 0건이었다 → `bossroom.prefab`에 tag `BossArea`
  트리거 박스(20.98×2×20.98, 중앙 (0.49, 0.61, 0.49))를 만들어 넣었다. `TwentyThreeArenaContext`는
  **일부러 안 붙였다**(붙이면 보스 이중 스폰).
- 분석할 것: No.23 BT가 BossArea를 **태그 검색으로 찾는지, 블랙보드 GameObject 참조로 받는지**.
  블랙보드 참조라면 프리팹에서 스폰된 보스는 씬 오브젝트를 못 잡으므로 **Director가 주입**해야 한다.
  BT 그래프(`8.BehaviorTreeGraph/Boss/Wells&No.23/No.23.asset`)는 **읽기만** 하고 수정하지 말 것(민경 님 영역).

**분석 4 — 어긋난 존 배치 = 구멍·몹 낙하의 근본 (재저작 대기, 코드로는 못 고침)**
- 사실: `Validate Slot Authoring` 결과 **미저작 9건 / 참조 잃은 저작 항목 9건**(개수 일치).
  대상 = `ZoneM_typeA`, `Zone_typeQuest01`, `Zone_typeQuest02` × Slot 4·8·9.
  원인 = 그 프리팹들이 재생성돼 GUID가 바뀌면서 저작 데이터가 고아가 됐다.
- 해결은 코드가 아니라 **재-fitting + Save Placements**(팀장 작업). 그전까지 그 존들은 baseline에 떨어져
  통로와 어긋난다 → 몹은 이제 스폰되지 않고 에러 로그만 남는다(낙하는 막았다).
- 분석할 것: 재저작 없이 임시로 버티려면 어떤 대체가 가능한지(예: 미저작 조합을 셔플 풀에서 제외).

**참고 — 이번 세션에 실제로 검증된 것**
- 보스 등장 흐름 정상: 로그 `SpawnPoint를 방 중앙 (500.49, 0.61, 0.49)으로 설정` → 하강 → `전투 시작 — BT 개방`.
  스크린샷의 보스 좌표 `(13.33, 0.08, -4.83)`은 그 수정 **이전** Play다.
- 미니맵 베이크 자체는 정상(`중앙 샘플 평균 밝기 0.308`). 남았던 원인은 실루엣 마스크였다.
- 레이저 통로 차단(26곳)은 **의도된 것**이며 유지한다. 내 판단으로 제거했다가 원복했다(`5dee39d`).
- ⚠️ 보스룸 저작 도구(`Rebuild Boss Room Bounds`)는 `PlayerArrivalPoints`·`BossLandingPoint`를 **재생성**한다
  → 실행 후 `Wire Boss Encounter (MapScene)`를 반드시 재실행(참조 끊김). 손으로 옮긴 지점도 초기화된다.

### ▶ 이전 세션 시작점(참고용)

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