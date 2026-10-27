using UnityEngine;

/// <summary>팔라딘 E 단계. Idle → Guarding → (창 닫힘) Releasing → Finished, 또는 Guarding → (방어 성공) Countering → Finished.</summary>
public enum FirstMeleeGuardPhase
{
    Idle,
    Guarding,
    Countering,
    Finished,
    /// <summary>방어 창이 닫히고 해제 모션(SubSkillGuardEnd)이 끝나기를 기다린다 — 그 클립 End 가 E 를 끝낸다.</summary>
    Releasing,
}

/// <summary>들어온 피격 하나에 대한 판정.</summary>
public enum GuardVerdict
{
    /// <summary>정상 적용 — 방어 불가·범위 밖·방어 창 밖.</summary>
    Pass,
    /// <summary>첫 방어 성공 — 이 피해를 무시하고 반격 단계로 들어갔다.</summary>
    BlockAndCounter,
    /// <summary>반격 단계 중 피해 무시(방향 무관). 반격을 다시 걸지 않는다.</summary>
    Ignore,
}

/// <summary>E 가 끝난 이유. <see cref="None"/> 이면 이번 이벤트로 끝나지 않았다.</summary>
public enum GuardEndReason
{
    None,
    /// <summary>방어 창이 닫힐 때까지 성공하지 못했고 해제 모션이 끝났다 — 반격 없음.</summary>
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

/// <summary>홀드 방식 방어중을 이번 틱에 해제해야 하는 이유(<see cref="FirstMeleeGuardCounterModel.TickHold"/>). None = 유지.</summary>
public enum GuardHoldRelease
{
    None,
    /// <summary>E 를 뗐다(준비 중에 뗐으면 방어중에 들어온 첫 틱).</summary>
    KeyReleased,
    /// <summary>방어중 최대 유지 시간을 넘겼다.</summary>
    MaxDuration,
}

/// <summary>애니메이션 이벤트 하나를 넣은 결과(<see cref="FirstMeleeGuardCounterModel.OnEnd"/>).</summary>
public readonly struct GuardEventResult
{
    /// <summary>반격을 지금 실행해야 한다. 방어 성공 한 번당 한 번만 true.</summary>
    public readonly bool CounterHitDue;
    public readonly GuardEndReason Ended;

    public bool HasEnded => Ended != GuardEndReason.None;

    public GuardEventResult(bool counterHitDue, GuardEndReason ended)
    {
        CounterHitDue = counterHitDue;
        Ended = ended;
    }
}

/// <summary>
/// 팔라딘 E(수호자의 의지) 단계 상태기계(PLAN-paladin-counter-e C3, PLAN-anim-event-timing P1-③).
///
/// 순수 C# 이다(MonoBehaviour 아님 — <see cref="CounterWindow"/> 와 같은 모양). 시간을 재지 않는다 —
/// 방어 창·반격 판정·반격 종료는 호출측이 넘기는 <b>애니메이션 이벤트</b>로 정해진다.
/// 준비 클립(SubSkillGuard): Custom0 = 방어 창 열림 → 방어중 루프(SubSkillGuardIdle) → 해제 클립(SubSkillGuardEnd): Custom1 = 방어 창 닫힘,
/// End = E 종료. 반격 클립(SubSkillCounter): Hit = 반격 판정, End = 반격 종료.
/// 피해·반격 판정·넉백·종료 처리는 이 클래스가 하지 않는다 — FirstMeleeSubSkill 의 몫이다.
///
/// 🔴 확정한 규칙(2026-10-16 결정 1~25 권장안):
/// <list type="number">
/// <item><b>방어 불가</b>(주체 없음·사망, forcedUnblockable, 피해 0 이하)는 어느 단계에서든 Pass 다.</item>
/// <item><b>방어 성공</b>은 Guarding 이고 방어 창이 열려 있고 공격 주체가 방어 범위
///       (<see cref="FirstMeleeGuardRules.IsInsideGuardArc"/>) 안일 때뿐이다. 성공 즉시 Countering 으로 바뀐다.</item>
/// <item><b>반격 중 무시</b>는 Countering 인 동안(End 이벤트 전까지), 방향 무관이다.</item>
/// <item><b>추가 성공은 없다.</b> 반격 중 들어온 피격은 반격을 다시 걸지 않는다.</item>
/// <item><b>단계에 맞지 않는 이벤트는 무시한다.</b> 반격 상태로 CrossFade 하는 동안 해제 클립의 Custom1 이
///       뒤늦게 오거나, 원격 오너 동기로 반격 상태가 다시 들어가 Hit·End 가 두 번 올 수 있다.
///       End 는 Releasing(해제 클립 End)·Countering(반격 클립 End) 에서만 끝낸다 — Guarding 에서 온 End 는 무시한다.</item>
/// <item><b>E 재입력 해제</b>(할 일 94)는 방어중(Guarding + 서버 애니메이터 SubSkillGuardIdle)에서 한 번만 받는다
///       (<see cref="TryRequestRelease"/>).</item>
/// <item><b>해제 진입 = 창 닫힘</b>(할 일 95). 방어중 → 해제로 넘어가는 순간(E 재입력·홀드 뗌·최대 유지 시간·탭 방식 exitTime 전이)
///       Custom1 을 기다리지 않고 방어 창을 닫고 Releasing 이 된다 — 해제 동작 초반 피격은 반격하지 않는다. 뒤에 온 Custom1 은 무시한다.
///       대시 취소는 OnEnd → <see cref="Reset"/> 이 같은 순간 창을 닫는다.</item>
/// <item><b>홀드 방식</b>(할 일 95)은 오너의 누름/뗌 통보(<see cref="OnHoldInput"/>)가 처음 온 시전에서만 켜진다. 방어중에 들어온 뒤
///       뗐거나(준비 중에 뗐으면 방어중 첫 틱) 방어중 최대 유지 시간을 넘기면 해제를 한 번 낸다(<see cref="TickHold"/>).
///       해제 요청은 E 재입력 해제와 같은 한 번짜리라 둘이 겹쳐도 CrossFade 는 한 번이다.</item>
/// </list>
/// </summary>
public sealed class FirstMeleeGuardCounterModel
{
    Vector3 _guardForward;
    float _guardAngle;
    bool _guardWindowOpen;
    bool _blocked;
    bool _counterHitRaised;
    bool _releaseRequested;
    bool _holdMode;
    bool _holdKeyReleased;
    bool _holdEntered;
    float _holdEnteredAt;

    public FirstMeleeGuardPhase Phase { get; private set; } = FirstMeleeGuardPhase.Idle;

    /// <summary>Begin 에서 고정한 방어 방향. 반격 부채꼴도 이 방향 기준이다.</summary>
    public Vector3 GuardForward => _guardForward;

    /// <summary>Guarding 이고 방어 창(Custom0 ~ Custom1)이 열려 있다.</summary>
    public bool IsGuardWindowOpen => Phase == FirstMeleeGuardPhase.Guarding && _guardWindowOpen;

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
    /// E 가 이 이유로 끊길 때(OnEnd) 보장 반격을 내야 하는가(결정 2). 방어 성공 뒤 반격 판정(Hit) 전이고 시전자 사망이 아니면 예 —
    /// 대시 취소(할 일 95)·구속·넉백 모두 같다. 준비·방어중·해제 중 취소, 반격 판정이 이미 난 뒤 취소는 아니오.
    /// </summary>
    public bool CounterDueOnCancel(SkillEndReason reason) => HasPendingCounter && reason != SkillEndReason.CasterDied;

    /// <summary>
    /// 방어 대기를 시작한다 — 방어 창은 닫힌 채다(Custom0 이 열어 준다). 이전 상태는 모두 지운다.
    /// </summary>
    /// <param name="guardForward">고정 방어 방향(Cast 시점 조준).</param>
    /// <param name="guardAngle">전방 방어 전체 각도(도).</param>
    public void Begin(Vector3 guardForward, float guardAngle)
    {
        Reset();

        _guardForward = guardForward;
        _guardAngle = guardAngle;
        Phase = FirstMeleeGuardPhase.Guarding;
    }

    /// <summary>[Custom0] 방어 창을 연다. Guarding 이 아니면 무시한다. 멱등이다.</summary>
    public void OnGuardWindowOpened()
    {
        if (Phase == FirstMeleeGuardPhase.Guarding)
            _guardWindowOpen = true;
    }

    /// <summary>
    /// [Custom1] 방어 창을 닫고 Releasing 으로 넘어간다 — E 는 아직 끝나지 않는다(해제 클립 End 가 끝낸다).
    /// Guarding 일 때만 — 반격 중 뒤늦게 온 Custom1, 해제 진입 때 이미 닫혀 Releasing 인 뒤 온 Custom1 은 무시한다.
    /// </summary>
    public void OnGuardWindowClosed() => EnterReleasing();

    /// <summary>
    /// [서버] 방어중 → 해제(SubSkillGuardEnd) 진입 순간 — Custom1 을 기다리지 않고 방어 창을 바로 닫고 Releasing 으로 넘어간다(할 일 95).
    /// 해제 요청(<see cref="TryRequestRelease"/>·<see cref="TickHold"/>)은 스스로 부르므로, 호출측은 요청 없이 애니메이터가 해제로
    /// 넘어간 경우(탭 방식 방어중 exitTime 전이)에만 부른다. Guarding 이 아니면 무시한다 — 멱등이다.
    /// </summary>
    public void OnReleaseEntered() => EnterReleasing();

    void EnterReleasing()
    {
        if (Phase != FirstMeleeGuardPhase.Guarding)
            return;

        _guardWindowOpen = false;
        Phase = FirstMeleeGuardPhase.Releasing;
    }

    /// <summary>
    /// [E 재입력] 방어중 해제 요청을 받는가(할 일 94). 참이면 호출측(서버)이 해제 상태(SubSkillGuardEnd)로 전 피어 CrossFade 한다 —
    /// 받는 즉시 방어 창을 닫고 Releasing 으로 넘어간다(할 일 95, <see cref="OnReleaseEntered"/>). 해제 클립 Custom1 은 그 뒤 무시되고 End 가 끝낸다.
    /// 받는 때는 Guarding 이고 서버 애니메이터가 방어중 상태(SubSkillGuardIdle, 전이 중 아님)일 때뿐이다 —
    /// 준비(SubSkillGuard)·해제(Releasing 또는 이미 해제로 넘어가는 중)·반격(Countering) 중 재입력은 거짓(무시)이다.
    /// 한 시전에 한 번만 참이다 — 해제 CrossFade 중(Custom1 전)의 연타가 CrossFade 를 다시 걸지 않게 한다.
    /// </summary>
    /// <param name="animatorInGuardHold">서버 애니메이터가 방어중 상태에 있고 다른 상태로 전이 중이 아니다.</param>
    public bool TryRequestRelease(bool animatorInGuardHold)
    {
        if (Phase != FirstMeleeGuardPhase.Guarding || _releaseRequested || !animatorInGuardHold)
            return false;

        _releaseRequested = true;
        EnterReleasing();
        return true;
    }

    /// <summary>이번 시전에서 E 재입력 해제를 받았다(<see cref="TryRequestRelease"/>). Reset 전까지 유지된다.</summary>
    public bool ReleaseRequested => _releaseRequested;

    /// <summary>이번 시전이 홀드 방식이다(오너 누름/뗌 통보를 받았다). Reset 전까지 유지된다.</summary>
    public bool HoldMode => _holdMode;

    /// <summary>
    /// [홀드] 오너의 E 누름/뗌 통보(할 일 95). 첫 통보가 이 시전을 홀드 방식으로 만든다 — 탭 방식이면 통보가 오지 않는다.
    /// 한 번 뗀 뒤 다시 눌러도 유지로 돌아가지 않는다. E 가 진행 중(Guarding·Releasing·Countering)일 때만 받는다.
    /// </summary>
    public void OnHoldInput(bool held)
    {
        if (Phase == FirstMeleeGuardPhase.Idle || Phase == FirstMeleeGuardPhase.Finished)
            return;

        _holdMode = true;
        if (!held)
            _holdKeyReleased = true;
    }

    /// <summary>
    /// [홀드, 서버 매 틱] 방어중을 지금 해제해야 하는가. None 이 아니면 해제 요청을 받은 것이고(<see cref="ReleaseRequested"/>),
    /// 호출측이 해제 상태(SubSkillGuardEnd)로 전 피어 CrossFade 한다 — 방어 창은 바로 닫고 Releasing 으로 넘어간다(종료는 해제 클립 End).
    /// 홀드 방식이고 Guarding 이고 서버 애니메이터가 방어중(전이 중 아님)일 때만 본다. 최대 유지 시간은 방어중에 처음 들어온
    /// 틱부터 잰다. 준비 중에 이미 뗐으면 방어중 첫 틱에 KeyReleased(최소 한 프레임 방어중).
    /// </summary>
    /// <param name="animatorInGuardHold">서버 애니메이터가 방어중 상태에 있고 다른 상태로 전이 중이 아니다.</param>
    /// <param name="now">시각(초).</param>
    /// <param name="maxHoldSeconds">방어중 최대 유지 시간(초). 0 이하면 방어중 첫 틱에 해제한다.</param>
    public GuardHoldRelease TickHold(bool animatorInGuardHold, float now, float maxHoldSeconds)
    {
        if (!_holdMode || Phase != FirstMeleeGuardPhase.Guarding || _releaseRequested || !animatorInGuardHold)
            return GuardHoldRelease.None;

        if (!_holdEntered)
        {
            _holdEntered = true;
            _holdEnteredAt = now;
        }

        GuardHoldRelease release = _holdKeyReleased
            ? GuardHoldRelease.KeyReleased
            : now - _holdEnteredAt >= maxHoldSeconds
                ? GuardHoldRelease.MaxDuration
                : GuardHoldRelease.None;

        if (release != GuardHoldRelease.None)
        {
            _releaseRequested = true;
            EnterReleasing();
        }
        return release;
    }

    /// <summary>
    /// 피격 하나를 판정한다. <see cref="GuardVerdict.BlockAndCounter"/> 면 Countering 으로 바뀐다.
    /// BlockAndCounter·Ignore 는 호출측이 피해를 막아야 한다는 뜻이다.
    /// </summary>
    public GuardVerdict Evaluate(in GuardIncoming incoming)
    {
        if (incoming.IsUnblockable)
            return GuardVerdict.Pass;

        switch (Phase)
        {
            case FirstMeleeGuardPhase.Guarding:
                if (!_guardWindowOpen)
                    return GuardVerdict.Pass;
                if (!FirstMeleeGuardRules.IsInsideGuardArc(_guardForward, incoming.playerPos, incoming.attackerPos, _guardAngle))
                    return GuardVerdict.Pass;

                _blocked = true;
                _guardWindowOpen = false;
                Phase = FirstMeleeGuardPhase.Countering;
                return GuardVerdict.BlockAndCounter;

            case FirstMeleeGuardPhase.Countering:
                return GuardVerdict.Ignore;

            default:
                return GuardVerdict.Pass;
        }
    }

    /// <summary>
    /// [Hit] 반격 판정 시점. Countering 이고 반격을 아직 내지 않았을 때만 true — 중복 Hit 은 무시한다.
    /// true 를 받으면 호출측은 반격을 실행하고 <see cref="MarkCounterFired"/> 를 부른다.
    /// </summary>
    public bool OnCounterHit()
    {
        if (Phase != FirstMeleeGuardPhase.Countering || _counterHitRaised || CounterFired)
            return false;

        _counterHitRaised = true;
        return true;
    }

    /// <summary>
    /// [End] 해제 클립 End = Releasing 에서 <see cref="GuardEndReason.GuardExpired"/>,
    /// 반격 클립 End = Countering 에서 <see cref="GuardEndReason.CounterFinished"/> 로 끝난다. 그 밖의 단계는 무시한다.
    ///
    /// 🔴 계약(반격): Hit 이 아직 안 왔으면(클립에서 Hit 이 End 뒤로 밀렸거나 빠짐) <see cref="GuardEventResult.CounterHitDue"/>
    /// 도 같이 true 다. 호출측은 반드시 <b>CounterHitDue 를 먼저</b> 처리(반격 실행 → <see cref="MarkCounterFired"/>)하고
    /// 그다음 종료를 처리한다. 순서를 바꾸면 반격이 사라진다.
    /// </summary>
    public GuardEventResult OnEnd()
    {
        switch (Phase)
        {
            case FirstMeleeGuardPhase.Releasing:
                Phase = FirstMeleeGuardPhase.Finished;
                return new GuardEventResult(false, GuardEndReason.GuardExpired);

            case FirstMeleeGuardPhase.Countering:
                bool hitDue = OnCounterHit();
                Phase = FirstMeleeGuardPhase.Finished;
                return new GuardEventResult(hitDue, GuardEndReason.CounterFinished);

            default:
                return default;
        }
    }

    /// <summary>호출측이 반격(즉시 또는 취소 후 보장 실행)을 실행했다. 이후 HasPendingCounter 는 false.</summary>
    public void MarkCounterFired() => CounterFired = true;

    /// <summary>반격 단계 동안(End 이벤트 전까지) 모든 넉백을 무시한다(결정 9 — 단계 전체 적용).</summary>
    public bool BlocksKnockback => Phase == FirstMeleeGuardPhase.Countering;

    /// <summary>Idle 로 되돌린다. 스킬 종료(OnEnd)에서 반격 보장 여부를 읽은 뒤 호출한다. 멱등이다.</summary>
    public void Reset()
    {
        Phase = FirstMeleeGuardPhase.Idle;
        _guardForward = Vector3.zero;
        _guardAngle = 0f;
        _guardWindowOpen = false;
        _blocked = false;
        _counterHitRaised = false;
        _releaseRequested = false;
        _holdMode = false;
        _holdKeyReleased = false;
        _holdEntered = false;
        _holdEnteredAt = 0f;
        CounterFired = false;
    }
}
