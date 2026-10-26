/// <summary>C7d — 끼임 단계(<see cref="QABoss103StuckEscalation"/>)의 이번 틱 행동.</summary>
public enum QABoss103StuckStep
{
    None = 0,
    /// <summary>뒤·옆 대시로 빠져나온다(대시 충전이 있을 때만 — 봇이 판단).</summary>
    Dash = 1,
    /// <summary>호스트에 재배치 요청(보스와 같은 NavMesh 섬의 보스 둘레 링 지점).</summary>
    Relocate = 2,
}

/// <summary>
/// 할 일 103 C7d — 끼임 단계화(순수, EditMode <c>QABoss103BotTests</c>). 2초 옆걸음(<see cref="QABoss103StuckDetector"/>)은 그대로 두고,
/// 이동하려는데 목표에 가까워지지 못한 시간(<b>진척 없음</b>)으로 단계를 올린다:
/// <see cref="DashAfterSeconds"/> 이상 → 대시 탈출(<see cref="DashRetrySeconds"/> 마다), <see cref="RelocateAfterSeconds"/> 이상 → 재배치 요청(<see cref="RelocateRetrySeconds"/> 마다).
/// <para>진척 = 목표 지표(<c>metric</c> — 다가가기면 목표까지 거리, 물러나기면 −거리)가 지금까지 가장 좋은 값보다 <see cref="ProgressMeters"/> 이상 좋아짐.
/// 옆걸음으로 좌우로만 오가면 진척이 아니다. 이동 의도가 없으면(멈춤·공격) 초기화. 목표가 바뀌면 호출자가 <see cref="Reset"/>.</para>
/// </summary>
public sealed class QABoss103StuckEscalation
{
    public const double DashAfterSeconds = 6.0;
    public const double RelocateAfterSeconds = 20.0;
    public const double DashRetrySeconds = 3.0;
    public const double RelocateRetrySeconds = 15.0;
    public const double ProgressMeters = 0.5;

    private bool _tracking;
    private double _since;
    private double _best;
    private double _nextDash, _nextRelocate;

    public int Dashes { get; private set; }
    public int Relocates { get; private set; }

    /// <summary>진척 없이 이동하려 한 시간(초). 추적 중이 아니면 0.</summary>
    public double StuckSeconds(double now) => _tracking ? now - _since : 0.0;

    public void Reset() => _tracking = false;

    public QABoss103StuckStep Tick(double now, double metric, bool wantsMove)
    {
        if (!wantsMove || double.IsNaN(metric))
        {
            _tracking = false;
            return QABoss103StuckStep.None;
        }
        if (!_tracking)
        {
            _tracking = true;
            _since = now;
            _best = metric;
            _nextDash = now + DashAfterSeconds;
            _nextRelocate = now + RelocateAfterSeconds;
            return QABoss103StuckStep.None;
        }
        if (metric <= _best - ProgressMeters)
        {
            _since = now;
            _best = metric;
            _nextDash = now + DashAfterSeconds;
            _nextRelocate = now + RelocateAfterSeconds;
            return QABoss103StuckStep.None;
        }
        if (now >= _nextRelocate)
        {
            _nextRelocate = now + RelocateRetrySeconds;
            Relocates++;
            return QABoss103StuckStep.Relocate;
        }
        if (now >= _nextDash)
        {
            _nextDash = now + DashRetrySeconds;
            Dashes++;
            return QABoss103StuckStep.Dash;
        }
        return QABoss103StuckStep.None;
    }

    /// <summary>대시 탈출 방향 차례(n = 1 부터): 뒤 → 왼쪽 → 오른쪽 반복. 반환 = 진행 방향 기준 각도(도, +는 왼쪽).</summary>
    public static float DashTurnDegrees(int n)
    {
        switch (((n - 1) % 3 + 3) % 3)
        {
            case 0: return 180f;
            case 1: return 90f;
            default: return -90f;
        }
    }

    /// <summary>대시 탈출에 충전을 써도 되나 — 최대 2개 이상이면 회피용 1개를 남긴다.</summary>
    public static bool CanSpendDash(int charges, int maxCharges) => charges >= (maxCharges >= 2 ? 2 : 1);
}

/// <summary>
/// 할 일 103 C7d — 정체 감시(순수, EditMode <c>QABoss103BatchTests</c>). Combat 중 파티가 보스에 준 피해 누계와 플레이어가 받은 피해 누계가
/// <see cref="StallSeconds"/> 동안 둘 다 그대로면 정체(판 Error, <see cref="QABoss103Batch.ReasonStall"/>). 어느 쪽이든 늘면 그 순간부터 다시 센다.
/// </summary>
public sealed class QABoss103StallWatch
{
    public const double StallSeconds = 90.0;

    private bool _started;
    private double _since;
    private long _dealt, _taken;

    /// <summary>마지막으로 피해가 오간 뒤 지난 초. 시작 전이면 0.</summary>
    public double QuietSeconds(double now) => _started ? now - _since : 0.0;

    /// <summary>매 틱. <paramref name="dealt"/>·<paramref name="taken"/> = 누계. 정체면 true.</summary>
    public bool Tick(double now, long dealt, long taken)
    {
        if (!_started || dealt != _dealt || taken != _taken)
        {
            _started = true;
            _since = now;
            _dealt = dealt;
            _taken = taken;
            return false;
        }
        return now - _since >= StallSeconds;
    }
}
