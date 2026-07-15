# Attack Detection / Resolution Rework Draft

Status: draft. This document reflects the current working-tree state, but the next migration steps are not approved yet.

## Goal

Separate attack detection from hit resolution.

Current attack code mixes several concerns:

- when a hit is detected
- how a hit is detected
- how a `Collider` becomes a `Unit`
- how damage, knockback, groggy, and future effects are applied

The intended direction is:

```text
Detection: trigger, overlap, projectile, animation event, boss gimmick
Resolution: Collider/GameObject -> valid Unit target
Application: damage, knockback, groggy, cancel, status effect
```

Detection can vary by attack type. Target resolution should be shared. Effect application should stay overridable because attacks such as knockback need more than damage.

## Current Code State

This is no longer a pure proposal. Some intermediate work is already in the working tree.

Implemented:

1. `BaseAttack` exists at `Assets/1.Scripts/Unit/Weapon/BaseAttack.cs`.
2. `BaseAttack` owns `damage`, `targetLayer`, `attackType`, `isGroggyAttack`, server authority checks, and setters for runtime snapshots.
3. `BaseAttack.TryResolveHit(Collider)` and `BaseAttack.TryResolveHit(Unit)` exist.
4. `OverlapAttack : BaseAttack` exists at `Assets/1.Scripts/Unit/Weapon/OverlapAttack.cs`.

Not migrated:

1. `ColliderBasicAttack` still performs its own direct `GameObject -> Unit -> TakeDamage` logic.
2. `KnockbackAttack` still performs its own `root -> Unit -> TakeDamage -> Knockback` logic.
3. `TriggerAttack` does not exist yet.
4. `ColliderBasicAttack` still lives in `ColilderBasicAttack.cs` with the filename typo.

Important current behavior:

- `BaseAttack.IsServer` allows local non-network test scenes by returning true when `NetworkManager.Singleton` is null or not listening.
- `BaseAttack.TryResolveHit(Collider)` currently resolves the target through `hit.transform.root.gameObject`.
- `ColliderBasicAttack.TakeDamage(GameObject)` currently checks the collided object itself, not the root.
- `KnockbackAttack.ApplyKnockbackAttack(GameObject)` currently checks the collided object's root.
- `OverlapAttack` filters the physics query by `targetLayer`, then `TryResolveHit(Collider)` checks `targetLayer` again on the root object.
- `OverlapAttack` uses `QueryTriggerInteraction.Ignore`.
- `OverlapAttack.Hit()` does not deduplicate multiple colliders belonging to the same unit.

## Naming Direction

Prefer `Attack` over `Weapon` for gameplay logic.

Reason:

- `Weapon` sounds like an equipment item or held object.
- `Attack` better covers melee, grab, area, projectile, explosion, and skill effects.
- Existing project names already include `ColliderBasicAttack`, `KnockbackAttack`, `BaseAttackChoice`, and now `BaseAttack`.

Potential final names:

```text
BaseAttack
TriggerAttack
OverlapAttack
ProjectileAttack
```

`AttackType` currently uses input-like values (`Default`, `Q`, `E`, `R`). That is acceptable for player skills but awkward for boss attacks. Before expanding the base class further, decide whether `AttackType` is:

- a player input slot,
- a damage/effect category,
- a telemetry/debug label,
- or something that should move out of `BaseAttack`.

## Proposed Responsibility Split

```text
BaseAttack
- damage
- targetLayer
- attackType
- isGroggyAttack
- server authority guard
- shared target resolution helpers
- default damage application

TriggerAttack / ColliderBasicAttack
- OnTriggerEnter/Stay/Exit detection
- TriggerMode and stay interval
- calls shared resolution helpers
- applies damage or custom effects

OverlapAttack
- ColliderInfo-based OverlapBox/Sphere/Capsule detection
- animation-event or skill-timing entry point
- deduplicates Units before applying effects
- calls shared resolution helpers
```

Resolution and application should be separate. `TryResolveHit(Collider)` is too narrow because it immediately applies damage and cannot support `KnockbackAttack` cleanly.

## Target Resolution Policy

This needs a team decision before migrating existing attacks.

Options:

1. Direct object policy: check `hit.gameObject.layer`, then `hit.GetComponent<Unit>()`.
   - Matches current `ColliderBasicAttack`.
   - Fails if hurtboxes are child colliders without `Unit`.

2. Root policy: check `hit.transform.root.gameObject.layer`, then root `GetComponent<Unit>()`.
   - Matches current `BaseAttack.TryResolveHit` and `KnockbackAttack`.
   - Can break if a unit is temporarily parented under another object, such as grab mechanics.
   - Can silently reject child hurtboxes when the child collider is on `targetLayer` but the root is not.

3. Parent unit policy: check the hit collider's layer for query eligibility, then resolve with `GetComponentInParent<Unit>()`.
   - Usually safer for child hurtboxes.
   - Needs a clear rule for which object's layer is authoritative: the collider, the resolved unit, or both.

Recommended policy:

- Physics queries and trigger entry should use the hit collider's layer as the first filter.
- Unit resolution should use `GetComponentInParent<Unit>()`.
- If the resolved `Unit` exists but the collider layer is not in `targetLayer`, reject the hit.
- Do not require the unit root layer to match unless the prefab convention explicitly says root layers are authoritative.

## Revised BaseAttack Sketch

```csharp
public class BaseAttack : MonoBehaviour
{
    [SerializeField] protected int damage = 0;
    [SerializeField] protected bool isGroggyAttack = false;
    [SerializeField] protected LayerMask targetLayer;
    [SerializeField] protected AttackType attackType = AttackType.None;

    protected bool IsServer =>
        NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening ||
        NetworkManager.Singleton.IsServer;

    protected bool TryGetTargetUnit(Collider hit, out Unit unit)
    {
        unit = null;

        if (!IsServer || hit == null)
            return false;

        if ((targetLayer.value & (1 << hit.gameObject.layer)) == 0)
            return false;

        unit = hit.GetComponentInParent<Unit>();
        if (unit == null)
        {
            Debug.LogError($"Target {hit.name} has no Unit in parent hierarchy.", this);
            return false;
        }

        return true;
    }

    protected bool ApplyDamage(Unit unit, int? overrideDamage = null)
    {
        if (!IsServer || unit == null)
            return false;

        unit.TakeDamage(overrideDamage ?? damage);
        return true;
    }

    protected bool TryApplyDamage(Collider hit, int? overrideDamage = null)
    {
        return TryGetTargetUnit(hit, out Unit unit) && ApplyDamage(unit, overrideDamage);
    }
}
```

Notes:

- Keep `TryApplyDamage` only as a convenience method. Custom effects should call `TryGetTargetUnit` and then apply their own behavior.
- If the team chooses root-authoritative targeting, update this sketch and document the prefab rule explicitly.

## OverlapAttack Requirements

`OverlapAttack` should not ship as-is.

Before using it for gameplay attacks:

1. Add same-unit hit deduplication.
2. Decide whether trigger hurtboxes must be hittable. If yes, expose `QueryTriggerInteraction` as a serialized field or use `Collide`.
3. Use the shared `TryGetTargetUnit` API so layer policy is identical across trigger and overlap attacks.

Sketch:

```csharp
private readonly HashSet<Unit> hitUnits = new();

public void Hit()
{
    if (!IsServer || colliderInfo == null)
        return;

    hitUnits.Clear();

    int hitCount = Overlap();
    for (int i = 0; i < hitCount; i++)
    {
        if (!TryGetTargetUnit(results[i], out Unit unit))
            continue;

        if (!hitUnits.Add(unit))
            continue;

        ApplyDamage(unit);
    }
}
```

## TriggerAttack / ColliderBasicAttack Requirements

Do not add a separate `TriggerAttack` until the `TriggerMode` migration is planned.

Current issues to preserve or fix intentionally:

- `TriggerMode` is declared in `ColilderBasicAttack.cs`. Adding another enum with the same name will cause a compile error.
- `ColliderBasicAttack` has one `_stayTimer` shared across all colliders. Multiple targets in the trigger can advance the timer faster than expected.
- Current `OnAttackTriggerEnter` resets `_stayTimer` before checking the trigger mode. A new implementation should not accidentally change `OnlyStay` behavior unless that change is intentional.

Recommended next step:

1. Keep `ColliderBasicAttack` name for serialized prefab compatibility.
2. Move `TriggerMode` to its own file only if needed.
3. Replace the private `TakeDamage(GameObject)` with shared resolution only after the target resolution policy is approved.
4. Consider per-target stay timers if `OnlyStay` needs to support multiple simultaneous targets.

## KnockbackAttack Requirement

`KnockbackAttack` is the reason resolution and application must be separate.

Target shape:

```csharp
public void ApplyKnockbackAttack(Collider hit)
{
    if (!TryGetTargetUnit(hit, out Unit unit))
        return;

    ApplyDamage(unit);
    unit.Knockback(GetDirection(unit.gameObject), knockbackStrength);
}
```

If the caller only has a `GameObject`, either pass its collider where possible or add a separate overload with the same target policy. Do not force knockback through `TryApplyDamage`, because it needs the resolved `Unit`.

## Migration Risk

High-risk changes:

- Making `BaseAttack` abstract while prefabs still reference it directly.
- Replacing `ColliderBasicAttack` internals before the target resolution policy is approved.
- Using root-based resolution for child hurtbox prefabs without an audit.
- Shipping `OverlapAttack` without same-unit deduplication.
- Hardcoding `QueryTriggerInteraction.Ignore` if hurtboxes use trigger colliders.

Manageable with standard Unity migration tools:

- Serialized field renames can use `[FormerlySerializedAs]`.
- Script/class file renames can be safe if `.meta` GUIDs are preserved and prefab references are audited.
- The `ColilderBasicAttack.cs` filename typo can be fixed later by renaming the file while preserving the `.meta` file.

## Recommended Migration Plan

Do this in small PRs:

1. Document and approve the target resolution policy.
2. Add `TryGetTargetUnit(Collider, out Unit)` and `ApplyDamage(Unit, int?)` to `BaseAttack`.
3. Keep existing `TryResolveHit` temporarily as a compatibility wrapper, or rename it to `TryApplyDamage` in the same PR if all callers are updated.
4. Update `OverlapAttack` to deduplicate `Unit` hits and make trigger-query behavior explicit.
5. Update `KnockbackAttack` to use shared resolution.
6. Update `ColliderBasicAttack` to use shared resolution only after confirming the direct-object to parent-unit behavior change is acceptable.
7. Fix `ColilderBasicAttack.cs` filename typo with `.meta` preservation.
8. Revisit `AttackType` naming and scope after boss attacks and player skills both have real callers.

## Recommendation

Before implementing more boss gimmicks on top of `OverlapAttack`, finish steps 1-4 above. They are small, but they close the main correctness risks: target policy drift, duplicate hits, trigger hurtbox misses, and custom attack effects.
