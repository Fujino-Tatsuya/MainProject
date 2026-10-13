using UnityEngine;

/// <summary>
/// <see cref="InteractPrompt"/> 의 화면 표시. <c>Assets/2.Prefabs/UI/InteractPrompt.prefab</c> 의 루트(일반 Transform)에 붙는다.
/// 루트 = 월드 기준점 — 대상 오브젝트의 자식으로 두고 로컬 위치로 높이를 정한다. 그 아래 Screen Space - Overlay Canvas 의
/// 아이콘 RectTransform 을 기준점의 화면 위치로 매 프레임 옮긴다(Overlay Canvas 루트 Transform 은 Unity 가 몰아서 기준점으로 못 쓴다).
/// 외형(스프라이트·크기·Canvas 설정)은 그 프리팹에서 직접 고친다. 배치 계산은 머리 위 체력바와 같은 <see cref="OverheadHealthBarScreenPlacement"/> 를 쓴다.
/// </summary>
public sealed class InteractPromptView : MonoBehaviour
{
    [Tooltip("자식 Screen Space - Overlay Canvas. sortingOrder -5 = 머리 위 체력바(-10) 위, CombatHUD(0) 아래.")]
    [SerializeField] private Canvas canvas;

    [Tooltip("키 아이콘(왼쪽 아래 앵커). 크기는 Canvas 단위 = 세로 1080 기준 픽셀이라 카메라 거리와 무관하게 일정하다.")]
    [SerializeField] private RectTransform icon;

    [Tooltip("아이콘 표시/숨김(alpha). 숨김 상태로 저장해 둔다.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("월드 기준점에서 화면상 추가 오프셋(Canvas 단위, 위 = +y).")]
    [SerializeField] private Vector2 screenOffset = Vector2.zero;

    [Header("키 표기 (설정창 리바인드 — PLAN-settings S14)")]
    [Tooltip("F 아이콘 그림. 상호작용 키가 F 일 때만 보인다.")]
    [SerializeField] private UnityEngine.UI.Image keyIconImage;
    [Tooltip("F 가 아닌 키일 때 키 이름을 쓰는 텍스트(아이콘 위). 비어 있으면 키가 바뀌어도 F 그림 그대로다.")]
    [SerializeField] private TMPro.TMP_Text keyLabel;

    private void OnEnable()
    {
        Canvas.willRenderCanvases += ApplyScreenPosition;
        KeyBindingConfig.Changed += RefreshKeyGlyph;
        RefreshKeyGlyph();
    }

    // 기본 키(F)면 기존 그림, 다른 키로 바꿨으면 그림을 끄고 키 이름을 쓴다.
    private void RefreshKeyGlyph()
    {
        if (keyLabel == null)
            return;

        string path = KeyBindingConfig.EffectivePath("Player/Interact");
        bool isDefaultF = string.IsNullOrEmpty(path) || string.Equals(path, "<Keyboard>/f", System.StringComparison.OrdinalIgnoreCase);
        if (keyIconImage != null)
            keyIconImage.enabled = isDefaultF;
        keyLabel.gameObject.SetActive(!isDefaultF);
        if (!isDefaultF)
            keyLabel.text = KeyBindingConfig.DisplayName("Player/Interact");
    }

    private void OnDisable()
    {
        KeyBindingConfig.Changed -= RefreshKeyGlyph;
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
        if (cam != null && InteractPrompt.IsDisplaying(this))
        {
            visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
                cam.WorldToScreenPoint(transform.position), new Vector2(Screen.width, Screen.height), canvas.scaleFactor,
                screenOffset, icon.sizeDelta * 0.5f, out Vector2 position);
            if (visible)
                icon.anchoredPosition = position;
        }

        group.alpha = visible ? 1f : 0f;
    }
}
