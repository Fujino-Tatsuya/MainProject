using System;
using System.Collections.Generic;

/// <summary>할 일 88 봇 간파 입력 모드 — A 경계 스윕 / B 사람형.</summary>
public enum QAInterruptBotMode
{
    /// <summary>A: 카운터 창 닫힘 시각 기준 −150~+50ms 를 시도 순서대로 고르게 훑어 누른다.</summary>
    Sweep = 0,
    /// <summary>B: 창이 보이면 150~350ms 무작위 반응 뒤 누른다.</summary>
    Human = 1,
}

/// <summary>할 일 88 강제 개시 대상.</summary>
public enum QAInterruptTargetKind
{
    /// <summary>존4 중간보스(MonsterCounterWindow 소유자 — GauntletBot·WallBot 등).</summary>
    Midboss = 0,
    /// <summary>23호 보스(TwentyThreeBoss).</summary>
    TwentyThree = 1,
}

/// <summary>
/// 할 일 88 간파 지연 QA 의 순수 계산(EditMode <c>QAInterruptLag88PlanTests</c>) — 지연 단계, 스윕 오프셋, 사람형 반응 시간,
/// 누를 시각(창 열림 관측 기준), 원격 지연 파일 파싱. Unity·네트워크 의존 없음.
/// 사양: <c>_lane-artifacts/MainProject/SPEC-88-interrupt-lag-qa.md</c>.
/// </summary>
public static class QAInterruptLag88Plan
{
    /// <summary>원격 클라에 차례로 거는 네트워크 시뮬레이터 지연(ms, 송수신 각각).</summary>
    public static readonly int[] DelayStepsMs = { 5, 35, 80, 120 };

    public const int DefaultAttempts = 30;
    public const double SweepMinMs = -150.0;
    public const double SweepMaxMs = 50.0;
    public const double ReactMinMs = 150.0;
    public const double ReactMaxMs = 350.0;
    public const int MaxDelayMs = 1000;

    /// <summary>
    /// A 경계 스윕의 <paramref name="index"/> 번째 시도 오프셋(ms, 창 닫힘 기준 — 음수 = 닫히기 전).
    /// [min, max] 를 양끝 포함 <paramref name="count"/> 등분한다. count 가 1 이하면 min, index 는 count 로 돌린다.
    /// </summary>
    public static double SweepOffsetMs(int index, int count, double minMs = SweepMinMs, double maxMs = SweepMaxMs)
    {
        if (minMs > maxMs)
            (minMs, maxMs) = (maxMs, minMs);
        if (count <= 1)
            return minMs;
        int i = ((index % count) + count) % count;
        return minMs + (maxMs - minMs) * i / (count - 1);
    }

    /// <summary>B 사람형 반응 시간(ms) — [min, max] 균등 무작위.</summary>
    public static double ReactionMs(Random rng, double minMs = ReactMinMs, double maxMs = ReactMaxMs)
    {
        if (minMs > maxMs)
            (minMs, maxMs) = (maxMs, minMs);
        return minMs + (maxMs - minMs) * rng.NextDouble();
    }

    /// <summary>
    /// A 스윕: 창 열림을 관측한 순간부터 누를 때까지(ms) = 창 길이 + 오프셋. 열림보다 앞이면 0(관측 즉시).
    /// </summary>
    public static double SweepPressDelayMs(double windowDurationMs, double offsetMs) =>
        Math.Max(0.0, windowDurationMs + offsetMs);

    /// <summary>23호 착지 창 판별 — 체공(몸 콜라이더 꺼짐)을 마지막으로 본 뒤 이 시간(초) 안에 열린 창만 착지 창이다.</summary>
    public const double LandingWindowMaxGapSec = 1.0;

    /// <summary>
    /// 23호: 이 창이 점프 착지 간파 창인가. 체공 중엔 전 피어에서 몸 콜라이더가 꺼지고(<c>SetHurtableClientRpc</c>) 착지 직후 창이 열린다.
    /// 개전 콤보 돌진(1.5s) 같은 다른 창은 체공이 없어 거른다 — 스모크 5차에서 이 창이 길이 학습을 1.5s 로 오염시켜 매번 ~0.7s 늦게 눌렀다.
    /// <paramref name="lastAirborneSeenAt"/> 음수 = 체공을 본 적 없음.
    /// </summary>
    public static bool IsLandingWindow(double openAt, double lastAirborneSeenAt, double maxGapSec = LandingWindowMaxGapSec) =>
        lastAirborneSeenAt >= 0.0 && openAt >= lastAirborneSeenAt && openAt - lastAirborneSeenAt <= maxGapSec;

    /// <summary>두 수평 벡터(x, z) 사이 각도(도, 0~180). 어느 쪽이든 길이 0 이면 NaN — 누름 진단(바라보는 방향 ↔ 보스 방향).</summary>
    public static double FlatAngleDeg(double ax, double az, double bx, double bz)
    {
        double la = Math.Sqrt(ax * ax + az * az);
        double lb = Math.Sqrt(bx * bx + bz * bz);
        if (la < 1e-6 || lb < 1e-6)
            return double.NaN;
        double c = (ax * bx + az * bz) / (la * lb);
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) * 180.0 / Math.PI;
    }

    /// <summary>원격 지연 파일 내용(정수 ms 한 줄) 파싱. 비었거나 숫자가 아니면 false. 0~<see cref="MaxDelayMs"/> 로 자른다.</summary>
    public static bool TryParseDelayMs(string text, out int delayMs)
    {
        delayMs = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!int.TryParse(text.Trim(), out int v))
            return false;
        delayMs = Math.Max(0, Math.Min(MaxDelayMs, v));
        return true;
    }

    /// <summary>로그 값용 — key=value 줄에 공백이 들어가지 않게 한다.</summary>
    public static string LogToken(string s) =>
        string.IsNullOrEmpty(s) ? "none" : s.Replace(' ', '_').Replace('=', '_');

    public static string ModeToken(QAInterruptBotMode mode) => mode == QAInterruptBotMode.Sweep ? "sweep" : "human";

    public static string KindToken(QAInterruptTargetKind kind) => kind == QAInterruptTargetKind.Midboss ? "midboss" : "23";
}

/// <summary>
/// 클라가 관측한 창 길이(열림→닫힘 엣지 간격)로 다음 창 길이를 추정한다 — 길이가 복제되지 않는 23호용.
/// 성공하면 창이 일찍 닫혀 짧게 잡히므로 최근 <see cref="Capacity"/> 개 중 <b>최대</b>를 쓴다.
/// </summary>
public sealed class QAWindowDurationLearner
{
    public const int Capacity = 5;

    private readonly Queue<double> _samples = new Queue<double>();

    public int Count => _samples.Count;

    public void Add(double seconds)
    {
        if (!(seconds > 0.0) || double.IsInfinity(seconds))
            return;
        _samples.Enqueue(seconds);
        while (_samples.Count > Capacity)
            _samples.Dequeue();
    }

    public bool TryEstimate(out double seconds)
    {
        seconds = 0.0;
        if (_samples.Count == 0)
            return false;
        foreach (double s in _samples)
            if (s > seconds)
                seconds = s;
        return true;
    }

    public void Clear() => _samples.Clear();
}
