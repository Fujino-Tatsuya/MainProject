using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <see cref="InteractPrompt"/> 의 화면 표시. 처음 Show 될 때 만드는 DontDestroyOnLoad Screen Space - Overlay Canvas 하나에
/// 아이콘(Image, 스프라이트가 없으면 TMP "F")을 두고, 대상 월드 위치를 매 프레임 Canvas 좌표로 옮긴다.
/// 배치 계산은 머리 위 체력바와 같은 <see cref="OverheadHealthBarScreenPlacement"/> 를 쓴다.
/// </summary>
public sealed class InteractPromptView : MonoBehaviour
{
    /// <summary>머리 위 체력바(-10) 위, CombatHUD(0) 아래.</summary>
    public const int SortingOrder = -5;

    InteractPromptSlot slot;
    Canvas canvas;
    RectTransform iconRect;
    CanvasGroup group;
    Vector2 screenOffset;

    public static InteractPromptView Create(InteractPromptSlot slot)
    {
        var settings = Resources.Load<InteractPromptSettings>(InteractPromptSettings.ResourcePath);
        if (settings == null)
            Debug.LogWarning($"[InteractPrompt] Resources/{InteractPromptSettings.ResourcePath} 가 없습니다 — 기본 크기와 텍스트 \"F\" 로 그립니다.");

        var root = new GameObject("InteractPromptCanvas", typeof(RectTransform));
        DontDestroyOnLoad(root);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = OverheadHealthBarCanvas.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var icon = new GameObject("InteractKeyIcon", typeof(RectTransform));
        var rect = (RectTransform)icon.transform;
        rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = settings != null ? settings.IconSize : new Vector2(56f, 56f);

        Sprite sprite = settings != null ? settings.Icon : null;
        if (sprite != null)
        {
            Image image = icon.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        else
        {
            TextMeshProUGUI text = icon.AddComponent<TextMeshProUGUI>();
            text.text = "F";
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
        }

        CanvasGroup group = icon.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        InteractPromptView view = root.AddComponent<InteractPromptView>();
        view.slot = slot;
        view.canvas = canvas;
        view.iconRect = rect;
        view.group = group;
        view.screenOffset = settings != null ? settings.ScreenOffset : Vector2.zero;
        return view;
    }

    private void OnEnable() => Canvas.willRenderCanvases += ApplyScreenPosition;

    private void OnDisable() => Canvas.willRenderCanvases -= ApplyScreenPosition;

    // 위치는 Canvas 렌더 직전에 반영한다 — 카메라가 LateUpdate 에서 움직인 뒤라 아이콘이 대상에서 밀리지 않는다(머리 위 체력바와 같은 이유).
    // 표시 안 함·카메라 없음·카메라 뒤·화면 밖은 투명 처리.
    private void ApplyScreenPosition()
    {
        if (slot == null || iconRect == null)
            return;

        bool visible = false;
        Camera cam = Camera.main;
        if (cam != null && slot.TryGetWorldAnchor(out Vector3 anchor))
        {
            visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
                cam.WorldToScreenPoint(anchor), new Vector2(Screen.width, Screen.height), canvas.scaleFactor,
                screenOffset, iconRect.sizeDelta * 0.5f, out Vector2 position);
            if (visible)
                iconRect.anchoredPosition = position;
        }

        group.alpha = visible ? 1f : 0f;
    }
}
