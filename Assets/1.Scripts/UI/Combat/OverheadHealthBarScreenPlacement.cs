using UnityEngine;

/// <summary>
/// 머리 위 체력바의 화면 배치 계산. 월드 기준점 → 스크린 좌표(Camera.WorldToScreenPoint 결과) → 오버레이 Canvas 좌표.
/// MonoBehaviour 밖 순수 계산이라 EditMode 테스트로 고정한다.
/// </summary>
public static class OverheadHealthBarScreenPlacement
{
    /// <summary>바를 띄울 월드 기준점 = 기준 위치에서 월드 위쪽으로 높이만큼.</summary>
    public static Vector3 AnchorWorld(Vector3 basePosition, float worldHeight) => basePosition + Vector3.up * worldHeight;

    /// <summary>
    /// 스크린 좌표를 오버레이 Canvas 좌표(왼쪽 아래 원점)로 바꾸고 보일지 판정한다.
    /// 카메라 뒤(z ≤ 0)거나 바 전체가 화면 밖이면 false. 화면 가장자리에 걸친 바는 보인다.
    /// </summary>
    /// <param name="screenPoint">Camera.WorldToScreenPoint 결과(픽셀, z = 카메라 앞 거리).</param>
    /// <param name="screenSize">화면 픽셀 크기.</param>
    /// <param name="canvasScaleFactor">Canvas.scaleFactor — 픽셀을 Canvas 단위로 나눈다.</param>
    /// <param name="canvasOffset">Canvas 단위 오프셋(위 = +y).</param>
    /// <param name="halfExtent">바 크기의 절반(Canvas 단위, 스케일 적용 후).</param>
    public static bool TryGetCanvasPosition(Vector3 screenPoint, Vector2 screenSize, float canvasScaleFactor,
        Vector2 canvasOffset, Vector2 halfExtent, out Vector2 canvasPosition)
    {
        canvasPosition = default;
        if (!(screenPoint.z > 0f) || float.IsNaN(screenPoint.x) || float.IsNaN(screenPoint.y))
            return false;

        float scale = Mathf.Max(0.0001f, canvasScaleFactor);
        canvasPosition = new Vector2(screenPoint.x / scale, screenPoint.y / scale) + canvasOffset;
        Vector2 canvasSize = screenSize / scale;

        return canvasPosition.x + halfExtent.x >= 0f && canvasPosition.x - halfExtent.x <= canvasSize.x
            && canvasPosition.y + halfExtent.y >= 0f && canvasPosition.y - halfExtent.y <= canvasSize.y;
    }
}
