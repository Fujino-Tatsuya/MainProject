# 인계 — 플레이어 쪽 회신: 인터럽트 식별자 + 돌진 밀기(Restrained)

> 받는 사람: **경석** · 보내는 사람: **은희** · 작성 2026-08-07
> 원 요청: `handoff-player-carry-socket.md` (2026-08-06, 기한 8/7 17:00)
> 브랜치: `feature/InterruptSkill-CarrySocket` (base `MainProject/development` `6dbc1c34a`)

## 한 장 요약

| # | 원 요청 | 결과 | 상태 |
|---|---|---|---|
| **A** | 인터럽트 식별자 | ✅ **완료.** 단 `AttackType` enum이 아니라 **`AttackInfo.isInterruptAttack` 플래그**로 갔습니다 | 컴파일 검증 완료 · **Play 미검증 · 미커밋** |
| **B** | 캐리 소켓 일반화 | ❌ **폐기.** 돌진이 넉백+기절 방식으로 바뀌면서 불필요해졌습니다 | 코드 원복 완료 |
| **B'** | 대체안 `Restrained` 상태 | ✅ **구현 완료** (개정 1판 반영 — `Push` 슈퍼아머 검사 + `bool` 반환) | 컴파일 검증 완료 · **Play 미검증 · 미커밋** |

> **2026-08-07 갱신** — 회신 개정 1판(질문 3·4 반전)을 반영해 구현을 마쳤습니다. §D 질문은 전부 해소됐습니다.
> 확정된 API는 §B-3 이며, 아래 코드 그대로 호출하시면 됩니다.

원 요청의 **전제 두 개가 실제 코드와 달랐습니다.** 그래서 A의 형태가 바뀌었습니다 — §A-1 참고.

---

# A. 인터럽트 식별자 — 완료

## A-1. 전제 정정 2건

**① 플레이어 스킬은 `BaseAttack`을 타지 않습니다.**

원 요청의 "단죄의 방패의 `BaseAttack.attackType`을 `Interrupt`로 지정"은 이 코드베이스에 없는 경로였습니다.
스킬은 `AttackInfo`를 **직접 만들어** `Hurtbox/Unit.ReceiveAttack`을 호출합니다
(`FirstMeleeMainSkill.cs:114`, `FirstMeleeUltimateSkill.cs:58`).

다만 **수용 기준의 취지는 그대로 성립합니다** — 서버 전용 경로이고, 오너→서버 직접 데미지 RPC가 아닙니다.
`PlayerSkillController`가 서버에서 승인·실행하고 판정도 서버에서만 돕니다.

**② 단죄의 방패가 아예 미구현이었습니다.**

`PlayerSkillController.interruptSkill`은 빈 슬롯이었고, `InterruptAttack` 앵커 노드는 컴포넌트 0개의 맨
Transform이었습니다. 우클릭은 `PlayerInterruptState`(전방 돌진 + Animator 트리거)로 빠졌고 **데미지가 0**이었습니다.

→ **이번에 신규 구현했습니다.** 다만 거동 스펙이 `Docs/design/` 어디에도 없어서(`character_garen.md`는 전부 TBD,
인계문이 링크한 `boss-fsm-design.md`는 존재하지 않음) **"짧은 전방 방패 강타"로 가정**했습니다.
수치는 전부 placeholder입니다. 기획 확정 시 SO에서만 조절하면 됩니다.

## A-2. 최종 형태 — enum이 아니라 플래그

```csharp
// Assets/1.Scripts/Unit/Weapon/BaseAttack.cs
public struct AttackInfo
{
    public bool isInterruptAttack;   // ← 기존 isGroggyAttack 개명
}
```

**`AttackType`에 값으로 넣지 않은 이유**: `AttackType`은 "어느 출처가 쐈나"이고 인터럽트는 그와 **직교한 능력**입니다.
enum에 넣으면 "Q 슬롯인데 인터럽트인 스킬"을 표현할 수 없고, 같은 사실을 두 곳에서 말하게 됩니다.

**플래그는 하나뿐이고, 소비 방식은 수신측이 정합니다.** 공격자는 "인터럽트를 썼다"까지만 알고,
그게 몇 대 쌓여야 하는지·정면이어야 하는지는 맞는 쪽 규칙입니다:

| 수신자 | 소비 방식 | 상태 |
|---|---|---|
| 몬스터 / 중간보스 | `maxGroggyCount`까지 누적 → 그로기 (`MonsterBase.cs:775`, `BossBase.cs:375`) | 기존 코드, 그대로 |
| **보스 No.23** | 카운터 창 + 정면 각도 → 그로기/Break | **경석 작성 예정** |

`Docs/design/level-system.md:70`의 분담(`SkillData{HasInterrupt}` vs `MonsterStateData{GroggyCount}`)과 같은 구조입니다.

**보스 쪽에서 쓰는 법:**

```csharp
public override void TakeDamage(AttackInfo attackInfo)   // 또는 ReceiveAttack override
{
    base.TakeDamage(attackInfo);

    if (!attackInfo.isInterruptAttack) return;
    // 여기부터 경석 영역: 카운터 창 열려있나 + 정면인가 → 그로기/Break
}
```

> 현재 `Enemy.TakeDamage`(`Enemy.cs:64`)는 `base` 호출만 하고 이 플래그를 안 읽습니다. 붙일 자리가 거기입니다.

## A-3. 곁들여 바뀐 것 — `AttackType` 축소

```csharp
public enum AttackType { None, Default, Skill }   // Q · E · R 제거
```

Q/E/R을 **구분해서 읽는 코드가 하나도 없었습니다**(비교하는 곳은 `Bomb` 하나, 나머지는 로그 문자열).

### 🔴 정수값 고정 — 값 추가는 반드시 끝에만

`BaseAttack.attackType`과 `Bomb.attackType`이 `[SerializeField]`라 프리팹에 정수로 박혀 있습니다.
`None=0`·`Default=1`은 기존 에셋 25곳과 맞춰 **고정된 값**입니다.

특히 **`Bomb.attackType = 1`(Default) = 폭탄은 플레이어 평타에만 반응합니다.**
값이 한 칸이라도 밀리면 이 기믹이 **에러도 경고도 없이** 뒤집힙니다(스킬에 터지고 평타에 안 터짐).
검증했고 지금은 보존돼 있습니다: `0`×22 / `1`×3.

### `BaseAttack`의 저작 토글은 삭제했습니다

기존 `[SerializeField] isGroggyAttack`은 **에셋 24곳 전부 `0`** = 아무도 안 켠 죽은 체크박스였습니다.
인터럽트를 켜는 주체는 스킬뿐이고 스킬은 `BaseAttack`을 안 타므로, 남기면 "체크했는데 왜 안 걸리지"가 됩니다.
**적 공격도 인터럽트를 걸어야 하면 말씀 주세요** — `[SerializeField] bool` + 생성자 인자 3줄로 되살립니다.

> ⚠️ 프리팹·SO에 `isGroggyAttack` YAML 키 24개가 고아로 남습니다(전부 값 0). Unity가 해당 에셋을
> 재직렬화할 때 자연 소멸합니다. 의미 손실은 없고 diff 노이즈로만 보입니다.

## A-4. 덤 — 중간보스 그로기가 살아났습니다

`GauntletBot`/`SpinnerBot`/`WallBot`에 `maxGroggyCount` 3/3/4가 저작돼 있었는데 **켜는 주체가 없어 죽은 경로**였습니다.
단죄의 방패가 그 첫 입력입니다. 의도한 밸런스가 아니면 알려주세요.

---

# B. 캐리 소켓 → 폐기, `Restrained`로 대체

## B-1. 왜 폐기했나

돌진이 **"소켓에 종속"에서 "넉백 + 기절"로 바뀌면서** `ICarrySocketProvider`의 존재 이유가 사라졌습니다.
소켓 제공자가 `GrabController` 하나뿐이라 `Kind` 기반 탐색 자체가 무의미해졌습니다.

## B-2. 그런데 넉백+기절만으로는 "밀고 가기"가 안 됩니다 ⚠️

**이게 가장 중요한 부분입니다.** 아래 §C-1 참고 — `Unit.Knockback`은 **임펄스 1회**라
돌진 시작에 한 번 튕기고 끝입니다. 보스가 계속 밀고 가는 그림이 안 나옵니다.

그래서 대체안으로 **`Restrained` 상태**를 제안합니다. 잡기와 밀기를 한 상태의 두 모드로 묶습니다.

## B-3. `Restrained` 설계 (확정, 착수 전)

```csharp
public enum RestraintMode : byte { Carry = 0, Push = 1 }
```

`PlayerActionState.Grabbed` → `Restrained` 개명(정수값 유지), `PlayerGrabbedState` → `PlayerRestrainedState`.
`Tick()`만 모드로 갈립니다:

| 모드 | 추종 대상 | 쓰는 쪽 |
|---|---|---|
| `Carry` | `GrabController.GrabSocket` (**지금과 완전히 동일**) | 잡기 — 회귀 0 |
| `Push` | `instigator.position + instigator.forward × offset` | 돌진 밀기 |

### Push는 소켓도, 방향/속도 동기화도 필요 없습니다

보스 **루트**는 `NetworkTransform`으로 복제되므로, 매 틱 "보스 앞 offset"을 계산하면 오너 클라에서 월드 위치가
저절로 맞습니다. 원 인계문이 걱정하신 **"돌진 소켓은 애니메이션으로 움직이지 않는 고정 자식이어야 한다"**는
제약이 원천적으로 사라집니다 — 소켓 GameObject를 만들 필요가 없습니다.

보스가 감속하든 가속하든 플레이어가 알아서 따라붙고, 넘길 파라미터는 `float offset` 하나입니다.

### 경석 쪽 호출 (구현된 최종 시그니처)

```csharp
public bool BeginRestrainedByInstigator(
    GameObject instigator, RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f);

public bool EndRestrainedByInstigator();
```

```csharp
// 돌진이 플레이어에 적중한 순간
bool pushed = player.BeginRestrainedByInstigator(gameObject, RestraintMode.Push, frontOffset);
player.ReceiveAttack(dashInfo, ctx);      // 데미지는 밀림 여부와 무관
if (pushed) _carried.Add(player);         // 실제로 밀린 대상만 추적

// 벽 앞에서 돌진 정지
foreach (var p in _carried)
{
    p.EndRestrainedByInstigator();
    p.StatusEffects.Apply(StatusEffectType.Stunned, wallStunDuration, NetworkObjectId);
}
```

**슈퍼아머 처리(개정 1판 §2 수용):** `Push`는 대상이 슈퍼아머면 진입을 거부하고 **`false`를 반환**합니다.
`Carry`에는 검사를 넣지 않았습니다 — 보스 Grab 체인 회귀 방지. 판정은 **서버 진입에서만** 하며,
오너는 서버 결정을 그대로 따릅니다(복제 지연으로 상태가 갈리는 것 방지).

**기존 `BeginGrabbedByInstigator(gameObject)`/`EndGrabbedByInstigator()`는 `Carry` 래퍼로 유지했습니다** —
`GrabController.cs:208`·`:228`은 **무수정**입니다.

**`followTarget` 널 허용도 유지했습니다** — `Carry`에서 소켓이 없으면 위치 추종만 건너뛰고
물리 위임·입력 차단은 그대로 갑니다("제자리에 붙잡힘" 성립).

### 알아두실 것

- **벽 판정은 여전히 보스 책임입니다.** 밀림 중 플레이어는 `detectCollisions = false`라 벽을 통과합니다.
  "플레이어가 kidnap 당했으면 벽에서 좀 떨어진 거리에서 정지"가 이 설계의 안전장치입니다.
  (콜라이더를 켜는 안도 검토했지만, 소품에 끼어 보스한테서 뒤처지는 사고가 더 커서 기존 동작을 유지합니다.)
- **밀림 중엔 `Stunned`가 필요 없습니다.** `CanMove`/`CanUseSkill`이 `Idle|Move`에서만 참이라
  (`PlayerStateController.cs:25,29`) `Restrained` 진입만으로 이동·스킬·공격·대시가 전부 막힙니다.
  기절은 **밀림이 끝난 뒤에만** 걸면 됩니다.
- **잡기와 밀기는 상태 하나를 공유합니다.** 밀림 중 그랩 시도는 조용히 거부되고(`CanReceiveGrab`),
  `End`는 시작 주체를 구분하지 않습니다. 보스 1기 기준 무해합니다.
- `GrabController`의 `IsGrabbed`/`GrabbedPlayer`는 **BT 블랙보드 변수명 문자열**이라 건드리지 않습니다.

---

# C. ⚠️ 넉백+기절 계통 함정 3개

새 돌진 설계가 이 계통에 의존하므로 먼저 확인해 주세요.

## C-1. 🔴 넉백은 임펄스 1회입니다 — 지속 밀기가 아닙니다

`PlayerKnockbackState.Enter`가 `AddForce(..., ForceMode.Impulse)`를 **한 번** 줍니다.

`AttackInfo`에 `knockbackStrength`·`knockbackDuration`·`staggerDuration`(지속 밀기 계약)이 있긴 한데
**`MonsterBase`만 소비하고 플레이어 수신 경로는 통째로 무시합니다** — `Unit.Knockback(dir, strength)`
시그니처에 duration 자체가 없습니다.

→ 그래서 §B-3의 `Restrained.Push`가 필요합니다. 굳이 넉백으로 하시겠다면 보스가 돌진 틱마다
`Knockback`을 재적용해야 하는데, 매 틱 임펄스라 속도가 누적돼 플레이어가 튀어나갑니다(감쇠 튜닝 필요).

## C-2. 🔴 슈퍼아머면 넉백이 통째로 무시됩니다

`Unit.cs:562` — `public void Knockback(...) { if (!IsServer) return; if (HasSuperArmor) return; ... }`

그런데 **Q 진격의 방패가 시전자 자신에게 SuperArmor를 겁니다**(`FirstMeleeMainSkill.cs:60`, 홀드 내내).

결과: **Q를 홀드 중인 플레이어는 보스 돌진에 안 밀립니다.** 반면 기절(`Stunned`)은 상태이상 경로라 안 막혀서
**"안 밀리는데 기절만 걸리는"** 조합이 나옵니다.

- "Q로 보스 돌진을 버틴다"가 의도면 그대로 두면 됩니다.
- 아니라면 기절도 슈퍼아머로 막아 대칭을 맞추거나, 돌진이 슈퍼아머를 관통하게 해야 합니다. **판단 부탁드립니다.**

`Restrained.Push`는 `Knockback`을 안 타므로 이 게이트에 안 걸립니다 — 원하시면 `Push` 진입에도
같은 슈퍼아머 검사를 넣겠습니다(기본은 **안 넣음**).

## C-3. 🟡 적용이 거부되는 두 경우

- **연출 잠금 중**: `PlayerEncounterLock.IsCinematicLocked`이면 상태이상 적용이 거부됩니다
  (`StatusEffectController.cs:152`). 보스 등장 연출 중엔 기절이 안 걸립니다 — 아마 의도대로일 겁니다.
- **오프라인/미스폰**: `CanWrite = IsSpawned && IsServer`라 에디터 솔로 실행에선 상태이상이 안 걸립니다.
  단독 Play로 테스트하실 때 "기절이 안 되는데?"의 원인이 이겁니다.

### 참고 — `Stunned`는 이미 원하시는 걸 전부 막습니다

`StatusEffectController.cs:47-54`의 차단 테이블이 `PlayerStateController.cs:25-29`와
`PlayerDashController.cs:231`에 물려 있어, `Stunned` 하나로 **이동·스킬·공격·대시**가 전부 차단됩니다.
서버 권한 + `NetworkList` 복제까지 됩니다. **플레이어 쪽에 새로 만들 것 없습니다.**

---

# D. 확인 필요 (답변 주시면 바로 착수합니다)

| # | 질문 | 제 기본값 |
|---|---|---|
| 1 | 밀림 종료 후 **기절은 보스가 거는 것**이 맞습니까? 지속시간이 보스 튜닝값이라 그쪽이 자연스럽습니다 | 보스가 건다 (플레이어 쪽 작업 0) |
| 2 | **돌진 중 보스가 방향을 틉니까?** 직선이면 §B-3 그대로 끝입니다. 곡선이면 플레이어가 보스 회전을 따라 옆으로 휘둘립니다(그게 자연스러울 수도) | 직선 |
| 3 | **C-2** — Q 슈퍼아머로 돌진을 버티는 게 의도입니까? | 현행 유지(버틴다) |
| 4 | `Restrained.Push`에도 슈퍼아머 검사를 넣습니까? | 넣지 않음 |
| 5 | 적 공격도 인터럽트를 걸어야 합니까? (A-3의 저작 토글 부활 여부) | 필요 없음 |

---

# E. 현재 검증 상태

| 항목 | 상태 |
|---|---|
| C# 컴파일 | ✅ 배치모드 **0 에러** (경고는 전부 기존 파일) |
| 직렬화 `attackType` 값 보존 | ✅ `0`×22 / `1`×3, `Bomb.attackType=1` 유지 |
| 단죄의 방패 프리팹 배선 | ✅ `Paladin` · `Player` · `TempPlayer_Armature` (재실행 안전 도구: `Tools/Player/Authoring/Wire Interrupt Skill`) |
| 우클릭 → 단죄의 방패 발동 | ⏳ **Play 미검증** |
| 인터럽트 누적 → 중간보스 그로기 | ⏳ **Play 미검증** |
| 잡기 회귀 (Grab → Hold → Throw) | ⏳ **Play 미검증** |
| `Restrained` 구현 | ✅ 컴파일 0 에러 / 0 경고 |
| `Push` 추종 · 슈퍼아머 거부 | ⏳ **Play 미검증** |
| MPPM 2인 | ⏳ 미검증 |
| 커밋 | ⛔ **미커밋** (워킹트리에만 존재) |

`ICarrySocketProvider` 관련 코드는 **원복 완료**했습니다 — 워킹트리에 없습니다.

머지 순서는 제안하신 대로 **저희가 `development`에 먼저** 올리겠습니다.
`MonsterBase.cs` 충돌은 §5 표의 해소안(둘 다 살리기, 조기 반환이 먼저) 그대로 부탁드립니다.
