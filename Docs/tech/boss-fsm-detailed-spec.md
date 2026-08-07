# 보스 FSM 구현 스펙 — 23호 & 웰즈

> 작성 2026-08-06 · 경석 · **구현 직전 스펙**. 주말 구현용.
> [boss-fsm-design.md](boss-fsm-design.md) = *무엇을·왜* / **이 문서 = *어떻게*** (클래스·파라미터·이벤트·전이 전수).
> 계획서 [PLAN-boss-fsm.md](../../PLAN-boss-fsm.md) · 플레이어 요청 [handoff-player-carry-socket.md](handoff-player-carry-socket.md)
>
> **§12 에 미확정 질문 6건**을 모아 뒀다(4건은 팀장 답변으로 닫힘). 그 6건 외에는 코드를 바로 칠 수 있는 수준으로 적었다.

---

## 1. 애니메이터 계약 ✅ 검증됨

`Assets/4.Animations/Wells&No.23/No.23/Controller/TwentyThreeController.controller` 를
Unity 에서 직접 읽어 확인했다. **애니메이터는 고칠 필요가 없다** — 기존 BT 가 쓰던 계약을 그대로 쓴다.

### 1.1 파라미터 8개

| 파라미터 | 타입 | 용도 |
|---|---|---|
| `State` | **Int** | **`(int)TwentyThreeState` 를 그대로 넣는다.** 통상 전이 전부 |
| `Jump` | Int | JumpAttack 내부 단계. `Leap` 진입 조건이 `State==8 && Jump==0` |
| `Groggy` | Int | 그로기 내부 단계. `1` = Start→Loop, `2` = Loop→End |
| `IsGroggy` | Trigger | **AnyState → GroggyStart** |
| `IsBreak` | Trigger | **AnyState → Break** |
| `IsDead` | Trigger | **AnyState → Dead** |
| `IsInit` | Trigger | `Break → Idle`, `GroggyEnd → Idle` (복귀) |
| `IsWalking` | Trigger | ⚠️ 전이에서 쓰이는 곳을 못 찾았다 → §12 Q2 |

### 1.2 🔴 가장 중요한 함정

**`Groggy`·`Break`·`Dead` 는 `State` 로 진입하지 않는다. AnyState 트리거다.**

```
State = 12 (Groggy)  →  아무 일도 안 일어난다.   ❌
IsGroggy 트리거      →  AnyState → GroggyStart   ✅
```

`TwentyThreeState` 에 `Groggy=12 / Break=13 / Dead=14` 가 있어서 "State 에 넣으면 되겠지" 로
가기 쉽다. **FSM 내부 상태값과 애니메이터 구동 방식이 다른 구간이다.**
→ `SetState()` 단일 지점에서 갈라 처리한다 (§4.1).

### 1.3 상태 ↔ 클립

클립은 전부 **`Assets/50.Art/Char/Boss/SK/SK_23.fbx` 의 내장 서브에셋**이다(21개). `.anim` 파일은 없다.

| State | enum 값 | 애니 스테이트 | 클립 (프레임) |
|---|---|---|---|
| Idle | 0 | `Idle` | `Boss_23_idle` (0~163) |
| Walk | 1 | `Walking` | `Boss_23_walk` (0~169) |
| LeftHookAttack | 2 | `LeftHook` | `Boss_23_hookL` (0~56) |
| RightHookAttack | 3 | `RightHook` | `Boss_23_hookR` (0~56) |
| UpperAttack | 4 | **`Uppercut`** | `Boss_23_uppercut` (0~56) |
| Grab | 5 | `Grab` | `Boss_23_grab` (0~187) |
| Hold | 6 | `Holding` | `Boss_23_grabshock` (0~68) |
| Throw | 7 | `Throw` | `Boss_23_grabdump` (0~39) |
| JumpAttack | 8 | **`Leap`** (+`Jump` Int) | `Boss_23_jump`(44~158) → `Boss_23_jumping`(0~1) → `Boss_23_landingattack`(8~123) |
| DashAttack | 9 | `DashAttack` | `Boss_23_dash` (17~102) |
| Charging | 10 | `Charging` | `Boss_23_charging` (0~158) |
| Rage | 11 | `Rage` | **전용 클립 없음 — 정상.** Rage 는 "전기 두르고 돌진 반복"이라 **상태일 뿐**이고 애니는 기존 돌진을 쓴다 (팀장 확인 2026-08-06) |
| Groggy | 12 | `GroggyStart`→`Groggy`→`GroggyEnd` | `Boss_23_.groggy_enter`(0~89) → `_ing`(88~105) → `_end`(105~158) |
| Break | 13 | `Break` | **Groggy 와 같은 애니. 지속시간만 다르다**(2초 vs 5초, 팀장 확인). 별도 클립 불필요 |
| Dead | 14 | `Dead` | `Boss_23_.die` (0~155) |

- **enum 이름 ≠ 애니 스테이트 이름 2개**: `UpperAttack`→`Uppercut`, `JumpAttack`→`Leap`.
- **`Boss_23_getowned01/02`**(0~29) = **피격 리액션**. ⏸ **카운터 성공 시 재생하는 방향**으로
  팀장 판단(2026-08-06). 전체 적용 여부는 회의 안건 → §12 A3.
- **미사용 클립**: `Boss_23_.groggy`(0~90, `_enter/_ing/_end` 3분할로 대체됨),
  `Boss_23_grabend`(0~82).
- `Arrive` 스테이트가 따로 있다(등장 연출, `State==0 && Jump==0` 으로 탈출). **범위 밖 — 안 건드린다.**

### 1.4 검증된 전이 (발췌)

```
AnyState  → Dead        [IsDead]
AnyState  → Break       [IsBreak]
AnyState  → GroggyStart [IsGroggy]
GroggyStart → Groggy    [Groggy == 1]
Groggy      → GroggyEnd [Groggy == 2]
Groggy      → Break     [IsBreak]  (exitTime 있음)
GroggyEnd   → Idle      [IsInit]   (exitTime 있음)
Break       → Idle      [IsInit]
Idle/Walking → 각 공격  [State == n]
공격 전부   → Idle/Walking [State == 0 / 1]
```

**공격 스테이트는 `hasExitTime: false`** 다 — 즉 **클립이 끝나도 스스로 안 빠져나온다.**
FSM 이 `State` 를 0 또는 1 로 되돌려야 복귀한다. 안 되돌리면 **공격 자세로 얼어붙는다.**

### 1.5 🔴 애니 이벤트는 **SVN** 이다

`Assets/50.Art` 는 **`.gitignore:84` 로 git 제외**이고 **SVN 워킹카피**(`Assets/50.Art/.svn`)다.
애니 이벤트는 `SK_23.fbx.meta` 의 `clipAnimations` 에 저장되므로:

- **§6 의 이벤트 추가는 전부 SVN 커밋**이다. git 에 안 올라간다.
- git 만 받는 팀원에게는 **C# 은 가는데 이벤트는 안 간다** → 히트가 안 나고 카운터 창이 안 열린다.
  **에러는 안 난다.** 조용히 아무 일도 안 일어난다.
- → 그래서 **코드 타이머 폴백을 반드시 남긴다**(§6.3). 이벤트가 없어도 보스가 얼지는 않게.

---

## 2. 컴포넌트 구성

| 컴포넌트 | 상태 | 책임 | 권한 |
|---|---|---|---|
| `TwentyThreeBoss : BossBase` | **신규** | FSM 본체 — 상태·전이·선택기·페이즈 | 서버 |
| `WellsBoss` | **신규** | Wells 4상태 FSM + 폭탄 주기 | 서버 |
| `BossAttackTableSO` | **신규** | 공격별 쿨·거리창·가중치·데미지·캐리 수치 | 데이터 |
| `DashCarryController` | **신규** | 돌진 이동 + 캐리 소켓 + 벽 판정 | 서버 |
| `IBossTelegraph` / `TintTelegraph` | **신규** | 카운터 창 표현 | 클라 |
| `GrabController` | **재작성** | BT 블랙보드 3개 제거 (§3). 판정 로직은 참고 | 서버 |
| `JumpController` | **재작성** | BT 블랙보드 2개 제거 (§3). 장판 2단 로직은 참고 | 서버 |
| `TwentyThreeAnimEvents` | **재작성** | 이벤트 5개 → 11개 (§6) | 서버 |
| `ChargeController` | **재작성** | BT 결합은 없으나 🔴 **`==` 교착 버그**가 있다 → `>=` + 파괴·도달 합산. `Clamp(1,3)` 도 함께 | 서버 |
| 폭탄 계열 (`BombLauncher`·`BombController`·`Bomb`) | 🔴 **재작성 + 물리 전환 + 분리** | **Rigidbody 물리로 전환**하고 투사체와 장판을 별도 오브젝트·수명으로 쪼갠다. 되쳐내기 비례계수 노출 (§10.5) | 서버 |
| `AreaZone` (지속 영역) | **신규** | 폭탄에서 분리된 지속 장판. **타입 있음**(화염/늪/독/번개…), 자기 타이머로 소멸, **같은 타입끼리만** 중첩 성장 | 서버 |
| `ChargingObject` | **재작성** | 송전탑 개체 | 서버 |

> **전면 재작성이다** — 기존 컨트롤러를 고쳐 쓰지 않는다(2026-08-06 팀장 판단).
> 근거는 [boss-current-problems-audit.md](boss-current-problems-audit.md).
> 기존 코드는 **로직 참고용**으로만 읽는다: 잡기 판정 모양, 장판 2단 점증, 폭탄 궤적 공식,
> `GroundProbe` 사용법은 이미 검증된 것이라 그대로 옮길 값이 있다.

**삭제 대상** (전환 검증 후 별도 커밋): `Assets/8.BehaviorTreeGraph/Boss/**`,
`Assets/1.Scripts/BT/Actions/Attack/*`(보스용), `BossStateChanged`(EventChannel),
`EnemyBTActivator` 의 보스 경로, 프리팹의 `BehaviorGraphAgent` 3개.

### 2.1 `BossBase` 를 어떻게 둘 것인가

`BossBase`·`BossState` 는 **외부 참조가 0건**이다(전수 확인). 자유롭게 바꿔도 된다.

**채택: `BossBase` 는 상태를 `NetworkVariable<int>` 로 들고, 파생이 enum 으로 해석한다.**

```csharp
protected readonly NetworkVariable<int> _stateRaw = new(0, Read.Everyone, Write.Server);
protected abstract void OnStateChangedClient(int raw);   // 파생이 (TwentyThreeState)raw 로 캐스팅
```

- 제네릭 `BossBase<TState>` 도 되지만, NGO `NetworkVariable` + 인스펙터 + 프리팹 참조가 같이
  까다로워진다. 보스가 하나뿐이니 **int 가 싸다.**
- 기존 `BossState` enum 은 **삭제**한다 (§1.1 의 단일 enum 결정).

---

## 3. BT 결합 지점 — 새 코드가 대체해야 하는 것

전면 재작성이지만 **BT 가 무엇을 대신해 주고 있었는지**는 알아야 새 코드가 그 구멍을 메운다.
아래는 "고칠 목록"이 아니라 **"새로 짤 때 반드시 제공해야 하는 기능 목록"** 이다.
(BT 를 끄기만 하고 옛 컨트롤러를 그대로 두면 여기서 `NullReference` 로 죽는다.)

### 3.1 `GrabController` — 블랙보드 3개

| 현재 | 바꿀 것 |
|---|---|
| `BlackboardVariable<bool> IsGrabbed` | `public bool IsGrabbed { get; private set; }` (FSM 이 읽음) |
| `BlackboardVariable<GameObject> GrabbedPlayer` | `public Player GrabbedPlayer { get; private set; }` |
| `BlackboardVariable<TwentyThreeState> CurrentState` — `Update()` 가 `== Hold` 를 읽어 홀드 데미지 주기 구동 (`:79`) | **`Update()` 의 자체 판단을 없애고** FSM 이 `Hold` 상태 Stay 에서 `TickHold(dt)` 를 호출 |

- `Start()` 의 블랙보드 조회 4블록(`:46~73`) 전부 삭제 → `bt` 필드 제거.
- `Detect()` / `Throw()` 는 애니 이벤트 진입점이므로 **시그니처 유지**.
- ⚠️ `UpdateBlackboard()` 가 잡기 성공/실패를 결정하므로, 반환값을 `bool` 로 바꿔
  **FSM 이 `Grab`→`Hold` / `Grab`→재판단 을 가를 수 있게** 한다. (지금은 BT 가 블랙보드를 폴링)

### 3.2 `JumpController` — 블랙보드 2개

| 현재 | 바꿀 것 |
|---|---|
| `BlackboardVariable<Vector3> ArrivePoint` (쓰기) | `public Vector3 ArrivePoint { get; private set; }` |
| `BlackboardVariable<float> JumpingTime` (읽기) | `BossAttackTableSO.jumpHoverTime` 주입 |

`SetTarget()` / `ShowMyMeshClientRpc()` / `OnLanded()` 는 애니 이벤트 진입점 — 유지.

### 3.3 순서

**BT 이탈(3.1·3.2) → FSM 골격(S1) → 프리팹에서 `BehaviorGraphAgent` off** 순으로 간다.
BT 와 FSM 이 **동시에 사는 구간을 만들지 않는다** (둘 다 `State` 를 쓰면 애니가 튄다).

---

## 4. 상태 전이 전수표 (23호)

`dt` = 서버 틱. 모든 항목 **서버 전용**. 클라는 `_stateRaw` 복제만 받는다.

### 4.1 `SetState(TwentyThreeState s)` — 단일 진입점

```
_stateRaw.Value = (int)s
switch (s):
  Groggy : animator.SetTrigger("IsGroggy");  animator.SetInteger("Groggy", 1)
  Break  : animator.SetTrigger("IsBreak")
  Dead   : animator.SetTrigger("IsDead")
  default: animator.SetInteger("State", (int)s)
```

**애니 구동을 여기 한 곳에만 둔다.** 상태마다 흩어지면 §1.2 함정을 반드시 밟는다.

### 4.2 전수표

| 상태 | Enter | Stay(매 틱) | Exit / 전이 조건 |
|---|---|---|---|
| **Idle** | `StopAgent()` · `State=0` · 타겟 해제 | 타겟 탐색(Soul 제외) | 타겟 有 → `Walk` |
| **Walk** | `State=1` | 타겟 추적 · `FaceTarget` · **선택기 실행**(§5) | 공격 선택됨 → 해당 공격 / 타겟 無 → `Idle` |
| **LeftHook / RightHook / Upper** | `StopAgent` · `FaceTarget` · `State=2/3/4` · 쿨 시작 · `RemoveType` | `FaceTarget` **금지**(선딜 후 방향 고정) | 클립 `End` 이벤트 → 재판단 |
| **Grab** | `StopAgent` · `FaceTarget` · `State=5` · 쿨 시작 · **카운터 창 Open** | — | `TryGrabEvent` 에서 잡기 성공 → `Hold` / 실패 → 재판단 / **카운터 성공 → `Groggy`** |
| **Hold** | `State=6` | `grab.TickHold(dt)` (주기 데미지) | `holdDuration` 종료 → `Throw` |
| **Throw** | `State=7` | — | `ThrowEvent` → 클립 `End` → 재판단 |
| **JumpAttack** | `StopAgent` · `State=8` · **`Jump=0`** · 쿨 시작 | `JumpController` 시퀀스(§7) | `OnLandedEvent` → 재판단 |
| **DashAttack** | `FaceTarget` · `State=9` · 쿨 시작 · **카운터 창 Open** · `dashCarry.Begin()` | 돌진 이동 · 캐리 갱신 | 벽/맵끝/최대거리 → 스턴 부여 후 재판단 / **카운터 성공 → `Groggy`(캐리 해제, 스턴 없음)** |
| **Charging** | `State=10` · 실드 점증 시작 · 전기 장판 on · `charge.StartCharge(playerCount)` | 근접자 데미지+밀치기 · 제한시간 카운트 | 송전탑 전멸 → `Groggy` / 시간 초과 → `Rage` |
| **Rage** | `State=11` · 전기 이펙트 on | 돌진 3회 순차 (**카운터 창 없음**) | 3회 종료 → 재판단 |
| **Groggy** | `IsGroggy` 트리거 · `Groggy=1` · `StopAgent` · **진행 중 전부 취소** · `GroggyCount+=1` | 타이머 2초 | 종료 → `Groggy=2` → `GroggyEnd` → `IsInit` → 재판단 |
| **Break** | `IsBreak` 트리거 · `StopAgent` · **`GroggyCount=0`** | 타이머 5초 | 종료 → `IsInit` → 재판단 |
| **Dead** | `IsDead` 트리거 · agent/콜라이더 off · `OnDeath()` | — | 디스폰 |

### 4.3 "재판단" = `DecideNextAfterAction()`

```
if (_pendingCharging) → Charging            # 페이즈 임계 통과 대기분을 여기서 소비
else if (타겟 無)      → Idle
else                  → Walk                # Walk 의 Stay 가 다음 틱에 선택기를 돌린다
```

**공격 상태에서 곧바로 다음 공격으로 가지 않는다.** 반드시 `Walk` 를 한 번 거친다 —
그래야 `State` 가 0/1 로 내려가 애니 전이가 성립하고(§1.4), 거리 재평가도 한 번 들어간다.

---

## 5. 공격 선택기

`Walk` 의 Stay 에서만 호출. 행동 중에는 호출하지 않는다.

```
SelectAttack(dist, now):
    후보 = []
    for a in table.attacks:
        if now - a.lastUsed < a.cooldown:      continue   # ① 쿨
        if !(a.minDist <= dist <= a.maxDist):  continue   # ② 거리창 (Jump 는 창 무시)
        if !a.AllowedIn(currentPhase):         continue   # ③ 페이즈 개방
        w = a.weight
        if a.type == _lastAttack: w *= table.repeatPenalty   # ④ 연속 감쇠 (제외 아님)
        후보.add(a, w)

    if 후보.empty:  return None        # → 폴백: Walk 유지 (제자리 정지 금지)
    return 가중치룰렛(후보)
```

- **④ 는 제외가 아니라 감쇠**다. 제외하면 후보가 1개일 때 아무것도 못 한다.
  `repeatPenalty` 기본값 **0.3** 제안.
- `None` 이 폴백으로 흡수되는 것이 이 설계의 핵심이다. 현행 `BossBase` 는 여기서
  **제자리에 선다** — 보스가 멍청해 보이는 원인 1위.
- 쿨 재등록: `RemoveType(type)` 은 Enter 에서, `AddType(type)` 은 **쿨 만료 시**.
  → 위 의사코드처럼 `lastUsed` 타임스탬프로 하면 `Add/Remove` 자체가 불필요하다.
  **둘 중 하나만 쓴다** (섞으면 이중 관리가 된다). → §12 Q4

### 5.1 공격표 초기값

| 공격 | 쿨 | 거리창 | 가중치 | 카운터 창 | 타겟 |
|---|---|---|---|---|---|
| LeftHook | 2.5s | 0 ~ 근접 | 25 | ✗ | 현재 타겟 |
| RightHook | 2.5s | 0 ~ 근접 | 25 | ✗ | 현재 타겟 |
| Uppercut | 3.0s | 0 ~ 근접 | 20 | ✗ | 현재 타겟 |
| Grab | **10s** | 0 ~ 근접 | 15 | **✓** | 현재 타겟 |
| DashAttack | **5s** | 원거리만 | 10 | **✓** | 현재 타겟 |
| JumpAttack | **10s** | **무시** | 5 | ✗ | **최원거리 플레이어** |

가중치는 전부 SO 노출. 거리 임계값은 §11 TBD.

---

## 6. 애니메이션 이벤트

독트린(몬스터 FSM 에서 확립): **히트·종료·커밋은 클립 이벤트**, `End` 는 `exitTime` **앞**에 둔다.
코드 타이머는 **이벤트 누락 대비 폴백**으로만 남긴다.

### 6.1 기존 5개 (유지) — FBX meta 에서 확인됨

| 이벤트 | 클립 | 하는 일 |
|---|---|---|
| `SetTargetEvent` | `Boss_23_jump` | `jumpController.SetTarget()` — 타겟 확정 + 장판 생성 + **메시 off** |
| `FallEvent` | `Boss_23_landingattack` | 메시 on |
| `OnLandedEvent` | `Boss_23_landingattack` | `jumpController.OnLanded()` — 착지 데미지 |
| `TryGrabEvent` | `Boss_23_grab` | `grabController.Detect()` |
| `ThrowEvent` | `Boss_23_grabdump` | `grabController.Throw()` |

> 메시 off 는 별도 이벤트가 아니라 `JumpController.SetTarget()` 안(`:138`)에 있다. **추가 불필요.**

### 6.2 추가할 이벤트 — **현재 근접 3종·돌진에는 이벤트가 0개다**

`hookL` · `hookR` · `uppercut` · `dash` · `charging` 클립에는 **이벤트가 하나도 없다.**
지금 근접 공격에 히트 판정 자체가 없다는 뜻이다.

| 이벤트 | 클립 (프레임 범위) | 놓을 위치 | 하는 일 |
|---|---|---|---|
| `OnAttackHit` | `hookL`·`hookR`·`uppercut` (0~56) | 팔이 내려가는 프레임 (≈30~38 추정, **육안 확인 필요**) | `meleeAttack.Hit()` |
| `OnAttackEnd` | `hookL`·`hookR`·`uppercut` (0~56) | **56 보다 앞** (≈52) | 재판단 |
| `OnAttackEnd` | `grabdump` (0~39) | ≈36 | 재판단 |
| `OnCounterWindowOpen` | `grab` (0~187) | 0 | `SetCounterWindow(true)` |
| `OnCounterWindowClose` | `grab` (0~187) | **`TryGrabEvent` 직전 프레임** | `SetCounterWindow(false)` |
| `OnCounterWindowOpen` | `dash` (17~102) | 17 | `SetCounterWindow(true)` |
| `OnCounterWindowClose` | `dash` (17~102) | 돌진 종료 프레임 | `SetCounterWindow(false)` |
| `OnDashHitboxOn` / `Off` | `dash` (17~102) | 돌진 구간 앞뒤 | 캐리 판정 on/off |
| `OnAttackEnd` | `dash` (17~102) | 102 보다 앞 (≈98) | 재판단 |

🔴 **`OnCounterWindowClose` 는 `TryGrabEvent` 보다 반드시 앞 프레임**에 둔다. 같은 프레임이면
호출 순서가 클립 등록 순서에 좌우돼 **"잡히면서 동시에 카운터 성공"** 이 난다.

### 6.3 폴백 (이벤트가 없어도 얼지 않게)

§1.5 때문에 **이벤트가 없는 환경이 실제로 생긴다**(git 만 받은 팀원, SVN 미갱신).

- `OnAttackEnd` 가 안 오면 → `attackDuration` 코드 타이머로 재판단. **필수.**
- `OnAttackHit` 가 안 오면 → 히트 없음(데미지 0). 폴백을 넣으면 이벤트 추가 후 **두 번 맞는다** →
  히트는 폴백을 **넣지 않고**, 대신 서버 경고 로그 1회를 남긴다.
- `OnCounterWindowOpen/Close` 가 안 오면 → 창이 안 열림(카운터 불가). 이것도 폴백 대신 로그.

**"종료"만 폴백, "판정"은 폴백 금지.** 판정을 폴백하면 이중 적용이 조용히 생긴다.

---

## 7. JumpAttack 시퀀스

**클립 3개로 쪼개져 있고 `Jump` Int 가 그 사이를 넘긴다.**

```
Enter:  State=8, Jump=0                → Leap = Boss_23_jump (44~158)
  ↓ 수직 도약 (수평 이동 없음)
SetTargetEvent  (최상단, 클립 내장)     → 최원거리 플레이어 확정 → ArrivePoint
                                        → 장판1(바닥+0.01, 고정 크기)
                                        → 장판2(바닥+0.02, 0.1 → 장판1 크기로 점증)
                                        → ShowMyMeshClientRpc(false)  = 메시 off (:138)
  ↓ jumpHoverTime 체공 (장판 점증 시간) — Boss_23_jumping(0~1) 1프레임 포즈 유지
  ↓ FSM: Jump=2                        → Boss_23_landingattack (8~123)
FallEvent       (낙하 시작, 클립 내장)  → 메시 on
OnLandedEvent   (착지, 클립 내장)       → 장판 내 플레이어 데미지 → 장판 제거
  ↓ FSM: Jump=0, State=1               → 재판단
```

- ✅ **`Jump` 는 2단계다 — `0` = 하늘로 올라감 / `2` = 땅으로 떨어짐** (팀장 확인 2026-08-06).
  별도의 "체공" 값은 없다. `Boss_23_jumping`(1프레임)은 체공 **포즈 유지**용이지 전환 단계가 아니다.
- 🔴 **`Jump` 를 0 으로 되돌리지 않으면 다음 JumpAttack 이 영원히 안 나온다** —
  `Leap` 진입 조건이 `State==8 && Jump==0` 이다. 종료 시 반드시 리셋.

---

## 8. 카운터 창

### 8.1 타임라인

```
Grab:   [클립 시작]───창 열림───[TryGrabEvent 직전 창 닫힘]──[잡기 판정]──Hold──Throw
Dash:   [돌진 시작]────────────창 열림────────────[돌진 종료 창 닫힘]
```

### 8.2 판정 (서버)

```
OnTakeDamage(attackInfo, attacker):
    base 처리(데미지·실드·HP)        # 카운터든 아니든 데미지는 항상 들어간다
    if !_counterWindow.Value:        return
    if !attackInfo.IsInterruptSkill: return      # ← 은희 요청 A
    if !IsInFront(attacker):         return      # ← 헤드어택 오면 교체될 지점
    OnCounterSuccess()

OnCounterSuccess():
    CancelCurrentAction()            # Grab: 히트박스 해제, Hold/Throw 로 안 감
                                     # Dash: 돌진 정지 + 캐리 해제(벽 스턴 없음)
    SetCounterWindow(false)
    GroggyCount += 1
    SetState(GroggyCount >= maxGroggyCount ? Break : Groggy)
```

- `IsInFront` 는 **별도 메서드로 분리**한다. 헤드어택 도입 시 이 한 곳만 바꾼다.
- `_counterWindow` = `NetworkVariable<bool>`(Server write) → `OnValueChanged` 로
  클라 `IBossTelegraph.OnCounterWindow(bool)`.

### 8.3 표현

```
IBossTelegraph { void OnCounterWindow(bool on); }
  └ TintTelegraph : 노란색 베이스 틴트   ← 지금
  └ VfxTelegraph  : 이펙트               ← 나중 (프리팹에서 컴포넌트 스왑)
```

🔴 `HitFlash` 가 `_originalColors` 를 초기화 시점 색으로 캐시하고 MPB 로 복원한다
([HitFlash.cs](../../Assets/1.Scripts/Unit/HitFlash.cs)). 같은 경로로 칠하면 **피격 한 번에
노란색이 날아간다.** → `HitFlash` 에 베이스 틴트 오버라이드 진입점을 추가하고,
카운터가 베이스를 밀고 피격 플래시는 그 위에서 Lerp 하게 한다.

---

## 9. 페이즈 · 송전기

```
TakeDamage → EvaluatePhase():
    hp% <= 0.33 → 목표 2 / <= 0.66 → 1 / else 0
    하향 통과 시에만: _phaseIndex 갱신, _pendingCharging = true, OnPhaseChanged(idx)
```

`_pendingCharging` 은 **현재 행동이 끝난 뒤**(§4.3) 소비한다 — 행동 도중 강제 중단하지 않는다.

### 9.1 송전기 시퀀스

1. `Charging` 진입 — 중앙 이동 후 대기
2. 전기 장판 on — 근접 시 데미지 + 뒤로 밀치기
3. 실드 HP 점증 시작
4. `charge.StartCharge(playerCount)` — **1인 1 / 2인 2 / 3인 4**
5. 전멸 → `Groggy` / 제한시간 초과 → `Rage`

🔴 **현 `ChargeController` 에 버그가 2개 있다. 둘 다 재작성에서 닫는다.**

1. **`Mathf.Clamp(playerCount, 1, 3)` + `player3` 기본값 3** → 3인에 3개만 활성된다.
   `player3 = 4` 로 하고 `Clamp` 상한은 유지한다(인원 **인덱스** 클램프이지 개수 클램프가 아니다).
   프리팹 인스펙터 값도 함께 확인.
2. 🔴 **종료 판정이 `==` 라 교착한다.**
   ```csharp
   if (_destroyCount == _max) _isDefeated = true;   // :168
   if (_reachedCount == _max) _isReached  = true;   // :192
   ```
   **파괴와 도달이 섞이면 두 플래그 모두 영원히 false** 가 되어 차징에서 못 나온다.
   코드에 2026-07-30자 진단 주석·에러 로그가 이미 이 교착을 정확히 기술하고 있다
   (`:173~186`, `:200~206`). 알려져 있었고 아직 안 고쳐졌다.
   → **완료 판정을 `_destroyCount + _reachedCount >= _max` 합산**으로 바꾼다.
   결과는 이분법이다 — **전부 파괴 → `Groggy` / 하나라도 도달 → `Rage`.**

---

## 10. Wells FSM

| 상태 | Enter | 전이 |
|---|---|---|
| `Idle` | 대기 | 폭탄 쿨 만료 → `Throw` |
| `Throw` | `BombHold()` → 애니 → `BombThrow()` | 클립 종료 → `Idle` |
| `Groggy` | 폭탄 주기 정지 | 23호가 그로기/브레이크 해제 → `Idle` |
| `Dead` | — | (없음) |

- **`Jump` 상태 삭제** (기존 `WellsState` 5 → 4). 애니메이터의 `Jump` 스테이트는 미사용이 된다.
- Wells 는 **피격 대상이 아니다** (hurtbox 없음, HP 는 23호 것만).
- 23호 → Wells 동기화는 **23호가 Wells 를 밀어주는 단방향**으로 한다
  (Wells 가 23호를 폴링하면 순서 의존이 생긴다).
- 그 외에는 23호 상태와 **무관하게** 자기 주기로 살포한다.

### 10.1 🔴 Wells 는 **스폰되지 않는** 중첩 NetworkObject다

`WellsAnimEvents.cs:7~23` 에 사고 기록이 있다 — NGO 는 프리팹의 중첩 NetworkObject 를
스폰하지 않는다(씬 오브젝트만 지원). `NetworkBehaviour.IsServer` 는 스폰 시 대입되는
**필드**라 미스폰이면 영원히 false → 그 클래스가 `NetworkBehaviour` 였을 때
**모든 애니메이션 이벤트를 조용히 삼켰다.**

**따라서 Wells 는 자기 `NetworkVariable` 을 가질 수 없다.**

```
23호 NetworkObject (서버)
  ├ _stateRaw        NetworkVariable<int>   ← 23호 상태
  └ _wellsStateRaw   NetworkVariable<int>   ← Wells 상태도 여기 싣는다
        ↓ OnValueChanged (각 클라)
     로컬 Wells Animator 를 구동
```

- Wells 관련 서버 판정은 `NetworkManager.Singleton.IsServer` 로 한다 (`NetworkBehaviour.IsServer` 금지).
- Wells 를 **최상위 NetworkObject 로 분리하는 대안**도 있으나, 위치가 23호에 종속(탑승)이라
  NetworkTransform 하나가 더 늘고 부모 추종을 따로 짜야 한다. **23호에 싣는 쪽이 싸다.**

### 10.2 Wells 애니메이터 계약 (23호와 규약이 다르다 ⚠️)

| 파라미터 | 타입 | 용도 |
|---|---|---|
| `IsThrow` | Trigger | Idle → Throw |
| `IsJump` | Trigger | Idle → Jump (**미사용 예정**) |
| `IsGroggy` | Trigger | **AnyState → Groggy** |
| `IsDead` | Trigger | **AnyState → Die** |
| `IsInit` | Trigger | Groggy → Idle (복귀) |
| `State` | Int | 🔴 **죽은 파라미터** — 전이 조건에 안 쓰인다 |

**23호는 `State` Int 로 구동하는데 Wells 는 전부 트리거다.** 같은 방식일 거라고 가정하지 말 것.
클립: `Boss_welz_idle` / `_throwing` / `_jump` / `_groggy` / `_die`.
`Throw`·`Jump` 는 `hasExitTime: true` 라 클립이 끝나면 스스로 Idle 로 돌아온다(23호와 반대).

---

## 10.5 폭탄 / 장판 — 물리 채택 + 장판 일반화 🔴 신규 범위

### 10.5.1 폭탄을 물리로 바꾼다 (팀장 결정 2026-08-06)

> 초안에서 "물리는 서버/클라 재현이 흔들리니 수동 보간 유지"를 권했는데 **틀린 판단이었다.**
> 보스 계열은 **서버 권한 + `NetworkTransform` 복제**라 클라가 물리를 재현할 필요가 없다.
> 서버만 시뮬레이션하고 결과 위치를 복제하면 된다. 물리로 가는 게 맞고,
> 그러면 수동 스윕(`CheckHitBetween`) 200줄이 통째로 사라진다.

| 지금 | 물리 |
|---|---|
| `isKinematic=true` · `useGravity=false` + 포물선 공식 직접 계산 | 논키네마틱 + 중력 + `AddForce(Impulse)` 1회 |
| 매 FixedUpdate `MovePosition` | 물리 엔진이 적분 |
| `CheckHitBetween()` 구간 SphereCast 수동 스윕 | `OnCollisionEnter` |
| `GroundProbe.TryFindGround` 착지 스냅 | 그냥 떨어진다 |
| 되쳐내기 = `LinearLaunch(거리 = 데미지)` | `AddForce((bomb−player).normalized × damage × 계수, Impulse)` |

**물리로 갈 때 반드시 챙길 것**

1. 🔴 **클라에서는 물리를 끈다.** NGO `NetworkRigidbody` 를 붙이면 비권한 쪽 `isKinematic`
   을 자동으로 세운다. 안 붙이면 **클라 물리와 `NetworkTransform` 이 싸워 떨린다.**
2. 🔴 **CCD 를 켠다** (`collisionDetectionMode = ContinuousDynamic`). 되쳐낸 폭탄은 빠르므로
   기본 Discrete 로는 **벽을 관통**한다.
3. 🔴 **"정지해야 터진다"의 판정** — 기획상 비행 중엔 폭발 타이머가 멈춘다. 물리에서는
   `OnCollisionEnter` 만으로 부족하다(바닥에 닿고도 구른다).
   → `rb.IsSleeping()` 또는 `velocity.sqrMagnitude < ε` 가 **일정 시간 유지**되면 정지로 본다.
4. **보스는 안 밀린다**(기획). 폭탄이 유닛 Rigidbody 에 힘을 주지 않게 한다.
   벽·유닛 충돌은 어차피 즉시 폭발이라 충돌 **응답**이 필요 없다 →
   유닛/벽은 트리거로 받고 **충돌 응답은 바닥만** 받는 구성이 깔끔하다(구현 시 확정).
5. **레이어 충돌 매트릭스를 확인한다.** 수동 스윕은 코드 마스크로 골랐지만
   물리는 **프로젝트 전역 매트릭스**를 탄다 — 지금 마스크와 매트릭스가 다를 수 있다.
6. **되쳐내기 비례계수를 SO 로 노출** (지금은 데미지 = 미터 하드코딩).

### 10.5.2 장판을 **타입 있는 일반 지속 영역**으로

**장판은 앞으로 여러 종류가 된다 — 늪 / 화염 / 독 / 번개 등**(팀장 2026-08-06).
그래서 "화염 장판" 전용이 아니라 **일반 `AreaZone`** 으로 짓는다.

```
AreaZone (NetworkObject)
  zoneType  : AreaZoneType { Fire, Swamp, Poison, Lightning, ... }
  수명      : 자기 타이머로 소멸
  중첩 성장 : 같은 zoneType 끼리만. 다른 타입은 겹쳐도 각자 유지
  효과      : 타입별 (지속 데미지 / 이동 저하 / 도트 / 감전 …)
```

⚠️ **축이 두 개다. 헷갈리지 말 것.** 기존 `FloorAreaEffect.AreaType { None, GrowOnOverlap,
GrowOverTime }` 은 **성장 방식**이지 원소 타입이 아니다.

- **원소 타입**(Fire/Swamp/Poison/Lightning) → **중첩 성장 여부를 가르는 기준**
- **성장 방식**(중첩 성장 / 시간 성장) → 어떻게 커지는가

폭탄은 폭발 시 `AreaZone(Fire)` 를 **별도 스폰**하고 자기는 즉시 despawn 한다.
같은 타입 장판이 이미 있으면 새로 스폰하지 않고 **기존 것을 성장**시킨다.

> JumpAttack 의 빨간 장판(장판1·장판2)은 **예고 표시**이지 지속 영역이 아니다.
> `AreaZone` 과 섞지 말 것 — 그쪽은 시간 성장 인디케이터다.

### 10.5.3 옮길 때 챙길 것

| 항목 | 내용 |
|---|---|
| 폭발 타이머 | 비행 중 **정지**, 멈추면 재개 (기획). 물리에서는 §10.5.1-3 의 정지 판정으로 |
| 폭발 조건 | 벽 / 다른 플레이어 / 보스에 부딪히면 폭발 |
| 상호작용 | **기본 공격(좌클릭)으로만.** 비행 중 추가 타격은 무시(재연산 안 함) |
| 넉백 | 보스는 안 밀리고 유저만. 밀린 유저는 스킬 시전 취소 + hit 판정 |
| 경사면 정렬 | 🔴 현 `MakeFloor()` 는 경사 회전을 계산하고 **`Quaternion.identity` 로 덮어쓴다**(`:533`). 이 버그를 같이 옮기지 말 것 |
| 진단 로그 | 🔴 `[진단]` 로그 4곳 제거 (폭탄마다 콘솔을 덮는다) |

---

## 11. 데이터 스키마 — `BossAttackTableSO`

```
[공격 엔트리 × 6]
  type, cooldown, minDistance, maxDistance, ignoreDistance(Jump 용),
  weight, damage, allowedFromPhase
[전역]
  repeatPenalty (0.3), meleeRange, rangedThreshold
[Grab]  holdDuration, holdDamagePercent, holdPeriod, throwDamagePercent
[Dash]  dashSpeed, carryPushSpeed, wallStunDuration, dashDamage, maxDashDistance
[Jump]  jumpHoverTime, floorGrowTime, landingDamage, aoeRadius
[Charge] timeLimit, shieldGainPerSec, zoneDamage, zonePushForce, pylonCount(1/2/4)
[Rage]  dashCount(3), dashDamage, interval
[그로기] maxGroggyCount(5), groggyDuration(2), breakDuration(5)
```

값이 비어 있는 것은 §12 이후 팀장이 플레이하며 조절. **인스펙터 노출까지만 이번 범위.**

---

## 12. 미확정 — 질문 6건

구현 전에 답이 필요하거나, 제가 임의로 정하면 안 되는 것들입니다.

**2026-08-06 팀장 답변으로 4건이 닫혔습니다.** 남은 건 6건이고, 전부 제 기본값으로 진행 가능합니다.

**닫힌 것**

- ✅ **Rage** — 전용 클립이 없는 게 정상. Rage 는 "전기 두르고 돌진 반복"이라 **상태일 뿐**이다.
- ✅ **Break** — Groggy 와 **같은 애니, 지속시간만 다르다**(2초 vs 5초). 별도 클립 불필요.
- ✅ **`Jump` Int** — **0 = 하늘로 올라감 / 2 = 땅으로 떨어짐**. 체공 전용 값은 없다.
- ✅ **SVN 커밋** — 주말 중 가능. 애니 이벤트 추가에 제약 없음.
- (앞서 조사로 닫힘) 클립 이름 전수 · 메시 off 시점(`SetTarget()` 내부)

**남은 것 — 답 없으면 기본값으로 갑니다**

| # | 질문 | 기본값 |
|---|---|---|
| **Q2** | `IsWalking` 트리거가 쓰이는 곳을 못 찾았습니다. 죽은 파라미터인가요? | 미사용으로 보고 안 건드림 |
| **Q4** | 쿨다운을 **`lastUsed` 타임스탬프**로 할까요, **`AddType`/`RemoveType` 재등록**으로 할까요? | **타임스탬프.** 코드 절반, 디버깅 쉬움 |
| **Q5** | 근접 3종 클립이 전부 0~56 프레임입니다. 히트 프레임을 같게 봐도 되나요? | 훅 2종 동일, 어퍼만 별도 (육안 확인) |
| **Q6** | 차징 중 페이즈 임계를 또 넘으면? (33% 를 차징 중 통과) | 시퀀스 완주 → 끝나고 즉시 다음 차징 |
| **Q7** | Break 중 HP 0 이면? | 즉시 `Dead` |
| **Q8** | 근접 사거리 / 원거리 임계 거리 초기값은? | 근접 3.0 / 원거리 6.0 |
| **Q9** | Grab 이 빗나갔을 때 쿨은? | 그대로 10초 (창을 연 대가) |

### 감사에서 나온 질문 — 2026-08-06 팀장 답변

| # | 결정 |
|---|---|
| **A1 폭탄 물리** | ✅ **물리로 바꾼다.** (제 "수동 보간 유지" 권고는 틀렸다 — 서버 권한 + `NetworkTransform` 이라 클라가 물리를 재현할 필요가 없다) → §10.5.1 |
| **A2 장판 수명** | ✅ **자기 타이머로 소멸.** 중첩 성장은 **같은 타입끼리만**. 장판은 앞으로 늪/화염/독/번개 등 **여러 종류가 된다** → 타입 있는 일반 `AreaZone` 으로 설계 (§10.5.2) |
| **A3 피격 리액션** | ⏸ **회의 안건.** `Boss_23_getowned01/02` 는 **카운터 성공 시** 쓰는 방향으로 판단됨. 다만 실질 hit 리액션은 **23호의 행동을 취소하고 hit 애니를 재생**하는 것이라, **카운터에만 적용 vs 전체 적용**은 회의에서 정한다 |

**A3 에 대한 구현 관점 메모** — 카운터 성공 경로는 **이미 행동을 취소하고 `Groggy` 로 간다**(§8.3).
따라서 `getowned` 를 **`Groggy` 진입 직전 리액션**으로 끼우는 것은 구조 변경 없이 된다.
반면 **전체 적용**(일반 피격에도 행동 취소)은 "보스는 아무 공격에나 경직되지 않는다"는
기존 원칙과 정면으로 충돌하므로, 그쪽으로 가면 **공격 선택기·쿨다운 설계가 함께 바뀐다.**
→ 회의 전까지는 **카운터 전용**을 가정하고 짠다.

### 구현 순서 제안

```
0) BT 이탈 (§3)              — 이거 안 하면 아무것도 안 돈다
1) BossBase int 상태 + SetState 단일 지점 (§4.1)
2) Idle/Walk/근접 3종 + 선택기 + 폴백 (§4·§5)     ← 여기까지가 "멍청하지 않은 보스"
3) Grab 체인 (§4.2)
4) JumpAttack (§7)
5) 카운터 창 + 텔레그래프 (§8)   ← 은희 A 수령 후 마지막 한 줄
6) Dash + 캐리 (§4.2)            ← 은희 B 수령 후
7) 페이즈/송전기/차징/레이지 (§9)
8) Wells (§10)
```

2번까지 되면 **BT 를 끄고 플레이가 가능**하다. 거기서부터는 증분이다.
