using UnityEngine;

/// <summary>팔라딘 E 단계. Idle → Guarding → (방어 성공) Countering → Finished.</summary>
public enum FirstMeleeGuardPhase
{
    Idle,
    Guarding,
    Countering,
    Finished,
}

/// <summary>들어온 피격 하나에 대한 판정.</summary>
public enum GuardVerdict
{
    /// <summary>정상 적용 — 방어 불가·범위 밖·시간 밖.</summary>
    Pass,
    /// <summary>첫 방어 성공 — 이 피해를 무시하고 반격 단계로 들어갔다.</summary>
    BlockAndCounter,
    /// <summary>반격 단계 중 피해 무시(방향 무관). 반격을 다시 걸지 않는다.</summary>
    Ignore,
}

/// <summary>E 가 끝난 이유. <see cref="None"/> 이면 이번 틱에 끝나지 않았다.</summary>
public enum GuardEndReason
{
    None,
    /// <summary>방어 대기 시간 안에 성공하지 못했다 — 반격 없음.</summary>
    GuardExpired,
    /// <summary>반격 단계가 끝났다.</summary>
    CounterFinished,
}

/// <summary>
/// 피격 하나의 입력. 공격 주체 해석·생존 확인·LethalOverride 판단은 호출측(서버 훅)이 끝내고 넘긴다.
/// </summary>
public readonly struct GuardIncoming
{
    /// <summary>공격 주체를 찾았고 살아 있다. false 면 방어 불가.</summary>
    public readonly bool hasAttacker;
    public readonly Vector3 attackerPos;
    public readonly Vector3 playerPos;
    /// <summary>이 히트의 피해. 0 이하면 방어 불가(방어 성공 트리거 안 함).</summary>
    public readonly float damage;
    /// <summary>강제 방어 불가(레이드 자폭 LethalOverride 등).</summary>
    public readonly bool forcedUnblockable;

    public GuardIncoming(bool hasAttacker, Vector3 attackerPos, Vector3 playerPos, float damage, bool forcedUnblockable)
    {
        this.hasAttacker = hasAttacker;
        this.attackerPos = attackerPos;
        this.playerPos = playerPos;
        this.damage = damage;
        this.forcedUnblockable = forcedUnblockable;
    }

    /// <summary>주체 없음·강제 방어 불가·피해 0 이하 — 단계와 무관하게 항상 Pass.</summary>
    public bool IsUnblockable => !hasAttacker || forcedUnblockable || damage <= 0f;
}

/// <summary><see cref="FirstMeleeGuardCounterModel.Tick"/> 결과.</summary>
public readonly struct GuardTickResult
{
    /// <summary>반격 판정 시각에 닿았다. 방어 성공 한 번당 한 번만 true.</summary>
    public readonly bool CounterHitDue;
    public readonly GuardEndReason Ended;

    public bool HasEnded => Ended != GuardEndReason.None;

    public GuardTickResult(bool counterHitDue, GuardEndReason ended)
    {
        CounterHitDue = counterHitDue;
        Ended = ended;
    }
}

/// <summary>
/// 팔라딘 E(수호자의 의지) 단계 상태기계(PLAN-paladin-counter-e C3, Re:C 변경안 §2·§5).
///
/// 순수 C# 이다(MonoBehaviour 아님 — <see cref="CounterWindow"/> 와 같은 모양). 시간은 호출측이 넘기는 절대 시각
/// (서버 Time.time)이다. 피해·반격 판정·넉백·종료 처리는 이 클래스가 하지 않는다 — FirstMeleeSubSkill 의 몫이다.
///
/// 🔴 확정한 규칙(2026-10-16 결정 1~25 권장안):
/// <list type="number">
/// <item><b>방어 불가</b>(주체 없음·사망, forcedUnblockable, 피해 0 이하)는 어느 단계에서든 Pass 다.</item>
/// <item><b>방어 성공</b>은 Guarding 이고 <c>now &lt; guardEnd</c> 이고 공격 주체가 방어 범위
///       (<see cref="FirstMeleeGuardRules.IsInsideGuardArc"/>) 안일 때뿐이다. 성공 즉시 Countering 으로 바뀐다.</item>
/// <item><b>반격 중 무시</b>는 <c>now &lt; counterEndAt</c> 일 때만, 방향 무관이다. 그 시각이 지난 피격은
///       종료 처리(Tick) 전이라도 Pass — 실행 순서에 기대지 않는다.</item>
/// <item><b>추가 성공은 없다.</b> 반격 중 들어온 피격은 반격을 다시 걸거나 counterEndAt 을 늘리지 않는다.</item>
/// <item>counterHitDelay 는 Begin 에서 [0, counterDuration] 으로 Clamp 한다 — 반격 판정이 반격 종료 뒤로 밀리지 않게.</item>
/// </list>
/// </summary>
public sealed class FirstMeleeGuardCounterModel
{
    Vector3 _guardForward;
    float _guardAngle;
    float _guardEndAt;
    float _counterHitDelay;
    float _counterDuration;
    float _counterHitAt;
    float _counterEndAt;
    bool _blocked;
    bool _counterHitRaised;

    public FirstMeleeGuardPhase Phase { get; private set; } = FirstMeleeGuardPhase.Idle;

    /// <summary>Begin 에서 고정한 방어 방향. 반격 부채꼴도 이 방향 기준이다.</summary>
    public Vector3 GuardForward => _guardForward;

    /// <summary>반격 판정 시각(성공 시각 + Clamp 된 counterHitDelay). 성공 전에는 의미 없다.</summary>
    public float CounterHitAt => _counterHitAt;

    /// <summary>반격 종료 시각(성공 시각 + counterDuration). 성공 전에는 의미 없다.</summary>
    public float CounterEndAt => _counterEndAt;

    /// <summary>Begin 에서 Clamp 된 반격 판정 지연.</summary>
    public float CounterHitDelay => _counterHitDelay;

    /// <summary>방어에 성공했다(이번 시전에서). Reset 전까지 유지된다.</summary>
    public bool Blocked => _blocked;

    /// <summary>호출측이 반격을 실행했다고 알렸다(<see cref="MarkCounterFired"/>).</summary>
    public bool CounterFired { get; private set; }

    /// <summary>
    /// 방어에 성공했고 반격을 아직 실행하지 않았다. E 가 시전자 사망 외의 이유로 끊길 때 이 값이 true 면
    /// 호출측이 다음 프레임에 반격 1회를 보장 실행한다(결정 2).
    /// </summary>
    public bool HasPendingCounter => _blocked && !CounterFired;

    /// <summary>
    /// 방어 대기를 시작한다. 이전 상태는 모두 지운다.
    /// </summary>
    /// <param name="guardForward">고정 방어 방향(Cast 시점 조준).</param>
    /// <param name="guardDuration">방어 대기 최대 시간(초). 음수는 0.</param>
    /// <param name="guardAngle">전방 방어 전체 각도(도).</param>
    /// <param name="counterHitDelay">성공 → 반격 판정 지연(초). [0, counterDuration] 으로 Clamp.</param>
    /// <param name="counterDuration">성공 → 반격 종료(피해·넉백 무시 종료) 시간(초). 음수는 0.</param>
    public void Begin(float now, Vector3 guardForward, float guardDuration, float guardAngle,
        float counterHitDelay, float counterDuration)
    {
        Reset();

        _guardForward = guardForward;
        _guardAngle = guardAngle;
        _guardEndAt = now + Mathf.Max(0f, guardDuration);
        _counterDuration = Mathf.Max(0f, counterDuration);
        _counterHitDelay = Mathf.Clamp(counterHitDelay, 0f, _counterDuration);
        Phase = FirstMeleeGuardPhase.Guarding;
    }

    /// <summary>
    /// 피격 하나를 판정한다. <see cref="GuardVerdict.BlockAndCounter"/> 면 Countering 으로 바뀌고
    /// 반격 시각들이 잡힌다. BlockAndCounter·Ignore 는 호출측이 피해를 막아야 한다는 뜻이다.
    /// </summary>
    public GuardVerdict Evaluate(float now, in GuardIncoming incoming)
    {
        if (incoming.IsUnblockable)
            return GuardVerdict.Pass;

        switch (Phase)
        {
            case FirstMeleeGuardPhase.Guarding:
                if (now >= _guardEndAt)
                    return GuardVerdict.Pass;
                if (!FirstMeleeGuardRules.IsInsideGuardArc(_guardForward, incoming.playerPos, incoming.attackerPos, _guardAngle))
                    return GuardVerdict.Pass;

                _blocked = true;
                _counterHitAt = now + _counterHitDelay;
                _counterEndAt = now + _counterDuration;
                Phase = FirstMeleeGuardPhase.Countering;
                return GuardVerdict.BlockAndCounter;

            case FirstMeleeGuardPhase.Countering:
                return now < _counterEndAt ? GuardVerdict.Ignore : GuardVerdict.Pass;

            default:
                return GuardVerdict.Pass;
        }
    }

    /// <summary>
    /// 시간을 흘린다(서버 틱마다).
    ///
    /// 🔴 계약: 프레임이 튀면 <see cref="GuardTickResult.CounterHitDue"/> 와
    /// <see cref="GuardTickResult.Ended"/>(CounterFinished)가 <b>같은 틱에 둘 다</b> 참일 수 있다.
    /// 호출측은 반드시 <b>CounterHitDue 를 먼저</b> 처리(반격 실행 → <see cref="MarkCounterFired"/>)하고
    /// 그다음 종료를 처리한다. 순서를 바꾸면 반격이 사라진다.
    /// CounterHitDue 는 방어 성공 한 번당 한 번만 true 다.
    /// </summary>
    public GuardTickResult Tick(float now)
    {
        switch (Phase)
        {
            case FirstMeleeGuardPhase.Guarding:
                if (now < _guardEndAt)
                    return default;
                Phase = FirstMeleeGuardPhase.Finished;
                return new GuardTickResult(false, GuardEndReason.GuardExpired);

            case FirstMeleeGuardPhase.Countering:
                bool hitDue = false;
                if (!_counterHitRaised && !CounterFired && now >= _counterHitAt)
                {
                    _counterHitRaised = true;
                    hitDue = true;
                }

                GuardEndReason ended = GuardEndReason.None;
                if (now >= _counterEndAt)
                {
                    Phase = FirstMeleeGuardPhase.Finished;
                    ended = GuardEndReason.CounterFinished;
                }
                return new GuardTickResult(hitDue, ended);

            default:
                return default;
        }
    }

    /// <summary>호출측이 반격(즉시 또는 취소 후 보장 실행)을 실행했다. 이후 HasPendingCounter 는 false.</summary>
    public void MarkCounterFired() => CounterFired = true;

    /// <summary>반격 단계 동안(<c>now &lt; counterEndAt</c>) 모든 넉백을 무시한다(결정 9 — 단계 전체 적용).</summary>
    public bool BlocksKnockback(float now) => Phase == FirstMeleeGuardPhase.Countering && now < _counterEndAt;

    /// <summary>Idle 로 되돌린다. 스킬 종료(OnEnd)에서 반격 보장 여부를 읽은 뒤 호출한다. 멱등이다.</summary>
    public void Reset()
    {
        Phase = FirstMeleeGuardPhase.Idle;
        _guardForward = Vector3.zero;
        _guardAngle = 0f;
        _guardEndAt = 0f;
        _counterHitDelay = 0f;
        _counterDuration = 0f;
        _counterHitAt = 0f;
        _counterEndAt = 0f;
        _blocked = false;
        _counterHitRaised = false;
        CounterFired = false;
    }
}
