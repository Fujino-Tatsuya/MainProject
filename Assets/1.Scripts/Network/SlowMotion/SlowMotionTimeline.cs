using System;
using System.Collections.Generic;

/// <summary>슬로우 모션 단계. 유휴·종료 후는 <see cref="None"/>.</summary>
public enum SlowMotionPhase
{
    None,
    Enter,
    Hold,
    Exit,
    FailExit,
}

/// <summary>
/// 전역 슬로우 모션의 산식 — 시각 t 의 배율·단계, 그리고 처음부터 t 까지 "슬로우로 덜 흐른 시간".
///
/// 순수 C# 이다(MonoBehaviour·Unity 시간 API 없음). 시간 출처를 모른다 — 호출측이 넘기는 t 는
/// "일시정지를 뺀 실시간" 도메인의 double 이다. 전 피어가 같은 이벤트·같은 t 를 넣으면 같은 값이 나와야
/// GameNow 를 별도 동기화 없이 맞출 수 있다(PLAN-interrupt-slowmo D7).
///
/// 🔴 확정한 규칙(2026-10-09, PLAN-interrupt-slowmo D5·D6·R4):
/// <list type="number">
/// <item><b>마지막 것이 이긴다.</b> 진행 중에 <see cref="Start"/> 하면 <b>그 순간의 배율에서</b> 진입부터 다시 —
///       배율이 튀면 화면이 덜컥거린다. 유휴면 1.0 에서 출발한다.</item>
/// <item><b>실패 복귀는 진행 중(진입·유지·복귀)일 때만.</b> 유휴·이미 실패 복귀 중이면 무시한다(멱등) —
///       서버가 빗나감을 두 번 알려도 곡선이 다시 시작되지 않는다.</item>
/// <item><b><see cref="LostTimeUntil"/> 은 해석적이고 단조 비감소다.</b> GameNow = 기존 − 이 값이라,
///       줄어들면 GameNow 가 되감긴다(상태이상 지속시간이 늘어나는 식의 버그). 1 − 배율 ≥ 0 이라 구조적으로 보장된다.</item>
/// <item><b>이벤트는 시간순으로만 받는다.</b> 마지막 이벤트보다 이른 이벤트는 조용히 버린다 — 과거를 고쳐 쓰면
///       이미 내준 GameNow 가 바뀐다. 조회는 과거 시각도 정확히 계산한다(구간 기록을 들고 있다).</item>
/// <item><b>길이 0 단계는 건너뛴다.</b> 전부 0 이면 시작해도 즉시 유휴다.</item>
/// </list>
/// </summary>
public sealed class SlowMotionTimeline
{
    // 구간 기록 상한. 과거 조회는 클라 시계 흔들림(수십 ms) 정도만 필요하다 — 무한히 쌓지 않는다.
    // 이보다 오래된 시각은 남은 가장 오래된 구간 시작 값으로 근사한다.
    const int MaxSegments = 64;

    enum SegmentKind { Idle, Run, FailRun }

    // 이벤트 하나가 연 구간. 다음 이벤트가 올 때까지 이 구간의 식으로 평가한다.
    readonly struct Segment
    {
        public readonly SegmentKind Kind;
        public readonly double Start;
        public readonly double LostAtStart;   // 이 구간 이전까지 확정된 누적 손실
        public readonly double From;          // 구간 시작 배율
        public readonly double Target;        // 유지 배율(Run)
        public readonly double Enter, Hold, Exit;
        public readonly double FailExit;      // Run: 이 슬로우가 실패했을 때 쓸 길이 / FailRun: 자기 길이

        public Segment(SegmentKind kind, double start, double lostAtStart, double from, double target,
                       double enter, double hold, double exit, double failExit)
        {
            Kind = kind;
            Start = start;
            LostAtStart = lostAtStart;
            From = from;
            Target = target;
            Enter = enter;
            Hold = hold;
            Exit = exit;
            FailExit = failExit;
        }
    }

    readonly List<Segment> _segments = new List<Segment>();
    bool _pruned; // 오래된 구간을 버린 적이 있다 — 그 이전 조회는 0 이 아니다

    /// <summary>
    /// 슬로우를 시작한다. 진행 중이면 현재 배율에서 진입부터 다시(last-wins), 유휴면 1.0 에서.
    /// 마지막 이벤트보다 이른 t 는 무시한다.
    /// </summary>
    public void Start(double t, SlowMotionProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (!AcceptsEventAt(t)) return;

        Push(new Segment(SegmentKind.Run, t, LostTimeUntil(t), ScaleAtExact(t), profile.ClampedScale,
            profile.ClampedEnterDuration, profile.ClampedHoldDuration, profile.ClampedExitDuration,
            profile.ClampedFailExitDuration));
    }

    /// <summary>
    /// 빗나감 — 진행 중(진입·유지·복귀)이면 현재 배율에서 실패 복귀로 넘긴다.
    /// 유휴·종료 후·이미 실패 복귀 중이면 무시한다.
    /// </summary>
    public void Fail(double t)
    {
        if (!AcceptsEventAt(t)) return;

        var phase = PhaseAt(t);
        if (phase != SlowMotionPhase.Enter && phase != SlowMotionPhase.Hold && phase != SlowMotionPhase.Exit) return;

        var current = _segments[_segments.Count - 1];
        Push(new Segment(SegmentKind.FailRun, t, LostTimeUntil(t), ScaleAtExact(t), 1.0,
            0.0, 0.0, 0.0, current.FailExit));
    }

    /// <summary>컷신·씬 전환용 — 즉시 1.0 유휴. 그 시각까지의 누적 손실은 그대로 남는다.</summary>
    public void ForceReset(double t)
    {
        if (!AcceptsEventAt(t)) return;
        if (_segments.Count == 0) return; // 한 번도 안 느려졌으면 기록할 것이 없다

        Push(new Segment(SegmentKind.Idle, t, LostTimeUntil(t), 1.0, 1.0, 0.0, 0.0, 0.0, 0.0));
    }

    /// <summary>시각 t 의 시간 배율. 유휴·종료 후 1.0.</summary>
    public float ScaleAt(double t) => (float)ScaleAtExact(t);

    /// <summary>시각 t 의 단계.</summary>
    public SlowMotionPhase PhaseAt(double t)
    {
        if (!TryFindSegment(t, out var s)) return SlowMotionPhase.None;

        double e = t - s.Start;
        switch (s.Kind)
        {
            case SegmentKind.Run:
                if (e < s.Enter) return SlowMotionPhase.Enter;
                if (e < s.Enter + s.Hold) return SlowMotionPhase.Hold;
                if (e < s.Enter + s.Hold + s.Exit) return SlowMotionPhase.Exit;
                return SlowMotionPhase.None;
            case SegmentKind.FailRun:
                return e < s.FailExit ? SlowMotionPhase.FailExit : SlowMotionPhase.None;
            default:
                return SlowMotionPhase.None;
        }
    }

    /// <summary>시각 t 에 슬로우가 진행 중인가(단계가 <see cref="SlowMotionPhase.None"/> 이 아님).</summary>
    public bool IsActiveAt(double t) => PhaseAt(t) != SlowMotionPhase.None;

    /// <summary>
    /// 처음부터 t 까지 ∫(1 − 배율) dt — "슬로우로 덜 흐른 시간"(초). 단조 비감소.
    /// 첫 이벤트 이전이면 0.
    /// </summary>
    public double LostTimeUntil(double t)
    {
        if (!TryFindSegment(t, out var s))
            return _pruned ? _segments[0].LostAtStart : 0.0;

        double e = t - s.Start;
        switch (s.Kind)
        {
            case SegmentKind.Run:
            {
                double lost = 0.0;

                // 진입: linear From → Target. ∫₀ˣ (1 − From − (Target − From)·τ/E) dτ
                double x = Math.Min(e, s.Enter);
                if (s.Enter > 0.0)
                    lost += x * (1.0 - s.From) - (s.Target - s.From) * x * x / (2.0 * s.Enter);

                // 유지: Target 고정.
                x = Math.Min(e - s.Enter, s.Hold);
                if (x > 0.0)
                    lost += x * (1.0 - s.Target);

                // 복귀: ease-out Target → 1.
                x = Math.Min(e - s.Enter - s.Hold, s.Exit);
                if (x > 0.0)
                    lost += EaseOutLost(1.0 - s.Target, x, s.Exit);

                return s.LostAtStart + lost;
            }
            case SegmentKind.FailRun:
                return s.LostAtStart + EaseOutLost(1.0 - s.From, Math.Min(e, s.FailExit), s.FailExit);
            default:
                return s.LostAtStart;
        }
    }

    double ScaleAtExact(double t)
    {
        if (!TryFindSegment(t, out var s)) return 1.0;

        double e = t - s.Start;
        switch (s.Kind)
        {
            case SegmentKind.Run:
                if (e < s.Enter) return s.From + (s.Target - s.From) * (e / s.Enter);
                e -= s.Enter;
                if (e < s.Hold) return s.Target;
                e -= s.Hold;
                if (e < s.Exit) return s.Target + (1.0 - s.Target) * EaseOut(e / s.Exit);
                return 1.0;
            case SegmentKind.FailRun:
                if (e < s.FailExit) return s.From + (1.0 - s.From) * EaseOut(e / s.FailExit);
                return 1.0;
            default:
                return 1.0;
        }
    }

    // ease-out quad: 1 − (1 − u)². 출발은 빠르고 1 에 부드럽게 닿는다.
    static double EaseOut(double u) => 1.0 - (1.0 - u) * (1.0 - u);

    // ease-out 으로 (1 − gap) → 1 을 가는 동안 ∫₀ˣ (1 − 배율) dτ.
    // 1 − 배율 = gap·(1 − τ/D)² 이므로 = gap·D/3·(1 − (1 − x/D)³).
    static double EaseOutLost(double gap, double x, double duration)
    {
        if (duration <= 0.0 || x <= 0.0) return 0.0;

        double r = 1.0 - x / duration;
        return gap * duration / 3.0 * (1.0 - r * r * r);
    }

    // NaN 은 비교가 모두 false 라 !(t >= …) 로 함께 거른다.
    bool AcceptsEventAt(double t) =>
        !double.IsNaN(t) && (_segments.Count == 0 || t >= _segments[_segments.Count - 1].Start);

    void Push(Segment segment)
    {
        _segments.Add(segment);
        if (_segments.Count <= MaxSegments) return;

        _segments.RemoveAt(0);
        _pruned = true;
    }

    // t 를 포함하는 구간 = 시작이 t 이하인 마지막 구간. 같은 시각 이벤트가 여럿이면 나중 것이 이긴다.
    bool TryFindSegment(double t, out Segment segment)
    {
        int lo = 0, hi = _segments.Count; // 시작 > t 인 첫 인덱스를 찾는다
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_segments[mid].Start <= t) lo = mid + 1;
            else hi = mid;
        }

        if (lo == 0)
        {
            segment = default;
            return false;
        }

        segment = _segments[lo - 1];
        return true;
    }
}
