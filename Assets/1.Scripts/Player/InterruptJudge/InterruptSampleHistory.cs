using System;

/// <summary>
/// 시각순 표본 링 버퍼(할 일 191 §3.1 ① — 약 1초 이력). 시각은 서버 시각(<see cref="InterruptServerClock"/>)으로 넣는다.
/// <list type="bullet">
/// <item>넣기: 마지막보다 이른 시각은 버린다. 같은 시각이면 덮어쓴다(같은 프레임 두 번).</item>
/// <item>보관: 최신 시각 − <see cref="HistorySeconds"/> 보다 오래된 표본은 지운다(사이 보간을 위해 경계 바로 앞 하나는 남긴다). 용량이 차도 가장 오래된 것부터 밀린다.</item>
/// <item>조회: [가장 오래된, 가장 최신] 시각 안이면 두 표본 사이를 보간한다. 범위 밖은 false — 추정하지 않는다.</item>
/// </list>
/// 순수 C# — EditMode 에서 보간·범위 밖을 고정한다.
/// </summary>
public sealed class InterruptSampleHistory<T> where T : struct, IInterruptSampleLerp<T>
{
    readonly double[] _times;
    readonly T[] _samples;
    int _start;   // 가장 오래된 칸
    int _count;

    public InterruptSampleHistory(int capacity, double historySeconds)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "보간하려면 2칸 이상 필요하다.");
        _times = new double[capacity];
        _samples = new T[capacity];
        HistorySeconds = Math.Max(0.0, historySeconds);
    }

    /// <summary>보관 길이(초).</summary>
    public double HistorySeconds { get; }

    public int Count => _count;
    public int Capacity => _times.Length;

    public double OldestTime => _count > 0 ? _times[_start] : double.NaN;
    public double NewestTime => _count > 0 ? _times[Index(_count - 1)] : double.NaN;

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    /// <summary>표본을 넣는다. 마지막보다 이른 시각이면 false(버림).</summary>
    public bool Add(double time, in T sample)
    {
        if (double.IsNaN(time) || double.IsInfinity(time))
            return false;

        if (_count > 0)
        {
            int last = Index(_count - 1);
            if (time < _times[last])
                return false;
            if (time == _times[last])
            {
                _samples[last] = sample;
                return true;
            }
        }

        if (_count == _times.Length)
        {
            _start = (_start + 1) % _times.Length;
            _count--;
        }

        int slot = Index(_count);
        _times[slot] = time;
        _samples[slot] = sample;
        _count++;

        // 보관 길이 밖 정리 — 두 번째 표본도 경계 밖일 때만 첫 표본을 지운다(경계 시각을 보간할 수 있게).
        double keepFrom = time - HistorySeconds;
        while (_count > 2 && _times[Index(1)] <= keepFrom)
        {
            _start = (_start + 1) % _times.Length;
            _count--;
        }

        return true;
    }

    /// <summary>가장 최신 표본.</summary>
    public bool TryGetLatest(out double time, out T sample)
    {
        if (_count == 0)
        {
            time = double.NaN;
            sample = default;
            return false;
        }

        int last = Index(_count - 1);
        time = _times[last];
        sample = _samples[last];
        return true;
    }

    /// <summary><paramref name="time"/> 의 표본 — 범위 안이면 보간, 밖이면 false.</summary>
    public bool TryGetAt(double time, out T sample)
    {
        sample = default;
        if (_count == 0 || double.IsNaN(time) || time < OldestTime || time > NewestTime)
            return false;

        // 최신 쪽에서 찾는다 — 조회는 대부분 최근 시각이다.
        for (int i = _count - 1; i >= 0; i--)
        {
            int idx = Index(i);
            double t0 = _times[idx];
            if (t0 > time)
                continue;

            if (t0 == time || i == _count - 1)
            {
                sample = _samples[idx];
                return true;
            }

            int nextIdx = Index(i + 1);
            double t1 = _times[nextIdx];
            float k = (float)((time - t0) / (t1 - t0));
            sample = _samples[idx].LerpTo(_samples[nextIdx], k);
            return true;
        }

        return false;
    }

    int Index(int offset) => (_start + offset) % _times.Length;
}
