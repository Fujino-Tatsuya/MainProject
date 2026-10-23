using UnityEngine;

/// <summary>
/// 보스 HP바 등장 차오름(경석 10-23 아트 요청) — 바가 처음 보일 때 <c>startFraction</c>(기본 1/4)에서 실제 HP 비율까지
/// <c>duration</c> 동안 빠르게 채운다(감속 곡선). 그 뒤로는 실제 값 그대로. 순수 계산(EditMode 테스트 대상).
/// 실제 HP 가 시작값보다 낮으면(늦게 붙은 클라 등) 시작값을 실제 값으로 낮춘다 — 차오르다 도로 줄지 않게.
/// </summary>
public sealed class BossHealthIntroFill
{
    private float _elapsed = -1f;

    /// <summary>차오르는 중인가.</summary>
    public bool Active => _elapsed >= 0f;

    public void Begin() => _elapsed = 0f;

    public void Cancel() => _elapsed = -1f;

    /// <summary>이번 프레임에 그릴 비율. 차오르는 중이 아니면 <paramref name="actual01"/> 그대로.</summary>
    public float Evaluate(float actual01, float deltaTime, float startFraction, float duration)
    {
        actual01 = Mathf.Clamp01(actual01);
        if (_elapsed < 0f) return actual01;

        _elapsed += Mathf.Max(0f, deltaTime);
        float t = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;
        if (t >= 1f)
        {
            _elapsed = -1f;
            return actual01;
        }

        float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic — 처음 빠르고 끝에 붙는다
        float from = Mathf.Min(Mathf.Clamp01(startFraction), actual01);
        return Mathf.Lerp(from, actual01, eased);
    }
}
