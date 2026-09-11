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

### Open questions (남음)

없음. 착수 가능.

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

**3단계**
- [ ] `Player.prefab` = `IsKinematic on` / `UseGravity off` / `Interpolate`.
- [ ] `isKinematic`·`useGravity`를 쓰는 코드가 **`PlayerMotor` 한 곳뿐**이다(D-6).
- [ ] 넉백 거리가 SO 값으로 튜닝된다(마찰 의존 제거).
- [ ] **넉백이 벽에 박으면 그 자리에 정지한다** — 튕김·슬라이드 없음.
- [ ] **벽에 박아도 넉백 경직 시간은 줄지 않는다**(벽 앞/개활지 경직 시간 동일).
- [ ] `Motor.SetMode`가 존재하고 속도 인계가 테스트로 덮인다. **사용처는 0개.**
- [ ] 장애물·지면 마스크가 **`PlayerGameRuleData` 단일 소스**다.
      `dashObstacleMask`·`aliveGroundMask`·`soulGroundMask`의 `~0`이 전부 제거됨.
- [ ] **플레이어끼리 통과한다**(기본값). `blockOtherPlayers`를 켜면 막힌다 — 양방향 확인.
- [ ] **Soul이 Player를 통과하고 Player도 Soul을 통과한다.** `blockOtherPlayers`가 켜져 있어도 Soul은 통과.
- [ ] 통과 상태에서도 서로 때릴 수 있다(히트박스 영향 없음).
- [ ] **몬스터/투사체/HazardArea/히트박스 위에 설 수 없다**(`groundMask`로 `~0` 제거된 결과).
- [ ] 0.3m 턱을 걷기·대시 모두 넘는다.
- [ ] 낙사 → 안전지점 복귀가 기존과 동일하게 동작.
- [ ] Soul 부유가 기존과 동일하게 동작.

**4단계**
- [ ] `Simulate`가 `MonoBehaviour` 없이 EditMode에서 호출 가능하다.
- [ ] 같은 입력 시퀀스를 두 번 시뮬레이션하면 위치가 **비트 단위로 동일**하다.
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
