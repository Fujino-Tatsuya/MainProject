using UnityEngine;

/// <summary>
/// 카메라 줌에 따른 데미지 숫자 배율(할 일 154). 배율 = 지금 화면의 "월드 1m 가 Canvas 몇 단위인가" ÷ 기준값.
/// 카메라 코드에 묶이지 않도록 줌 값을 직접 읽지 않고 투영 결과로 잰다 — 팔로우 거리·FOV·직교 크기 어느 쪽으로 줌해도 같다.
/// </summary>
public static class FloatingDamageZoomScalePolicy
{
    /// <summary>끔이거나 값이 잘못됐으면 1, 아니면 현재 ÷ 기준을 [최소, 최대] 로 자른 값.</summary>
    public static float Evaluate(bool enabled, float currentUnitsPerMeter, float referenceUnitsPerMeter,
        float minScale, float maxScale)
    {
        if (!enabled || currentUnitsPerMeter <= 0f || referenceUnitsPerMeter <= 0f)
            return 1f;

        float min = Mathf.Max(0.01f, Mathf.Min(minScale, maxScale));
        float max = Mathf.Max(min, maxScale);
        return Mathf.Clamp(currentUnitsPerMeter / referenceUnitsPerMeter, min, max);
    }

    /// <summary>
    /// 카메라 정면이 높이 <paramref name="groundY"/> 평면과 만나는 점(= 화면 중앙의 바닥)에서 카메라 위쪽 1m 가 Canvas 몇 단위인지.
    /// 숫자 위치가 아니라 화면 중앙에서 재므로 화면 위·아래 원근 차이나 앵커 높이와 무관하게 줌만 반영한다.
    /// 바닥을 내려다보지 않는 카메라면 0(= 배율 1).
    /// </summary>
    public static float MeasureUnitsPerMeter(Camera camera, float groundY, float canvasScaleFactor)
    {
        if (camera == null)
            return 0f;

        Transform view = camera.transform;
        Vector3 forward = view.forward;
        if (forward.y > -0.01f)
            return 0f;

        float distance = (view.position.y - groundY) / -forward.y;
        if (distance <= 0f)
            return 0f;

        Vector3 focus = view.position + forward * distance;
        Vector2 a = camera.WorldToScreenPoint(focus);
        Vector2 b = camera.WorldToScreenPoint(focus + view.up);
        return (b - a).magnitude / Mathf.Max(0.0001f, canvasScaleFactor);
    }
}
