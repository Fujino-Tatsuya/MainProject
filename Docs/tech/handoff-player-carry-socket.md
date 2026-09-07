# 인계 — 플레이어 쪽 요청 2건 (보스 FSM 재작성 전제)

> 받는 사람: **은희** · 보내는 사람: **경석** · 작성 2026-08-06 · **기한 2026-08-07(금) 17:00**
> 배경 설계: [boss-fsm-design.md](boss-fsm-design.md) · [PLAN-boss-fsm.md](../../PLAN-boss-fsm.md)
>
> 보스를 BT → 코드 FSM 으로 재작성하면서 **플레이어 쪽에 두 가지가 필요**해졌습니다.
> 둘 다 **기존 동작을 바꾸지 않는 순수 추가**이고, 보스 쪽 작업은 제가 합니다.

## 한 장 요약

| # | 요청 | 왜 | 크기 |
|---|---|---|---|
| **A** | `AttackInfo` 로 **"이 히트가 인터럽트 스킬인가"** 를 서버가 알 수 있게 | 보스 카운터(그로기) 판정이 이걸로만 난다 | enum 값 1개 + 대입 |
| **B** | `PlayerGrabbedState` 의 **따라갈 소켓을 지정 가능하게** | 보스 돌진의 "밀고 가기"가 잡기와 **같은 문제**라 재사용하고 싶다 | 인자 1개 전파 + 인터페이스 1개 |

---

# 요청 A — 인터럽트 스킬 식별자

## 지금 상태

`AttackType` 에 **우클릭 슬롯에 해당하는 값이 없습니다.**

```csharp
// Assets/1.Scripts/Unit/Weapon/BaseAttack.cs:4
public enum AttackType { None, Default, Q, E, R }
```

슬롯은 4개인데(`PlayerSkillSlot`: `Main`(Q) / `Sub`(E) / **`Interrupt`(우클릭)** / `Ultimate`(R))
`AttackType` 에는 Q·E·R 만 있습니다. 그래서 보스가 `TakeDamage(AttackInfo)` 를 받아도
**그게 단죄의 방패였는지 판별할 수단이 없습니다.**

## 필요한 것

보스가 **서버에서** "이 히트 = 인터럽트 스킬"을 판별할 수 있으면 형태는 무엇이든 좋습니다. 가장 작은 안:

```csharp
public enum AttackType { None, Default, Q, E, R, Interrupt }   // ← 끝에 추가
```

그리고 단죄의 방패의 `BaseAttack.attackType` 을 `Interrupt` 로 지정.
(`AttackInfo` 에 `bool isInterruptSkill` 을 새로 다는 것도 괜찮습니다 — 편한 쪽으로.)

## ⚠️ 주의 — enum 은 반드시 **끝에** 추가

`AttackType` 은 `BaseAttack` 의 `[SerializeField]` 라 **프리팹·SO 에 정수로 직렬화돼 있습니다.**
중간에 삽입하면 뒤 값이 전부 한 칸씩 밀려 **기존 프리팹의 공격 타입이 조용히 바뀝니다.**
(에러도 경고도 안 납니다. 이 프로젝트에서 이미 유사 사고가 있었습니다.)

## 수용 기준

- 단죄의 방패로 적을 때리면, 맞은 쪽 서버 코드에서 `attackInfo` 만 보고 인터럽트 여부를 알 수 있다.
- 히트는 기존 **`BaseAttack → ReceiveAttack` 서버 경로**로 들어온다 (오너→서버 직접 데미지 RPC 아님).
- 기존 Q/E/R 공격의 `attackType` 값이 바뀌지 않는다.

## 보스 쪽에서 제가 하는 일 (은희 님이 안 하셔도 되는 것)

카운터 창 관리, 정면 각도 판정, 그로기/Break 전이, 시각 피드백 — 전부 보스 쪽입니다.
플레이어는 **"인터럽트 스킬이다"** 라는 사실만 실어 보내주시면 됩니다.

> 참고: 정면 판정은 나중에 **헤드어택**이 들어오면 그쪽으로 교체할 예정이라,
> 보스 쪽에서 교체 가능한 지점으로 분리해 둡니다. 지금 헤드어택을 신경 쓰실 필요 없습니다.

---

# 요청 B — 캐리 소켓 일반화

## 왜 이걸 부탁드리는가

보스 `DashAttack` 이 이렇게 바뀝니다:

```
돌진 → 경로상 플레이어 적중 → 보스 앞에 붙여서 계속 민다 → 벽/맵 끝 도달 → 스턴
```

이건 **잡기와 똑같은 문제**입니다 — *서버가 몇 초간 플레이어 위치를 강제해야 하는데
플레이어 이동은 오너 권한*. 그리고 은희 님이 만드신 `PlayerGrabbedState` 가
**이미 이 문제를 다 풀어놨습니다**:

- `DelegatePhysicsAndCollisionToInstigator()` — 물리 위임 (`PlayerStateController.cs:733`)
- `Tick()` — 소켓에 슬레이브 (`:718`)
- `RestorePlayerPhysicsAndCollision()` — 원상복구 (`:748`)
- 서버 → 오너 RPC 왕복 (`Player.cs:257~311`)

**같은 문제를 보스 쪽에 따로 한 벌 더 만들면 반드시 어긋납니다.** 그래서 재사용하고 싶은데,
딱 한 줄이 막고 있습니다.

## 막고 있는 지점

```csharp
// PlayerStateController.cs:700-703  (PlayerGrabbedState.Enter)
GrabController grabController = instigator != null
    ? instigator.GetComponentInChildren<GrabController>()
    : null;
followTarget = grabController != null ? grabController.GrabSocket : null;
```

**따라갈 소켓을 `GrabController` 라는 구체 타입으로 찾습니다.** 돌진은 다른 소켓
(보스 정면)을 써야 하는데 지정할 방법이 없습니다.

## 제안 — 소켓 종류를 인자로 넘긴다

`GetComponentInChildren<ICarrySocketProvider>()` **한 줄 교체로는 안 됩니다.**
보스에 잡기 소켓과 돌진 소켓이 **둘 다** 붙으므로, 타입으로만 찾으면 **먼저 걸리는 쪽이 이깁니다.**
그래서 **종류를 명시**해야 합니다.

```csharp
public enum CarrySocketKind : byte { Grab = 0, Dash = 1 }

public interface ICarrySocketProvider
{
    CarrySocketKind Kind { get; }
    Transform CarrySocket { get; }
}
```

`PlayerGrabbedState.Enter` 는 종류로 고릅니다:

```csharp
followTarget = null;
if (instigator != null)
{
    foreach (var p in instigator.GetComponentsInChildren<ICarrySocketProvider>())
        if (p.Kind == kind) { followTarget = p.CarrySocket; break; }
}
```

### 전파해야 하는 경로 (인자 하나가 끝까지 따라갑니다)

`Transform` 은 네트워크로 못 보내므로, **종류(byte)** 를 RPC 에 실어야 합니다.

| 파일:줄 | 지금 | 바뀌는 것 |
|---|---|---|
| `Player.cs:257` | `BeginGrabbedByInstigator(GameObject)` | `+ CarrySocketKind kind = CarrySocketKind.Grab` |
| `Player.cs:273` | `BeginGrabbedClientRpc(ref, params)` | `+ kind` |
| `Player.cs:288` | `BeginGrabbedClientRpc(...)` 본문 | `ApplyGrabbedFromServer(go, kind)` |
| `PlayerStateController.cs:147` | `ApplyGrabbedFromServer(GameObject)` | `+ kind` |
| `PlayerStateController.cs:161` | `BeginGrabbed(GameObject)` | `+ kind` |
| `PlayerStateController.cs:688` | `PlayerGrabbedState(ctx, instigator)` | `+ kind` 필드 |
| `PlayerStateController.cs:700` | `GetComponentInChildren<GrabController>()` | 위 루프로 교체 |
| `GrabController.cs:232` | `public Transform GrabSocket => grabSocket;` | `ICarrySocketProvider` 구현 (`Kind => Grab`) |

**전부 기본값 `Grab` 으로 두면 기존 호출부는 그대로 컴파일되고 동작도 동일합니다.**
현재 `BeginGrabbedByInstigator` 호출부는 `GrabController.cs:208` **한 곳뿐**입니다.

## ⚠️ 알아두실 함정 3개

1. **`detectCollisions = false` 라 캐리 중 플레이어는 벽을 통과합니다** (`:745`).
   그래서 **벽 판정은 보스가 합니다** — 플레이어 쪽에서 신경 쓰실 것 없습니다.
   다만 소켓 위치가 보스 앞으로 너무 멀면 플레이어가 벽에 박힌 채 풀릴 수 있어서,
   **소켓 배치는 제가 조절**하겠습니다.

2. **소켓을 따라가는 주체는 오너 클라입니다** (`IsMovementAuthority => !IsNetworkActive || IsOwner`,
   `Player.cs:324`). 즉 소켓의 **월드 위치가 클라에서도 맞아야** 합니다.
   보스 본체는 `NetworkTransform` 으로 복제되므로, 소켓이 **보스의 정적 자식**이면 문제없습니다.
   → **돌진 소켓은 애니메이션으로 움직이지 않는 고정 자식**으로 두겠습니다. (제 쪽 책임)

3. **`Kind` 를 enum 으로 쓰신다면 이것도 끝에만 추가**해 주세요 — 요청 A 와 같은 이유입니다.

## 수용 기준

- 기존 잡기(`Grab` → `Hold` → `Throw`)가 **지금과 100% 동일하게** 동작한다 (회귀 없음).
- 보스가 `BeginGrabbedByInstigator(boss, CarrySocketKind.Dash)` 를 호출하면
  플레이어가 **돌진 소켓**을 따라간다.
- `EndGrabbedByInstigator()` 로 풀면 물리·콜라이더가 원상복구된다 (기존과 동일).
- MPPM 2인에서 호스트/클라 양쪽 다 확인.

---

## 제 일정 (참고)

- **~8/7(금) 17:00**: 보스 FSM 상세 구성 확정 (이 두 건 없이 진행 가능한 부분)
- **주말**: 전체 구현 + 테스트

그래서 두 건 다 **금요일 17시 전**에 주시면 주말 구현에 그대로 들어갑니다.
형태를 바꾸고 싶으시면 편하신 쪽으로 하셔도 됩니다 — **보스가 서버에서 판별만 되면 되고,
소켓만 지정할 수 있으면 됩니다.** 위 제안은 "이렇게 하면 제일 작다"는 안일 뿐입니다.

막히거나 더 큰 변경이 필요해 보이면 알려주세요. 보스 쪽에서 흡수할 수 있는 부분이 있을 수 있습니다.
