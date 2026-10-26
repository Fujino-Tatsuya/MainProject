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
/// 할 일 103 C7f — 전투 활동 누계(순수). 계측기(<c>QABoss103Recorder</c>)가 활동마다 <see cref="Note"/> — 보스·보스 외 유닛(송전탑 등)에 준 피해,
/// 피격·체력 손실, 생명 상태 변화(사망·영혼·부활·PermanentDead), 공용 목숨 변화, 전멸, 간파, 기믹 알림(<c>QA103GimmickResult</c>),
/// 보스 전투 기록·<c>[23호/</c> 로그(북진 회차·릴레이 진행), 보스 HP 변화. <see cref="LastKind"/> 는 stall 로그용.
/// </summary>
public sealed class QABoss103Activity
{
    public const string None = "none";

    public long Count { get; private set; }
    public string LastKind { get; private set; } = None;

    public void Note(string kind)
    {
        Count++;
        LastKind = string.IsNullOrEmpty(kind) ? None : kind;
    }
}

/// <summary>
/// 할 일 103 C7d·C7f — 정체 감시(순수, EditMode <c>QABoss103BatchTests</c>). Combat 중 활동 누계(<see cref="QABoss103Activity.Count"/>)가
/// <see cref="StallSeconds"/> 동안 그대로면 정체(판 Error, <see cref="QABoss103Batch.ReasonStall"/>). 늘면 그 순간부터 다시 센다.
/// C7d 는 보스에 준 피해·받은 피해만 봐서 북진 압박(보스 무적 · 탑 공격 · 즉사 방전 → QA 부활 반복)을 정체로 오판했다.
/// </summary>
public sealed class QABoss103StallWatch
{
    public const double StallSeconds = 90.0;

    private bool _started;
    private double _since;
    private long _activity;

    /// <summary>마지막 활동 뒤 지난 초. 시작 전이면 0.</summary>
    public double QuietSeconds(double now) => _started ? now - _since : 0.0;

    /// <summary>매 틱. <paramref name="activity"/> = 활동 누계. 정체면 true.</summary>
    public bool Tick(double now, long activity)
    {
        if (!_started || activity != _activity)
        {
            _started = true;
            _since = now;
            _activity = activity;
            return false;
        }
        return now - _since >= StallSeconds;
    }
}
