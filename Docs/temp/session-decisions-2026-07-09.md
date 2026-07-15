# Session Decisions - 2026-07-09

Status: working decision log. Some items are design decisions; some are temporary merge-recovery decisions.

## Merge Recovery

- Keep `Assets/1.Scripts/Player/DefaultAttack.cs` deleted.
- Keep `Assets/1.Scripts/Unit/Weapon/BaseWeapon.cs` deleted.
- Treat `Assets/1.Scripts/Player/PlayerDefaultAttack.cs` and `Assets/1.Scripts/Unit/Weapon/BaseAttack.cs` as the active structure.
- Add only a thin compatibility layer to `BaseAttack` when old Wells-side code still expects `AttackInfo`, `_attackInfo`, or `InitializeAttackInfo()`.
- The compatibility layer is a bridge for merge recovery, not the final attack architecture.

## AttackInfo Bridge

Reason:

- Wells-side code still calls `Unit.TakeDamage(AttackInfo)`.
- Some boss attack scripts still call `new AttackInfo(damage)`, `_attackInfo`, and `InitializeAttackInfo()`.
- Since `BaseWeapon.cs` is deleted, `AttackInfo` must temporarily live somewhere else to restore compilation.

Decision:

- Define `AttackInfo` in `BaseAttack.cs` for now.
- Keep `Unit.TakeDamage(AttackInfo)` as the public damage entry point used by attack code.
- Keep `Unit.TakeDamage(int)` as protected internal damage calculation.
- `BaseAttack.TryResolveHit(Unit)` should pass `AttackInfo`, not raw `int`.

Open risk:

- `AttackInfo` is currently a compatibility shape. It should be revisited when Hurtbox and final hit reception are implemented.

## Hurtbox Direction

Decision:

- Adopt a Hurtbox-based hit reception model.
- A damageable Unit must have exactly one damageable Hurtbox.
- Body colliders are for movement and physical blocking.
- Hurtbox colliders are for damage reception.
- Attack code should not repeatedly call `GetComponentInParent<Unit>()` on arbitrary hit colliders.
- Hurtbox should cache and expose `OwnerUnit`.

Target invariant:

```text
Unit -> exactly one damageable Hurtbox
Hurtbox -> OwnerUnit
Attack overlap -> Hurtbox layer
Hurtbox.OwnerUnit -> TakeDamage / ReceiveAttack
```

## No HashSet For Single Hurtbox

Decision:

- Do not use `HashSet<Unit>` as a runtime deduplication mechanism once exactly-one Hurtbox is enforced.

Reason:

- If a Unit accidentally has multiple damageable Hurtboxes, a `HashSet<Unit>` would hide that setup bug.
- Duplicate damage should reveal the broken prefab/setup quickly.
- The invariant should be enforced by prefab validation, editor checks, or runtime assertions, not by silent deduplication.

Preferred validation:

```text
Assert each damageable Unit has exactly one Hurtbox.
Assert each Hurtbox has a valid OwnerUnit.
Assert attacks query the Hurtbox layer.
```

## PlayerDefaultAttack Responsibility

Decision:

- `PlayerDefaultAttack` represents player default-attack hit execution, not the whole default-attack state machine.
- `DefaultAttackController` owns state, combo, animation timing, input, movement, and rotation.
- `PlayerDefaultAttack` owns hit execution for the current default-attack step.

`PlayerDefaultAttack` should know:

- current default-attack step
- current attack direction
- damage snapshot
- owner player
- step hit type: overlap, projectile, raycast
- owner exclusion
- target hit execution

`PlayerDefaultAttack` should not own:

- combo-window state machine
- input buffering
- animation state transitions
- high-level player action state

## OverlapHitDetector Composition

Decision:

- Prefer `PlayerDefaultAttack has-a OverlapHitDetector` over `PlayerDefaultAttack is-a OverlapAttack`.

Reason:

- Player default attack is not always an overlap attack.
- `DefaultAttackStep.HitType` may be `Overlap`, `Projectile`, or `Raycast`.
- Inheritance would incorrectly model default attack as a specialized overlap attack.
- Composition keeps overlap physics querying reusable without forcing player default attack into that type hierarchy.

Expected split:

```text
PlayerDefaultAttack
- default attack rules
- step hit type selection
- owner exclusion
- damage application

OverlapHitDetector
- read ColliderInfo
- call Physics.OverlapBox/Sphere/Capsule
- return hit colliders or Hurtboxes
- no gameplay decisions
```

Later target:

```text
OverlapHitDetector returns Hurtbox candidates.
PlayerDefaultAttack resolves Hurtbox.OwnerUnit.
```

## Bomb Interaction

Problem:

- `PlayerDefaultAttack` currently uses physics overlap to find targets.
- `Bomb.OnTriggerEnter(Collider other)` expects an attack collider carrying `BaseAttack`.
- Therefore player default attack does not naturally call `Bomb.OnTriggerEnter`.
- This is why the bomb may not launch when hit by player default attack.

OOP decision:

- An attack should only attack.
- An attack should not know `BombController.LinearLaunch`.
- A bomb should react when it is attacked.
- If the incoming attack is a default attack, the bomb should launch itself.

Short-term:

- Do not introduce a large bomb refactor during merge recovery.
- Keep compatibility thin until Hurtbox/receiver design is implemented.

Medium-term:

- Move bomb reaction away from `OnTriggerEnter(BaseAttack)`.
- Route bomb hit reception through Hurtbox plus `Unit.TakeDamage(AttackInfo)` or a future `ReceiveAttack` entry point.

Target flow:

```text
PlayerDefaultAttack overlap
-> Hurtbox
-> OwnerUnit.ReceiveAttack / TakeDamage(AttackInfo)
-> Bomb checks AttackType.Default
-> BombController.LinearLaunch(...)
```

## BaseAttack / DefaultAttack Relationship

Current useful mental model:

```text
Unit
├─ Player
└─ Enemy

BaseAttack
├─ PlayerDefaultAttack
├─ DefaultAttackProjectile
├─ ColliderBasicAttack
├─ KnockbackAttack
└─ OverlapAttack
```

Simplified relation:

```text
BaseAttack -> Unit.TakeDamage(AttackInfo)
PlayerDefaultAttack -> BaseAttack.TryResolveHit(Unit)
```

Note:

- `PlayerDefaultAttack` may later stop inheriting overlap-specific behavior and instead compose detectors/executors.
- `BaseAttack` currently remains the shared damage metadata and hit-resolution compatibility point.

## Animation Events For Player Default Attack

Decision already applied earlier in the session:

- Add animation events to all four Garen default attack clips.
- Event types:
  - `0`: Hit
  - `1`: ComboWindowOpen
  - `2`: ComboWindowClose
  - `3`: End
- Animation event logs should use prefix `[PlayerAtttackEventLog]`.
- Avoid duplicate logs when wrapper animation event methods only forward to the typed handler.

## Boss Jump Attack

Finding:

- Boss jump target selection and landing sign are script-driven.
- `JumpController.SetTarget()` finds target, sets `ArrivePoint`, and positions the landing sign.
- `JumpController.OnLanded()` performs landing damage using overlap.
- Boss object movement toward `ArrivePoint` is script/BT-driven through `MoveForDurationAction`.
- `MoveForDurationAction` moves using `NavMeshAgent.Move`, `Rigidbody.MovePosition`, or `transform.position += delta`.
- The boss prefab Animator has `m_ApplyRootMotion: 0`, so GameObject transform movement is not driven by Animator root motion.

Conclusion:

```text
Horizontal/target movement: code/BT action.
Upward visual jump motion: likely animation pose/bone motion.
Root-motion Transform movement: disabled.
```

## Documentation To Update Later

When the design is no longer temporary, update stable docs instead of leaving this only in `Docs/temp`:

- `Docs/tech/physics.md`
- `Docs/tech/base-weapon-rework-draft.md`
- possibly `Docs/design/interaction-policy.md`

Required future doc points:

- exactly-one damageable Hurtbox invariant
- no runtime `HashSet<Unit>` dedup for single-Hurtbox hits
- attack overlap queries Hurtbox layer
- `Hurtbox.OwnerUnit` replaces repeated collider-to-Unit lookup
- bomb reacts through hit reception, not attack trigger collision
