using UnityEngine;

/// <summary>
/// 보스 HP바 등장 차오름(경석 10-23, Shape of Dreams 참고) — 바가 처음 보일 때 <b>흰 구간이 먼저 달려 나가고 빨간 바가 뒤따른다.</b>
/// 빨강은 <c>startFraction</c>(기본 0.45)에서, 흰색은 그보다 <c>leadStartGap</c> 앞에서 출발: 흰색 = <c>leadSeconds</c> 만에 실제 HP 까지,
/// 빨강 = <c>fillSeconds</c> 만에 실제 HP 까지(더 느림), 빨강이 다 차면 흰색이 <c>fadeSeconds</c> 동안 사그라든다. 순수 계산(EditMode 테스트 대상).
/// 🔴 멈춤 구간이 없다(경석 Play 6차) — 바가 처음 보이는 프레임에 이미 흰 구간이 앞서 있고 둘 다 가장 빠른 속도로 움직이는 중이라
///    "반에서 끊겼다가 차오른다"로 안 읽힌다(레퍼런스도 UI 가 보일 때 이미 채워지는 중).
/// 실제 HP 가 시작값보다 낮으면(늦게 붙은 클라 등) 시작값을 실제 값으로 낮춘다 — 차오르다 도로 줄지 않게.
/// </summary>
public sealed class BossHealthIntroFill
{
    public struct Frame
    {
        public bool Active;      // 차오름 중(흰 구간을 그려야 함)
        public float Red;        // 앞(빨간) 바 비율
        public float White;      // 흰 구간 끝 비율(빨강 뒤에 깔린다)
        public float WhiteAlpha; // 흰 구간 불투명도(끝에 0 으로)
    }

    private float _elapsed = -1f;

    /// <summary>차오르는 중인가.</summary>
    public bool Active => _elapsed >= 0f;

    public void Begin() => _elapsed = 0f;

    public void Cancel() => _elapsed = -1f;

    /// <param name="leadStartGap">흰 구간이 빨강보다 앞서 출발하는 비율 — 첫 프레임부터 흰 덩어리가 보이게(0 이면 같은 자리에서 출발).</param>
    public Frame Evaluate(float actual01, float deltaTime, float startFraction, float leadSeconds, float fillSeconds, float fadeSeconds,
                          float leadStartGap = 0f)
    {
        actual01 = Mathf.Clamp01(actual01);
        if (_elapsed < 0f) return new Frame { Red = actual01, White = actual01 };

        _elapsed += Mathf.Max(0f, deltaTime);
        float run = _elapsed;
        float from = Mathf.Min(Mathf.Clamp01(startFraction), actual01);
        float whiteFrom = Mathf.Min(from + Mathf.Max(0f, leadStartGap), actual01);

        float white = Mathf.Lerp(whiteFrom, actual01, EaseOutCubic(Progress(run, leadSeconds)));
        float red = Mathf.Lerp(from, actual01, EaseOutCubic(Progress(run, fillSeconds)));

        float fillEnd = Mathf.Max(0f, fillSeconds);
        float alpha = run <= fillEnd ? 1f : 1f - Progress(run - fillEnd, fadeSeconds);
        if (run >= fillEnd + Mathf.Max(0f, fadeSeconds))
        {
            _elapsed = -1f;
            return new Frame { Red = actual01, White = actual01 };
        }

        return new Frame { Active = true, Red = red, White = Mathf.Max(white, red), WhiteAlpha = Mathf.Clamp01(alpha) };
    }

    private static float Progress(float elapsed, float duration) => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;   // 처음 빠르고 끝에 붙는다
    }
}
