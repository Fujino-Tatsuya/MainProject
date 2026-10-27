using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>간파 지연 측정에서 어느 시각의 카운터 창 상태. 기록이 없거나 링버퍼에서 밀려났으면 Unknown.</summary>
public enum InterruptWindowState
{
    Unknown,
    Open,
    Closed,
}

/// <summary>
/// 카운터 창 소유자 하나의 열림/닫힘 서버 시각 기록(링버퍼). 순수 C# — 간파 지연 측정 전용(할 일 74 후속).
/// 판정에는 쓰지 않는다. 시각은 서버 ServerTime(초)이고 기록 순서대로 들어온다고 가정한다.
/// </summary>
public sealed class InterruptWindowHistory
{
    public const int DefaultCapacity = 16;

    private readonly double[] _times;
    private readonly bool[] _open;
    private int _start;
    private int _count;

    public InterruptWindowHistory(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            capacity = 1;
        _times = new double[capacity];
        _open = new bool[capacity];
    }

    public int Count => _count;
    public int Capacity => _times.Length;

    /// <summary>창 전이 하나를 기록한다. 가득 차면 가장 오래된 기록을 덮어쓴다.</summary>
    public void Record(double serverTime, bool open)
    {
        int index;
        if (_count < _times.Length)
        {
            index = (_start + _count) % _times.Length;
            _count++;
        }
        else
        {
            index = _start;
            _start = (_start + 1) % _times.Length;
        }

        _times[index] = serverTime;
        _open[index] = open;
    }

    /// <summary>
    /// <paramref name="serverTime"/> 에 창이 열려 있었는가. 그 시각 이하의 마지막 기록을 따른다(경계 시각 = 바뀐 뒤 상태).
    /// 기록이 없거나 남은 가장 오래된 기록보다 이른 시각이면 Unknown — 덮어써진 구간은 추측하지 않는다.
    /// </summary>
    public InterruptWindowState StateAt(double serverTime)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            int index = (_start + i) % _times.Length;
            if (_times[index] <= serverTime)
                return _open[index] ? InterruptWindowState.Open : InterruptWindowState.Closed;
        }

        return InterruptWindowState.Unknown;
    }

    // ── 대쉬 간파 예측 로그(할 일 116) — 읽기 전용 질의, 판정 불변 ──

    /// <summary><paramref name="serverTime"/> 에 창이 열려 있었으면 그 열림의 시작 시각. 닫힘·기록 없음이면 false.</summary>
    public bool TryGetOpenSince(double serverTime, out double openSince)
    {
        openSince = 0d;
        for (int i = _count - 1; i >= 0; i--)
        {
            int index = (_start + i) % _times.Length;
            if (_times[index] > serverTime)
                continue;
            if (!_open[index])
                return false;
            openSince = _times[index];
            return true;
        }

        return false;
    }

    /// <summary><paramref name="serverTime"/> 뒤(초과) 첫 닫힘 기록 시각. 없으면 false.</summary>
    public bool TryGetFirstCloseAfter(double serverTime, out double closeAt)
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_start + i) % _times.Length;
            if (_times[index] > serverTime && !_open[index])
            {
                closeAt = _times[index];
                return true;
            }
        }

        closeAt = 0d;
        return false;
    }

    /// <summary>
    /// 링버퍼에 남은 완결된 열림 구간(열림 → 바로 다음 닫힘) 길이의 중앙값. 구간이 없으면 false.
    /// 간파 성공으로 일찍 닫힌 구간도 섞인다 — 추정용이다.
    /// </summary>
    public bool TryGetTypicalOpenDuration(out double duration)
    {
        var lengths = new List<double>(_count);
        for (int i = 0; i + 1 < _count; i++)
        {
            int a = (_start + i) % _times.Length;
            int b = (_start + i + 1) % _times.Length;
            if (_open[a] && !_open[b])
                lengths.Add(_times[b] - _times[a]);
        }

        if (lengths.Count == 0)
        {
            duration = 0d;
            return false;
        }

        lengths.Sort();
        int mid = lengths.Count / 2;
        duration = lengths.Count % 2 == 1 ? lengths[mid] : (lengths[mid - 1] + lengths[mid]) * 0.5;
        return true;
    }
}

/// <summary>간파 지연 판정 계산. 순수 함수 — 실제 판정 결과는 바꾸지 않고 "지연만큼 당겨 판정했으면" 만 계산한다.</summary>
public static class InterruptLagJudge
{
    public const float DefaultCapMs = 100f;

    /// <summary>상한 적용 지연(초) = min(도착 시각 − 누른 시각, 상한). 음수 지연(클라 시계가 앞섬)은 0.</summary>
    public static double CappedLagSeconds(double pressServerTime, double arriveServerTime, double capSeconds)
    {
        double lag = arriveServerTime - pressServerTime;
        if (lag <= 0d)
            return 0d;
        return Math.Min(lag, Math.Max(0d, capSeconds));
    }

    /// <summary>보정 판정 시각 = 판정 시각 − 상한 적용 지연. 이 시각의 창 상태가 보정했으면의 결과다.</summary>
    public static double CorrectedJudgeTime(
        double pressServerTime, double arriveServerTime, double hitServerTime, double capSeconds) =>
        hitServerTime - CappedLagSeconds(pressServerTime, arriveServerTime, capSeconds);

    /// <summary>
    /// 보정했으면 성공했을까 = 보정 판정 시각에 창이 열려 있었는가. 기록이 없으면(Unknown) false.
    /// 사거리·명중은 여기서 보지 않는다(서버 현재 기준 — 호출측이 대상이 맞았을 때만 부른다).
    /// </summary>
    public static bool WouldSucceed(InterruptWindowState correctedWindow) =>
        correctedWindow == InterruptWindowState.Open;

    /// <summary>보정했으면 성공 = 실제 성공 ∨ 보정 시각 기준 성공.</summary>
    public static bool CorrectedSuccess(bool actualSuccess, bool wouldSucceed) =>
        actualSuccess || wouldSucceed;

    public static string Format(InterruptWindowState state)
    {
        switch (state)
        {
            case InterruptWindowState.Open: return "open";
            case InterruptWindowState.Closed: return "closed";
            default: return "unknown";
        }
    }
}

/// <summary>간파 판정 1건의 측정값. 시각은 전부 서버 ServerTime(초), RTT 는 ms.</summary>
public struct InterruptLagSample
{
    public ulong ClientId;
    public bool IsHost;
    public string Skill;
    public string Target;             // 카운터 창 소유자 이름. 사거리에 없으면 null
    public double PressServerTime;
    public double ArriveServerTime;
    public double HitServerTime;
    public double RttMs;
    public InterruptWindowState PressWindow;
    public InterruptWindowState HitWindow;
    public InterruptWindowState CorrectedWindow;
    public bool Success;
    public bool WouldSucceed;
    public float CapMs;

    public bool HasTarget => !string.IsNullOrEmpty(Target);

    /// <summary>판정 1줄. 키 목록·의미 = Docs/tech/networking.md "Interrupt lag measurement".</summary>
    public string ToLogLine()
    {
        var sb = new StringBuilder(256);
        sb.Append("[InterruptLag] event=judge");
        InterruptLagFormat.Append(sb, "clientId", ClientId.ToString(CultureInfo.InvariantCulture));
        InterruptLagFormat.Append(sb, "host", IsHost);
        InterruptLagFormat.Append(sb, "skill", InterruptLagFormat.Token(Skill));
        InterruptLagFormat.Append(sb, "target", HasTarget ? InterruptLagFormat.Token(Target) : "none");
        InterruptLagFormat.Append(sb, "pressT", PressServerTime, "0.000");
        InterruptLagFormat.Append(sb, "arriveT", ArriveServerTime, "0.000");
        InterruptLagFormat.Append(sb, "hitT", HitServerTime, "0.000");
        InterruptLagFormat.Append(sb, "lagMs", (ArriveServerTime - PressServerTime) * 1000.0, "0.0");
        InterruptLagFormat.Append(sb, "hitAfterPressMs", (HitServerTime - PressServerTime) * 1000.0, "0.0");
        InterruptLagFormat.Append(sb, "rttMs", RttMs, "0");
        InterruptLagFormat.Append(sb, "pressWindow", InterruptLagJudge.Format(PressWindow));
        InterruptLagFormat.Append(sb, "hitWindow", InterruptLagJudge.Format(HitWindow));
        InterruptLagFormat.Append(sb, "correctedWindow", InterruptLagJudge.Format(CorrectedWindow));
        InterruptLagFormat.Append(sb, "result", Success ? "success" : "fail");
        InterruptLagFormat.Append(sb, "wouldSucceed", WouldSucceed);
        InterruptLagFormat.Append(sb, "capMs", CapMs, "0");
        return sb.ToString();
    }
}

/// <summary>한 판의 간파 지연 집계. 호스트 자신(지연 0)과 원격 클라를 따로 센다. 순수 C#.</summary>
public sealed class InterruptLagSummary
{
    public sealed class Group
    {
        public int Attempts;
        public int WithTarget;
        public int Success;
        public int CorrectedSuccess;
        public int Rescued;           // 실제 실패 ∧ 보정했으면 성공(wouldSucceed)
        public double RttSumMs;
        public double RttMaxMs;

        public double RttAvgMs => Attempts > 0 ? RttSumMs / Attempts : 0.0;

        internal void Record(in InterruptLagSample sample)
        {
            Attempts++;
            if (sample.HasTarget)
                WithTarget++;
            if (sample.Success)
                Success++;
            if (InterruptLagJudge.CorrectedSuccess(sample.Success, sample.WouldSucceed))
                CorrectedSuccess++;
            if (!sample.Success && sample.WouldSucceed)
                Rescued++;
            RttSumMs += sample.RttMs;
            RttMaxMs = Math.Max(RttMaxMs, sample.RttMs);
        }
    }

    public Group Host { get; } = new Group();
    public Group Remote { get; } = new Group();

    public bool IsEmpty => Host.Attempts == 0 && Remote.Attempts == 0;

    public void Record(in InterruptLagSample sample) => (sample.IsHost ? Host : Remote).Record(sample);

    public void Clear()
    {
        Reset(Host);
        Reset(Remote);
    }

    /// <summary>요약 줄(그룹마다 1줄 — host, remote 순). 키 목록 = Docs/tech/networking.md.</summary>
    public void AppendLogLines(string reason, List<string> lines)
    {
        lines.Add(FormatGroup(reason, "host", Host));
        lines.Add(FormatGroup(reason, "remote", Remote));
    }

    private static string FormatGroup(string reason, string name, Group g)
    {
        var sb = new StringBuilder(192);
        sb.Append("[InterruptLag] event=summary");
        InterruptLagFormat.Append(sb, "reason", InterruptLagFormat.Token(reason));
        InterruptLagFormat.Append(sb, "group", name);
        InterruptLagFormat.Append(sb, "attempts", g.Attempts.ToString(CultureInfo.InvariantCulture));
        InterruptLagFormat.Append(sb, "withTarget", g.WithTarget.ToString(CultureInfo.InvariantCulture));
        InterruptLagFormat.Append(sb, "success", g.Success.ToString(CultureInfo.InvariantCulture));
        InterruptLagFormat.Append(sb, "correctedSuccess", g.CorrectedSuccess.ToString(CultureInfo.InvariantCulture));
        InterruptLagFormat.Append(sb, "rescued", g.Rescued.ToString(CultureInfo.InvariantCulture));
        InterruptLagFormat.Append(sb, "rttAvgMs", g.RttAvgMs, "0.0");
        InterruptLagFormat.Append(sb, "rttMaxMs", g.RttMaxMs, "0.0");
        return sb.ToString();
    }

    private static void Reset(Group g)
    {
        g.Attempts = g.WithTarget = g.Success = g.CorrectedSuccess = g.Rescued = 0;
        g.RttSumMs = g.RttMaxMs = 0.0;
    }
}

internal static class InterruptLagFormat
{
    public static void Append(StringBuilder sb, string key, string value) =>
        sb.Append(' ').Append(key).Append('=').Append(value);

    public static void Append(StringBuilder sb, string key, bool value) => Append(sb, key, value ? "true" : "false");

    public static void Append(StringBuilder sb, string key, double value, string format) =>
        Append(sb, key, value.ToString(format, CultureInfo.InvariantCulture));

    // 값에 공백·'=' 이 들어가면 key=value 파싱이 깨진다 — '_' 로 바꾼다.
    public static string Token(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "none";

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(char.IsWhiteSpace(c) || c == '=' ? '_' : c);
        return sb.ToString();
    }
}
