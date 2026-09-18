# PLAN — 플레이어 이동을 Motor 기반으로 재정립 (2026-09-11, 승인 대기)

작성: 은희(Claude) / 대상: `Assets/1.Scripts/Player/**`, `Assets/2.Prefabs/Player/**`
관련: [AGENTS.md](AGENTS.md) §4 코어 설계 원칙 · [CONTEXT.md](CONTEXT.md) · [AIRULE.md](AIRULE.md)

> 🔴 **기준 브랜치: `origin/development` (`72392d6`).**
> 이 조사는 `fix/PlayerSlopeMovement`(development 대비 ahead 4 / **behind 78**)에서 수행했으나,
> 착수는 **`development`에서 새로 딴 `feature/player-motor`** 에서 한다. 78커밋 뒤쳐진 브랜치에서
> `Player.prefab`을 건드리면 머지 시 GUID 파손 위험이 있다([AGENTS.md](AGENTS.md) §3).
>
> development와의 차이 확인 완료 — 플레이어 관련 변경 4파일 중 **이동에 관계된 것은
> `PlayerMovement.ApplyFlatGroundYLock` 하나**(§2.4). 나머지(`PlayerDefaultAttack`,
> `FirstMeleeMainSkill`, `FirstMeleeInterruptSkill`)는 파괴 가능 상자 피격 처리라 무관하다.
> 프리팹 값(`IsKinematic=0`·`UseGravity=1`·`Interpolate=0`·마스크 `~0`)은 양쪽 동일.

---

## 1. Goal

플레이어 위치를 **단일 소유자(`PlayerMotor`)** 가 `FixedUpdate`에서만 변경하는 구조로 바꾼다.
최종적으로 이동을 결정론적 함수로 만들어 **서버 재시뮬레이션 기반 예측/재조정**까지 간다.

플레이어가 보는 변화는 원칙적으로 **없어야 한다**(감각 동일). 이 작업은 기능 추가가 아니라
기존 버그군의 발생 원인을 제거하는 구조 변경이다.

---

## 2. Current understanding

### 2.1 위치를 쓰는 곳 = 7개 (싱크)

| # | 위치 | 루프 |
|---|---|---|
| A-1 | `PlayerMovement.Move()` → `rb.MovePosition` ([:139](Assets/1.Scripts/Player/PlayerMovement.cs:139)) | `Update` |
| A-2 | `PlayerMovement.MoveRoot()` → `rb.MovePosition` ([:239](Assets/1.Scripts/Player/PlayerMovement.cs:239)) — **호출자 5곳** | `Update` / `OnAnimatorMove` |
| A-3 | `PlayerMovement.MoveTowardsPoint()` → `rb.MovePosition` ([:326](Assets/1.Scripts/Player/PlayerMovement.cs:326)) | `Update` |
| A-4 | `PlayerRestrainedState.Tick()` → `MovePosition` + `MoveRotation` ([:792](Assets/1.Scripts/Player/PlayerStateController.cs:792)) | `Update` |
| A-5 | `PlayerKnockbackState.Enter()` → `AddForce(Impulse)` ([:915](Assets/1.Scripts/Player/PlayerStateController.cs:915)) | `FixedUpdate`(PhysX) |
| A-6 | 텔레포트/리셋 — `PlayerFallRecovery`, `BossTeleportManager`, `DevBossEntranceWarp` | 각기 다름 |
| A-7 | **중력** — `UseGravity=on` + `IsKinematic=off`이라 PhysX가 매 스텝 위치를 바꾼다 | `FixedUpdate`(PhysX) |

A-7은 코드에 호출부가 없지만 위치를 쓰는 주체다. **`Update`가 지정한 위치 위에
`FixedUpdate`가 중력을 얹고 솔버가 되밀어내는 3자 경쟁이 매 스텝 벌어진다.**

### 2.2 의도를 만드는 곳 = 12개, 루프는 3개

`Update`(걷기·대시·러시·스킬전진·캐리·Grabbed) / `OnAnimatorMove`(평타 루트모션) /
`FixedUpdate`(중력·넉백). `ProjectSettings`에 `MonoManager.asset`이 없다 —
**Script Execution Order가 아예 설정돼 있지 않아** 이 셋의 순서가 불확정이다.

### 2.3 충돌·경사 해결 = 4개, 그중 2쌍이 중복

| | 무엇 | 기준 | 쓰는 곳 |
|---|---|---|---|
| C-1 | `PlayerMotionSweep.Resolve` | `ObstacleMask`, 등판각 통과, 접선 슬라이드 | 걷기·대시 |
| C-2 | `PlayerMovement.ClampByStaticGeometry` ([:243](Assets/1.Scripts/Player/PlayerMovement.cs:243)) | 마스크 하드코딩, **등판각 무시**, 벽 앞 정지 | `MoveRoot` 전부 |
| C-3 | `PlayerMovement.ProjectOntoGround` ([:149](Assets/1.Scripts/Player/PlayerMovement.cs:149)) | 지면 노멀 투영 | 걷기 |
| C-4 | `PlayerDashState.ResolvePlanarSlopeDirection` ([:1177](Assets/1.Scripts/Player/PlayerStateController.cs:1177)) | C-3의 사본 | 대시 |

**C-1 → C-2가 같은 프레임에 연달아 적용된다.** 마스크·등판각 판정이 달라서 C-1이 통과시킨
경사를 C-2가 다시 막는다. 코드가 이미 알고 있다 ([PlayerMovement.cs:265](Assets/1.Scripts/Player/PlayerMovement.cs:265) 경고 주석).

### 2.3.1 🔴 장애물 마스크가 경로마다 다르다

| 경로 | 마스크 | 다른 플레이어/몬스터를 |
|---|---|---|
| 걷기 스윕 | `Default/Ground/Wall/Env` ([:54](Assets/1.Scripts/Player/PlayerMovement.cs:54)) | 통과한다 |
| `MoveRoot` 클램프(C-2) | 위와 동일(하드코딩) | 통과한다 |
| 대시 스윕 | **`~0` (전체 레이어)** | **막힌다** |
| PhysX 솔버 | Layer Collision Matrix | **밀어낸다**(dynamic이라) |

대시 마스크가 `~0`인 이유: [PlayerDashData.asset](Assets/9.ScriptableObject/Player/PlayerDashData.asset)에 `dashObstacleMask` 키가 **없어서**
코드 기본값 `= ~0`([PlayerDashData.cs:31](Assets/1.Scripts/Player/Dash/PlayerDashData.cs:31))이 적용된다. 대시는 `Player`·`Enemy`·`Projectile`·
`HazardArea`·`PlayerHurtbox`·`Corpse`·`Soul`까지 전부 장애물로 본다 — **"대시가 안 나간다"의 또 다른 후보.**

그리고 대시만의 문제가 아니다. **`Player.prefab`의 마스크가 전부 `~0`(`m_Bits: 4294967295`)이다**:

| 필드 | 값 | 결과 |
|---|---|---|
| `aliveGroundMask` | `~0` | **접지 판정이 모든 레이어를 지면으로 본다** — 다른 플레이어 캡슐·몬스터·투사체·`HazardArea`·히트박스 위에도 선다 |
| `soulGroundMask` | `~0` | 위와 동일 |
| `platformRiderMask` | `~0` | `RaycastAll`이 전부 맞음. `ISurfaceCarrier` 필터가 있어 무해하지만 낭비 |
| `dashObstacleMask` | `~0` (키 없음) | 위 표 참조 |
| 걷기 `rootMoveBlockingMask` | 코드 하드코딩 4개 | 유일하게 좁혀진 것 |

**즉 "어떤 레이어가 지면/장애물인가"라는 규칙이 어디에도 저작돼 있지 않다.** 3단계의 GameRule
단일화(§3 3단계)가 이 전체를 덮는다.

### 2.4 `ApplyFlatGroundYLock` — development의 우회책 (Motor가 삭제한다)

`development`의 `PlayerMovement`에는 평지 접지 중 `rb.constraints`에 `FreezePositionY`를 거는
`ApplyFlatGroundYLock()`이 있다(`FixedUpdate`에서 갱신). QA `P9-PlayerWallClimb` 대응이다.

그 doc 주석이 **A-7의 메커니즘을 그대로 확인해준다** — *"루트 Rigidbody는 저작상 non-kinematic이라
`rb.MovePosition`이 텔레포트가 아니라 '목표까지 가는 속도 + 솔버의 충돌 해석'으로 동작한다. …
모서리·베벨 면의 법선에 섞인 미세한 +Y가 매 스텝 쌓여 벽을 타고 오른다. `PlayerMotionSweep`은
MovePosition **전에** 끝나 이걸 막을 수 없고 사후 보정은 한 프레임 늦으므로, 솔버가 Y를 아예 못
건드리게 제약으로 막는다."*

시사점 셋:

1. **솔버 디페네트레이션이 Y를 주입한다는 진단이 독립적으로 확인됐다.** (D-2 근거 보강)
2. **`rb.constraints` 런타임 조작 = 솔버와 싸우는 다섯 번째 지점.** §2.1의 싱크 7개에 더해
   `initialConstraints`를 Awake에 캡처·복원하는 프리팹 플래그 의존이 하나 더 있다.
3. **3단계에서 통째로 삭제된다.** kinematic이면 디페네트레이션이 없으므로 Y 잠금이 불필요하다.
   `FlatGroundNormalY`·`VerticalIntentEpsilon`·`lastVerticalIntentY`·`initialConstraints`도 함께 사라진다.

부수 효과: development의 `PlayerMovement`는 **이미 `FixedUpdate`를 갖고 있다**(Y 잠금용).
`Move()`는 여전히 `Update`이므로 1단계 작업은 그대로 유효하며, 오히려 진입점이 이미 있어 더 간단하다.

### 2.5 이 구조가 만든 기존 버그

| 커밋 | 증상 | 원인 |
|---|---|---|
| `f990c73` | 경사 못 올라감 | `MovePosition`은 경사 보정을 안 한다 |
| `412d31f`, `81ee10e` | 벽 관통 | `MovePosition`은 경로를 스윕하지 않는다 |
| `48b7149` → `73294bd`(revert) | 벽 모서리 Y 누수 | 수동 경사 투영 vs 솔버 디페네트레이션 |
| `db960dc` | 대시가 제자리에서 끝남 | 한 스텝에 `MovePosition` 2회 → 뒤엣것이 이김 |
| (미보고) | **고프레임에서 이동 속도 저하** | `Update` 다중 호출이 같은 `rb.position`에서 계산 후 서로 덮어씀 |

---

## 3. Approach — 4단계

### 1단계 — 루프 정정 (걷기만)

- `PlayerMovement.Move()`를 `Update` → `FixedUpdate`로, `Time.deltaTime` → `Time.fixedDeltaTime`.
- `Rotate()`는 시각 요소이므로 `Update` 유지.
- `Player.ApplyPlatformCarry()`도 함께 `FixedUpdate`로(캐리는 `Move()`가 소비한다).

**주의:** `stateController.Tick()`은 아직 `Update`다. 즉 이 단계에서는 걷기만 옮겨지고
대시·러시는 여전히 `Update`에 있다. 두 루프가 공존하는 과도기이며, A-1/A-2 경쟁은
2단계까지 남는다. 이 단계가 해소하는 것은 **걷기 속도의 프레임 의존성 하나**다.

### 2단계 — Motor 도입, 싱크 7 → 1

신설 `PlayerMotor` (`FixedUpdate` 전용, 위치 독점). 두 종류의 의도를 받는다:

| 채널 | 단위 | 예 |
|---|---|---|
| 속도(velocity) | m/s — 틱 길이에 비례 | 걷기, 중력, 넉백 |
| 변위(displacement) | m — 틱 길이와 무관 | 대시, 러시 스텝, 플랫폼 캐리, 루트모션 |

틱 순서(Script Execution Order로 고정):

```
Update          입력 읽기 + 엣지 입력 래치, 회전, 애니메이션
FixedUpdate ①   GroundingSensor.Sample()       접지/지면 노멀 확정
FixedUpdate ②   각 시스템이 의도 제출
FixedUpdate ③   PlayerMotor.Tick()             합산 → 경사 투영 → 스윕 → MovePosition ×1
```

작업 내역:

- A-1~A-4를 Motor 제출로 교체. 실제 `MovePosition`은 ③에서 **한 번**.
- **C-2 삭제, C-4 삭제.** 합산된 최종 델타에 C-1(스윕) + C-3(경사 투영)을 한 번만 적용한다.
- `PlayerMovement.MoveRoot` / `MoveTowardsPoint` public 제거 → Motor 제출 API로 대체.
- 🔴 **`PlayerStateContext`에서 `Rigidbody` 제거.** 이 참조가 모든 상태에 노출돼 있어서
  A-4·A-5가 직접 물리를 만질 수 있었다. 남겨두면 같은 일이 반복된다.
- B-6(`OnAnimatorMove` 루트모션)은 루프가 다르므로 **델타를 래치**했다가 다음 `FixedUpdate`에 제출.
- 엣지 입력(`DashPressed`) 래치 — 프레임이 물리보다 빠를 때 입력이 씹히는 것을 막는다.
  (현재도 잠재 문제다: [PlayerStateController.cs:80](Assets/1.Scripts/Player/PlayerStateController.cs:80) 경고 로그가 이 계열)
- 물리 플래그는 **이 단계에서 건드리지 않는다.** A-5(넉백)·A-7(중력)은 그대로 남는다.

### 3단계 — kinematic 전환 + 넉백 재작성

- `Player.prefab` 루트: `IsKinematic=on`, `UseGravity=off`, **`Interpolate=Interpolate`**.
  (Motor가 `FixedUpdate` 전용이 된 뒤에야 보간이 맞다. 현재 `None`이라 화면상 이동이 50Hz다.)
- 중력을 Motor의 속도 채널로: `verticalVelocity += Physics.gravity.y * dt`, 접지 시 스냅.
- **넉백 재작성**: `AddForce` → 초기 속도 + 명시적 감쇠(데이터). 현재 감속은 **지면 마찰**에서
  오는데, Collider `m_Material`과 프로젝트 `m_DefaultMaterial`이 둘 다 미지정이라
  **PhysX 내장 기본값(마찰 0.6)이 넉백 거리를 정하고 있다** — 아무도 튜닝한 적 없는 값이다.
  - **데이터는 `PlayerGameRuleData`에 둔다**(등판각과 같은 단일 소스). 값은 **기존 동작에 준하는
    기본값**으로 착수하고 별도 튜닝은 하지 않는다 — 현행 `minKnockbackTime 0.15` / `maxKnockbackTime 1.5` /
    `knockbackStopSpeed 0.15`([PlayerStateController.cs:13](Assets/1.Scripts/Player/PlayerStateController.cs:13))를 옮겨온다.
  - 🔴 **벽 충돌 = 그 자리 정지.** 튕기지 않고 **슬라이드도 하지 않는다**(걷기와 다르다).
  - **상태 지속시간은 유지한다** — 이동이 0이 돼도 넉백(경직)은 원래 종료시각까지 간다.
    대시의 기존 불변식([:1056](Assets/1.Scripts/Player/PlayerStateController.cs:1056))과 같은 규칙.
    (가) 즉시 종료로 하면 **벽 근처에서 넉백이 무력화된다** — 보스가 벽으로 밀어붙일수록
    경직이 짧아지는 역전이 생긴다.
- `isKinematic` / `useGravity` 토글 전부 제거:
  [Player.cs:362·381](Assets/1.Scripts/Player/Player.cs:362), [PlayerStateController.cs:836·851·910](Assets/1.Scripts/Player/PlayerStateController.cs:836),
  [PlayerSoulController.cs:185](Assets/1.Scripts/Player/Soul/PlayerSoulController.cs:185). Soul 부유는 "중력 채널 off" 한 줄이 된다.
- 🔴 **stepOffset 상당(계단·턱 넘기)을 `PlayerMotionSweep`에 추가.** 이 계획에서 **유일하게
  선례가 없는 신규 알고리즘**이자 최대 리스크. EditMode 테스트로 고정한다.
  맵에 계단 에셋이 아직 없으므로 **0.3m 후보값으로 진행**하고, 에셋이 들어오면 실측해 조정한다.

#### 3단계 — 마스크를 `PlayerGameRuleData` 단일 소스로

kinematic 전환의 **부수 효과**로 솔버 디페네트레이션이 사라진다. 그러면 "다른 유닛을 통과하는가"가
**오직 Motor 스윕 마스크의 비트 하나**로 결정된다 — 지금처럼 다섯 갈래(§2.3.1)로 흩어지지 않는다.

`PlayerGameRuleData`를 확장해 **이동 규칙을 한 애셋에 모은다.** 등판각(`maxWalkableSlopeAngle`)이
이미 여기 단일 소스로 있으니 같은 자리에 둔다.

```
PlayerGameRuleData
  maxWalkableSlopeAngle  = 60      (기존)
  ── 이동 장애물 ──
  obstacleMask           = Default | Ground | Wall | Env   (현행 걷기 마스크가 기본값)
  groundMask             = Default | Ground | Env          (Wall 제외 — 벽 위에 서지 않는다)
  blockOtherPlayers      = false   ← 🔴 기본값 = 통과
  ── 넉백 ──
  knockbackDecay         (감쇠), knockbackStopSpeed, knockbackMinTime, knockbackMaxTime
```

- `Motor`의 유효 장애물 마스크 = `obstacleMask | (blockOtherPlayers ? Player : 0)`.
  걷기·대시·러시가 같은 Motor를 거치므로 **마스크가 자동으로 하나**다. 대시의 `~0`은 여기서 사라진다.
- `groundMask`가 `aliveGroundMask`/`soulGroundMask`의 `~0`을 대체한다.
- 🔴 **회귀 위험: 기본 동작이 뒤집힌다.** 지금 플레이어끼리 안 겹치는 건 **솔버 덕분**이지
  스윕 때문이 아니다(걷기 마스크는 이미 Player를 통과시킨다). kinematic이 되면
  **"플레이어끼리 통과"가 기본값**이 되는데, **이번 결정은 그 기본값을 그대로 채택한다**
  (`blockOtherPlayers = false`). 즉 의도된 동작 변경이며, 나중에 막고 싶으면 이 토글 하나만 켠다.
- **Enemy는 계속 통과한다** — 현행 동작 유지("러시가 몹 사이를 지나는 감각", [PlayerMovement.cs:54](Assets/1.Scripts/Player/PlayerMovement.cs:54)).
  필요해지면 `blockMonsters` 형제 토글을 같은 자리에 추가한다(이번 범위 아님).
- ⚠️ **비대칭 금지.** 토글은 **GameRule 애셋 하나**에서 오므로 전 플레이어가 같은 값을 본다.
  Motor별 로컬 bool로 두면 A는 B를 통과하는데 B는 A에 막혀 겹친 채 갇힌다 — 그래서 GameRule에 둔다.
- ✅ **판정과는 자동 분리된다.** 스윕 마스크는 이동만 정한다. `PlayerHurtbox` 히트박스·Overlap 쿼리·
  트리거는 영향 없다 → "통과하지만 맞긴 맞는다"가 공짜로 나온다.

#### 3단계 — Soul 통과 (GameRule 밖)

**규칙: Soul은 Player를 통과하고, Player도 Soul을 통과한다.** GameRule 필드 없이 처리된다 —
`PlayerSoulController`가 **루트 레이어를 실제로 바꾸기** 때문이다
(`aliveLayer`(Player) ↔ `soulLayer`(Soul), [:415·433](Assets/1.Scripts/Player/Soul/PlayerSoulController.cs:415)).

| 방향 | 성립 근거 |
|---|---|
| Player → Soul 통과 | `obstacleMask`에 `Soul` 비트를 **넣지 않는다**(기본값에 없음) → 자동 |
| Soul → Player 통과 | Soul 모드에서는 **`blockOtherPlayers`를 무시**한다 → Motor가 `GroundingMode`처럼 모드로 분기 |

즉 코드 규약 두 줄이고, GameRule에는 노출하지 않는다. `Corpse`도 별도 레이어라 같은 방식으로 통과한다.

### 4단계 — 결정론 + 이동 전체 예측/재조정 (8월 이후)

- Motor의 스윕·경사·합산을 `MonoBehaviour` 밖 **순수 함수** `Simulate(state, input, dt)`로 추출.
- 입력에 틱 번호 부여 + 링버퍼. 서버 보정 수신 시 과거 틱으로 되감아 재시뮬레이션.
- `PlayerDashValidationManager`의 대시 전용 스냅샷 검증을 **이동 전체 검증으로 흡수**.

---

## 4. Key decisions and tradeoffs

**D-1. `CharacterController`가 아니라 Motor 직접 구현.**
CC는 "무엇을 얼마나 움직일지"는 자유롭게 두지만 **"충돌을 어떻게 해결할지" 한 층을 고정**한다
(노브는 `slopeLimit`/`stepOffset`/`skinWidth`/`minMoveDistance` 4개뿐).
그런데 이 프로젝트는 **이미 그 층을 게임 규칙으로 다루고 있다**:

- 등판각이 `PlayerGameRuleData.MaxWalkableSlopeAngle` 단일 소스로 걷기·대시·접지에 공유된다.
  CC의 `slopeLimit`은 `Move()` 내부에만 걸려 이 단일화가 깨진다.
- 지면 마스크가 상태별로 바뀐다 (`soulGroundMask` vs `aliveGroundMask`, [GroundingSensor:141](Assets/1.Scripts/Player/PlayerGroundingSensor.cs:141)).
- 충돌 대상을 의도적으로 좁혀놨다 — "유닛은 통과, 정적 지오메트리만 막음"
  ([PlayerMovement.cs:54](Assets/1.Scripts/Player/PlayerMovement.cs:54), 러시가 몹 사이를 지나는 감각 유지).

여기에 4단계(되감기)가 CC의 내부 상태 + `enabled` 토글 패턴과 상충한다.
**대가:** 계단 처리를 직접 짜야 한다(3단계 리스크). CC의 검증된 구현을 포기한다.

**D-2. kinematic + 수동 중력 (dynamic + velocity 아님).**
4단계의 전제가 **위치 결정론**이다. PhysX 솔버가 위치를 정하면 재시뮬레이션이 성립하지 않는다.
**대가:** 중력·넉백을 직접 적분. 다른 Rigidbody를 미는 동작이 약해진다(kinematic MovePosition은
밀어주지만 스윕 선해결 뒤라 제한적).

**D-3. `MovePosition`은 유지한다.**
`linearVelocity` 기반으로 가면 대시/러시의 **정확한 거리 보장**이 깨지고,
위치 스냅샷 기반인 서버 검증과 어긋난다. 문제는 `MovePosition` 자체가 아니라
**`MovePosition`과 중력이 같은 바디에 공존하는 것**이었다.

**D-4. 속도 채널과 변위 채널을 타입으로 구분한다.**
현재 `AddCarryDelta`(변위)가 입력 이동(속도×dt)과 그냥 더해지는 애매함이 여기서 정리된다.

**D-5. 충돌 응답 정책은 채널마다 다르다 — 스윕은 그래도 1회.**
걷기는 벽을 따라 **슬라이드**하고, 넉백은 박은 자리에서 **정지**한다.
구현은 채널별 스윕이 아니다 — Motor가 **한 번만** 스윕하고 "무엇에 막혔는지"를 돌려주면,
채널 소유자(넉백 컴포넌트)가 그걸 읽고 자기 속도를 0으로 만든다.
**정책은 채널 소유자에게, 스윕은 Motor에** — 싱크 1개 원칙은 유지된다.

> 이 요구는 D-1(CC 기각)을 한 번 더 뒷받침한다. CC의 `Move()`는 **항상 슬라이드**하며
> "이 이동은 막히면 멈춰라"를 지시할 수단이 없다.

**D-6. Dynamic 모드 도피로는 뚫어두되 구현하지 않는다.**
특정 상태가 진짜 물리를 필요로 할 수 있으므로 `Motor.SetMode(Kinematic | Dynamic)` 인터페이스와
속도 인계 규약을 **설계에는 넣는다**. 단 3단계에서 **실제로 Dynamic을 쓰는 상태는 0개**로 시작한다.
넉백을 수동 감쇠로 만들어보고 감각이 부족하면 그때 켠다.

도피로를 쓸 때 "Motor를 안 거치면 된다"로는 **부족하다.** 세 가지가 같이 필요하다:

1. **Motor는 호출자가 없어도 자기 `FixedUpdate`를 돈다.** 의도 0이라 `MovePosition`을 안 부르는 건
   *우연히* 안 부르는 것이다. Dynamic 구간에는 Motor가 "지금은 물리가 운전한다"를 **알고** 있어야 한다.
2. **전환 시 속도를 양방향 인계한다.** kinematic→dynamic이면 Motor의 수직 속도를 `linearVelocity`로
   넘기고, 반대 방향이면 되받는다. 지금 코드는 양쪽 다 `linearVelocity = 0`으로 끊는다
   ([:911](Assets/1.Scripts/Player/PlayerStateController.cs:911), [:958](Assets/1.Scripts/Player/PlayerStateController.cs:958)) — 낙하 중 넉백에서 수직 속도가 사라진다.
3. **전환을 Motor가 독점한다.** 현재 `isKinematic` 토글이 4곳(`Knockback` / `Restrained`의 `wasKinematic`
   저장·복원 / `Player.ConfigureMovementPhysicsAuthority` / `SoulController`의 `useGravity`)이라
   **그랩 중 넉백이 들어오면 복원 스택이 깨진다.** 위치를 독점하는 것과 같은 이유로 모드도 독점한다.

**참고 선례:** 이 프로젝트는 이미 "진짜 물리가 필요한 건 별도 바디로 뺀다"를 하고 있다 —
`Corpse`가 Player 하위의 **별도 GameObject + 자체 Rigidbody**(dynamic, 중력 on)다.
Dynamic 모드가 필요해지면 이 패턴이 먼저 검토 대상이다.

> ⚠️ Dynamic 구간은 **재시뮬레이션이 불가능**하다. 4단계에서 그 구간은 예측/재조정에서 빼고
> 서버 권위 + 복제로 처리해야 한다 — **상태에 따라 권한 모델이 바뀐다.**

---

## 5. Multiplayer / networking authority assumptions

- 이동 권한 = **오너(클라)**. 보스·데미지·상태이상·장판 = 서버. ([AGENTS.md](AGENTS.md) §4 불변)
- 비권한 피어는 루트 NetworkTransform으로 복제된다. 현재 `ConfigureMovementPhysicsAuthority`가
  비권한 피어를 `isKinematic=true`로 만들어 물리 경쟁을 막는데, **3단계에서 전 피어가
  kinematic이 되므로 이 분기는 의미를 잃는다** → `PlayerMotor.enabled` 토글로 대체.
- 캐리 이중 적용 방지 규약([Player.cs:168](Assets/1.Scripts/Player/Player.cs:168))은 그대로 유지.
- 넉백은 현재 "오너가 종료를 서버에 보고 + 서버 타임아웃 안전망(`ServerKnockbackReportGraceTime`)"
  구조인데, 이건 마찰·솔버 반복에 의존하는 **비결정론에 대한 정당한 대응**이었다.
  3단계에서 결정론이 확보되면 이 안전망을 단순화할 수 있다 — 단 **이번 범위에서는 건드리지 않는다**(회귀 위험).
- 4단계에서 서버가 Motor를 재시뮬레이션하는 권한 모델로 확장. 오너 권한 자체는 유지.

---

## 6. Risks and open questions

### 리스크

| | 내용 | 완화 |
|---|---|---|
| R-1 | **stepOffset 신규 구현** — 선례 없음, 감각 회귀 위험 | EditMode 테스트로 고정. 실패 시 3단계에서 분리해 별도 PR |
| R-2 | **몬스터 넉백과 감각이 갈린다** — 플레이어만 수동 감쇠로 가면 몬스터는 여전히 마찰 기반이다 | **공유만 하면 된다**(합의 불필요 — 아래 참조). 경석에게 통보 |
| R-3 | **`Player.prefab` / `Paladin.prefab` 수정** — 현재 워킹트리에 미커밋 변경이 있다(`M` 2건). 프리팹은 GitHub 관리라 머지 충돌 시 GUID 파손 위험 | 착수 전 커밋/정리. 작은 PR로 분할 |
| R-4 | **민경 VFX / 경석 보스가 플레이어 위치를 직접 밀거나 읽는 경로**가 있으면 Motor 독점 규칙에 예외가 필요 | 2단계 착수 전 조사 항목으로 명시 |
| R-5 | 감각 회귀를 자동 검증할 수단이 없다 | fps 고정 측정 + MPPM 2인 수동 검증 체크리스트 |
| R-6 | **kinematic 전환이 유닛 겹침 기본값을 뒤집는다** — 솔버가 밀어내던 것이 사라져 "플레이어끼리 통과"가 기본이 된다. 조용히 회귀하기 쉽다 | 3단계 완료조건에 명시 + MPPM 2인 겹침 확인. Open Q-3 선결 |
| R-7 | 🔴 **4단계의 "결정론" 전제가 절반만 성립한다** — Motor의 충돌 해석이 `Physics.CapsuleCast`(씬 상태 의존)를 탄다. 같은 머신·같은 씬에서 되감아 재시뮬레이션하는 것은 되지만, **서버와 클라가 비트 단위로 같은 결과를 내는 것은 보장되지 않는다**(PhysX 쿼리는 크로스 머신 결정론을 약속하지 않고, 씬에 움직이는 콜라이더가 있으면 애초에 상태가 다르다) | 4단계 착수 전 **재조정 모델을 확정**할 것 — §6 "4단계 선결 결정" 참조 |

### 조사 결과 — 플레이어/몬스터 넉백은 이미 분리돼 있다 (R-2 하향)

`LinearKnockback`은 `IKnockbackable`을 구현한 **수신측** 컴포넌트이고, 부착 프리팹은
**몬스터 8개뿐**(`ChompBot`·`GauntletBot`·`HumanoidBot`·`MortarBot`·`PeekABot`·`SpinnerBot`·
`TeslaBot`·`WallBot`)이다. 플레이어에는 붙어 있지 않다(`NavMeshAgent` 분기가 몬스터 전용 증거).

```
Unit.Knockback(dir, strength)              공통 진입점 (서버 가드 + 슈퍼아머 선차단)
  └─ Unit.OnKnockback (virtual)
       ├─ 기본: IKnockbackable 위임   →  몬스터 = LinearKnockback
       └─ Player.OnKnockback override →  PlayerStateController.BeginKnockback → PlayerKnockbackState
```

**두 구현은 이미 완전히 분리돼 있다.** 닮은 건 패턴(`isKinematic` 토글 + `AddForce` + `stopSpeed`)뿐이고
튜닝 값도 각자의 `[SerializeField]`다. 공통 진입점 `Unit.Knockback`과 슈퍼아머 규칙은 **건드리지 않는다**
→ 인터페이스 변경 없음 → **경석 합의는 착수 블로커가 아니다.** 공유만 한다.

### 확정됨 (2026-09-11, 은희)

- **stepOffset** — 맵에 계단 에셋이 아직 없다. **0.3m 후보값으로 진행**, 에셋 입고 후 실측 조정.
- **틱레이트** — 현행 기본값(Fixed Timestep 0.02 = 50Hz) 유지.
- **넉백 벽 충돌** — 그 자리 정지(슬라이드 없음) + **경직 시간은 유지**.
- **Dynamic 모드** — 인터페이스만 두고 사용처 0개로 시작(D-6).
- **플레이어끼리 통과** — **기본값 = 통과**(`blockOtherPlayers = false`). 막고 싶으면 GameRule 토글 하나.
- **Soul ↔ Player 통과** — 규칙으로 확정. GameRule에는 노출하지 않는다(레이어 분리로 성립).
- **넉백 감쇠 데이터** — `PlayerGameRuleData`에 추가. **값은 기본값 사용**(별도 튜닝 없이 착수).
- **몬스터 넉백** — 이번 범위에서 손대지 않는다. 경석에게 공유만.

### 🔴 4단계 선결 결정 — 착수 전 확정 필요

R-7 때문이다. "결정론적 이동"이라는 목표를 **두 가지로 나눠야** 한다:

| | 필요한가 | 성립하는가 |
|---|---|---|
| **로컬 재시뮬레이션** — 오너가 자기 과거 틱으로 되감아 다시 계산 | 예(예측 보정의 핵심) | **성립한다.** 같은 머신·같은 씬이면 `CapsuleCast` 결과가 재현된다 |
| **피어 간 비트 동일** — 서버와 클라가 같은 입력에 같은 위치 | **아니오** | 성립하지 않는다. PhysX 쿼리 크로스 머신 보장 없음 + 움직이는 콜라이더가 있으면 씬 상태 자체가 다름 |

즉 **lockstep이 아니라 "예측 + 허용오차 기반 재조정"** 이 맞는 모델이다:
오너가 로컬 예측 → 서버가 권위 시뮬레이션 → 위치 차이가 임계값을 넘을 때만 보정 전송 →
오너가 보정 지점부터 버퍼된 입력으로 재시뮬레이션. 비트 동일은 **로컬 되감기에만** 요구한다.

### ✅ 4단계 결정 (2026-09-12, 은희)

**범위 = 넓게.** 클라는 **raw 입력**(방향 벡터·버튼)을 보내고, **서버가 플레이어 로직 전체를 돌려**
권위 위치를 구한다. 계산된 속도 벡터를 보내는 좁은 안은 기각 — 그 안은 충돌 해석만 검증되고
속도핵을 못 잡는다(클라가 `velocity=100`을 보내면 서버가 그대로 믿는다).

**회전 = 물리 틱 확정** (`256b0a0` → 토글 제거). 회전은 시각 효과가 아니라 **시뮬레이션 상태**다 —
`Move()`의 속도 계산이 `armature.forward`에 의존하므로(`dot >= alignThreshold`면 즉시 최고속),
회전이 렌더 레이트로 돌면 같은 입력이 프레임레이트에 따라 다른 속도를 낸다. A/B 실측 결과 감각
영향 없음. **따라서 추출 대상은 `PlayerMotor`만이 아니라 `회전 + 걷기 속도 산출 + Motor 해석` 전체다.**

나머지는 **아래 값으로 시작하고 MPPM에서 조인다**(튜닝값이지 구조가 아니다):
1. **보정 임계값 0.25m** — 작으면 상시 보정으로 끊기고, 크면 벽 통과가 남는다.
2. **보정 적용 = 3~5틱에 걸친 수렴.** 순간 이동은 탑다운 협동에서 눈에 거슬린다.
3. **입력 버퍼 0.5초** (참고: 대시 스냅샷은 `snapshotCapacity 32` = 0.64s).
4. **`PlayerDashValidationManager`는 병존.** 흡수가 최종 목표지만 대시는 무적·충전 장부까지
   얽혀 있다. 이동 재조정을 먼저 세우고 대시 흡수는 별건으로 뺀다.

### 🔴 권한 모델 = 완전한 서버 권위 (2026-09-15, 은희 결정)

내가 처음 제안한 "서버 검증 + 오너 권한 복제 유지"를 **기각하고 서버 권위로 간다.**
오너 권한을 유지하면 수정된 클라가 보정을 무시할 수 있어 검증이 실효를 잃는다.
지금이 뒤집기 가장 싼 시점이기도 하다 — 뒤로 갈수록 호출부가 늘어난다.

**수반 비용 둘. 둘 다 b2 범위다.**

**① `NetworkTransform`을 플레이어 루트에서 걷어내야 한다.**

> 🔴 **2026-09-15 정정 — 앞선 기술이 틀렸다.** "루트는 `AuthorityMode: 1`(Owner)" 는 오답이다.
> 🔴 **2026-09-16 재정정 — 위 "AuthorityMode: 0 = Server" 는 오답이었다. 두 번째 같은 실수다.**
>
> `Player.prefab:145` 는 **루트가 아니라 `Corpse` 자식**의 NetworkTransform이다
> (`m_GameObject: 800000000000000001`). 루트는 `8559504096609571310` 이고 **Owner(1)** 였다.
> 실제로 스폰되는 `Paladin.prefab` 도 마찬가지로 **루트 NT = Owner(1)** 였다.
>
> | prefab | GameObject | 동기화 | 2026-09-16 이전 |
> |---|---|---|---|
> | Paladin | 루트 `6077492126708577103` | 위치 XYZ | **Owner** |
> | Paladin | `Paladin_Armature` | 회전 XYZ | **Owner** |
> | Paladin | `Corpse` | — | Server |
> | Player | 루트 `8559504096609571310` | 위치 XYZ | **Owner** |
> | Player | `Corpse` | — | Server |
>
> **이 대화 첫머리에 은희가 지적한 것과 똑같은 실수다** — 컴포넌트 블록을 `m_GameObject` fileID 로
> 짚지 않고 줄 위치로 읽었다. Rigidbody 때 한 번, NetworkTransform 때 또 한 번.
> **프리팹 YAML 은 반드시 `m_GameObject` fileID 로 대상을 확정한 뒤 읽는다.**
>
> **이 오답 위에 세운 것들도 같이 무효다:** "서버 권위 NT 가 오너 클라의 transform 을 매 프레임
> 덮어쓴다 → 클라 이동 불가의 1순위 용의자" 라는 진단, 그에 근거한 코덱스 핸드오프,
> `Player.cs` 주석을 owner-authority → server-authority 로 "정정"한 것.
> `Player.cs` 의 원래 주석("owner-authority NetworkTransform이 복제한다")이 **맞았다.**
>
> **진짜 원인.** 루트 NT 가 Owner 권위인데 4b2-α 가 **서버**에게 위치를 시뮬레이션·커밋시켰다.
> 한 transform 에 주인이 둘이 되어 매 프레임 싸웠고, 호스트에서 그 결과가 원점으로 수렴했다.
> 증상이 정확히 갈린 이유도 이것이다 — **Armature 회전은 Owner 권위 NT 라 정상 복제되고(회전은 된다),
> 루트 위치만 두 주인이 싸웠다(이동은 안 된다).**
>
> **조치(2026-09-16).** 결정된 서버 권위 모델에 데이터를 맞춘다 — Paladin 루트·Armature,
> Player 루트의 `AuthorityMode` 를 0(Server)으로 변경. Corpse 는 원래 0이라 그대로.

> `Player.prefab:145` 은 `AuthorityMode: 0` = **Server** 이고, `1ccf0d3`(2026-07-27) 이후 계속 Server다.
> `Player.cs:443`의 "owner-authority NetworkTransform이 복제한다" 주석도 같은 오류다.
> **즉 아래의 모순은 가정이 아니라 지금 돌고 있는 상태다.**

NGO 기본 `NetworkTransform`은 서버 권위 모드에서 **오너 클라의 transform도 매 프레임 덮어쓴다.**
오너가 예측으로 움직여봐야 지워진다. (~~오너만 제외하는 옵션이 없다~~ — **틀렸다. `nt.enabled` 로 인스턴스마다 끌 수 있다.**)

**현재 실제로 벌어지는 일 (코드 근거):**
- `NetworkTransform.cs:3743` — `CanCommitToTransform = IsServerAuthoritative() ? IsServer : IsOwner`
  → 서버 권위이므로 **클라는 전부 비권위**.
- `NetworkTransform.cs:4461 OnUpdate()` — 비권위 인스턴스는 조기 반환 없이 매 프레임
  `ApplyAuthoritativeState()` 로 transform 을 **무조건 덮어쓴다**(`m_UseRigidbodyForMotion` 은 거짓).
- `Player.cs:450` — `motor.enabled = IsMovementAuthority(= IsOwner)`
  → **서버에서 원격 플레이어의 Motor 는 꺼져 있다. 서버는 그 플레이어를 영원히 안 움직인다.**

⇒ 오너 클라는 로컬에서 움직이지만, 같은 프레임 뒤에 NT 가 **서버의 정지한 위치로 되돌린다.**
**"클라가 스폰 직후 이동을 못 한다" 증상의 1순위 용의자다.** 4b2 가 이 모순을 제거한다.
착수 전 10초 확인법: 프리팹 `AuthorityMode` 를 1(Owner)로 임시 변경 → 클라가 움직이면 확정.
> 🔴 **아래 네 줄은 폐기됐다 (2026-09-15, 은희 지적).** NGO 는 NT 를 컴포넌트 `enabled` 로 게이팅하므로
> **오너에서 `nt.enabled = false` 하면 끝난다.** 제거도 자체 복제도 필요 없다 — "4b2 상세 설계" 절 참조.

→ ~~루트에서 `NetworkTransform`을 제거하고 복제를 직접 구현한다:~~
- 서버 → 전 피어: 권위 상태(위치·회전·틱)
- 오너: 그 값을 **재조정 기준점**으로 사용(덮어쓰기 ✕, 되감기+재생 ○)
- 원격 피어: 받은 값을 보간해 표시 — **지금 `NetworkTransform`이 해주던 보간을 직접 짜야 한다**

**② `IsMovementAuthority`의 의미가 갈라진다** (사용처 17곳 / 8파일).
지금은 `!IsNetworkActive || IsOwner` 하나로 "이동을 계산하는 주체"를 뜻한다. 서버 권위에서는
**시뮬레이션 권위(서버) / 예측 주체(오너)** 로 나뉜다. 대시 시작 가부·넉백 적용·구속 물리 위임이
각각 어느 쪽인지 **17곳을 하나씩 판단**해야 한다. 기계적 치환이 아니다.

### 4단계를 a/b로 쪼갠다

| | 내용 | 성격 | 상태 |
|---|---|---|---|
| **4a** | 시뮬레이션을 순수 함수로 추출 + 재생 경로에서 부작용 분리 + 결정론 EditMode 테스트 | **리팩터** — 동작 변화 없음 | ✅ 리뷰 통과 (`b4acb58`+`980bf19`) |
| **4b1** | 틱 번호 · raw 입력 분리 · 입력/상태 링버퍼 · 서버 병행 시뮬레이션 + `[Recon]` 관측 로그 | **동작 변화 0** | ✅ 은희 검증 통과 (2026-09-15, `5d0d576`) |
| **4b2** | `IsMovementAuthority` 분해 + 서버 시뮬레이션 커밋 + 프리팹 NT 를 서버 권위로 | **네트워크 권한 변경** | ✅ 완료 (2026-09-16, 은희 Play 검증) |
| **4b3** | 틱 교정 + 예측 · 되감기 · 재생 · 보정 + 텔레포트 서버 권위화 | **기능 추가** | ✅ 서버 권위 브랜치에 보존. 오너 권위 브랜치에서는 꺼져 있다 |

4a는 "감각 동일 + 결정론 테스트 통과"로 검증이 끝나 안전했다.
**b1은 보정을 넣지 않는다** — 서버가 병행 계산만 해서 divergence 분포를 잰다. PLAN에 적어둔
임계값 0.25m는 근거 없는 값이라, b1 실측으로 b3의 값을 정한다.
**b2가 위 "수반 비용" 둘을 처리하는 구간**이고 이 계획에서 가장 위험하다 — 복제를 직접 짜는 데다
원격 피어 보간까지 우리 몫이 된다.

### 🟡 미해결 (후순위) — 이동 표면 위에서 상하 떨림

**증상.** 이동 플랫폼·컨베이어 위에 서 있으면 플레이어가 위아래로 미세하게 떤다.
2026-09-15 은희 1단계 검증에서 발견. **"게임을 크게 해치지 않는다" 판단으로 후순위 확정.**
1단계의 나머지 항목(걷기·fps 편차·경사·계단·벽·대시·낙하·루트모션·스킬·Soul)은 전부 통과.

**미조사.** 원인 후보 둘, 어느 쪽인지 안 봤다.
1. **레이트 불일치** — `MovingPlatform.cs:88 Update()` 는 플랫폼 **시각 위치를 렌더 레이트로**
   갱신하고(`76824f2`에서 의도적으로 그렇게 고쳤다), 플레이어는 50Hz 물리 틱에서만 위치가 바뀐다.
   루트 `Rigidbody` 는 `Interpolate 0` 이라 보간이 없다 → 둘의 갱신 주기가 어긋나 보인다.
   다만 이 가설은 **수평 이동인 컨베이어에서 상하 떨림이 나는 것을 설명하지 못한다.**
2. **접지 스냅 ↔ 중력 진동** — 캐리 변위(`AddDisplacement`, 투영 안 함)가 들어간 뒤 스윕·접지 스냅이
   매 틱 다시 풀면서 스냅 거리가 오르내릴 수 있다. `P9-PlayerWallClimb` 진단이 평지에서 잡는
   틱당 +mm 이상 상승과 같은 계열일 가능성.

**착수 시 먼저 할 것.** 컨베이어(수평 전용)에서도 나는지 재확인 → 나면 2번, 플랫폼에서만 나면 1번.
b2/b3 와 독립이므로 그 뒤에 본다.

### 4b2 상세 설계 (2026-09-15)

b2 는 원래 이 계획에서 가장 위험한 구간이었다. **더는 아니다.**
핵심 관찰: **`NetworkTransform` 은 이미 서버 권위다.** 그러니 "서버가 실제로 시뮬레이션하게"
만드는 것 + **오너에서만 NT 를 끄는 것**, 둘이면 끝난다. NT 제거도 자체 복제도 필요 없다.

| | 내용 | 얻는 것 |
|---|---|---|
| **b2-α** (= 4b2 전부) | 권한 개념 분해 + **서버가 전 플레이어를 시뮬레이션해 커밋** + **오너에서만 `nt.enabled = false`** | 서버 권위 성립. **클라 이동 불가 버그 소멸**. 오너 예측이 NT 에 안 지워짐 |
| ~~b2-β~~ | ~~NT 제거 + 자체 복제 + 원격 보간~~ | **폐기** — 아래 재정정 참조 |

α 가 깨지면 되돌릴 대상이 작고, α 가 서면 게임이 **동작은 하는** 상태로 남는다.

#### b2-α

**1. `IsMovementAuthority` (19곳 / 8파일) 를 넷으로 분해한다. 기계 치환 금지 — 한 곳씩 판단.**

| 이름 | 정의 | 뜻 |
|---|---|---|
| `IsInputSource` | `!IsNetworkActive \|\| IsOwner` | 입력 장치를 읽는 주체 |
| `IsSimulating` | `!IsNetworkActive \|\| IsOwner \|\| IsServer` | Motor 를 로컬에서 도는 주체 |
| `IsMotionAuthority` | `!IsNetworkActive \|\| IsServer` | **그 결과가 진실인** 주체 |
| `IsRemoteProxy` | `IsNetworkActive && !IsOwner && !IsServer` | 복제값을 보여주기만 하는 주체 |

판단 기준: 입력/UI 게이트 → `IsInputSource`. Motor 구동·플랫폼 캐리 → `IsSimulating`.
넉백 적용·대시 개시 가부·구속 물리·사망 처리 → `IsMotionAuthority`.
`Player.cs:443` 의 "owner-authority NetworkTransform" 주석은 **오류이므로 같이 고친다.**

**2. 서버가 원격 플레이어도 시뮬레이션해서 커밋한다.**
b1 의 `PlayerMotor.TrySimulateServerObservation` 은 결과를 **비교만** 하고 버린다.
이걸 커밋 경로로 승격한다 — 서버에서 결과 상태를 transform 에 적용하고, NT 가 그걸 복제한다.
`[Recon]` divergence 로깅은 **그대로 유지**한다(β·b3 임계값 산정에 계속 필요).

**3. 입력이 안 오는 틱을 정의한다.** RPC 는 손실·지연된다. 서버 틱마다 큐가 비면:
**마지막 입력을 최대 10틱(0.2s) 반복**하고, 그 뒤에는 입력 0 으로 간주해 감속시킨다.
상수는 `PlayerGameRuleData` 가 아니라 코드 상수로 두고 이름을 붙인다(튜닝 대상 아님).

**4. 오너 인스턴스에서만 루트 `NetworkTransform` 을 끈다.** 제거가 아니라 `enabled` 제어다.

    nt.enabled = !(IsOwner && !IsServer);

`ConfigureMovementAuthority` 안에서 한다 — 이미 `OnNetworkSpawn` / `OnGainedOwnership` /
`OnLostOwnership` 에서 불리므로 소유권이 바뀌어도 따라온다. 세 경우의 근거는 아래 폐기 절의 표 참조.

**5. 범위 밖.** 자체 복제·원격 보간·되감기/재생/보정은 전부 b3(또는 불필요). NT 를 **제거하지 않는다.**

#### ✅ 4b2-α 최종 통과 (2026-09-16, 은희 MPPM 호스트+클라1)

정상 동작 확인. 남은 것은 **오너 입력 지연 체감** 하나이며 이는 α 의 설계된 대가다 — b3 가 회수한다.

α 를 막고 있던 실제 원인은 **위치의 주인이 둘이었던 것**이었다(루트 NT = Owner 권위인데
서버가 시뮬레이션·커밋). 프리팹 `AuthorityMode` 를 서버 권위로 맞춰 해결했다(`4e3b292f`).
그 과정에서 내가 낸 오진 셋(씬 프리팹 오버라이드 미확인 / NT 덮어쓰기를 비용으로만 본 것 /
NT 블록을 fileID 로 안 짚고 줄 위치로 읽은 것)은 각 절에 기록해 뒀다.

**지연 예산 (RTT 8~29ms 실측 기준).** 체감 지연은 RTT 하나가 아니라 넷의 합이다:

| 요소 | 크기 | 성격 |
|---|---|---|
| RTT/2 (입력 상행) | 4~15ms | 물리적 하한 |
| 서버 지터 버퍼 목표 2틱 | 40ms | 상수, 조정 가능 |
| 서버 소비 지연 `queueWaitTicks` | **2틱 = 40ms** (실측) | 지터 버퍼 목표치와 일치 |
| NT 보간 `PositionMaxInterpolationTime` | ~100ms | **오너 자신에게도 걸린다** |

> 🔴 **2026-09-16 정정 — 옛 `lagTicks` 로 잰 "3~4틱(60~80ms)" 은 믿을 수 없는 값이었다.**
> b3-0 이 틱을 피어별 단조 카운터로 바꾸면서 오너 틱과 서버 틱은 **각자 자기 피어의 스폰 시점에
> 0에서 출발하는 별개 카운터**가 됐다. 둘을 빼던 `lagTicks` 는 스폰 시각 차이만큼 통째로 어긋난다
> (1초 차이면 50틱). 서버 틱끼리 빼는 `queueWaitTicks` 로 교체했다 — 입력이 지터 버퍼에서 실제로
> 기다린 틱 수다. **b3 검증 때 이 값을 다시 재서 표를 채운다.**

⇒ 로컬 MPPM 인데도 100ms 를 훌쩍 넘는다. **가장 큰 조각은 RTT 가 아니라 NT 보간이다** —
서버 권위에서는 오너도 비권위라 자기 캐릭터를 보간 버퍼 너머로 본다.

**b3 가 이걸 없애는 방식.** 오너는 자기 입력을 즉시 로컬 예측으로 반영하고(지연 0),
서버 확정 상태와 어긋날 때만 되감기·재생으로 보정한다. 그때 오너 인스턴스의 루트 NT 를
끈다 — `nt.enabled` 와 보정 채널은 반드시 같은 커밋이다(아래 실패 기록 참조).

#### 🔴 nt.enabled=false 는 b3 와 한 세트다 (2026-09-15 실패 기록)

α 에 `nt.enabled = !(IsOwner && !IsServer)` 를 넣었다가 **되돌렸다**(`20acd04`).

끄면 이렇게 된다:
- 클라는 보정 없는 순수 로컬 예측으로 굴러가고
- 서버는 받은 입력으로 자기 시뮬레이션을 굴리며
- **둘을 맞춰주는 장치가 아무것도 없다.**

실제로 호스트 화면에서는 그 플레이어가 상자에 박혀 있는데 정작 그 클라는 전혀 다른 곳을
걸어다니는 상태가 됐다(은희 실측).

**NT 의 덮어쓰기는 "입력 지연이라는 비용"이기만 한 게 아니다. b3 이전까지 클라와 서버의 위치를
일치시켜 주는 유일한 장치다.** 비용만 보고 없애면 안 된다.
b3 착수 시 `nt.enabled` 분기 복원과 서버→오너 보정 채널(되감기+재생)은 **반드시 같은 커밋**에 넣는다.

#### 4b2-α 실측 (2026-09-15, 은희 MPPM 호스트+클라1)

앵커 수정(`85bdc78`) 전후:

| | 수정 전 | 수정 후 |
|---|---|---|
| `divergence avg` / `max` | **50.593m / 50.593m** (소수점까지 고정) | **0.021~0.024m / 0.040m** |
| `samples` / `discarded` | 22~51 / 1~29 | 41~52 / 0~10 |
| `lagTicks` | 3~5 | 3~4 |
| `RTT` | 6~31ms | 8~29ms |
| `motorTicks` | 초당 ~50 (정상) | 초당 ~50 |

**읽는 법 — 고정 발산은 물리 발산이 아니다.** 발산이 창마다 소수점까지 같으면 두 시뮬레이션이
같은 입력에 같은 운동을 내되 원점만 어긋난 것이다. 그 증상은 앵커 누락을 가리킨다.

**4cm 는 한 틱 이동량(5m/s × 0.02s = 10cm)보다 작다.** 서버와 클라의 시뮬레이션이 같은
결과를 낸다는 뜻이고, 4단계 전체의 전제(결정론)가 실측으로 확인된 것이다.

🔴 **이 값을 b3 임계값으로 바로 쓰면 안 된다.** 지금은 NT 가 오너에서 켜져 있어 클라가 매 프레임
서버로 스냅된다 — 이건 **보정받는 클라의 값, 즉 바닥값**이다. b3 에서 NT 를 끄면 클라가
`lagTicks`(3~4틱) 동안 자유 예측하므로 발산이 그만큼 누적된다. 대략 틱당 2cm × 4틱 ≈ 10cm 가
출발점이고, 정확한 값은 **b3 에서 NT 를 끈 뒤 다시 재서** 확정한다.

**남은 관찰 — `sequenceBreakSamples` 가 초당 0~10.** `droppedInputsTotal` 은 안 늘어나므로
큐 폐기가 아니라 **오너의 틱 번호가 연속이 아닌 경우**다. `CurrentSharedSimulationTick()` 이
`NetworkClock.MainGameElapsed` 에서 계산되니 클럭과 FixedUpdate 횟수가 어긋나면 같은 틱이 두 번
나오거나 한 틱이 건너뛰어진다. α 동작에는 지장 없지만 **b3 의 되감기·재생은 틱 번호가 입력열의
인덱스여야 성립**하므로 b3 착수 시 먼저 해결한다.

#### ~~b2-β~~ 폐기 (2026-09-15)

> 🔴 **2026-09-15 재정정 — β(NT 제거 + 자체 복제)는 폐기한다. 은희 지적이 옳았다.**
>
> "NGO 기본 `NetworkTransform` 은 오너만 제외하는 옵션이 없다" 는 **오답이다.**
> NGO 는 NT 를 **컴포넌트 단위 `enabled` 로 게이팅한다.**
> - `NetworkManager.cs:425-432` (PreLateUpdate) — `// only update if enabled`
>   → `if (networkTransformEntry.enabled) networkTransformEntry.OnUpdate();`
> - `NetworkManager.cs:355-372` (FixedUpdate 경로) 도 같은 검사
> - `Player.prefab` 에 `NetworkRigidbody` 가 없어 `m_UseRigidbodyForMotion = false` → OnUpdate 경로만 탄다
>
> ⇒ **오너 인스턴스에서 `nt.enabled = false` 한 줄이면 끝난다.** 복제·보간을 직접 짤 이유가 없다.
>
> | 인스턴스 | `nt.enabled` | 이유 |
> |---|---|---|
> | 호스트 자기 캐릭터 (`IsServer && IsOwner`) | `true` | 권위. 상태를 송신한다 |
> | 클라 자기 캐릭터 (`IsOwner && !IsServer`) | **`false`** | 로컬 예측이 안 지워진다 |
> | 남의 캐릭터 (`!IsOwner`) | `true` | 서버 상태를 NT 가 보간해 표시 |
>
> **남는 진짜 제약 하나.** NT 가 실어 나르는 상태에는 **우리 시뮬레이션 틱이 안 붙는다.**
> 되감기·재생은 "틱 N 의 입력을 먹은 뒤 서버 위치가 P 였다" 가 있어야 성립하므로,
> b3 의 **서버→오너 보정 채널은 어차피 자체 RPC 로 만든다.** 그건 b3 범위이고,
> 원격 보간을 직접 짜는 것과는 비용이 다르다. 원격 피어는 틱이 필요 없다 — 부드럽기만 하면 된다.
>
> ⇒ **`nt.enabled` 제어를 α 로 흡수하고 β 를 없앤다. 4b2 = α 하나다.**

### 4b3 상세 설계 (2026-09-16, 은희 승인)

**목표.** 오너의 입력 지연을 0 으로 만든다. 지금 체감 지연은 RTT 가 아니라 §4b2-α 의 지연 예산
네 요소의 합이고(가장 큰 조각은 NT 보간 ~100ms), b3 는 그 전부를 없앤다.

**방식.** 오너는 자기 입력을 즉시 로컬 시뮬레이션에 반영한다(예측). 서버는 권위 결과를 오너에게만
돌려주고, 오너는 그 틱의 자기 예측과 비교해 **어긋날 때만** 되감기(rewind) + 재생(replay) 한다.

셋으로 나눈다. **각각 따로 커밋한다.**

| | 내용 | 성격 |
|---|---|---|
| **b3-0** | 틱 번호를 입력열 인덱스로 교정 | 선결. 동작 변화 0 |
| **b3-1** | 서버→오너 보정 채널 + 되감기·재생 + 오너 `nt.enabled=false` | **한 커밋이어야 한다** |
| **b3-2** | 텔레포트(낙사 복귀·보스 그랩)를 서버 권위로 | 예측과 공존시키려면 필수 |

#### b3-0 — 틱 번호 (선결)

지금 `Player.CurrentSharedSimulationTick()` 은 `NetworkClock.MainGameElapsed / fixedDeltaTime`
로 계산한다. 클럭과 FixedUpdate 횟수가 어긋나면 **같은 틱이 두 번 나오거나 한 틱이 건너뛰어진다** —
`[Recon] sequenceBreakSamples` 가 초당 0~10 으로 찍히는 게 그 증거다.

되감기·재생은 **틱이 입력열의 인덱스**여야 성립한다. N 번 입력을 먹은 뒤의 상태를 N 으로 저장하고,
보정 시 N+1..현재를 순서대로 다시 먹여야 하기 때문이다. 중복/누락이 있으면 재생 결과가 어긋난다.

→ **각 피어에서 `FixedUpdate` 마다 정확히 +1 되는 단조 카운터**로 바꾼다. 서버는 이 값을
절대 시각이 아니라 **오너의 입력 시퀀스 번호**로만 쓴다(서버 자기 틱과 일치할 필요가 없다).

**완료 조건.** 1분 플레이에서 `sequenceBreakSamples` 가 실제 패킷 손실분을 빼면 0 에 수렴.

#### b3-1 — 보정 채널 + 되감기·재생 (한 커밋)

**서버 → 오너(만).** 매 서버 시뮬레이션 틱마다 unreliable RPC 로 보낸다:
- `long inputTick` — 이번에 소비한 입력의 시퀀스 번호
- 그 입력을 먹은 뒤의 권위 상태 중 **재생을 이어가는 데 필요한 값만**:
  `Position`, `VerticalVelocity`, `CurrentSpeed`, `ArmatureRotation`, `KnockbackVelocity`,
  `DashDirection`/`DashSpeed`/`DashRemainingTime`, `GravityEnabled`
  (접지·지면 노멀은 장면에서 다시 구하므로 보내지 않는다)
- `bool forceSnap` — 텔레포트 등 예측 대상이 아닌 변경(b3-2)

**오너.** b1 이 만든 `ownerRawInputHistory` / `ownerSimulationStateHistory` 를 그대로 쓴다.
`inputTick = N` 수신 시:
1. `ownerSimulationStateHistory[N]` 이 없으면(너무 오래됨) 무조건 스냅 + 기록 초기화
2. `Distance(서버 Position, 내 예측 Position)` ≤ 임계값이면 **아무것도 하지 않는다.** N 이하 기록 폐기
3. 초과하거나 `forceSnap` 이면 **되감기**: 상태를 서버 값으로 덮고
   **재생**: `ownerRawInputHistory` 의 N+1..현재를 순서대로 `PlayerMovementSimulation.Simulate`
   에 통과시킨 뒤 최종 위치를 Motor 에 적용한다. 재생은 4a 에서 순수 함수로 뽑아둔 그 경로다.

**임계값.** 실측 기준으로 **0.10m 에서 시작**한다 — 틱당 서버·클라 일치도가 2~4cm 였고
(§4b2-α 실측) 자유 예측 구간이 `lagTicks` 3~4 틱이므로 그 누적이 대략 이 수준이다.
PLAN 초안의 0.25m 는 근거 없는 값이었다. **이름 붙인 코드 상수**로 두고 보정 발생 빈도·크기를
`[Recon]` 에 찍어 튜닝한다.

**오너 NT 끄기.** `ConfigureMovementAuthority` 의 `networkTransform.enabled = true` 를
`!(IsOwner && !IsServer)` 로 되돌린다. 🔴 **반드시 이 커밋 안에서** — 아래 실패 기록 참조.

**주의 둘.**
- 재생은 `Physics.CapsuleCast` 를 타므로 비싸다. **임계값을 넘었을 때만** 재생한다.
- 보정 스냅은 눈에 띈다. 1차 구현은 스냅 + 로깅으로 두고, 빈도를 본 뒤에 시각 보간을 검토한다.
  (시뮬레이션 상태는 항상 즉시 스냅. 부드럽게 만드는 것은 **표시**만이다.)

#### b3-2 — 텔레포트를 서버 권위로

`PlayerFallRecovery` 의 복귀 순간이동은 `[Rpc(SendTo.Owner)]` 라 **오너만 자기를 옮기고
서버는 자기 사본을 옮기지 않는다**(`PlayerFallRecovery.cs:82,88,134,140`). 루트 NT 가
Owner 권위이던 시절 코드이고, 서버 권위에서는 성립하지 않는다. 예측이 붙으면 더 나빠진다 —
오너가 순간이동해도 다음 보정에서 서버 위치로 되돌아온다.

→ **서버가 자기 사본을 먼저 옮기고**, 그 결과를 b3-1 의 보정 채널에 `forceSnap` 으로 실어 보낸다.
오너는 예측/재생을 건너뛰고 그 값을 그대로 받는다. 낙사 복귀·생존 복귀 둘 다 같은 처리.
보스 그랩(`SetPoseTarget`)은 이미 Motor 를 거치므로 그대로 둔다.


#### b3-3 — 예측이 다루지 못한 이동 채널 둘 (2026-09-16)

은희 Play 검증에서 **입력 지연은 해결**됐으나(b3 목적 달성) **루트모션 이동이 스냅**되는 증상이 남았다.
원인 둘 다 b3 사양의 누락이었다.

**D1 — 서버가 원격 플레이어의 루트모션을 아예 안 만들었다** (`71eda2f4`).
`DefaultAttackController.HandleAnimatorMove` 가 `if (IsNetworkActive && !IsOwner) return;` 로
오너 전용이었다(owner 권위 시절 코드, 낙사 복귀와 같은 계열). 서버 권위에서는 오너만 전진하고
서버는 제자리 → 발산이 임계값을 넘어 보정 → **전진이 지워진다.**
→ 게이트를 `player.IsSimulating` 으로 바꿔 서버도 같은 전진 의도를 만든다. scripted 전진도 동일.

**D2 — 재생이 raw 외의 의도 채널을 전부 버렸다** (`94a4e8c6`).
`TryApplyAuthoritativeStateAndReplay` 가 재생 입력을 `CaptureSimulationInput(rawInput)` 로
**다시 만들어** `AddedVelocity`/`GroundedDisplacement`/`Displacement`/포즈가 0이 됐다.
⇒ 보정 한 번마다 그 구간의 루트모션·스킬 전진·플랫폼 캐리·자동접근이 통째로 사라진다.
→ Motor 의 `SimulationCompleted` 가 그 틱에 **실제로 사용한 `PlayerSimulationInput` 전체**를 넘기고,
오너가 `ownerReplayInputHistory` 에 저장해 재생이 그대로 먹인다.

🔴 **서버로 가는 RPC 는 raw 두 필드 그대로다.** 클라가 보고한 변위를 서버가 신뢰하면 서버 권위가
무너진다. 이 기록은 **오너의 로컬 재생 전용**이고 진실의 기준은 여전히 서버 보정값이다.

**교훈 — 예측은 "그 틱의 모든 의도"를 재현해야 한다.** 내가 사양에 "재생은 raw 입력 이력을 먹인다"
라고만 적어 이 결함을 만들었다. Motor 에 의도를 넣는 채널이 넷(velocity/grounded/displacement/pose)
인데 재생은 하나만 되돌리고 있었다.

**테스트 (`03cdd917`).** 처음 추가된 재생 테스트는 기댓값을 `settings = default` 로 계산해
모터의 실제 설정과 다른 조건끼리 bit-identical 비교를 하고 있었다(실패의 원인은 제품이 아니라 테스트).
기댓값을 모터와 같은 설정으로 계산하도록 고치고, **Displacement 를 0으로 지운 입력열의 결과와
달라야 한다**는 검사를 더했다 — D2 회귀를 직접 겨냥한다. EditMode 112/112 통과.


**✅ 2026-09-16 은희 Play 검증 — 루트모션 정상, 스냅 없음.** D1·D2 가 의도대로 동작했다.
남은 확인: 스킬 전진 · 플랫폼 캐리 · 자동접근(같은 채널이므로 함께 해소됐을 가능성이 높다),
낙사 복귀(b3-2), `[Recon]` 보정 빈도 감소 여부, `queueWaitTicks` 실측.

#### ✅ 4b3 Play 검증 실측 (2026-09-16, MPPM 호스트+클라1)

**호스트**
```
divergence avg=0.000m max=0.000m   queueWaitTicks=2
sequenceBreakSamples=0   droppedInputsTotal=6(증가 없음)   RTT 8~23ms
```
**오너(클론)**
```
received=50  withinThreshold=50  corrected=0  forceSnap=0
historyMiss=0  stale=0  replayedTicks=0  threshold=0.10m  correction avg=0.000m max=0.000m
```

- `queueWaitTicks=2` — 지터 버퍼 목표 2틱과 정확히 일치. §4b2-α 지연 예산 표의 빈칸이 채워졌다.
- `sequenceBreakSamples=0` — **b3-0 완료 조건 충족.**
- 체감: 입력 지연 없음, 루트모션·스킬 전진·플랫폼 캐리·자동접근 전부 정상, 스냅 없음.

🔴 **이 숫자로 증명되지 않는 것 — MPPM 은 보정 경로를 검증하지 못한다.**
MPPM 의 두 피어는 **같은 기계·같은 씬**에서 돈다. R-7 대로 그 조건에서 `Simulate` 는 비트 단위로
같은 결과를 내므로 발산이 구조적으로 0 이고, 따라서 **되감기·재생·보정이 한 번도 실행되지 않았다**
(`corrected=0`, `replayedTicks=0`). 파이프라인이 도는 것과 보정이 옳게 도는 것은 별개다.

**따라서 다음이 미검증으로 남는다:**
- 되감기+재생의 실제 동작(EditMode 결정론 테스트로만 확인됨)
- 임계값 `0.10m` 의 적정성 — 한 번도 넘지 않았으므로 크고 작음을 판단할 근거가 없다
- 보정 스냅의 시각적 거슬림 정도

**검증 방법은 둘 중 하나다.** ① 실제 2대 기계로 테스트(공모전/출시 전 필수),
② 디버그 스위치로 서버 시뮬레이션에 인위적 오차를 주입해 보정을 강제 발동.
②는 지금 넣을 수 있고, 임계값을 실측으로 정하려면 결국 필요하다.



#### ✅ 오너 권위 브랜치 검증 완료 (2026-09-16, 은희 MPPM)

`feature/player-motor-owner-auth` 에서 **전 항목 통과**. 그 브랜치는 `development` 에 머지 후 삭제됐고,
지스타까지 이 코드로 간다. 새 작업은 `development` 에서 분기한다.
검증 항목: 스폰 위치 · 이동 · 낙사 복귀(생존/사망) · 넉백 · 보스 그랩 · 대시 ·
루트모션 전진 · 스킬 전진 · 플랫폼 캐리 · 스킬 자동접근.

**전환 과정에서 드러난 구멍 하나** — 오너 권위에서는 NGO 가 클라에 프리팹을 **원점**에 만든 뒤
NT 가 복제 상태로 옮기는데, **오너 인스턴스는 NT 가 권위라 그 상태를 적용하지 않는다.**
오너만 원점에 남고 그 원점을 모두에게 내보냈다. 서버가 `OnNetworkSpawn` 에서 스폰 포즈를
오너에게 직접 전달해 해결(`ef4119d2`). 서버 권위에서는 오너가 비권위라 드러나지 않던 문제다.

**남은 것 (급하지 않음)**
- 진단 계측 제거 — `[MoveDiag]` · `[Recon]` · `DevMoveSpeedProbe` · Motor 의 외부 이동 감지 경고.
  이 브랜치에서 `[Recon]` 은 아무것도 찍지 않는다(서버 시뮬레이션이 없다). 서버 권위로 돌아갈 때
  다시 필요하므로 **삭제보다 조건부 컴파일이 낫다.**
- 이동 플랫폼·컨베이어 상하 떨림(후순위 확정, 미조사)
- `development` PR — 팀장·은희 리뷰

### 브랜치 둘 — 서버 권위 / 오너 권위 (2026-09-16)

매치메이킹은 **친구 초대·협동 위주**로 확정됐다(은희). 그 조건에서는 리슨 서버로 충분하고,
서버 권위가 막는 것과 못 막는 것이 분명하다 — 참가자의 위치 핵은 막지만 **호스트 자신은 못 막는다.**
공개 매칭 + 랭킹/경제가 들어오면 그때는 데디케이티드 서버 문제이지 이 코드의 문제가 아니다.

두 선택지를 브랜치로 남긴다. **1~3단계 Motor 성과(단일 위치 소유자, 물리 틱, 마스크 통일,
kinematic, stepOffset, 결정론 추출)는 양쪽 모두 동일하다.** 갈리는 것은 4b 뿐이다.

| | `feature/player-motor-server-auth` | `feature/player-motor-owner-auth` |
|---|---|---|
| 위치의 주인 | 서버 | 오너 |
| 루트/Armature `NetworkTransform.AuthorityMode` | 0 (Server) | 1 (Owner) |
| 오너 인스턴스 NT | 끔(예측이 덮이지 않게) | 켬 |
| 서버의 원격 플레이어 시뮬레이션 | 함 | 안 함 |
| 입력 RPC · 보정 채널 · 되감기/재생 | 동작 | 코드는 남고 **꺼짐** |
| 치트 방지(참가자) | ○ | ✕ |
| 이동 신규 기능 저작 비용 | 오너·서버 양쪽 + 재생 가능해야 함 | 한 번 |

**가르는 스위치는 하나다** — `Player.ServerAuthoritativeMovement` (const).
`IsSimulating` 과 `IsMotionAuthority` 가 이 값으로 갈리고, 나머지(루트모션·스킬 전진·플랫폼 캐리·
자동접근·넉백·구속)는 그 둘만 보므로 자동으로 따라온다. 그 외 이 값을 직접 보는 곳은 셋뿐이다:
서버 시뮬레이션 호출, 입력 RPC 송신, `networkTransform.enabled`, 그리고 `PlayerMotor.
CapturesServerObservation` 과 `PlayerFallRecovery.TeleportOnServer`.

**오너 권위로 전환할 때 잊기 쉬운 것** — 낙사 복귀. 서버 권위에서는 서버가 자기 사본을 옮기고
`forceSnap` 으로 오너를 맞추지만, 오너 권위에서는 서버가 옮겨봐야 오너 권위 NT 가 되돌린다.
오너에게 직접 옮기라고 지시해야 한다(`TeleportOwnerRpc`).


#### 🔴 결정 — 지스타(1차 커트라인)까지는 오너 권위 (2026-09-16, 은희)

**오너 권위 이동은 `development` 에 머지됐다(2026-09-16). 그 브랜치는 삭제됐다.** 서버 권위 구현은
`feature/player-motor-server-auth` 에 완성된 채로 보존한다.

근거:
- 매치메이킹이 **친구 초대·협동 위주**로 확정됐다. 그 조건에서 리슨 서버 + 오너 권위로 충분하다.
- 서버 권위가 막는 것은 **참가자의 위치 핵**이고, **호스트 자신은 어차피 못 막는다.**
  공개 매칭 + 랭킹·경제가 들어올 때는 코드가 아니라 **데디케이티드 서버** 문제가 된다.
- 이동 신규 기능을 오너·서버 양쪽에서 성립시키고 재생까지 가능하게 만드는 비용을
  지스타 전까지는 치르지 않는다.

**되살리는 비용은 작다.** `Player.ServerAuthoritativeMovement` 를 true 로 바꾸고 프리팹
루트·Armature 의 `AuthorityMode` 를 0(Server) 으로 되돌리면 된다. 예측·되감기·재생·보정 코드는
오너 권위 브랜치에도 **지우지 않고 남아 있다.**

**지스타 이후 서버 권위로 돌아갈 때 반드시 다시 볼 것** (이번에 실제로 터진 것들):
- 오너 전용 게이트(`!IsOwner return`)가 새로 생기지 않았는지 — 루트모션·스킬 전진·자동접근·낙사
  복귀가 전부 이 이유로 깨졌다
- 예측 재생은 **그 틱의 모든 의도**를 재현해야 한다(velocity/grounded/displacement/pose 네 채널)
- 서버 틱과 오너 입력 틱은 **별개 번호 공간**이다. 섞으면 입력 필터와 보정 ack 이 함께 무너진다
- 실기기 2대 검증이 남아 있다. MPPM 은 같은 기계라 발산이 0 이라 보정 경로를 검증하지 못한다

### Open questions (1~3단계·stepOffset)

없음. 전부 완료·승인됨.

---

## 7. Out of scope

- 몬스터/보스 이동 및 `LinearKnockback` 변경 (경석 담당 — R-2는 합의만)
- `CharacterController` 도입 (D-1에서 기각)
- 카메라, 애니메이션 클립/루트모션 저작 자체
- 넉백 종료 보고·서버 타임아웃 안전망 단순화 (결정론 확보 후 별건)
- 텔레포트/리셋 계열(A-6)의 통합 — Motor 예외로 남긴다(정상 이동 경로가 아님)

---

## 8. Acceptance criteria

**1단계 — ✅ 완료 (2026-09-11 승인, `1e6113b`+`76824f2`)**
- [x] `PlayerMovement.Move()`가 `FixedUpdate`에서만 돈다. `Time.fixedDeltaTime` 사용.
- [x] fps 30/60/144 지속속도 5.072 / 5.009 / 4.992 m/s — **편차 1.58%**. 컴파일 0에러.
- [x] 경사 등판·벽 슬라이드 — 이상 없음.
- [~] **플랫폼 탑승은 실측하지 않고 승인됐다.** `76824f2`(캐리 주기 회귀 수정)의 대상이므로,
      이후 플랫폼 관련 이상이 보고되면 **여기를 먼저 볼 것**.

> 🔴 1단계에서 얻은 교훈 — **소비자만 옮기면 안 된다.** `Player.ApplyPlatformCarry`를
> `FixedUpdate`로 옮겼는데 생산자 `MovingPlatform`이 `Update`에 남아 주기가 어긋났다
> (144fps에서 이동량의 ~35%만 전달). Motor에 의도를 넣는 **모든 생산자의 루프**를 함께 봐야 한다.
> `ISurfaceCarrier` 구현체도 계약이 갈린다 — `ConveyorTile`은 속도형(`speed×dt`, 루프 무관),
> `MovingPlatform`은 변위형(차분, 루프 결합).

**2단계**
- [x] `Assets/1.Scripts/Player/**`에서 `MovePosition` 호출이 **`PlayerMotor` 1곳뿐**이다(A-6 제외).
- [x] `PlayerStateContext`에 `Rigidbody`가 없다.
- [x] `ClampByStaticGeometry`, `ResolvePlanarSlopeDirection` 삭제됨.
- [x] Script Execution Order가 `ProjectSettings`에 커밋됨.
- [ ] 대시 중 "요청이동 ≈ 적용이동 ≈ 실제이동"(기존 `[Dash] 종료` 로그 기준, 오차 10% 이내).
- [ ] 평타 루트모션 전진이 기존과 동일한 거리를 낸다.

**3단계 — ✅ 완료 (2026-09-11 승인, `b9bd537`+`5f8e014`)**
- [x] `Player.prefab` = `IsKinematic on` / `UseGravity off` / `Interpolate`.
- [x] `isKinematic`·`useGravity`를 쓰는 코드가 **`PlayerMotor` 한 곳뿐**이다(D-6, 별도 Corpse 물리 제외).
- [x] 넉백 거리가 SO 값으로 튜닝된다(마찰 의존 제거).
- [x] **넉백이 벽에 박으면 그 자리에 정지한다** — 튕김·슬라이드 없음.
- [x] **벽에 박아도 넉백 경직 시간은 줄지 않는다**(벽 앞/개활지 경직 시간 동일).
      `plannedDuration`을 진입 시점에 감속도로 계산해 고정하므로 이동과 분리된다.
- [~] `Motor.SetMode` 존재 · **사용처 0개** 확인. 단 **속도 인계는 테스트로 덮이지 않았다**
      (EditMode 테스트는 4단계에서 생긴다). 실제로 쓰기 전에 테스트를 먼저 붙일 것.
- [x] 장애물·지면 마스크가 **`PlayerGameRuleData` 단일 소스**다.
      `dashObstacleMask`·`aliveGroundMask`·`soulGroundMask`의 `~0`이 전부 제거됨.
- [x] **플레이어끼리 통과한다**(기본값). `blockOtherPlayers`를 켜면 막힌다 — 양방향 확인.
- [x] **Soul이 Player를 통과하고 Player도 Soul을 통과한다.** `blockOtherPlayers`가 켜져 있어도 Soul은 통과.
- [x] 통과 상태에서도 서로 때릴 수 있다(히트박스 영향 없음).
- [x] **몬스터/투사체/HazardArea/히트박스 위에 설 수 없다**(`groundMask`로 `~0` 제거된 결과).
- [x] 낙사 → 안전지점 복귀가 기존과 동일하게 동작.
- [x] Soul 부유가 기존과 동일하게 동작.

**stepOffset — ✅ 완료 (2026-09-12 승인, `9aed2cd`+`3ca164a`)**
- [x] `stepOffset`(0.3m 기본) 이하 턱을 걷기·대시 모두 넘는다. 초과 턱은 막힌다.
- [x] 경사 등판·벽 차단·절벽 낙하·접지 스냅 회귀 없음.
- [x] **EditMode 테스트 6개 통과** — 이 계획에서 자동 테스트가 처음 생긴 지점.
      낮은 턱 / 초과 턱 / 급경사 우회 금지 / 공중 미발동 / 접지 스냅 안정 / 낙하 바닥 관통 방지.
- [~] **실물 계단으로는 검증하지 못했다.** 맵에 계단 에셋이 없어 `0.3m`은 추정값이다.
      에셋 입고 후 실측해 `PlayerGameRuleData.stepOffset`을 조정할 것.
- 🔴 테스트에 `.asmdef`를 못 붙였다 — asmdef 어셈블리는 미리 정의된 `Assembly-CSharp`을 참조할 수
  없는데 `PlayerMotionSweep`이 거기 있다. `Editor/` 폴더 + `Assembly-CSharp-Editor`가 유일한 경로다.
  대가로 `UNITY_INCLUDE_TESTS` 제약을 못 건다. **4단계의 순수 로직 추출 때 자체 asmdef로 옮길 것.**

**4단계**
- [ ] `Simulate`가 `MonoBehaviour` 없이 EditMode에서 호출 가능하다.
- [ ] 같은 입력 시퀀스를 두 번 시뮬레이션하면 위치가 **비트 단위로 동일**하다.
      ⚠️ **단서 필요** — 스윕이 `Physics.CapsuleCast`(씬 상태 의존)를 타므로 이 조건은
      **같은 머신·같은 씬 상태**에서만 성립한다. 피어 간 비트 동일은 보장되지 않는다(§6 R-7).
- [ ] 인위적 지연(200ms) 주입 시 오너 위치가 서버 위치로 수렴한다.

## 9. Verification plan

1. **EditMode 테스트** — `Assets/1.Scripts/Player/Motor/Tests/EditMode/`.
   기존 `Dash/Tests/EditMode/` 패턴을 따른다. 스윕·경사 투영·계단·감쇠는 전부 순수 로직이라 테스트 가능.
2. **fps 고정 측정** — `Application.targetFrameRate`를 30/60/144로 바꿔가며 이동 시간 측정(1단계 핵심).
3. **MPPM 2인 검증** — 🔴 **Play는 사용자가 직접 실행한다**(MCP로 Play 진입 금지 — MPPM이 깨진다).
   체크리스트: 경사 등판 / 벽 슬라이드 / 모서리 / 계단 / 이동 플랫폼 탑승·하차 /
   대시(평지·경사·벽·절벽) / 평타 러시 / **넉백(개활지·벽 정면·벽 비스듬히·경사·공중)** /
   그랩(Carry·Push) / **그랩 중 넉백 동시 진입** / 낙사 복귀 / Soul 이동·부유 / 보스룸 연출 잠금 /
   **플레이어끼리 겹침(마주 걷기·대시로 관통·좁은 통로 동시 통과) + 겹친 상태에서 상호 피격**.
4. **단계별 PR 분리** — 4단계를 한 PR로 묶지 않는다. 각 단계가 독립적으로 검증·롤백 가능해야 한다.
   3단계는 "kinematic 전환 + 넉백"과 "stepOffset"을 다시 쪼갠다(R-1).

---

## 10. 착수 전 체크

- [ ] 사용자(은희) 승인
- [ ] **R-3 워킹트리 프리팹 변경 정리** — 유일한 착수 블로커(`Player.prefab`·`Paladin.prefab` 미커밋 155줄)
- [ ] `CONTEXT.md` 작업 세션 등록 + 브릿지 `work_started` 발신

3단계 착수 전:
- [ ] R-4 조사 (VFX·보스가 플레이어 위치를 직접 건드리는 경로)
- [ ] 경석에게 넉백 메커니즘 분기 공유 (합의 아님 — 통보)

~~Open questions~~ 전부 확정됨. ~~R-2 경석 합의~~ 블로커 아님(§6 조사 결과).
