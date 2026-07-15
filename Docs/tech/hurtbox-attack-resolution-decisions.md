# Hurtbox Attack Resolution Decisions

Status: accepted direction, implementation in progress on `feature/HurtBox`.

This document records the decisions from the merge-recovery and Hurtbox design session.

## Context

The merge between the player attack rework and the Wells/No.23 boss work exposed two separate issues:

1. Compile-time API drift from the old `BaseWeapon` / `DefaultAttack` code.
2. Runtime model drift between trigger-based bomb reaction and overlap-based player default attack.

`BaseWeapon.cs` and the old `DefaultAttack.cs` remain deleted. The current branch keeps the newer `BaseAttack.cs`, `PlayerDefaultAttack.cs`, `DefaultAttackController.cs`, and related player attack structure.

## Short-Term Merge Recovery

The immediate goal is compile recovery, not the final combat architecture.

Decisions:

- Keep `BaseWeapon.cs` deleted.
- Keep old `DefaultAttack.cs` deleted.
- Restore only a thin compatibility layer in `BaseAttack.cs`.
- Put `AttackInfo` at the top of `BaseAttack.cs` for now, not in a separate file.
- Treat this `AttackInfo` support as a bridge until Hurtbox integration replaces the old paths.
- Leave generated `.csproj` stale references to Unity regeneration instead of editing them manually.

The bridge currently exists so Wells-side code that still expects `AttackInfo`, `Unit.TakeDamage(AttackInfo)`, `InitializeAttackInfo()`, and `_attackInfo` can compile while the larger Hurtbox migration is planned.

## AttackInfo Scope

Current minimal `AttackInfo` shape:

```csharp
public struct AttackInfo
{
    public int damage;
    public AttackType attackType;
    public bool isGroggyAttack;
}
```

Decisions:

- Every attack should eventually deliver an `AttackInfo` to the damaged target.
- `damage` stays lowercase for compatibility with existing `Unit.TakeDamage(AttackInfo)` usage.
- `attackType` stays because bomb behavior needs to distinguish default attacks.
- `isGroggyAttack` stays because it already existed on `BaseAttack`.
- Do not add attacker/source data to `AttackInfo` yet.
- Do not split `AttackInfo` and `AttackerInfo` yet.

Rationale:

- `AttackInfo` should describe the incoming attack effect.
- Attacker/source identity has different lifecycle and authority concerns.
- Projectiles, traps, reflected bombs, summons, and network ownership can make "attacker" ambiguous.
- If source data becomes necessary, add a separate concept later, such as `AttackSourceInfo` or `HitContext`.

Possible future split:

```text
AttackInfo
- damage
- attackType
- groggy/stagger flags
- future effect tags

AttackSourceInfo
- attackerUnit
- sourceObject
- ownerClientId

HitContext
- hurtbox
- hitPoint
- hitNormal
- hitDirection
```

Do not implement this split until a real caller needs it.

## Hurtbox Direction

The accepted direction is Hurtbox-based hit resolution.

Decisions:

1. Each damageable `Unit` has exactly one damageable `Hurtbox`.
2. Body colliders are for movement and physical collision.
3. Damage resolution enters through `Hurtbox`, not body collider lookup.
4. Attack code should not repeatedly call `GetComponentInParent<Unit>()`.
5. `Hurtbox` caches `OwnerUnit`.
6. Attack code gets the damaged unit from `Hurtbox.OwnerUnit`.
7. Primary attack detection is overlap-based.
8. Attack colliders are mostly for debugging and visualization, not the source of truth for hit detection.
9. Because there is exactly one damageable Hurtbox per Unit, duplicate Unit hits should be treated as prefab/setup bugs, not hidden with `HashSet<Unit>`.

Current `HashSet<Unit>` deduplication in player attack code is considered temporary and should be removed or re-evaluated during Hurtbox migration.

## Bomb Reaction Problem

The current bomb issue is a model mismatch.

Current trigger-based path:

```text
BaseAttack collider enters Bomb trigger
-> Bomb.OnTriggerEnter
-> AttackEventArgs(BaseAttack)
-> BombController.BombHit
-> LinearLaunch
```

Current player default attack path:

```text
PlayerDefaultAttack.HitOverlap()
-> Physics.Overlap...
-> Unit.TakeDamage(...)
```

Because player default attack is overlap-based, no attack collider enters the bomb trigger. Therefore `Bomb.OnTriggerEnter(BaseAttack)` is not called and the bomb does not launch.

Decision:

- Do not make player attack know about `BombController.LinearLaunch`.
- Attacks should attack.
- Bomb should react to being hit by an incoming default attack.
- `Bomb.OnTriggerEnter(BaseAttack)` is legacy bridge behavior and should be removed during Hurtbox integration.

Temporary state:

- `AttackEventArgs` remains only to keep the old bomb trigger bridge compiling.
- `AttackEventArgs` should be removed when Hurtbox becomes the shared hit entry point.

## Expected Final Flow

Target direction:

```text
Attack overlap detects Hurtbox
-> Hurtbox exposes IAttackReceiver
-> Attack creates/passes AttackInfo
-> Unit or non-Unit receiver handles ReceiveAttack
-> Bomb-specific receiver checks AttackType.Default
-> Bomb reacts by launching itself
```

Bomb-specific behavior should live on the bomb/receiver side, not inside player attack code.

## Receiver Interface Decision

Bomb is not a `Unit` in the current code or prefab structure.

Decision:

- Do not force `Bomb` to inherit from `Unit`.
- Add a thin `IAttackReceiver` interface for objects that can receive attacks.
- Let `Unit` implement `IAttackReceiver` by delegating to `TakeDamage(AttackInfo)`.
- Let `Bomb` implement `IAttackReceiver` by checking the incoming `AttackType` and raising its existing reaction event.
- Let `Hurtbox` resolve an `IAttackReceiver` first, while still exposing `OwnerUnit` for owner exclusion and temporary deduplication.

Reason:

- Bomb is an attack-reactive object, not a health-driven unit.
- Making Bomb a Unit would drag in health, shield, initialization, and network HP assumptions that Bomb does not currently need.
- `AttackInfo` should remain the attack effect payload.
- Source-dependent reactions, such as bomb launch direction, should use a separate `AttackHitContext`.

Applied flow:

```text
PlayerDefaultAttack overlap
-> Bomb Hurtbox
-> Bomb.ReceiveAttack(AttackInfo, AttackHitContext)
-> BombController.BombHit
-> LinearLaunch(context.sourcePosition, attackInfo.damage)
```

## Collaboration And Branch Policy

There is a realistic chance that Wells-side contributors who are hand-coding boss and attack behavior will push back against the Hurtbox pattern.

Expected objections:

- Direct `GetComponentInParent<Unit>()` lookup feels simpler than adding a `Hurtbox`.
- Prefab-level Hurtbox setup feels like extra work.
- Existing `OnTriggerEnter(BaseAttack)` behavior feels more direct.
- Existing boss attack scripts may appear to work without this structure.
- `AttackInfo`, `Hurtbox`, and a future receiver API may look like over-engineering during short-term gameplay scripting.

Decision:

- Continue the Hurtbox implementation on `feature/HurtBox` without treating this expected pushback as a blocker.
- Do not dilute the branch direction just to preserve the old trigger-first mental model.
- Keep compatibility bridges only where needed for compile recovery and staged migration.
- Do not rewrite all Wells-side attack code immediately unless the Hurtbox integration requires it.
- New or migrated hit reception should follow the Hurtbox path.

Reason:

- The current bug is caused by split hit-reception paths, not by a single bad bomb script.
- Player default attack is overlap-based, while the old bomb reaction is trigger-based.
- A shared Hurtbox entry point makes attack delivery independent from whether an attack used trigger collision, overlap query, projectile contact, or another detector.
- The goal is not to make every attack implementation more abstract. The goal is to make the damaged side receive hits through one predictable path.

Communication stance:

```text
Existing Wells code does not need to be rewritten all at once.
The Hurtbox branch establishes the common hit-reception path.
Compatibility remains temporary.
New or touched attack interactions should migrate toward Hurtbox.
```

## Current Commit

Initial compile bridge was committed on `feature/HurtBox`:

```text
7cbee99 Restore attack info bridge after merge
```

This commit is not the final Hurtbox architecture. It only restores a narrow compatibility path so the merge can move forward.

## Open Questions

- Should `AttackInfo` remain in `BaseAttack.cs` after Hurtbox lands, or move to its own file then?
- Should the final receiver API be `TakeDamage(AttackInfo)` or a broader `ReceiveAttack(AttackInfo, HitContext)`?
- How should non-Unit damageable objects, such as bombs, expose their Hurtbox receiver?
- Should `AttackType` remain input-slot-like (`Default`, `Q`, `E`, `R`) or become a gameplay category/tag?
- When Hurtbox is enforced as one per damageable Unit, what validation tool should catch duplicate/missing Hurtboxes?
