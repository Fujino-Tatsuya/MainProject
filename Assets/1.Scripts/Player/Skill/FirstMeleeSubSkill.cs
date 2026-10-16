using System.Collections;
using UnityEngine;

/// <summary>
/// E 수호자의 의지 — 전방 방어 대기 → 첫 방어 성공 시 그 피해 흡수 + 반격 단계(피해·넉백 무시) → 종료.
/// 단계·시간 경계는 <see cref="FirstMeleeGuardCounterModel"/>, 방향 판정은 <see cref="FirstMeleeGuardRules"/> 가 맡고
/// 이 클래스는 서버 배선만 한다. 피격 가로채기는 <see cref="Player"/>.ReceiveAttack 맨 위의 <see cref="IPlayerIncomingAttackGuard"/> 훅.
/// 설계·결정: PLAN-paladin-counter-e (Re:C 변경안).
/// </summary>
public class FirstMeleeSubSkill : PlayerInstantSkill, IPlayerIncomingAttackGuard
{
    // 반격 순간 연출 자리값(PlayerShieldVfx.ServerInterruptWave 재사용, 결정 25) — 반격 판정 커밋에서 쓴다.
    [Header("연출")]
    [Tooltip("연출 창구. 플레이어 루트의 PlayerShieldVfx 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PlayerShieldVfx shieldVfx;

    private readonly FirstMeleeGuardCounterModel model = new FirstMeleeGuardCounterModel();
    private PlayerInputReader inputReader;
    private bool maxDurationWarned;

    // 방어 성공 뒤 반격 전에 E 가 끊겼을 때(구속·기타 Cancelled) 다음 프레임에 실행할 반격 1회(결정 2).
    // 모델은 OnEnd 에서 Reset 되므로 방향은 따로 들고 있는다.
    private bool pendingDeferredCounter;
    private Vector3 deferredCounterForward;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // 방어 대기·반격 중 이동·회전 불가(결정 3). 평타·다른 스킬·대시는 Skill 상태 규칙으로 이미 막힌다(CanBeCanceledByDash 기본 false).
    public override bool CanMoveWhileActive => false;

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

        if (!maxDurationWarned && data.MaxActiveDuration < data.GuardDuration + data.CounterDuration)
        {
            maxDurationWarned = true;
            Debug.LogWarning($"[Skill] 수호자의 의지 — maxActiveDuration({data.MaxActiveDuration})이 guardDuration + counterDuration({data.GuardDuration + data.CounterDuration})보다 작습니다. 반격 전에 안전망이 E 를 끊을 수 있습니다.", this);
        }

        // 방향 = Cast 시점 조준(결정 4). 반격 부채꼴도 이 방향을 쓴다.
        model.Begin(Time.time, direction, data.GuardDuration, data.GuardAngle, data.CounterHitDelay, data.CounterDuration);
        Edit.Log($"[Skill] 수호자의 의지 — 방어 대기 {data.GuardDuration}s (각도 {data.GuardAngle}°)", this);
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 방어 자세 연출은 아직 없다(애니·VFX 기획 제외). 반격 연출은 서버에서 ServerInterruptWave 로 퍼뜨린다.
    }

    // 애니메이션 클립이 없다 — 종료는 모델 시간(OnTick)이 정한다. base 의 End → Completed 를 타지 않게 막는다.
    public override void OnAnimationEvent(SkillAnimationEventType eventType) { }

    public override void OnTick()
    {
        GuardTickResult tick = model.Tick(Time.time);

        // 🔴 순서 고정: 반격 → 종료. 같은 틱에 둘 다 오면 종료를 먼저 처리할 경우 반격이 사라진다(모델 계약).
        // CounterHitDue 를 받으면 반드시 MarkCounterFired — 안 하면 HasPendingCounter 가 남아 OnEnd 보장 반격이 중복된다.
        if (tick.CounterHitDue)
        {
            // TODO(C8): 반격 판정(전방 counterAngle·counterRange·대상당 1회·공격력 배율)
            model.MarkCounterFired();
            Edit.Log("[Skill] 수호자의 의지 — 반격 시점(피해는 아직 없음)", this);
        }

        if (tick.HasEnded)
            EndSelf(SkillEndReason.Completed);
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
        // TODO(C8): ResolveCounterHit(owner.transform.position) — 방향은 deferredCounterForward
        Edit.Log($"[Skill] 수호자의 의지 — deferred counter (E 종료 뒤 보장 반격, 방향 {deferredCounterForward}, 피해는 아직 없음)", this);
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

        GuardVerdict verdict = model.Evaluate(Time.time, incoming);
        if (verdict == GuardVerdict.BlockAndCounter)
        {
            Vector3 toAttacker = attackerPos - playerPos;
            toAttacker.y = 0f;
            Vector3 forward = model.GuardForward;
            forward.y = 0f;
            float angle = toAttacker.sqrMagnitude > 0.0001f ? Vector3.Angle(forward, toAttacker) : 0f;
            Edit.Log($"[Skill] 수호자의 의지 — 방어 성공: {attacker.name} (각도 {angle:0.#}°, 피해 {attackInfo.damage} 흡수)", this);
        }

        return verdict != GuardVerdict.Pass;
    }

    public bool ServerBlocksKnockback => model.BlocksKnockback(Time.time);
}
