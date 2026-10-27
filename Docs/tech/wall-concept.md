# 벽 개념 — Wall / Ground / Barrier 레이어 · 공용 벽 충돌 이벤트 (할 일 32)

> 사용자 결정(2026-10-13): 게임 세계에 '벽' 개념을 둔다. **벽 = Wall 태그 + Wall 레이어, 바닥 = Ground 태그 + Ground 레이어.**
> 존·보스방 프리팹의 태그·레이어 지정은 **사용자가 직접** 한다(코드는 그 세팅을 전제로 동작). 벽 효과는 **이벤트만 먼저** — 피해·기절 등은 없다.
> 코드: `Utility/GameLayers.cs` · `Unit/WallImpact/UnitWallImpactJudge.cs`(순수) · `Unit/WallImpact/UnitWallImpacts.cs`(서버 판정) · 테스트 `UnitWallImpactJudgeTests`(플레이어 러너).
> 몬스터 쪽 훅·23호 이관 방법: [handoff-wall-impact.md](handoff-wall-impact.md) · 튜토리얼 존3 연계: [tutorial-zone3.md](tutorial-zone3.md).

## 1. 정의
| 이름 | 태그 | 레이어 | 무엇 | 이동 막음 | 벽 충돌 이벤트 | NavMesh 베이크 |
|---|---|---|---|---|---|---|
| 벽 | `Wall` | `Wall`(7) | 존·보스방의 벽(저작된 콜라이더) | O(플레이어 모터·NavMesh) | **O** | O(수집) |
| 바닥 | `Ground` | `Ground`(3) | 밟는 면 | — | — | O(수집) |
| 배리어 | (없음) | **`Barrier`(19, 새로 추가)** | 튜토리얼 배리어·개인 뒤 배리어(런타임 생성 `MapSceneBarriers`) | O(플레이어 모터) · 몬스터는 NavMeshObstacle 카브 | **X** | X(카브) |
| 그 밖 | — | `Default`·`Env` | 아직 분류 안 된 지오메트리·소품 | O | X(벽을 가리는 '막힘') | Default O · Env X |

- Barrier 레이어의 물리 충돌 매트릭스는 Wall 과 같게 복사했다(`ProjectSettings/DynamicsManager.asset`).
- 🔴 배리어 레이어는 코드가 정한다(`GameLayers.BarrierLayer`) — 프리팹·씬에서 Barrier 를 쓰지 말고, 배리어 오브젝트를 Wall 로 바꾸지도 말 것.

## 2. 사용자용 세팅 체크리스트
1. 존·보스방 프리팹의 **벽** 오브젝트(콜라이더가 붙은 것): 태그 `Wall` + 레이어 `Wall`. 자식 콜라이더가 따로 있으면 그 오브젝트의 레이어도 Wall.
2. **바닥**: 태그 `Ground` + 레이어 `Ground`(대부분 c5826a3 에서 이미 Ground).
3. **배리어는 건드리지 않는다** — 런타임에 Barrier 레이어로 생긴다.
4. 세팅 뒤 확인(Play): 넉백·밀치기로 몬스터/플레이어를 벽에 박으면 콘솔에 `[WallImpact] <유닛> → 벽 '<콜라이더>' …` 1줄(넉백 한 번에 1회). 배리어·상자·다른 유닛에 막히면 안 나와야 한다.
5. 튜토리얼 존3: 활성 때 `[Tutorial] 존3 Wall 레이어 콜라이더 0개` 경고가 나오면 존3 벽이 아직 Wall 이 아니다(그 상태로는 연계가 성공하지 않는다).
6. ⚠️ `Tools/Map/Authoring/Tutorial` 의 `MapSceneStageAuthoring`(벽 BoxCollider 다시 붙이기)은 이제 벽 레이어를 **Wall** 로 둔다(예전 Default). 태그는 바꾸지 않는다.

## 3. 공용 벽 충돌 — API
- 판정 호출(서버): `UnitWallImpacts.ServerProbe(target, episode, direction, requested, moved, speed, pusher, out impact[, contactSlack, blockedSlack])`
  - 외부 힘으로 이번 틱 움직인 **뒤** 부른다. 지금 위치에서 몸 캡슐(루트 비트리거 CapsuleCollider → NavMeshAgent → 0.5/1.8)을 `direction` 으로 스윕.
  - 판정 거리 = 막힘 없으면 `contactSlack`(0.4m), 이동이 막혔으면(`moved` < `requested`/2 — NavMesh 끝) `blockedSlack`(1.6m).
  - 스윕 레이어 = Wall + 막힘(Barrier · Default · Env · Player · Enemy · EnemyHurtBox(상자)). **가장 가까운 것이 Wall 이어야** 벽 충돌. 자기 콜라이더·시작부터 겹친 비벽(바닥·옆 유닛)은 버린다.
  - 정면 판정: 진행 방향 · 벽 수평 법선 ≤ −0.3(약 73° 안). 윗면(수평 법선 없음)은 벽 아님.
  - `WallImpactEpisode` = 외부 힘 한 번. `Begin()` 한 뒤 첫 충돌만 1회(`TryConsume`). 같은 넉백 갱신(Q 홀드 견인)은 Begin 을 다시 부르지 않는다.
- 결과 `UnitWallImpact`: `Target`(맞은 Unit) · `Pusher`(민 쪽, 모르면 null) · `Point`(접촉점) · `Normal`(벽 수평 법선, 벽→유닛) · `Speed`(충격 속도 = 속도 × 법선 반대 성분) · `Wall`(콜라이더, 서버 로컬).
- 이벤트
  - `UnitWallImpacts.ServerImpacted`(static, 서버) — 전역 구독.
  - `Unit.ServerWallImpacted`(인스턴스, 서버) — 특정 유닛만(튜토리얼 스피너봇이 씀).
  - `Unit.WallImpactPresented(unit, point, normal, speed)`(모든 피어) — 연출 훅. Unreliable RPC `WallImpactPresentRpc`. **지금 구독자 없음**(연출 미정).
- 서버 권한(Docs/tech/networking.md): 판정·이벤트는 서버만. 클라에서 `ServerProbe` 를 불러도 아무 일도 없다.

## 4. 호출 경로(현재)
| 외부 힘 | 파일 | 에피소드 시작 | 민 쪽 | 비고 |
|---|---|---|---|---|
| 몬스터 지속넉백(AttackInfo.knockback*) | `MonsterBase.HandleKnockback` | `TryEnterKnockback` 새 진입 | `hitContext.sourceTransform` 의 Unit | NavMesh 클램프 이동 — 막힘 거리 1.6 |
| 몬스터 임펄스 넉백(`Unit.Knockback`) | `LinearKnockback.FixedUpdate` | `ApplyKnockbackClientRpc`(오너 = 서버) | null | 물리가 벽에서 멈춤 — 처음 민 방향·직전 속도로 판정 |
| 플레이어 넉백(`Unit.Knockback`) | `Player.Update`(서버) → `PlayerWallImpactTracker` | `Player.OnKnockback` 수락 | null | 이동이 오너 권한 → 서버는 복제 위치로 판정. 속도 = 세기 − 감속×경과 |
| 튜토리얼 스피너봇 우클릭 밀치기 | `TutorialSpinnerPractice.TickCombo` | `NotifyInterruptHit`(새 밀치기) | 우클릭 시전자 | 존3 연계 판정이 이 이벤트를 쓴다 |
| 허수아비 지속넉백(AttackInfo.knockback* — 팔라딘 Q 견인 등) | `TrainingDummy.TickKnockback`(서버) → `TrainingDummyKnockbackSweep` | — (**벽 충돌 이벤트 없음**) | — | 할 일 51. transform 직접 이동이라 매 프레임 몸 캡슐(`UnitWallImpacts.GetBodyCapsule`)을 Wall·Barrier 로만 스윕해 벽 앞(스킨 0.02)에서 멈춤. 시작 겹침 = 정지. 타이머·자리 복귀는 그대로 |
| 23호 취약 넉백 | (안 바꿈) | — | — | `InvisibleBoundaries` 경계 판정 그대로 — 이관 방법은 handoff |

## 5. 마스크 전수 조사 — 벽이 Default → Wall 로 옮겨질 때 / 배리어가 Wall → Barrier 로 옮겨질 때
| 위치 | 마스크(전 → 후) | 벽 Default→Wall 이면 | 배리어 Wall→Barrier 이면 | 조치 |
|---|---|---|---|---|
| `PlayerGameRuleData.obstacleMask`(에셋 2441 → 526729) · 코드 기본값 | Default·Ground·Wall·Env → **+Barrier** | 이미 Wall 포함 — 그대로 막힘 | 빠지면 배리어 통과 | **Barrier 추가** |
| `PlayerMotor.ResolveObstacleMask` 폴백(규칙 없음) | 같음 → **+Barrier** | 그대로 | 통과 | **Barrier 추가** |
| `PlayerGameRuleData.groundMask`·`soulGroundMask`(2057) · `PlayerGroundingSensor`·`Player.ResolvePlatformRiderMask` 폴백 | Default·Ground·Env | 벽 윗면이 더는 접지 아님(의도 — 벽 위에 서지 않는다) | 무관 | 없음(바닥=Ground 로 동작) |
| `MapNavMeshBaker.surface.layerMask` | Default·Ground → **+Wall** | **벽이 베이크에서 빠져 벽 자리에 NavMesh 가 깔림** | 무관(카브) | **Wall 추가** · Barrier 는 넣지 않음 |
| 씬 NavMeshSurface(4.MapScene·BossScene·TrashMobScene 등 = Everything) | Everything | 그대로 | (베이크 시점에 배리어 없음) | 없음 — `MapNavMeshBaker` 가 런타임에 덮어씀 |
| 씬 NavMeshSurface `MonsterScene`(9 = Default·Ground) | Default·Ground | **수동 베이크하면 벽 빠짐**(테스트 씬) | — | 보고만(경석 테스트 씬 — 재베이크 전 Wall 추가 필요) |
| `TurretHeadAim.laserBlockers`(PeekABot·TeslaBot 프리팹 = 0 → 코드 기본) | Default·Ground → **+Wall** | **예고선이 벽을 뚫음** | — | **코드 기본에 Wall 추가**(프리팹 값 0 그대로) |
| 거너 `GunnerBasicAttackData`·`GunnerChargeLaserData.blockingLayers`(에셋 2177 → 526465) · 코드 기본 | Default·Wall·Env → **+Barrier** | 이미 Wall 포함 — **일반 존 벽에 계속 막힘** | 빠지면 배리어 관통 | **Barrier 추가**(지금처럼 배리어에 막힘) |
| `MapSceneDeathRules._obstacleMask`(부활 지점 점유) | ground·Wall → **+Barrier** | 그대로 | 배리어 위 부활 가능 | **Barrier 추가** |
| `PartyWipeRespawner` 패드 링 점 장애물 | ground·Wall → **+Barrier** | 그대로 | 같음 | **Barrier 추가** |
| `GroundProbe.TryFindGround` | Default·Ground(+호출부) | 벽 윗면에 착지 안 함(개선) | 무관 | 없음 |
| `MapContentSpawner.TryResolveSpawnPoint` | Default·Ground | 마커가 벽 위면 아래 바닥으로 | 무관 | 없음 |
| `MonsterCollapseDebris.PartCollisionMask` | Default·Ground·Wall·Env | 그대로 | 파편이 배리어 통과(전과 달라짐 — 무해) | 없음 |
| `BossBomb.wallMask`(Bomb 프리팹 128 = Wall) | Wall | 보스방 벽이 Wall 이어야 미끄러짐 멈춤(BossRoomAuthoring 이 이미 Wall) | 무관 | 없음 |
| `FogManager.losWallMask`(씬 Everything) | Everything | 그대로 | 그대로 | 없음 |
| `MapSceneBarrierPreview`(에디터 점검) | 'Wall 아닌 것 = 벽' | **Wall 벽을 못 찾음** | — | **Barrier 제외로 수정** |
| `MapSceneStageAuthoring`(에디터) | 벽 = Default 로 지정 | 사용자 세팅을 되돌림 | — | **Wall 로 지정하게 수정** |
| 플레이어 모터 스윕·대시·넉백 | obstacleMask | 위와 같음 | 위와 같음 | — |

- 그 밖의 직렬화 LayerMask(2.Prefabs · 9.ScriptableObject 1051개 전수 스캔): Default 있고 Wall 없는 것은 바닥 마스크(BossBomb.groundMask 9 · 규칙 groundMask 2057)와 카메라 볼륨뿐 — 벽과 무관.
- 23호 보스방 `InvisibleBoundaries` 는 바꾸지 않았다(위 표 밖 — handoff 참조).

## 6. 알려진 한계
- 플레이어 넉백 판정은 서버가 보는 **복제 위치** 기준이다(이동 = 오너 권한). 원격 클라의 위치 갱신 간격만큼 늦게 날 수 있다.
- 임펄스 넉백(`LinearKnockback`)·플레이어 넉백은 `Unit.Knockback(방향, 세기)` 에 공격자가 없어 `Pusher` 가 null.
- 연출(이펙트·사운드·흔들림)은 없다 — `Unit.WallImpactPresented` 에 붙이면 된다.
