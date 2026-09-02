# Boss Counter and Monster Hit Commitment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give No.23 tunable Grab/Dash counter windows and let ordinary monsters finish attacks through basic-attack hit reactions.

**Architecture:** Keep `AttackInfo.isInterruptAttack` as the sole player-to-target interrupt contract. Extract deterministic windup, counter-progression, and automatic-hit policies for EditMode testing; `TwentyThreeBoss` remains the server-authoritative orchestrator for animation hold/release and existing attack chains. Store balance values in existing monster/boss ScriptableObjects plus one new per-attack duration.

**Tech Stack:** Unity 6000.3.16f1, C#, Netcode for GameObjects, Animator events, NavMeshAgent, NUnit EditMode tests, Multiplayer Play Mode.

**Spec:** `Docs/superpowers/specs/2026-09-02-boss-counter-and-monster-hit-commitment-design.md`

## Global Constraints

- Boss timing, success checks, attack release, Groggy/Break, and monster reactions remain server-authoritative.
- Keep `AttackInfo.isInterruptAttack`; do not restore `isGroggyAttack` or add player skill IDs.
- Only Grab and Dash are counterable: 1.0 and 1.5 seconds initially, with a 0-2 second inspector range.
- Groggy is a total 0.5 seconds; fifth-success Break is a total 2 seconds.
- During `MonsterState.Attack`, protect only `AttackType.Default` from automatic Hit; damage, knockback, and stun remain effective.
- Mid-boss counter attack selection is out of scope; preserve current accumulation behavior.
- Preserve unrelated working-tree changes and do not touch SVN-owned `Assets/50.Art`.
- Add Unity-generated `.meta` files beside every new `.cs` file.

---

### Task 1: Deterministic Counter Windup Gate

**Files:**
- Create: `Assets/1.Scripts/Monster/Boss/BossCounterWindupGate.cs`
- Create: `Assets/1.Scripts/Monster/Editor/BossCounterWindupGateTests.cs`
- Add: both Unity-generated `.meta` files

**Interfaces:**
- Consumes: `Begin(float duration)`, `Tick(float deltaTime)`, `MarkAnimationReady()`.
- Produces: `IsActive`, `IsAnimationReady`, `IsTimerElapsed`, `ShouldRelease`, `TimerElapsedBeforeAnimationReady`, `Reset()`.

- [ ] **Step 1: Write the failing EditMode tests**

```csharp
using NUnit.Framework;

public sealed class BossCounterWindupGateTests
{
    [Test]
    public void ReadyFirst_ReleasesOnlyAfterTimer()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.MarkAnimationReady();
        gate.Tick(0.99f);
        Assert.That(gate.ShouldRelease, Is.False);
        gate.Tick(0.01f);
        Assert.That(gate.ShouldRelease, Is.True);
    }

    [Test]
    public void TimerFirst_WaitsForAnimationAndReportsOrdering()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.Tick(1f);
        Assert.That(gate.ShouldRelease, Is.False);
        Assert.That(gate.TimerElapsedBeforeAnimationReady, Is.True);
        gate.MarkAnimationReady();
        Assert.That(gate.ShouldRelease, Is.True);
    }

    [Test]
    public void Reset_ClearsPendingRelease()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.MarkAnimationReady();
        gate.Tick(1f);
        gate.Reset();
        Assert.That(gate.IsActive, Is.False);
        Assert.That(gate.ShouldRelease, Is.False);
        Assert.That(gate.TimerElapsedBeforeAnimationReady, Is.False);
    }
}
```

- [ ] **Step 2: Run the fixture and confirm the missing-type failure**

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.16f1\Editor\Unity.exe' -batchmode -quit -projectPath 'C:\Users\user\Projects\MainProject' -runTests -testPlatform EditMode -testFilter BossCounterWindupGateTests -testResults 'C:\Users\user\Projects\MainProject\tmp\counter-gate.xml' -logFile 'C:\Users\user\Projects\MainProject\tmp\counter-gate.log'
```

Expected: FAIL because `BossCounterWindupGate` is undefined.

- [ ] **Step 3: Implement the minimal gate**

```csharp
using UnityEngine;

public sealed class BossCounterWindupGate
{
    float _remaining;
    public bool IsActive { get; private set; }
    public bool IsAnimationReady { get; private set; }
    public bool IsTimerElapsed { get; private set; }
    public bool TimerElapsedBeforeAnimationReady { get; private set; }
    public bool ShouldRelease => IsActive && IsAnimationReady && IsTimerElapsed;

    public void Begin(float duration)
    {
        Reset();
        IsActive = true;
        _remaining = Mathf.Max(0f, duration);
        IsTimerElapsed = _remaining <= 0f;
    }

    public void Tick(float deltaTime)
    {
        if (!IsActive || IsTimerElapsed) return;
        _remaining = Mathf.Max(0f, _remaining - Mathf.Max(0f, deltaTime));
        IsTimerElapsed = _remaining <= 0f;
        if (IsTimerElapsed && !IsAnimationReady)
            TimerElapsedBeforeAnimationReady = true;
    }

    public void MarkAnimationReady()
    {
        if (IsActive) IsAnimationReady = true;
    }

    public void Reset()
    {
        _remaining = 0f;
        IsActive = false;
        IsAnimationReady = false;
        IsTimerElapsed = false;
        TimerElapsedBeforeAnimationReady = false;
    }
}
```

- [ ] **Step 4: Refresh Unity, rerun the fixture, and confirm three passes**
- [ ] **Step 5: Commit only the gate, tests, and their `.meta` files**

```powershell
git add -- 'Assets/1.Scripts/Monster/Boss/BossCounterWindupGate.cs' 'Assets/1.Scripts/Monster/Boss/BossCounterWindupGate.cs.meta' 'Assets/1.Scripts/Monster/Editor/BossCounterWindupGateTests.cs' 'Assets/1.Scripts/Monster/Editor/BossCounterWindupGateTests.cs.meta'
git commit -m 'feat(boss): 카운터 선딜 게이트 추가'
```

---

### Task 2: Counter Progression and SO Shape

**Files:**
- Create: `Assets/1.Scripts/Monster/Boss/BossCounterProgress.cs`
- Create: `Assets/1.Scripts/Monster/Editor/BossCounterProgressTests.cs`
- Modify: `Assets/1.Scripts/Monster/Boss/BossDataSO.cs:44-50`
- Add: both Unity-generated `.meta` files

**Interfaces:**
- Produces: `BossCounterProgress.Resolve(int,int,bool,float,float)` returning `BossCounterOutcome` and `BossAttackEntry.counterWindowDuration`.

- [ ] **Step 1: Write failing tests for normal success, fifth-success Break, charge non-Break, and the inspector range**

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class BossCounterProgressTests
{
    [TestCase(3, true, 4, false, 0.5f)]
    [TestCase(4, true, 0, true, 2f)]
    [TestCase(4, false, 5, false, 0.5f)]
    public void Resolve_ReturnsExpectedOutcome(
        int current, bool allowBreak, int next, bool isBreak, float duration)
    {
        BossCounterOutcome result = BossCounterProgress.Resolve(current, 5, allowBreak, 0.5f, 2f);
        Assert.That(result.NextCount, Is.EqualTo(next));
        Assert.That(result.IsBreak, Is.EqualTo(isBreak));
        Assert.That(result.Duration, Is.EqualTo(duration));
    }

    [Test]
    public void CounterDuration_HasZeroToTwoRange()
    {
        var field = typeof(BossAttackEntry).GetField(nameof(BossAttackEntry.counterWindowDuration));
        var range = field?.GetCustomAttributes(typeof(RangeAttribute), false)
            .Cast<RangeAttribute>().SingleOrDefault();
        Assert.That(range?.min, Is.EqualTo(0f));
        Assert.That(range?.max, Is.EqualTo(2f));
    }
}
```

- [ ] **Step 2: Run `BossCounterProgressTests` and confirm missing-type/field failures**
- [ ] **Step 3: Implement progression**

```csharp
using UnityEngine;

public readonly struct BossCounterOutcome
{
    public int NextCount { get; }
    public bool IsBreak { get; }
    public float Duration { get; }
    public BossCounterOutcome(int nextCount, bool isBreak, float duration) =>
        (NextCount, IsBreak, Duration) = (nextCount, isBreak, duration);
}

public static class BossCounterProgress
{
    public static BossCounterOutcome Resolve(int current, int threshold, bool allowBreak,
        float groggyDuration, float breakDuration)
    {
        int next = Mathf.Max(0, current) + 1;
        bool isBreak = allowBreak && next >= Mathf.Max(1, threshold);
        return new BossCounterOutcome(isBreak ? 0 : next, isBreak,
            Mathf.Max(0.05f, isBreak ? breakDuration : groggyDuration));
    }
}
```

- [ ] **Step 4: Add the SO row field after `opensCounterWindow`**

```csharp
[Tooltip("카운터 창 지속 시간(초). opensCounterWindow가 false면 사용하지 않는다.")]
[Range(0f, 2f)] public float counterWindowDuration = 0f;
```

- [ ] **Step 5: Refresh Unity and confirm all progression tests pass**
- [ ] **Step 6: Commit the new types, tests, `.meta` files, and `BossDataSO.cs`**

```powershell
git add -- 'Assets/1.Scripts/Monster/Boss/BossCounterProgress.cs' 'Assets/1.Scripts/Monster/Boss/BossCounterProgress.cs.meta' 'Assets/1.Scripts/Monster/Editor/BossCounterProgressTests.cs' 'Assets/1.Scripts/Monster/Editor/BossCounterProgressTests.cs.meta' 'Assets/1.Scripts/Monster/Boss/BossDataSO.cs'
git commit -m 'feat(boss): 카운터 진행도와 SO 시간 계약 추가'
```

---

### Task 3: Wire No.23 Windup, Pose Hold, and Total-Duration Groggy

**Files:**
- Modify: `Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs:56-64,450-587,665-761,855-900,2211-2296`

**Interfaces:**
- Consumes: Tasks 1-2 types and the existing `FireAttackHitOnce`, `AcquireGrab`, `BeginDash`, and `AbortAttackChain` paths.
- Produces: `SetCounterPoseHeldClientRpc(bool)`, `RestoreCounterPose()`, `BeginCounterWindup()`, `TryReleaseCounterAttack()`.

- [ ] **Step 1: Add a duplicate-readiness test to `BossCounterWindupGateTests` and confirm it passes**

```csharp
[Test]
public void DuplicateReadySignal_ReleasesOnlyOnceThroughSameDecision()
{
    var gate = new BossCounterWindupGate();
    gate.Begin(1f);
    gate.MarkAnimationReady();
    gate.MarkAnimationReady();
    gate.Tick(1f);
    Assert.That(gate.ShouldRelease, Is.True);
}
```

- [ ] **Step 2: Add runtime fields and capture the per-row duration**

```csharp
readonly BossCounterWindupGate _counterWindup = new BossCounterWindupGate();
bool _counterAnimatorHeldLocally;
float _counterAnimatorResumeSpeed = 1f;
bool _warnedCounterTimerBeforeAnimation;
float CounterWindowDuration => _currentEntry != null
    ? Mathf.Clamp(_currentEntry.counterWindowDuration, 0f, 2f) : 0f;
```

- [ ] **Step 3: Put both Grab and Dash into `BossAttackPhase.Windup` with complete chain budgets**

```csharp
void BeginCounterWindup()
{
    _attackPhase = BossAttackPhase.Windup;
    _attackPhaseTimer = CounterWindowDuration;
    _counterWindup.Begin(CounterWindowDuration);
}
```

Use `CounterWindowDuration + GrabHold + GrabThrowTime + GrabRecovery` for Grab and `CounterWindowDuration + DashDuration + data.attackDuration` for Dash. Keep other attacks unchanged.

- [ ] **Step 4: Override `NotifyAttackHit` so counter attacks latch and hold instead of firing**

```csharp
public override void NotifyAttackHit()
{
    if (!IsServer || State != MonsterState.Attack) return;
    if (!_counterWindup.IsActive) { base.NotifyAttackHit(); return; }
    _counterWindup.MarkAnimationReady();
    SetCounterPoseHeldClientRpc(true);
    TryReleaseCounterAttack();
}
```

- [ ] **Step 5: Tick and release the gate in the Windup branch**

```csharp
_counterWindup.Tick(dt);
if (_counterWindup.TimerElapsedBeforeAnimationReady && !_warnedCounterTimerBeforeAnimation)
{
    _warnedCounterTimerBeforeAnimation = true;
    Debug.LogWarning($"[23호] {_currentEntry?.attackId} 카운터 시간이 준비 이벤트보다 먼저 끝났다. 이벤트를 기다린다.", this);
}
TryReleaseCounterAttack();
```

```csharp
void TryReleaseCounterAttack()
{
    if (!_counterWindup.ShouldRelease) return;
    _counterWindup.Reset();
    SetCounterWindow(false);
    SetCounterPoseHeldClientRpc(false);
    FireAttackHitOnce();
}
```

- [ ] **Step 6: Add idempotent pose hold/restore for every peer**

```csharp
[ClientRpc]
void SetCounterPoseHeldClientRpc(bool held)
{
    if (animator == null) return;
    if (held)
    {
        if (_counterAnimatorHeldLocally) return;
        _counterAnimatorResumeSpeed = animator.speed;
        animator.speed = 0f;
        _counterAnimatorHeldLocally = true;
        return;
    }
    RestoreCounterPose();
}

void RestoreCounterPose()
{
    if (!_counterAnimatorHeldLocally || animator == null) return;
    animator.speed = _counterAnimatorResumeSpeed;
    _counterAnimatorHeldLocally = false;
}
```

- [ ] **Step 7: Reset gate/window/pose in `AbortAttackChain`, state exit, death, timeout, and despawn**

Call `_counterWindup.Reset()`, `SetCounterWindow(false)`, and the safe pose-restore path before existing Grab/Dash/NavMesh cleanup. Do not remove existing cleanup branches.

- [ ] **Step 8: Replace `Hit + Groggy` stacking with one `BossCounterOutcome` duration**

```csharp
BossCounterOutcome outcome = BossCounterProgress.Resolve(
    _counterGroggyCount, data != null ? data.maxGroggyCount : 5, allowBreak,
    data != null ? data.groggyDuration : 0.5f,
    _boss != null ? _boss.breakDuration : 2f);
_counterGroggyCount = outcome.NextCount;
AbortAttackChain();
ForceGroggy(outcome.Duration);
```

Update the log to report `outcome.IsBreak`, `outcome.NextCount`, and `outcome.Duration`. Remove `HitReactionDuration` from this path so 0.4 seconds is not added.

- [ ] **Step 9: Validate that open counter rows are Grab/Dash with durations in `(0,2]`**

Add explicit `ValidateContract` errors containing the attack ID and invalid duration.

- [ ] **Step 10: Run all `BossCounter` EditMode tests and confirm zero compile errors**
- [ ] **Step 11: Commit only `TwentyThreeBoss.cs` and the added test case**

```powershell
git add -- 'Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs' 'Assets/1.Scripts/Monster/Editor/BossCounterWindupGateTests.cs'
git commit -m 'feat(boss): Grab Dash 카운터 창과 자세 홀드 연결'
```

---

### Task 4: Ordinary Monster Basic-Attack Commitment

**Files:**
- Create: `Assets/1.Scripts/Monster/MonsterHitReactionPolicy.cs`
- Create: `Assets/1.Scripts/Monster/Editor/MonsterHitReactionPolicyTests.cs`
- Modify: `Assets/1.Scripts/Monster/MonsterBase.cs:889-895`
- Add: both Unity-generated `.meta` files

**Interfaces:**
- Produces: `MonsterHitReactionPolicy.ShouldEnterAutomaticHit(MonsterState, AttackType, bool)`.

- [ ] **Step 1: Write failing policy tests**

```csharp
using NUnit.Framework;

public sealed class MonsterHitReactionPolicyTests
{
    [TestCase(MonsterState.Attack, AttackType.Default, false, false)]
    [TestCase(MonsterState.Chase, AttackType.Default, false, true)]
    [TestCase(MonsterState.Attack, AttackType.Skill, false, true)]
    [TestCase(MonsterState.Chase, AttackType.Default, true, false)]
    [TestCase(MonsterState.Groggy, AttackType.Default, false, false)]
    [TestCase(MonsterState.Return, AttackType.Default, false, false)]
    [TestCase(MonsterState.Knockback, AttackType.Default, false, false)]
    public void AutomaticHitDecision_MatchesCommitmentRule(
        MonsterState state, AttackType attackType, bool armor, bool expected)
    {
        Assert.That(MonsterHitReactionPolicy.ShouldEnterAutomaticHit(state, attackType, armor),
            Is.EqualTo(expected));
    }
}
```

- [ ] **Step 2: Run `MonsterHitReactionPolicyTests` and confirm the missing-type failure**
- [ ] **Step 3: Implement the policy**

```csharp
public static class MonsterHitReactionPolicy
{
    public static bool ShouldEnterAutomaticHit(MonsterState state, AttackType type, bool armor)
    {
        if (armor || state == MonsterState.Groggy || state == MonsterState.Return ||
            state == MonsterState.Knockback) return false;
        return state != MonsterState.Attack || type != AttackType.Default;
    }
}
```

- [ ] **Step 4: Replace only the automatic `EnterHit` condition in `MonsterBase.TakeDamage`**

```csharp
bool superArmor = status != null && status.BlocksInterrupt;
if (MonsterHitReactionPolicy.ShouldEnterAutomaticHit(
        _state.Value, attackInfo.attackType, superArmor))
    EnterHit();
```

Do not change interrupt accumulation, `ReceiveAttack`, `TryEnterKnockback`, or status-effect handling.

- [ ] **Step 5: Refresh Unity, rerun the fixture, and confirm all seven cases pass**
- [ ] **Step 6: Commit the policy, tests, `.meta` files, and `MonsterBase.cs`**

```powershell
git add -- 'Assets/1.Scripts/Monster/MonsterHitReactionPolicy.cs' 'Assets/1.Scripts/Monster/MonsterHitReactionPolicy.cs.meta' 'Assets/1.Scripts/Monster/Editor/MonsterHitReactionPolicyTests.cs' 'Assets/1.Scripts/Monster/Editor/MonsterHitReactionPolicyTests.cs.meta' 'Assets/1.Scripts/Monster/MonsterBase.cs'
git commit -m 'fix(monster): 평타 경직이 공격 커밋을 끊지 않게 한다'
```

---

### Task 5: Author Both No.23 SO Variants

**Files:**
- Create: `Assets/1.Scripts/Monster/Editor/BossCounterDataTests.cs`
- Modify: `Assets/1.Scripts/Monster/Editor/TwentyThreeBossAuthoring.cs:431-498`
- Modify: `Assets/1.Scripts/Monster/Editor/BossDataWiring.cs:29-46`
- Modify: `Assets/2.Prefabs/Monster/Data/No23.asset`
- Modify: `Assets/2.Prefabs/Monster/Data/No23_Solo.asset`
- Add: Unity-generated test `.meta`

**Interfaces:**
- Produces: Grab 1.0, Dash 1.5, threshold 5, Groggy 0.5, Break 2 in both assets and readable verification output.

- [ ] **Step 1: Write failing asset tests**

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;

public sealed class BossCounterDataTests
{
    [TestCase("Assets/2.Prefabs/Monster/Data/No23.asset")]
    [TestCase("Assets/2.Prefabs/Monster/Data/No23_Solo.asset")]
    public void Variant_HasApprovedCounterDefaults(string path)
    {
        BossDataSO data = AssetDatabase.LoadAssetAtPath<BossDataSO>(path);
        BossAttackEntry grab = data.attacks.Single(a => a.attackId == BossAttackId.Grab);
        BossAttackEntry dash = data.attacks.Single(a => a.attackId == BossAttackId.Dash);
        Assert.That(grab.opensCounterWindow, Is.True);
        Assert.That(grab.counterWindowDuration, Is.EqualTo(1f));
        Assert.That(dash.opensCounterWindow, Is.True);
        Assert.That(dash.counterWindowDuration, Is.EqualTo(1.5f));
        Assert.That(data.attacks.Where(a => a.opensCounterWindow).Select(a => a.attackId),
            Is.EquivalentTo(new[] { BossAttackId.Grab, BossAttackId.Dash }));
        Assert.That(data.maxGroggyCount, Is.EqualTo(5));
        Assert.That(data.groggyDuration, Is.EqualTo(0.5f));
        Assert.That(data.breakDuration, Is.EqualTo(2f));
    }
}
```

- [ ] **Step 2: Run `BossCounterDataTests` and confirm failures on the old zero/2/5 values**
- [ ] **Step 3: Serialize `counterWindowDuration` after every `opensCounterWindow` key**

```yaml
# Grab
opensCounterWindow: 1
counterWindowDuration: 1
# Dash
opensCounterWindow: 1
counterWindowDuration: 1.5
# Other rows
opensCounterWindow: 0
counterWindowDuration: 0
```

In both assets set `maxGroggyCount: 5`, `groggyDuration: 0.5`, and `breakDuration: 2`. Preserve every solo-only difference.

- [ ] **Step 4: Update `TwentyThreeBossAuthoring` defaults and row authoring**

Use the existing `Set` log/assign helper for `maxGroggyCount=5`, `groggyDuration=0.5f`, `breakDuration=2f`, Grab duration `1f`, and Dash duration `1.5f`. Other attack rows receive `0f` and `opensCounterWindow=false`.

- [ ] **Step 5: Show `[카운터 N초]` in `BossDataWiring.Verify`**

```csharp
string counter = a.opensCounterWindow ? $" [카운터 {a.counterWindowDuration:0.##}초]" : "";
```

- [ ] **Step 6: Refresh Unity and confirm both asset cases pass without import warnings**
- [ ] **Step 7: Commit only the test, `.meta`, authoring tools, and two SO assets**

```powershell
git add -- 'Assets/1.Scripts/Monster/Editor/BossCounterDataTests.cs' 'Assets/1.Scripts/Monster/Editor/BossCounterDataTests.cs.meta' 'Assets/1.Scripts/Monster/Editor/TwentyThreeBossAuthoring.cs' 'Assets/1.Scripts/Monster/Editor/BossDataWiring.cs' 'Assets/2.Prefabs/Monster/Data/No23.asset' 'Assets/2.Prefabs/Monster/Data/No23_Solo.asset'
git commit -m 'chore(boss): Grab Dash 카운터 기본값 저작'
```

---

### Task 6: Full Regression and MPPM Verification

**Files:**
- Modify: `PLAN.md`
- Modify only if a deviation occurred: `IMPLEMENTATION_NOTES.md`

**Interfaces:**
- Produces: automated test evidence, MPPM evidence, and closed plan status.

- [ ] **Step 1: Run all new fixtures**

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.16f1\Editor\Unity.exe' -batchmode -quit -projectPath 'C:\Users\user\Projects\MainProject' -runTests -testPlatform EditMode -testFilter 'BossCounter|MonsterHitReactionPolicy' -testResults 'C:\Users\user\Projects\MainProject\tmp\combat-interrupt.xml' -logFile 'C:\Users\user\Projects\MainProject\tmp\combat-interrupt.log'
```

Expected: every new test passes and the log has zero `error CS` entries.

- [ ] **Step 2: Run the full existing EditMode suite**

Use the same command without `-testFilter`; expect all tests to pass.

- [ ] **Step 3: Verify Grab in MPPM 2-player mode**

Check front interrupt before 1.0 seconds cancels Grab and gives total 0.5-second Groggy; back interrupt, basic attack, and no interrupt all allow Grab; no interrupt releases exactly once at 1.0 seconds.

- [ ] **Step 4: Verify Dash in MPPM 2-player mode**

Check front interrupt before 1.5 seconds prevents movement/hit-window start; back/late/no interrupt allows one Dash; animation speed is normal after success, death, and timeout.

- [ ] **Step 5: Verify five-success progression**

Confirm successes 1-4 last 0.5 seconds, success 5 lasts 2 seconds and resets, and success 6 begins at 1/5 on both peers.

- [ ] **Step 6: Verify ordinary monsters**

For one melee and one ranged monster, confirm repeated basics during Attack reduce health without Hit cancellation; basics after Attack still cause Hit; knockback and stun during Attack still interrupt. Confirm the three mid-bosses retain current accumulation/superarmor behavior.

- [ ] **Step 7: Inspect scope and serialization safety**

```powershell
git diff --check
git status --short
git diff --stat 9c67b12..HEAD
git diff 9c67b12..HEAD -- 'Assets/1.Scripts/Monster' 'Assets/2.Prefabs/Monster/Data/No23.asset' 'Assets/2.Prefabs/Monster/Data/No23_Solo.asset' 'PLAN.md' 'IMPLEMENTATION_NOTES.md'
```

Expected: no whitespace errors, no SVN FBX/meta changes, and no unrelated staged changes.

- [ ] **Step 8: Record results and commit plan closure**

Update the top of `PLAN.md` with exact test counts, MPPM outcomes, and remaining risks. If the approved plan changed during implementation, record the exact deviation and reason in `IMPLEMENTATION_NOTES.md`.

```powershell
git add -- 'PLAN.md'
git commit -m 'docs(combat): 카운터와 공격 커밋 검증 결과 기록'
```

Add `IMPLEMENTATION_NOTES.md` to that commit only when it actually changed.
