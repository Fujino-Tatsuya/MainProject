using UnityEngine;

/// <summary>
/// 이 RectTransform 을 <see cref="Screen.safeArea"/> 에 맞춘다(할 일 158 — 목표 패널 여백 좌44·위40 은 Safe Area 안쪽 기준).
/// 앵커만 바꾼다(오프셋 0). 화면 크기·안전 영역이 바뀔 때만 다시 계산한다. Overlay 캔버스의 자식 전제.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class LobbySafeAreaFitter : MonoBehaviour
{
    private Rect _applied;
    private Vector2Int _screen;

    private void OnEnable() => Apply(true);

    private void Update() => Apply(false);

    private void Apply(bool force)
    {
        Rect safe = Screen.safeArea;
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (!force && safe == _applied && screen == _screen)
            return;

        _applied = safe;
        _screen = screen;
        if (screen.x <= 0 || screen.y <= 0)
            return;

        var rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
        rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
