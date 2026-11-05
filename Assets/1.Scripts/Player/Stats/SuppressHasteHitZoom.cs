using System;

/// <summary>N타 줌인 수치(기본 4타)(xlsx <c>SuppressHaste</c> 시트 → <see cref="SuppressHasteData"/>).</summary>
public readonly struct SuppressHasteHitZoomValues
{
    public readonly int EveryHits;
    public readonly float ZoomFactor;
    public readonly float InSeconds;
    public readonly float HoldSeconds;
    public readonly float ReturnSeconds;

    public SuppressHasteHitZoomValues(int everyHits, float zoomFactor, float inSeconds, float holdSeconds, float returnSeconds)
    {
        EveryHits = everyHits;
        ZoomFactor = zoomFactor;
        InSeconds = inSeconds;
        HoldSeconds = holdSeconds;
        ReturnSeconds = returnSeconds;
    }

    /// <summary>SO 가 없을 때 — 시트 기본값과 같다(4타 · 0.9배 · 들어감 0.04 · 유지 0.11 · 복귀 0.12초).</summary>
    public static SuppressHasteHitZoomValues Default => new SuppressHasteHitZoomValues(4, 0.9f, 0.04f, 0.11f, 0.12f);

    public float TotalSeconds => Math.Max(0f, InSeconds) + Math.Max(0f, HoldSeconds) + Math.Max(0f, ReturnSeconds);
}

/// <summary>
/// 제압 가속 N타 줌인(기본 4타, 할 일 174·233) — 로컬 오너가 보스에게 맞힌 타격 N번마다 내 화면만 짧게 당긴다.
///   · 셈 = 서버가 확정한 내 보스 피해 RPC 1회 = 1타(빗나감·0 피해는 RPC 가 없어 안 센다). 가속 중이 아니면 안 세고 0 으로 리셋.
///   · 슬로우 줌과 겹치면 **건너뛴다** — 슬로우 줌(내 간파 0.7~0.85배)이 도는 동안은 펄스를 버리고 1. 곱하지 않는다(suppress-haste.md §2.2).
///   · 가속이 끝나도 진행 중인 펄스는 끝까지 돈다(복귀 도중 튀지 않게). 셈만 리셋.
/// 결과 = 플레이어 vcam FollowOffset 에 곱할 배율(1 = 평소). MonoBehaviour·UnityEngine 의존 없음 — EditMode 테스트 대상.
/// </summary>
public sealed class SuppressHasteHitZoom
{
    /// <summary>배율 하한 — <c>SlowMotionCameraZoom.MinZoomFactor</c> 와 같다.</summary>
    public const float MinZoomFactor = 0.1f;

    int hits;
    float pulseTime = -1f;

    public int Hits => hits;
    public bool PulseActive => pulseTime >= 0f;

    /// <summary>내 보스 적중 1회. N번째면 펄스를 시작하고 true.</summary>
    public bool RegisterHit(bool hasteActive, int everyHits)
    {
        if (!hasteActive)
        {
            hits = 0;
            return false;
        }

        hits++;
        if (hits < Math.Max(1, everyHits))
            return false;

        hits = 0;
        pulseTime = 0f;
        return true;
    }

    /// <summary>매 프레임 — 이번 프레임 FollowOffset 배율.</summary>
    /// <param name="slowZoomActive">슬로우 줌 배율이 1 이 아닌가(이 프레임 슬로우 줌이 걸려 있다).</param>
    /// <param name="unscaledDeltaTime">실시간 — 슬로우 줌과 같이 히트스톱·슬로우와 무관하게 같은 길이로 끝난다.</param>
    public float Step(bool hasteActive, bool slowZoomActive, float unscaledDeltaTime, SuppressHasteHitZoomValues values)
    {
        if (!hasteActive)
            hits = 0;

        if (slowZoomActive)
        {
            pulseTime = -1f;
            return 1f;
        }

        if (pulseTime < 0f)
            return 1f;

        pulseTime += Math.Max(0f, unscaledDeltaTime);
        if (pulseTime >= values.TotalSeconds)
        {
            pulseTime = -1f;
            return 1f;
        }

        return PulseMultiplier(pulseTime, values);
    }

    /// <summary>펄스 시작 후 <paramref name="t"/> 초의 배율 — 들어감(1 → 배율) · 유지 · 복귀(배율 → 1), 선형.</summary>
    public static float PulseMultiplier(float t, SuppressHasteHitZoomValues values)
    {
        float factor = ClampZoomFactor(values.ZoomFactor);
        float inSeconds = Math.Max(0f, values.InSeconds);
        float holdSeconds = Math.Max(0f, values.HoldSeconds);
        float returnSeconds = Math.Max(0f, values.ReturnSeconds);

        if (float.IsNaN(t) || t < 0f)
            return 1f;
        if (t < inSeconds)
            return Lerp(1f, factor, t / inSeconds);
        t -= inSeconds;
        if (t < holdSeconds)
            return factor;
        t -= holdSeconds;
        if (t < returnSeconds)
            return Lerp(factor, 1f, t / returnSeconds);
        return 1f;
    }

    public static float ClampZoomFactor(float zoomFactor) =>
        float.IsNaN(zoomFactor) ? 1f : Math.Min(1f, Math.Max(MinZoomFactor, zoomFactor));

    static float Lerp(float a, float b, float t) => a + (b - a) * Math.Min(1f, Math.Max(0f, t));
}
