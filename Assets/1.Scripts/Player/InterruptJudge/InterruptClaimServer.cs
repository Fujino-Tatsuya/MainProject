using System;
using System.Collections.Generic;

/// <summary>
/// [서버] 간파 대상의 성공 처리를 둘로 나눈 것(할 일 191 D9) — 승인된 신고를 <see cref="InterruptClaimServer"/> 가 여기로 넘긴다.
/// 세션 중엔 대상이 피격(ReceiveAttack·TakeDamage)으로 성공을 정하지 않는다(<see cref="InterruptManager.ServerVerifiesClaims"/>).
/// </summary>
public interface IInterruptClaimTarget
{
    /// <summary>
    /// [서버] 첫 승인 직전 대상 규칙 — 사망·레이드 기믹 규칙(23호 릴레이 잠금 등). false = 서버 거부.
    /// 동시 성공(D8) 후보에는 묻지 않는다 — 첫 승인의 효과(취약·기믹 진행)가 이미 상태를 바꿨기 때문.
    /// </summary>
    bool ServerAcceptsInterruptClaim(ulong casterClientId);

    /// <summary>
    /// [서버] 대상 성공 효과(창 소비·경직·그로기·제압 칸·섬광 등) — 창마다 첫 승인 1회만 부른다.
    /// 서버 창이 이미 닫혔어도(조기 닫힘 + 허용 오차 안, D7) 효과를 준다. <paramref name="caster"/> = 시전자(모르면 null).
    /// </summary>
    void ServerApplyInterruptSuccessEffect(ulong casterClientId, Unit caster);

    /// <summary>[서버] 시전자 개인 성공 기록(결과 화면 통계 등) — 승인마다(동시 성공자 각자) 부른다.</summary>
    void ServerRecordInterruptSuccess(ulong casterClientId);
}

/// <summary>서버 검증 수치(할 일 191 D11·D8) — 런타임 값은 <see cref="InterruptManager"/> 데이터 테이블 칸.</summary>
public struct InterruptVerifySettings
{
    /// <summary>시각 여유(초) — 그 클라 측정 지연 절반에 더한다.</summary>
    public double TimeSlack;
    /// <summary>판정 상자 수평 여유(m).</summary>
    public float DistanceSlack;
    /// <summary>대상 정면 허용 반각 여유(도).</summary>
    public float AngleSlack;
    /// <summary>창의 첫 승인 뒤 동시 성공으로 인정하는 시간(초).</summary>
    public double SimultaneousSeconds;

    public static InterruptVerifySettings Default => new InterruptVerifySettings
    {
        TimeSlack = 0.033,
        DistanceSlack = 0.2f,
        AngleSlack = 5f,
        SimultaneousSeconds = 0.05,
    };

    /// <summary>그 클라의 허용 오차 — 시각 = 측정 지연 절반 + 여유.</summary>
    public InterruptJudgeTolerance ToleranceFor(double halfLagSeconds) =>
        new InterruptJudgeTolerance
        {
            TimeSlack = Math.Max(0.0, halfLagSeconds) + Math.Max(0.0, TimeSlack),
            DistanceSlack = Math.Max(0f, DistanceSlack),
            AngleSlack = Math.Max(0f, AngleSlack),
        };
}

/// <summary>검증 결과.</summary>
public struct InterruptClaimCheck
{
    public bool Passed;
    public InterruptJudgeFailReason FailReason;
    public uint WindowId;
    /// <summary>통과한 표본 시각 오프셋(초).</summary>
    public double SampleOffset;
}

/// <summary>
/// [서버] 신고 검증(할 일 191 D11) — 2단계와 같은 판정 함수(<see cref="InterruptJudgeCalculator.Judge"/>)를 서버 이력으로 다시 돌린다. 별도 규칙 없음.
/// 허용 오차: 창 = 판정 시각 ± 시각 여유(조기 닫힘도 닫힘 시각 + 여유까지 인정, D7) · 상자 + 거리 여유 · 정면각 + 각도 여유.
/// 시각 여유는 표본 시각에도 쓴다 — 판정 시각 ± 여유 안의 시전자·대상 표본 중 하나라도 성공이면 통과(오너 권한 이동의 지연 보정).
/// </summary>
public static class InterruptClaimValidator
{
    /// <summary>표본 시각을 훑는 간격(초) — 60fps 한 프레임.</summary>
    public const double SampleStep = 1.0 / 60.0;

    public static InterruptClaimCheck Validate(IInterruptJudgeWorld world, in InterruptClaim claim, in InterruptJudgeTolerance tolerance)
    {
        var check = new InterruptClaimCheck { FailReason = InterruptJudgeFailReason.None };
        bool anyFailure = false;
        double slack = Math.Max(0.0, tolerance.TimeSlack);
        int steps = (int)Math.Ceiling(slack / SampleStep - 1e-9);

        // 판정 시각 → 바깥쪽으로 번갈아(0, +1, −1, +2, …), 마지막 칸은 여유 끝 그대로.
        for (int i = 0; i <= steps * 2; i++)
        {
            int k = (i + 1) / 2;
            double offset = Math.Min(k * SampleStep, slack) * (i % 2 == 1 ? 1.0 : -1.0);
            if (i == 0)
                offset = 0.0;

            InterruptJudgeResult r = InterruptJudgeCalculator.Judge(world, claim.ToAttempt(offset), tolerance);
            if (r.IsSuccess && r.TargetId == claim.TargetId)
            {
                check.Passed = true;
                check.FailReason = InterruptJudgeFailReason.None;
                check.WindowId = r.WindowId;
                check.SampleOffset = offset;
                return check;
            }

            // 실패 중 성공에 가장 가까운 것(검사 순서가 뒤인 것)을 남긴다. 다른 대상만 성공 = 이유 없음(서버 거부).
            InterruptJudgeFailReason reason = r.Outcome == InterruptJudgeOutcome.Failure && r.TargetId == claim.TargetId
                ? r.FailReason
                : InterruptJudgeFailReason.None;
            if (!anyFailure || reason > check.FailReason)
            {
                check.FailReason = reason;
                check.WindowId = r.WindowId;
            }
            anyFailure = true;
        }

        return check;
    }

    /// <summary>검증 실패 이유 → 거부 이유(QA 기록 칸).</summary>
    public static InterruptClaimRejectReason ToRejectReason(InterruptJudgeFailReason reason)
    {
        switch (reason)
        {
            case InterruptJudgeFailReason.OutOfRange: return InterruptClaimRejectReason.OutOfRange;
            case InterruptJudgeFailReason.Angle: return InterruptClaimRejectReason.Angle;
            case InterruptJudgeFailReason.NoWindow:
            case InterruptJudgeFailReason.WindowClosed: return InterruptClaimRejectReason.WindowClosed;
            default: return InterruptClaimRejectReason.ServerRejected;
        }
    }
}

/// <summary>
/// [서버] 창마다 승인 장부(할 일 191 D8) — 첫 승인 시각(서버 도착 시각)을 기록하고, 그 뒤 동시 인정 시간 안 승인 = 동시 성공, 이후 = 선착순 패배.
/// 경계 포함(첫 승인 + 동시 인정 시간 = 동시).
/// </summary>
public sealed class InterruptClaimLedger
{
    sealed class Entry
    {
        public ulong TargetId;
        public uint WindowId;
        public double FirstTime;
        public readonly List<ulong> Approved = new List<ulong>(3);
    }

    /// <summary>장부 보관 시간(초) — 이력 길이와 같다.</summary>
    public const double KeepSeconds = 1.0;

    readonly List<Entry> _entries = new List<Entry>();
    static readonly List<ulong> Empty = new List<ulong>();

    public int Count => _entries.Count;

    /// <summary>장부를 바꾸지 않고 순서만 본다(오래된 기록 정리는 한다).</summary>
    public InterruptClaimOrder Evaluate(ulong targetId, uint windowId, ulong actorId, double now, double simultaneousSeconds)
    {
        Prune(now);
        Entry entry = Find(targetId, windowId);
        if (entry == null)
            return InterruptClaimOrder.First;
        if (entry.Approved.Contains(actorId))
            return InterruptClaimOrder.Duplicate;
        if (now - entry.FirstTime > Math.Max(0.0, simultaneousSeconds) + 1e-9)
            return InterruptClaimOrder.Lost;
        return InterruptClaimOrder.Simultaneous;
    }

    /// <summary>순서를 보고 승인(첫·동시)이면 기록한다.</summary>
    public InterruptClaimOrder Register(ulong targetId, uint windowId, ulong actorId, double now, double simultaneousSeconds)
    {
        InterruptClaimOrder order = Evaluate(targetId, windowId, actorId, now, simultaneousSeconds);
        if (order == InterruptClaimOrder.First)
        {
            var entry = new Entry { TargetId = targetId, WindowId = windowId, FirstTime = now };
            entry.Approved.Add(actorId);
            _entries.Add(entry);
        }
        else if (order == InterruptClaimOrder.Simultaneous)
        {
            Find(targetId, windowId).Approved.Add(actorId);
        }

        return order;
    }

    /// <summary>창의 승인된 시전자(첫 승인이 [0]). 없으면 빈 목록.</summary>
    public IReadOnlyList<ulong> ApprovedActors(ulong targetId, uint windowId) =>
        Find(targetId, windowId)?.Approved ?? Empty;

    public void Prune(double now)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
            if (now - _entries[i].FirstTime > KeepSeconds)
                _entries.RemoveAt(i);
    }

    public void Clear() => _entries.Clear();

    Entry Find(ulong targetId, uint windowId)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].TargetId == targetId && _entries[i].WindowId == windowId)
                return _entries[i];
        return null;
    }
}

/// <summary>
/// [서버] 신고 처리 순서(할 일 191 §3.1 ③) — 소유 확인 → 검증(D11) → 대상 규칙 → 선착순/동시(D8) → 첫 승인만 대상 효과, 승인마다 개인 기록(D9).
/// 네트워크와 무관한 순수 처리 — <see cref="InterruptManager"/> 가 메시지·배포를 감싼다. 호스트 오너도 같은 함수를 직접 부른다.
/// </summary>
public sealed class InterruptClaimServer
{
    readonly InterruptClaimLedger _ledger = new InterruptClaimLedger();

    public InterruptClaimLedger Ledger => _ledger;

    /// <summary>RTT(ms) → 측정 지연 절반(초). 호스트 자신은 RTT 0 → 0.</summary>
    public static double HalfLagSeconds(double rttMs) => Math.Max(0.0, rttMs) / 2000.0;

    public InterruptClaimOutcome Process(IInterruptJudgeWorld world, in InterruptClaim claim, ulong senderClientId, bool senderOwnsActor,
        double halfLagSeconds, double now, in InterruptVerifySettings settings, IInterruptClaimTarget target, Unit caster = null)
    {
        var verdict = new InterruptClaimOutcome { Claim = claim, SenderClientId = senderClientId };
        if (!senderOwnsActor || target == null)
            return Reject(verdict, InterruptClaimRejectReason.ServerRejected, InterruptJudgeFailReason.None);

        InterruptClaimCheck check = InterruptClaimValidator.Validate(world, claim, settings.ToleranceFor(halfLagSeconds));
        verdict.WindowId = check.WindowId;
        if (!check.Passed)
            return Reject(verdict, InterruptClaimValidator.ToRejectReason(check.FailReason), check.FailReason);

        InterruptClaimOrder order = _ledger.Evaluate(claim.TargetId, check.WindowId, claim.ActorId, now, settings.SimultaneousSeconds);
        if (order == InterruptClaimOrder.Lost)
            return Reject(verdict, InterruptClaimRejectReason.FirstComeLost, InterruptJudgeFailReason.None);
        if (order == InterruptClaimOrder.Duplicate)
            return Reject(verdict, InterruptClaimRejectReason.ServerRejected, InterruptJudgeFailReason.None);
        if (order == InterruptClaimOrder.First && !target.ServerAcceptsInterruptClaim(senderClientId))
            return Reject(verdict, InterruptClaimRejectReason.ServerRejected, InterruptJudgeFailReason.None);

        _ledger.Register(claim.TargetId, check.WindowId, claim.ActorId, now, settings.SimultaneousSeconds);
        verdict.Approved = true;
        verdict.IsFirst = order == InterruptClaimOrder.First;
        if (verdict.IsFirst)
            target.ServerApplyInterruptSuccessEffect(senderClientId, caster);
        target.ServerRecordInterruptSuccess(senderClientId);
        return verdict;
    }

    public void Clear() => _ledger.Clear();

    static InterruptClaimOutcome Reject(InterruptClaimOutcome verdict, InterruptClaimRejectReason reason, InterruptJudgeFailReason judgeReason)
    {
        verdict.Approved = false;
        verdict.IsFirst = false;
        verdict.RejectReason = reason;
        verdict.JudgeFailReason = judgeReason;
        return verdict;
    }
}
