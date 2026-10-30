/// <summary>
/// P9 벽 타기 판정기 — 씬/MonoBehaviour 없이 물리 틱 입력만으로 판정한다(할 일 180).
/// kinematic PlayerMotor 는 스텝 오프셋·접지 스냅 때문에 정상 이동에서도 틱마다 mm~cm 씩 Y 가 오른다.
/// 그래서 틱당 상승이 아니라 "기준 높이에서 StepOffset 을 넘는 누적 상승이, 벽 막힘과 함께,
/// 약 1초 동안 이어질 때"만 벽 타기로 본다. 한 상승 구간(episode)당 Triggered 는 한 번만 켜진다.
/// </summary>
public sealed class WallClimbJudge
{
    // 이보다 작은 틱 변화는 상승/하강으로 보지 않는다(부동소수 흔들림).
    public const float RiseEpsilon = 0.0005f;
    // 기준에서 이만큼 넘게 누적 상승한 채 이 시간 이상 이어져야 벽 타기로 본다.
    public const float ClimbSeconds = 1f;
    // Y 가 이 폭 안에서 이 시간 이상 머물면(접지 중) 기준 높이를 다시 잡는다.
    public const float StableTolerance = 0.01f;
    public const float StableSeconds = 0.5f;
    // A/D 연타로 막힘 신호가 틱마다 깜빡이므로, 최근 이 시간 안의 막힘도 "막힌 채"로 본다.
    public const float BlockedMemorySeconds = 0.25f;
    // 실제 모서리 투영 누수는 한 틱에 mm 단위(관측 2.6~34.4mm)다 — 이보다 큰 단발 변화는
    // 턱 오르기 스냅·정착·텔레포트 등 다른 메커니즘이므로 그 자리에서 기준을 다시 잡는다.
    public const float MaxPlausibleDelta = 0.05f;

    public struct Tick
    {
        public float Time;
        public float Y;
        /// <summary>평지 접지(법선 y≈1) 여부 — 경사로 상승은 세지 않는다.</summary>
        public bool Grounded;
        /// <summary>이번 틱 벽 막힘(PlayerMotor.WasBlockedThisTick).</summary>
        public bool Blocked;
        public bool MoveIntent;
    }

    public enum Verdict
    {
        None,
        /// <summary>이동 의도가 있는 벽 타기 — Error.</summary>
        Climb,
        /// <summary>조건은 맞았지만 이동 의도 없음 — FP-P9 Info.</summary>
        ClimbWithoutIntent,
    }

    public struct Result
    {
        public Verdict Verdict;
        public float BaselineY;
        public float PeakY;
        public float ClimbRise;
        public float ClimbSeconds;
    }

    private readonly float _stepOffset;

    private bool _hasSample;
    private float _previousY;
    private bool _hasBaseline;
    private float _baselineY;
    private float _stableRefY;
    private float _stableSince;
    private float _lastBlockedTime = float.NegativeInfinity;

    private bool _inEpisode;
    private bool _episodeTriggered;
    private float _episodeStart;
    private float _climbRise;
    private float _peakY;

    public WallClimbJudge(float stepOffset)
    {
        _stepOffset = stepOffset;
    }

    public float StepOffset => _stepOffset;

    /// <summary>스폰·씬 전환·필터 구간 뒤에는 처음부터 다시 관찰한다.</summary>
    public void Reset()
    {
        _hasSample = false;
        _hasBaseline = false;
        _lastBlockedTime = float.NegativeInfinity;
        EndEpisode();
    }

    public Result Step(Tick tick)
    {
        if (tick.Blocked)
            _lastBlockedTime = tick.Time;

        if (!_hasSample)
        {
            _hasSample = true;
            _previousY = tick.Y;
            _stableRefY = tick.Y;
            _stableSince = tick.Time;
            if (tick.Grounded)
                SetBaseline(tick.Y);
            return Snapshot(Verdict.None, tick.Time);
        }

        float delta = tick.Y - _previousY;
        _previousY = tick.Y;

        if (delta > MaxPlausibleDelta || delta < -MaxPlausibleDelta)
        {
            Rebaseline(tick);
            return Snapshot(Verdict.None, tick.Time);
        }

        if (!_hasBaseline || (tick.Grounded && tick.Y < _baselineY))
        {
            if (tick.Grounded)
                SetBaseline(tick.Y);
        }

        if (UpdateStability(tick))
            return Snapshot(Verdict.None, tick.Time);

        bool recentlyBlocked = tick.Time - _lastBlockedTime <= BlockedMemorySeconds;
        bool blockedRise = delta > RiseEpsilon && tick.Grounded && recentlyBlocked;
        if (blockedRise)
        {
            if (!_inEpisode)
            {
                _inEpisode = true;
                _episodeTriggered = false;
                _episodeStart = tick.Time;
                _climbRise = 0f;
            }

            _climbRise += delta;
        }
        else if (_inEpisode && delta < -RiseEpsilon)
        {
            // 접지 스냅의 오르내림은 상쇄된다 — 순(net) 상승만 남긴다.
            _climbRise += delta;
            if (_climbRise <= 0f)
                EndEpisode();
        }

        if (!_inEpisode)
            return Snapshot(Verdict.None, tick.Time);

        if (tick.Y > _peakY)
            _peakY = tick.Y;

        // "계속 오르는 중"이어야 한다 — 막힌 채 오른 이번 틱에서만 판정한다.
        bool exceeded =
            blockedRise &&
            !_episodeTriggered &&
            _hasBaseline &&
            tick.Time - _episodeStart >= ClimbSeconds &&
            _climbRise > _stepOffset &&
            tick.Y - _baselineY > _stepOffset;
        if (!exceeded)
            return Snapshot(Verdict.None, tick.Time);

        _episodeTriggered = true;
        return Snapshot(tick.MoveIntent ? Verdict.Climb : Verdict.ClimbWithoutIntent, tick.Time);
    }

    /// <summary>접지 중 Y 가 일정 시간 거의 그대로면 기준을 다시 잡는다. 다시 잡았으면 true.</summary>
    private bool UpdateStability(Tick tick)
    {
        float drift = tick.Y - _stableRefY;
        if (drift > StableTolerance || drift < -StableTolerance)
        {
            _stableRefY = tick.Y;
            _stableSince = tick.Time;
            return false;
        }

        if (!tick.Grounded || tick.Time - _stableSince < StableSeconds)
            return false;

        SetBaseline(tick.Y);
        _stableRefY = tick.Y;
        _stableSince = tick.Time;
        EndEpisode();
        return true;
    }

    private void Rebaseline(Tick tick)
    {
        _stableRefY = tick.Y;
        _stableSince = tick.Time;
        _hasBaseline = false;
        if (tick.Grounded)
            SetBaseline(tick.Y);
        EndEpisode();
    }

    private void SetBaseline(float y)
    {
        _hasBaseline = true;
        _baselineY = y;
        if (!_inEpisode)
            _peakY = y;
    }

    private void EndEpisode()
    {
        _inEpisode = false;
        _episodeTriggered = false;
        _climbRise = 0f;
        _peakY = _hasBaseline ? _baselineY : _previousY;
    }

    private Result Snapshot(Verdict verdict, float time)
    {
        return new Result
        {
            Verdict = verdict,
            BaselineY = _baselineY,
            PeakY = _peakY,
            ClimbRise = _climbRise,
            ClimbSeconds = _inEpisode ? time - _episodeStart : 0f,
        };
    }
}
