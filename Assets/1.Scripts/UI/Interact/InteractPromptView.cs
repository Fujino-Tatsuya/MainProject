using UnityEngine;

/// <summary>
/// <see cref="InteractPrompt"/> 의 화면 표시. 독립 프리팹 <c>Assets/2.Prefabs/UI/InteractPrompt.prefab</c> 의
/// Screen Space - Overlay Canvas 루트에 붙고, 아이콘 RectTransform 을 대상 월드 위치에 맞춰 매 프레임 Canvas 좌표로 옮긴다.
/// 인스턴스는 <see cref="InteractPrompt"/> 가 프리팹마다 하나 만들어 재사용한다. 외형(스프라이트·크기·Canvas 설정)은 그 프리팹에서 직접 고친다. 배치 계산은 머리 위 체력바와 같은 <see cref="OverheadHealthBarScreenPlacement"/> 를 쓴다.
/// </summary>
public sealed class InteractPromptView : MonoBehaviour
{
    [Tooltip("이 뷰의 Screen Space - Overlay Canvas. sortingOrder -5 = 머리 위 체력바(-10) 위, CombatHUD(0) 아래.")]
    [SerializeField] private Canvas canvas;

    [Tooltip("키 아이콘(왼쪽 아래 앵커). 크기는 Canvas 단위 = 세로 1080 기준 픽셀이라 카메라 거리와 무관하게 일정하다.")]
    [SerializeField] private RectTransform icon;

    [Tooltip("아이콘 표시/숨김(alpha). 숨김 상태로 저장해 둔다.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("월드 기준점에서 화면상 추가 오프셋(Canvas 단위, 위 = +y).")]
    [SerializeField] private Vector2 screenOffset = Vector2.zero;

    private void OnEnable() => Canvas.willRenderCanvases += ApplyScreenPosition;

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ApplyScreenPosition;
        if (group != null)
            group.alpha = 0f;
    }

    // 위치는 Canvas 렌더 직전에 반영한다 — 카메라가 LateUpdate 에서 움직인 뒤라 아이콘이 대상에서 밀리지 않는다(머리 위 체력바와 같은 이유).
    // 이 뷰가 표시 뷰가 아님·카메라 없음·카메라 뒤·화면 밖은 투명 처리.
    private void ApplyScreenPosition()
    {
        if (canvas == null || icon == null || group == null)
            return;

        bool visible = false;
        Camera cam = Camera.main;
        if (cam != null && InteractPrompt.TryGetWorldAnchor(this, out Vector3 anchor))
        {
            visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
                cam.WorldToScreenPoint(anchor), new Vector2(Screen.width, Screen.height), canvas.scaleFactor,
                screenOffset, icon.sizeDelta * 0.5f, out Vector2 position);
            if (visible)
                icon.anchoredPosition = position;
        }

        group.alpha = visible ? 1f : 0f;
    }
}
