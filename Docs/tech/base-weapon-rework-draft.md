# BaseWeapon Rework Draft

Status: draft only. Do not treat this as an approved implementation plan.

## Goal

Separate attack detection from hit resolution.

Current weapon code mixes several concerns:

- when a hit is detected
- how a hit is detected
- how a Collider becomes a Unit
- how damage, knockback, groggy, and future effects are applied

The rework below proposes a cleaner hierarchy. It is intentionally larger than the current minimal change and should not be applied without team agreement because it can affect prefab serialization and existing boss attacks.

## Naming Direction

Prefer `Attack` over `Weapon` for gameplay logic.

Reason:

- `Weapon` sounds like an equipment item or held object.
- `Attack` better covers melee, grab, area, projectile, explosion, and skill effects.
- Existing project names already include `ColliderBasicAttack`, `KnockbackAttack`, and `BaseAttackChoice`.

Potential final names:

```text
BaseAttack
TriggerAttack
OverlapAttack
```

Conservative migration can keep `BaseWeapon` first, then rename later if the team agrees.

## Proposed Responsibility Split

```text
BaseAttack
- damage
- targetLayer
- attackType
- isGroggyAttack
- server authority guard
- Collider -> Unit resolution
- Unit damage application

TriggerAttack
- OnTriggerEnter/Stay/Exit detection
- TriggerMode and stay interval
- calls BaseAttack.TryResolveHit

OverlapAttack
- ColliderInfo-based OverlapBox/Sphere/Capsule detection
- animation-event or skill-timing entry point
- calls BaseAttack.TryResolveHit
```

Detection can vary by attack type. Resolution should be shared.

## BaseAttack Sketch

```csharp
using Unity.Netcode;
using UnityEngine;

public enum AttackType
{
    None,
    Default,
    Q,
    E,
    R
}

public abstract class BaseAttack : MonoBehaviour
{
    [SerializeField] protected int damage = 0;
    [SerializeField] protected bool isGroggyAttack = false;
    [SerializeField] protected LayerMask targetLayer;
    [SerializeField] protected AttackType attackType = AttackType.None;

    public int Damage => damage;
    public bool IsGroggyAttack => isGroggyAttack;
    public AttackType AttackType => attackType;

    protected bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    protected bool TryResolveHit(Collider hit, int? overrideDamage = null)
    {
        if (!IsServer || hit == null)
            return false;

        GameObject target = hit.transform.root.gameObject;
        if ((targetLayer.value & (1 << target.layer)) == 0)
            return false;

        Unit unit = target.GetComponent<Unit>();
        if (unit == null)
        {
            Debug.LogError($"Target {target.name} has no Unit component.", this);
            return false;
        }

        return TryResolveHit(unit, overrideDamage);
    }

    protected bool TryResolveHit(Unit unit, int? overrideDamage = null)
    {
        if (!IsServer || unit == null)
            return false;

        unit.TakeDamage(overrideDamage ?? damage);
        return true;
    }
}
```

## TriggerAttack Sketch

```csharp
using UnityEngine;

public enum TriggerMode
{
    OnlyEnter,
    OnlyStay,
    OnlyExit
}

public class TriggerAttack : BaseAttack
{
    [SerializeField] private TriggerMode triggerMode;
    [SerializeField] private float stayTime;

    private float stayTimer;

    private void OnTriggerEnter(Collider other)
    {
        if (triggerMode != TriggerMode.OnlyEnter)
            return;

        stayTimer = 0f;
        TryResolveHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (triggerMode != TriggerMode.OnlyStay)
            return;

        stayTimer += Time.deltaTime;
        if (stayTimer < stayTime)
            return;

        stayTimer = 0f;
        TryResolveHit(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (triggerMode != TriggerMode.OnlyExit)
            return;

        TryResolveHit(other);
    }
}
```

## OverlapAttack Sketch

```csharp
using UnityEngine;

public class OverlapAttack : BaseAttack
{
    [SerializeField] private ColliderInfo colliderInfo;
    [SerializeField] private int maxHitCount = 16;

    private Collider[] results;

    private void Awake()
    {
        results = new Collider[Mathf.Max(1, maxHitCount)];
    }

    public void Hit()
    {
        if (!IsServer || colliderInfo == null)
            return;

        int hitCount = Overlap();
        for (int i = 0; i < hitCount; i++)
            TryResolveHit(results[i]);
    }

    private int Overlap()
    {
        switch (colliderInfo.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo box = default;
                colliderInfo.GetBoxColliderInfo(ref box);
                return Physics.OverlapBoxNonAlloc(
                    box.center,
                    box.halfExtents,
                    results,
                    box.orientation,
                    targetLayer,
                    QueryTriggerInteraction.Ignore);

            case OverlapCollider.Sphere:
                SphereColliderInfo sphere = default;
                colliderInfo.GetSphereColliderInfo(ref sphere);
                return Physics.OverlapSphereNonAlloc(
                    sphere.center,
                    sphere.radius,
                    results,
                    targetLayer,
                    QueryTriggerInteraction.Ignore);

            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsule = default;
                colliderInfo.GetCapsuleColliderInfo(ref capsule);
                return Physics.OverlapCapsuleNonAlloc(
                    capsule.point0,
                    capsule.point1,
                    capsule.radius,
                    results,
                    targetLayer,
                    QueryTriggerInteraction.Ignore);

            default:
                return 0;
        }
    }
}
```

## Migration Risk

High-risk changes:

- Renaming `BaseWeapon` to `BaseAttack`.
- Renaming serialized fields such as `targetLayer`.
- Making `BaseWeapon` abstract while prefabs still reference it directly.
- Removing `AttackType`.
- Replacing `ColliderBasicAttack` in prefabs without a migration pass.

Low-risk intermediate step:

1. Keep `BaseWeapon` name.
2. Keep all serialized fields unchanged.
3. Add only `TryResolveHit(Collider)` and `TryResolveHit(Unit)`.
4. Add `OverlapAttack : BaseWeapon`.
5. Later migrate `ColliderBasicAttack` internals to call `TryResolveHit`.
6. Consider renaming only after prefab usage is audited.

## Recommendation

Use the low-risk intermediate step first.

The full `BaseAttack` rename should wait until:

- existing prefabs are audited,
- boss attack scripts are checked,
- animation-event entry points are agreed,
- and field migration can be done intentionally.

---

## Review (2026-07-06)

방향(탐지/해석 분리, 점진적 마이그레이션)에는 동의. 다만 아래 사항을 보강하기 전에는 이 문서 그대로 팀 합의를 받으면 안 됨.

### 문서가 현실과 어긋남

문서는 "draft only, do not apply"라고 하지만, **low-risk intermediate step의 1~4단계는 이미 워킹 트리에 적용됨.** `BaseWeapon.cs`에 `TryResolveHit` 2종이 들어갔고, `OverlapAttack.cs`도 스케치와 거의 동일하게 존재. "제안"과 "이미 한 것"이 구분되지 않아 처음 읽는 팀원이 오해할 수 있음. → "현재 상태" 섹션을 분리해 1~4단계 완료 / 5~6단계 미착수를 명시할 것.

### 설계상 실질 문제

**1. `transform.root` 기반 해석은 마이그레이션 시 동작이 바뀌고, 그 자체로도 취약함.**

- 현재 `ColliderBasicAttack.TakeDamage`는 **부딪힌 콜라이더 오브젝트 자체**의 레이어를 검사하고 거기서 `GetComponent<Unit>`을 함. 반면 `TryResolveHit(Collider)`는 **root**의 레이어를 검사하고 root에서 Unit을 찾음. 자식 히트박스가 본체와 다른 레이어인 유닛이 하나라도 있으면 5단계 마이그레이션 시 히트 판정이 조용히 바뀜. 5단계는 low-risk가 아니라 **동작 변경**임.
- `transform.root`는 유닛이 무언가에 부모 지정되는 순간 깨짐(그랩 메카닉 존재). `GetComponentInParent<Unit>()`가 더 안전한 대안인데 논의가 없음.
- OverlapAttack 경로는 오버랩 쿼리에서 이미 `targetLayer`로 콜라이더를 걸러놓고 `TryResolveHit`에서 다시 **root의** 레이어를 검사함. 콜라이더는 타겟 레이어인데 root가 아니면 히트가 소리 없이 사라짐.

**2. "Resolution should be shared"라면서 `KnockbackAttack`을 커버 못 함.**

`TryResolveHit`는 데미지만 적용하고 `bool`을 반환. `KnockbackAttack`은 해석된 `Unit`을 받아 넉백까지 적용해야 하는데 현재 API로는 불가능. 기존 `BaseWeapon` 상속자 3개 중 1개가 새 구조에 안 들어맞는데 책임 분리 표에서 아예 언급되지 않음. `TryGetTargetUnit(Collider, out Unit)` 같은 **해석(resolution)과 적용(application)을 분리한 API**가 문서의 원래 목표에 더 부합. 지금 스케치는 그 둘을 다시 한 메서드에 묶어놓음.

**3. 같은 유닛 중복 타격 방지가 없음.**

`OverlapAttack.Hit()`은 한 유닛이 콜라이더를 여러 개 갖고 있으면 그 수만큼 `TakeDamage`가 호출됨. 애니메이션 이벤트 기반 공격에서 흔한 버그인데 hit-dedup(예: `HashSet<Unit>`)이 스케치에도 현재 코드에도 없음.

**4. TriggerAttack 스케치는 기존 버그를 계승하고 새 버그도 하나 추가함.**

- 계승: `stayTimer` 하나를 모든 콜라이더가 공유. 트리거 안에 타겟이 2명이면 `OnTriggerStay`가 프레임당 2번 불려 타이머가 2배속으로 참.
- 신규: 기존 코드는 `OnTriggerEnter`에서 모드와 무관하게 `_stayTimer = 0f`를 하는데, 스케치는 리셋을 `OnlyEnter` 가드 뒤로 옮김. `OnlyStay` 모드에서 새 대상이 들어와도 타이머가 리셋되지 않는 동작 변경이 몰래 섞임.
- `TriggerMode` enum이 현재 `ColilderBasicAttack.cs` 파일 안에 선언돼 있어, TriggerAttack을 추가하는 순간 중복 정의 컴파일 에러 발생. 마이그레이션 순서에 이 정리가 빠져 있음.

**5. `QueryTriggerInteraction.Ignore` 하드코딩.**

유닛 허트박스가 트리거 콜라이더라면 OverlapAttack은 아무것도 못 맞춤. 허트박스 구성이 확정인지 먼저 확인해야 할 전제인데 언급이 없음.

### 리스크 평가가 부정확한 부분

- **필드 리네임 과대평가**: `[FormerlySerializedAs]`를 쓰면 직렬화 필드 리네임은 안전하게 가능. 클래스 리네임도 .meta(GUID)를 보존하면 프리팹 참조가 유지됨. 문서가 이 표준 완화책을 언급하지 않아 리네임이 실제보다 위험해 보임. (반대로 "BaseWeapon을 abstract로 만들면서 프리팹이 직접 참조" 리스크는 정확함.)
- **5단계 과소평가**: 위 1번 때문에 "internals를 TryResolveHit 호출로 교체"는 low-risk 목록에 있으면 안 됨.
- 덤: 파일명이 `ColilderBasicAttack.cs`(오타)인데 클래스는 `ColliderBasicAttack`. 리워크가 이걸 정리할 기회인데 문서에 없음.

### 사소한 것

- `AttackType`의 `Q/E/R`은 플레이어 입력 중심 네이밍인데 보스 공격도 상속하는 베이스 클래스에 있음. "Weapon → Attack" 네이밍을 고민할 정도면 이것도 같이 다뤄야 일관적.
- `IsServer`가 `NetworkManager.Singleton` 의존이라 네트워크 없이 도는 테스트 씬에서는 모든 공격이 조용히 무시됨. 전제로 한 줄 적어둘 가치가 있음.

### 결론

합의 전 보강 필요 사항:

1. 현재 적용 상태 반영 (1~4단계 완료 명시)
2. root vs 직접 오브젝트 해석 정책 결정 (`GetComponentInParent` 검토 포함)
3. `KnockbackAttack`까지 커버하는 해석/적용 분리 API
4. 같은 유닛 중복 타격 방지
5. `FormerlySerializedAs` 기반의 현실적인 리네임 계획
