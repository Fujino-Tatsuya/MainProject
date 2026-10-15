using UnityEngine;

/// <summary>
/// 인터럽트 강조(RMB 슬롯·선택 비네팅)의 순수 계산 — 켤지 말지와 페이드·맥동 값.
/// MonoBehaviour(<see cref="InterruptHighlightHUD"/>)는 대상 상태·시간만 넘기고 값은 여기서 받는다 — EditMode 테스트 대상.
/// </summary>
public static class InterruptHighlightModel
{
    /// <summary>대상 하나가 강조 사유가 되는가 — 카운터 창이 열려 있고 내 인터럽트 사거리 안.</summary>
    public static bool IsHighlightTarget(bool counterWindowOpen, bool inInterruptRange)
    {
        return counterWindowOpen && inInterruptRange;
    }

    /// <summary>화면 강조를 켜는가 — 살아 있고 강조 사유 대상이 하나라도 있을 때. 사망·Soul·관전에선 끈다.</summary>
    public static bool ShouldHighlight(bool anyHighlightTarget, bool alive)
    {
        return alive && anyHighlightTarget;
    }

    /// <summary>
    /// 강조 세기(0~1)를 목표(켜짐 1, 꺼짐 0)로 한 프레임 옮긴다. 0 ↔ 1 전체를 각각 페이드 인/아웃 시간에 지난다.
    /// 시간이 0 이하면 즉시 도달.
    /// </summary>
    public static float StepIntensity(float current, bool show, float fadeInDuration, float fadeOutDuration,
        float deltaTime)
    {
        float target = show ? 1f : 0f;
        float duration = target > current ? fadeInDuration : fadeOutDuration;
        if (duration <= 0f)
            return target;

        return Mathf.MoveTowards(Mathf.Clamp01(current), target, deltaTime / duration);
    }

    /// <summary>맥동 위상(0~1, 한 주기 = 1)을 진행한다. 주기가 0 이하면 0 에 고정.</summary>
    public static float AdvancePhase(float phase, float period, float deltaTime)
    {
        return period > 0f ? Mathf.Repeat(phase + deltaTime / period, 1f) : 0f;
    }

    /// <summary>맥동 파형(0~1). 위상 0 = 1(켜지는 순간 가장 밝다), 0.5 = 0.</summary>
    public static float PulseWave(float phase)
    {
        return 0.5f + 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
    }

    /// <summary>강조 알파 = 세기 × 맥동(최저 <paramref name="minAlpha"/> ~ 1).</summary>
    public static float GlowAlpha(float intensity, float wave, float minAlpha)
    {
        return Mathf.Clamp01(intensity) * Mathf.Lerp(Mathf.Clamp01(minAlpha), 1f, Mathf.Clamp01(wave));
    }
}
