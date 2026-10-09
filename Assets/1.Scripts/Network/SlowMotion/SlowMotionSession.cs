/// <summary><see cref="SlowMotionSession.Apply"/> 결과. 로컬 이벤트(Started/Failed/Reset)는 앞의 셋에서만 쏜다.</summary>
public enum SlowMotionApplyResult
{
    /// <summary>받아들였고 슬로우가 (다시) 시작됐다.</summary>
    Started,
    /// <summary>받아들였고 진행 중이던 슬로우가 실패 복귀로 넘어갔다.</summary>
    Failed,
    /// <summary>받아들였고 진행 중이던 슬로우가 즉시 1.0 으로 끊겼다.</summary>
    Reset,
    /// <summary>규칙상 받아들였지만 바뀐 것이 없다(이미 끝난 슬로우의 Fail·Reset).</summary>
    NoEffect,
    /// <summary>버렸다 — 낡은 발동 번호·중복·역순·알 수 없는 종류.</summary>
    Ignored,
}

/// <summary>
/// 서버 이벤트(<see cref="SlowMotionEvent"/>)를 받아 <see cref="SlowMotionTimeline"/> 에 반영하는 문지기.
/// 서버·클라 모두 같은 객체로 같은 규칙을 적용한다 — 서버는 자기가 만든 이벤트를, 클라는 받은 이벤트를 넣는다.
///
/// 순수 C# 이다(EditMode 테스트 대상). 시각은 호출측이 이벤트에 실어 넣는다.
///
/// 🔴 발동 번호 규칙(PLAN-interrupt-slowmo D6·D8):
/// <list type="number">
/// <item><b>Start 는 현재 번호보다 큰 번호만.</b> 같은 번호(중복)·작은 번호(역순)는 버린다.</item>
/// <item><b>Fail 은 현재 번호와 같을 때만.</b> 옛 스킬의 늦은 빗나감이 새 슬로우를 죽이지 않는다.</item>
/// <item><b>Reset 은 현재 번호 이상이면.</b> 옛 번호의 Reset 은 그 뒤에 시작된 슬로우를 끊지 않는다.</item>
/// <item><b>시각이 마지막 이벤트보다 이르면 버린다</b>(타임라인의 시간순 규칙과 같다 — 번호만 맞고 시각이 역행한 이벤트가
///       번호만 올리고 타임라인엔 안 들어가는 어긋남을 막는다).</item>
/// </list>
/// </summary>
public sealed class SlowMotionSession
{
    SlowMotionTimeline _timeline = new SlowMotionTimeline();
    double _lastEventTime = double.NegativeInfinity;

    /// <summary>마지막으로 받아들인 발동 번호. 0 = 아직 없음.</summary>
    public uint CurrentTriggerId { get; private set; }

    /// <summary>마지막으로 시작된 슬로우의 발동자 clientId. 슬로우가 끝난 뒤에도 값은 남는다 — 진행 여부는 <see cref="IsActiveAt"/>.</summary>
    public ulong CurrentTriggerClientId { get; private set; }

    public SlowMotionApplyResult Apply(in SlowMotionEvent e)
    {
        if (double.IsNaN(e.Time) || e.Time < _lastEventTime)
            return SlowMotionApplyResult.Ignored;

        switch (e.Kind)
        {
            case SlowMotionEventKind.Start:
            {
                if (e.TriggerId <= CurrentTriggerId)
                    return SlowMotionApplyResult.Ignored;

                Accept(e);
                CurrentTriggerClientId = e.TriggerClientId;
                _timeline.Start(e.Time, e.ToProfile());
                return SlowMotionApplyResult.Started;
            }
            case SlowMotionEventKind.Fail:
            {
                if (e.TriggerId == 0 || e.TriggerId != CurrentTriggerId)
                    return SlowMotionApplyResult.Ignored;

                var before = _timeline.PhaseAt(e.Time);
                Accept(e);
                _timeline.Fail(e.Time);
                return before == SlowMotionPhase.Enter || before == SlowMotionPhase.Hold || before == SlowMotionPhase.Exit
                    ? SlowMotionApplyResult.Failed
                    : SlowMotionApplyResult.NoEffect;
            }
            case SlowMotionEventKind.Reset:
            {
                if (e.TriggerId < CurrentTriggerId)
                    return SlowMotionApplyResult.Ignored;

                bool wasActive = _timeline.IsActiveAt(e.Time);
                Accept(e);
                _timeline.ForceReset(e.Time);
                return wasActive ? SlowMotionApplyResult.Reset : SlowMotionApplyResult.NoEffect;
            }
            default:
                return SlowMotionApplyResult.Ignored;
        }
    }

    /// <summary>세션 종료 — 타임라인·번호를 처음 상태로. 새 세션의 서버는 번호를 1 부터 다시 매긴다.</summary>
    public void Clear()
    {
        _timeline = new SlowMotionTimeline();
        _lastEventTime = double.NegativeInfinity;
        CurrentTriggerId = 0;
        CurrentTriggerClientId = 0;
    }

    public float ScaleAt(double t) => _timeline.ScaleAt(t);
    public SlowMotionPhase PhaseAt(double t) => _timeline.PhaseAt(t);
    public bool IsActiveAt(double t) => _timeline.IsActiveAt(t);
    public double LostTimeUntil(double t) => _timeline.LostTimeUntil(t);

    void Accept(in SlowMotionEvent e)
    {
        _lastEventTime = e.Time;
        if (e.TriggerId > CurrentTriggerId)
            CurrentTriggerId = e.TriggerId;
    }
}
