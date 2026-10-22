using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// E 수호자의 의지 — 전방 방어 대기 → 첫 방어 성공 시 그 피해 흡수 + 반격 단계(피해·넉백 무시) → 종료.
/// 단계는 <see cref="FirstMeleeGuardCounterModel"/>, 방향 판정은 <see cref="FirstMeleeGuardRules"/> 가 맡고
/// 이 클래스는 서버 배선만 한다. 피격 가로채기는 <see cref="Player"/>.ReceiveAttack 맨 위의 <see cref="IPlayerIncomingAttackGuard"/> 훅.
/// 타이밍은 클립 이벤트다(PLAN-anim-event-timing P1-③): 방어 시작(SubSkillGuard) Custom0 방어 창 열림 →
/// 방어 유지 루프(SubSkillGuardIdle, 전이 exitTime 으로 길이 조정) → 방어 종료(SubSkillGuardEnd) Custom1 창 닫힘·종료,
/// 반격(SubSkillCounter) Hit 반격 판정 · End 종료. 이벤트·전이를 옮기면 그대로 반영된다.
/// 설계·결정: PLAN-paladin-counter-e (Re:C 변경안).
/// </summary>
public class FirstMeleeSubSkill : PlayerInstantSkill, IPlayerIncomingAttackGuard
{
    // 반격 순간 연출 자리값(PlayerShieldVfx.ServerInterruptWave 재사용, 결정 25). 반격 판정마다 대상 유무와 무관하게 1회.
    [Header("연출")]
    [Tooltip("연출 창구. 플레이어 루트의 PlayerShieldVfx 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PlayerShieldVfx shieldVfx;

    // 방어 성공 시 CrossFade 할 반격 자세 상태(PlayerAnimatorController Base Layer). 시작 상태(SubSkillGuard)는 데이터의
    // animatorStateName 이고, 반격 상태는 이 스킬 전용이라 데이터 열로 늘리지 않는다(xlsx 열 추가 회피).
    private const string CounterStateName = "SubSkillCounter";
    private static readonly int CounterStateHash = Animator.StringToHash(CounterStateName);
    // 방어 시작 → 유지 → 종료는 애니메이터 exitTime 전이로 이어진다. 창을 닫고 E 를 끝내는 Custom1 은 종료 상태 클립에 있다.
    private const string GuardEndStateName = "SubSkillGuardEnd";

    // 방어 시작 클립(데이터 animatorStateName) = Custom0, 방어 종료 클립(GuardEndStateName) = Custom1, 반격 클립(CounterStateName) = Hit·End.
    private static readonly SkillAnimationEventType[] GuardEvents = { SkillAnimationEventType.Custom0 };
    private static readonly SkillAnimationEventType[] CounterEvents =
        { SkillAnimationEventType.Hit, SkillAnimationEventType.End };
    // 시작 상태 CrossFade(PlayerSkillController.PlaySkillPresentation)와 같은 값.
    private const float CounterCrossFadeSeconds = 0.05f;

    private readonly FirstMeleeGuardCounterModel model = new FirstMeleeGuardCounterModel();
    private PlayerInputReader inputReader;

    // 방어 성공 뒤 반격 전에 E 가 끊겼을 때(구속·기타 Cancelled) 다음 프레임에 실행할 반격 1회(결정 2).
    // 모델은 OnEnd 에서 Reset 되므로 방향은 따로 들고 있는다.
    private bool pendingDeferredCounter;
    private Vector3 deferredCounterForward;

    // 반격 판정 버퍼. 데이터에 MaxHitResults 열을 늘리지 않으려고 고정 크기로 둔다(xlsx 열 추가 회피).
    private const int MaxCounterHitResults = 16;
    private readonly Collider[] counterHitResults = new Collider[MaxCounterHitResults];
    // 키가 Object 인 이유는 FirstMeleeMainSkill.tickTargets 와 같다(Unit 이 아닌 IAttackReceiver 는 Hurtbox 가 키).
    private readonly HashSet<Object> counterTargets = new HashSet<Object>();
    private readonly List<Unit> counterLandedUnits = new List<Unit>();

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // 방어 대기·반격 중 이동·회전 불가(결정 3). 평타·다른 스킬·대시는 Skill 상태 규칙으로 이미 막힌다(CanBeCanceledByDash 기본 false).
    public override bool CanMoveWhileActive => false;

    public override IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => GuardEvents;

    public override void CollectAnimationEventRequirements(List<AnimationEventRequirement> requirements)
    {
        base.CollectAnimationEventRequirements(requirements);
        requirements.Add(AnimationEventRequirement.ForSkillState(GuardEndStateName, SkillAnimationEventType.Custom1));
        foreach (SkillAnimationEventType eventType in CounterEvents)
            requirements.Add(AnimationEventRequirement.ForSkillState(CounterStateName, eventType));
    }

    private FirstMeleeSubSkillData SubSkillData => Data as FirstMeleeSubSkillData;

    // 서버(또는 오프라인) — OnEnd 는 모든 피어에서 돌 수 있다
    private bool IsServerPeer => owner != null && (owner.IsServer || !owner.IsSpawned);

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        inputReader = owner != null ? owner.GetComponent<PlayerInputReader>() : null;
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        FirstMeleeSubSkillData data = SubSkillData;
        if (data == null)
        {
            Debug.LogError("[Player] 수호자의 의지에는 FirstMeleeSubSkillData가 필요합니다.", this);
            return; // 모델이 Idle 이라 막지 않는다 — 안전망(MaxDurationReached)이 끝낸다
        }

        // 방향 = Cast 시점 조준(결정 4). 반격 부채꼴도 이 방향을 쓴다. 방어 창은 방어 클립 Custom0 이 연다.
        model.Begin(direction, data.GuardAngle);
        Edit.Log($"[Skill] 수호자의 의지 — 방어 대기 (각도 {data.GuardAngle}°, 방어 창은 클립 Custom0~Custom1)", this);
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 방어 자세 애니(SubSkillGuard)는 컨트롤러가 시작 상태로 전 피어에서 재생한다. 반격 자세는 방어 성공 때
        // PlayerShieldVfx.ServerPlayGuardCounterPose 가, 반격 연출은 ServerInterruptWave 가 전 피어로 퍼뜨린다.
    }

    // 서버(또는 오프라인)에서만 온다(PlayerSkillController.HandleAnimationEvent). base 의 End → Completed 는 타지 않는다 —
    // 반격 → 종료 순서는 이 스킬이 정한다. 단계에 맞지 않는 이벤트(CrossFade 중 늦게 온 방어 클립 Custom1,
    // 오너 동기로 반격 상태가 다시 들어가 두 번 온 Hit·End)는 모델이 무시한다.
    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        switch (eventType)
        {
            case SkillAnimationEventType.Custom0:
                model.OnGuardWindowOpened();
                break;

            case SkillAnimationEventType.Custom1:
                if (model.OnGuardWindowClosed().HasEnded)
                    EndSelf(SkillEndReason.Completed);
                break;

            case SkillAnimationEventType.Hit:
                if (model.OnCounterHit())
                    FireCounter();
                break;

            case SkillAnimationEventType.End:
                GuardEventResult end = model.OnCounterEnd();
                // 🔴 순서 고정: 반격 → 종료. Hit 없이 End 가 오면 둘 다 참이다 — 종료를 먼저 처리하면 반격이 사라진다(모델 계약).
                if (end.CounterHitDue)
                    FireCounter();
                if (end.HasEnded)
                    EndSelf(SkillEndReason.Completed);
                break;
        }
    }

    // CounterHitDue 를 받으면 반드시 MarkCounterFired — 안 하면 HasPendingCounter 가 남아 OnEnd 보장 반격이 중복된다.
    private void FireCounter()
    {
        model.MarkCounterFired();
        if (IsServerPeer && owner.CurrentHealth > 0)
            ResolveCounterHit(owner.transform.position, model.GuardForward);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);

        // 반격 보장(결정 2): 성공했는데 반격 전에 시전자 사망 외의 이유로 끊겼으면 다음 프레임에 1회 실행한다.
        // 🔴 동기로 실행하지 않는다 — OnEnd 는 TwentyThreeBoss.TryCarryDashTarget 루프 안(돌진 스침 → 캐리 구속 →
        // HandleSkillStateExit)에서 불릴 수 있어, 여기서 보스 ReceiveAttack 을 부르면 보스 재진입이 된다
        // (보스가 죽은 뒤에도 _dashCarried·StartDashMove 가 이어질 수 있다).
        if (IsServerPeer && reason != SkillEndReason.CasterDied && model.HasPendingCounter)
        {
            model.MarkCounterFired();
            pendingDeferredCounter = true;
            deferredCounterForward = model.GuardForward;
            if (isActiveAndEnabled)
                StartCoroutine(RunDeferredCounterNextFrame());
            else
                pendingDeferredCounter = false;
        }

        model.Reset();

        // 입력 예약 없음(결정 22): 방어·반격 중 누른 입력은 E 가 끝나도 재생하지 않는다.
        // 순서 확인(PlayerSkillController): 서버 EndActiveSkillServer·클라 EndSkillClientRpc 모두
        // skill.OnEnd 를 stateController.EndSkill()(Idle 복귀)보다 먼저 부르고, 선입력 재생은 Idle/Move 상태의
        // Tick(TryConsumeActionInput)에서 일어난다 — 그래서 여기서 비우면 재생 전에 비워진다.
        // 외부 강제 이탈(HandleSkillStateExit)은 이미 다음 상태로 넘어간 뒤라 그 상태의 다음 Tick 전에 비워진다.
        if (owner != null && owner.IsInputSource && inputReader != null)
            inputReader.ClearInputBuffer();
    }

    private IEnumerator RunDeferredCounterNextFrame()
    {
        yield return null;

        if (!pendingDeferredCounter)
            yield break;

        pendingDeferredCounter = false;

        // 한 프레임 사이에 시전자가 파괴·사망했으면 반격하지 않는다(결정 2 — 사망 취소는 보장 대상이 아니다).
        if (owner == null || owner.CurrentHealth <= 0 || !IsServerPeer)
            yield break;

        // 원점은 지금 위치다 — 구속·이동 중이었으면 방어 성공 시점과 다를 수 있다(PLAN C8 리스크, 문서화).
        Edit.Log($"[Skill] 수호자의 의지 — deferred counter (E 종료 뒤 보장 반격, 방향 {deferredCounterForward})", this);
        ResolveCounterHit(owner.transform.position, deferredCounterForward);
    }

    /// <summary>
    /// [서버] 반격 1회 — 고정 방어 방향 기준 전방 counterAngle·counterRange 안의 대상에게 대상당 1회 피해.
    /// 흐름은 <see cref="FirstMeleeMainSkill"/>.OnHoldTick 과 같다. 방어 성공과 별개라
    /// 막은 공격자가 범위 밖이면 피해가 없다. 연출은 대상이 없어도 1회 낸다.
    /// </summary>
    private void ResolveCounterHit(Vector3 origin, Vector3 forward)
    {
        FirstMeleeSubSkillData data = SubSkillData;
        if (data == null || owner == null)
            return;

        forward.y = 0f;

        int hitCount = Physics.OverlapSphereNonAlloc(
            origin, data.CounterRange, counterHitResults, data.HittableLayers, QueryTriggerInteraction.Collide);
        counterTargets.Clear();
        counterLandedUnits.Clear();
        bool onHitBonusTaken = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = counterHitResults[i];
            if (hit == null)
                continue;

            if (!FirstMeleeGuardRules.IsInsideCounterSector(
                    origin, forward, hit.ClosestPoint(origin), hit.bounds.center, data.CounterRange, data.CounterAngle))
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);

            // Unit 이 아닌 대상(파괴 가능한 상자 등)은 hurtbox 를 키로 쓴다 — FirstMeleeMainSkill 과 같은 이유.
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || !counterTargets.Add(target))
                continue;

            int resolvedDamage = damageSnapshot;
            // 시체(공격 거절)는 첫 대상 자리를 차지하지 않는다 — 뒤의 살아 있는 첫 Unit 이 보너스를 받는다.
            if (!onHitBonusTaken && unit != null && unit.CurrentHealth > 0)
            {
                onHitBonusTaken = true;
                int bonus = owner.ServerTakeOnHitBonus(data.TriggersOnHit, unit);
                resolvedDamage = bonus >= int.MaxValue - resolvedDamage
                    ? int.MaxValue
                    : resolvedDamage + bonus;
            }

            AttackInfo attackInfo = new AttackInfo(resolvedDamage, DamageAttackType,
                isInterruptAttack: false,
                hitPattern: DamageHitPattern);
            AttackHitContext hitContext = new AttackHitContext(origin, owner.transform, hit, owner);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            if (unit != null)
                counterLandedUnits.Add(unit);

            Edit.Log($"[Skill] 수호자의 의지 반격 — {target.name} 피해 {attackInfo.damage}", this);
        }

        owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, counterLandedUnits, this);

        if (shieldVfx != null)
            shieldVfx.ServerInterruptWave();
    }

    // ── IPlayerIncomingAttackGuard (서버) ──

    public bool ServerTryAbsorbAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (owner == null)
            return false;

        Unit attacker = AttackSourceResolver.ResolveOriginUnit(hitContext);
        bool hasAttacker = attacker != null && attacker != owner && attacker.CurrentHealth > 0;
        Vector3 playerPos = owner.transform.position;
        Vector3 attackerPos = hasAttacker ? attacker.transform.position : playerPos;

        var incoming = new GuardIncoming(
            hasAttacker, attackerPos, playerPos, attackInfo.damage, PlayerHitDamageCap.LethalOverride);

        GuardVerdict verdict = model.Evaluate(incoming);
        if (verdict == GuardVerdict.BlockAndCounter)
        {
            // 반격 판정·종료는 반격 클립의 Hit·End 가 정한다 — 전 피어에서 그 상태로 넘긴다(오너 권한 NetworkAnimator).
            // 연출 창구가 비어 있으면 Hit·End 가 안 와 안전망(maxActiveDuration)이 끝내고, OnEnd 가 보장 반격을 낸다.
            if (shieldVfx != null)
                shieldVfx.ServerPlayGuardCounterPose(CounterStateHash, CounterCrossFadeSeconds);
            else
                Debug.LogWarning("[Skill] 수호자의 의지 — shieldVfx 가 비어 있어 반격 자세로 넘어가지 못합니다. 안전망이 E 를 끝냅니다.", this);

            Vector3 toAttacker = attackerPos - playerPos;
            toAttacker.y = 0f;
            Vector3 forward = model.GuardForward;
            forward.y = 0f;
            float angle = toAttacker.sqrMagnitude > 0.0001f ? Vector3.Angle(forward, toAttacker) : 0f;
            Edit.Log($"[Skill] 수호자의 의지 — 방어 성공: {attacker.name} (각도 {angle:0.#}°, 피해 {attackInfo.damage} 흡수)", this);
        }

        return verdict != GuardVerdict.Pass;
    }

    public bool ServerBlocksKnockback => model.BlocksKnockback;
}
