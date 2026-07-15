# Player Bomb AttackInfo Worklog

Status: reverted after review request.

## Context

The attempted direction was option 2 from the design discussion:

```text
Attack detects a hit and passes attack metadata.
The damaged target decides how to react.
Bomb reacts to a default attack by launching itself.
```

This followed the OOP rule discussed in chat:

- An attack only attacks.
- A bomb should not depend on an attack collider trigger if the actual player attack is overlap-based.
- A bomb should react when it is attacked, and should launch only when the incoming attack is a default attack.

## Temporary Implementation That Was Applied

The implementation briefly added:

- `AttackInfo` to `Assets/1.Scripts/Unit/Weapon/BaseAttack.cs`
  - `Damage`
  - `IsGroggyAttack`
  - `AttackType`
  - `SourcePosition`
- `BaseAttack.CreateAttackInfo(...)`
- `BaseAttack.TryResolveHit(Unit)` forwarding `Unit.TakeDamage(AttackInfo)`
- `Unit.TakeDamage(AttackInfo)` as an overload that delegates to `TakeDamage(int)`
- `AttackEventArgs` storing `AttackInfo` instead of `BaseAttack`
- `Bomb.TakeDamage(AttackInfo)` reacting to matching `AttackType`
- `BombController.BombHit(...)` using `AttackInfo.SourcePosition` and `AttackInfo.Damage`
- `LinearLaunch(...)` direction changed from `referencePoint - bombPosition` to `bombPosition - referencePoint`

## Why This Solved The Immediate Bomb Issue

Current player default attack is overlap-based:

```text
PlayerDefaultAttack.HitOverlap()
-> BaseAttack.TryResolveHit(Unit)
-> Unit.TakeDamage(...)
```

The existing bomb path was trigger-based:

```text
Bomb.OnTriggerEnter(Collider other)
-> other.GetComponent<BaseAttack>()
-> BombController.BombHit(...)
-> LinearLaunch(...)
```

Because overlap attacks do not physically enter the bomb trigger as an attack collider, `Bomb.OnTriggerEnter` does not run for player default attack. Passing attack metadata through `TakeDamage(...)` makes the bomb reaction reachable from the overlap path.

## Risks Found While Applying It

- `Bomb` inherits `Unit`, but no `Initialize(...)` call was found for `Bomb`, so calling `base.TakeDamage(attackInfo)` could null-reference `_health`.
- Existing boss attack code still calls `TakeDamage(int)`, so the metadata path would only be used by `BaseAttack` callers.
- `ColliderBasicAttack` and `KnockbackAttack` still bypass the shared metadata path.
- `BaseAttack.TryResolveHit(Collider)` still uses root-based target resolution, which is a separate unresolved design issue.
- The design document would need to clearly distinguish "current state" from "proposed migration" before this approach is committed.

## Revert Scope

Per request, the temporary implementation is being reverted for now. The notes above are preserved so the option can be reconsidered later without rediscovering the same constraints.
