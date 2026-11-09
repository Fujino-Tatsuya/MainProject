using UnityEngine;

/// <summary>
/// 제압 가속 비네팅의 순수 계산 — 고정 알파까지의 페이드 인/아웃.
/// MonoBehaviour(<see cref="SuppressHasteVignetteHUD"/>)는 시간·상태만 넘기고 값은 여기서 받는다 — EditMode 테스트 대상.
/// (옛 실드 비네팅 모델에서 페이드 계산만 남겨 옮겼다 — 실드 비네팅은 삭제.)
/// </summary>
public static class SuppressHasteVignetteModel
{
    /// <summary>
    /// 현재 알파를 목표(보이면 <paramref name="shownAlpha"/>, 아니면 0)로 한 프레임 옮긴다.
    /// 0 ↔ 고정 알파 전체 구간을 각각 페이드 인/아웃 시간에 걸쳐 지나는 속도. 시간이 0 이하면 즉시 도달.
    /// </summary>
    public static float StepAlpha(float currentAlpha, bool show, float shownAlpha,
        float fadeInDuration, float fadeOutDuration, float deltaTime)
    {
        float target = show ? Mathf.Clamp01(shownAlpha) : 0f;
        float duration = target > currentAlpha ? fadeInDuration : fadeOutDuration;
        if (duration <= 0f)
            return target;

        // 고정 알파를 인스펙터에서 낮춰도 현재 알파에서 멈추지 않도록 둘 중 큰 쪽을 구간으로 잡는다.
        float range = Mathf.Max(Mathf.Clamp01(shownAlpha), currentAlpha);
        float step = range * deltaTime / duration;
        return Mathf.MoveTowards(currentAlpha, target, step);
    }
}
