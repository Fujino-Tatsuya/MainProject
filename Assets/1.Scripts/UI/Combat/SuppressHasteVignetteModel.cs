using UnityEngine;

/// <summary>
/// 제압 가속 비네팅의 순수 계산 — 고정 알파까지의 페이드 인/아웃 + 스피드 라인(할 일 249) 켜짐 버스트 배율·프레임 번호.
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

    /// <summary>
    /// 켜진 뒤 경과 <paramref name="elapsed"/> 의 버스트 배율. 켜진 순간 <paramref name="multiplier"/> 에서 <paramref name="duration"/> 동안 선형으로 1 까지.
    /// 그 뒤(또는 시간 0 이하·배율 1 이하)는 1.
    /// </summary>
    public static float BurstFactor(float elapsed, float duration, float multiplier)
    {
        if (duration <= 0f || elapsed < 0f || elapsed >= duration || multiplier <= 1f)
            return 1f;

        return Mathf.Lerp(multiplier, 1f, elapsed / duration);
    }

    /// <summary>스피드 라인 프레임 번호 = floor(시간 × fps). fps 0 이하면 0(고정 — 안 바뀜).</summary>
    public static int LineFrame(float time, float fps)
    {
        return fps > 0f && time > 0f ? Mathf.FloorToInt(time * fps) : 0;
    }

    /// <summary>켜지는 순간 = 이전엔 꺼져 있었고 지금 켜짐(버스트 시작).</summary>
    public static bool IsTurnOn(bool previous, bool current)
    {
        return !previous && current;
    }
}
