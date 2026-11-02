using System;

/// <summary>
/// 이 피어의 로컬 게임초(<c>Time.timeAsDouble</c> — 스킬·보스 타이머가 쓰는 시계) ↔ 서버 시각(<see cref="NetworkClock.GameNow"/> — 슬로우·일시정지를 뺀 세션 공용 게임시간) 변환(할 일 191).
/// 둘 다 슬로우에 같이 느려지므로 차이(오프셋)는 거의 일정하다. 매 프레임 표본으로 오프셋을 부드럽게 따라가고, 크게 튀면(세션 시작·시계 재동기) 바로 맞춘다.
/// 순수 C# — EditMode 에서 변환을 고정한다.
/// </summary>
public sealed class InterruptServerClock
{
    /// <summary>오프셋이 이보다 크게 바뀌면 부드럽게 따라가지 않고 바로 맞춘다(초).</summary>
    public const double SnapThreshold = 0.25;

    /// <summary>프레임마다 새 오프셋으로 다가가는 비율.</summary>
    public const double Smoothing = 0.1;

    /// <summary>서버 시각 − 로컬 시각.</summary>
    public double Offset { get; private set; }

    public bool HasSample { get; private set; }

    public void Reset()
    {
        Offset = 0.0;
        HasSample = false;
    }

    /// <summary>같은 순간의 로컬 시각과 서버 시각 표본.</summary>
    public void Sample(double localTime, double serverTime)
    {
        if (double.IsNaN(localTime) || double.IsNaN(serverTime) || double.IsInfinity(localTime) || double.IsInfinity(serverTime))
            return;

        double raw = serverTime - localTime;
        if (!HasSample || Math.Abs(raw - Offset) > SnapThreshold)
            Offset = raw;
        else
            Offset += (raw - Offset) * Smoothing;
        HasSample = true;
    }

    public double ToServer(double localTime) => localTime + Offset;

    public double ToLocal(double serverTime) => serverTime - Offset;
}
