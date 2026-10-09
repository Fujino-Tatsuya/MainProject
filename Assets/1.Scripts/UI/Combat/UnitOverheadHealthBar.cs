using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유닛 머리 위 체력바. 대상 Unit 의 자식에 부착하고, 비주얼(barRoot)은 Awake 에서 공용 오버레이 Canvas
/// (OverheadHealthBarCanvas)로 옮겨 화면 공간에 그린다 — 지형·바닥 메쉬에 파묻히지 않는다.
/// 이 컴포넌트는 Unit 의 체력·실드를 읽어 게이지만 그린다. 색·표시·라벨은 자기/부모의
/// IOverheadHealthBarRule 이 공급하고(플레이어 = PlayerOverheadHealthBarRule), 규칙이 없으면
/// 항상 표시 + defaultFillColor + 라벨 없음.
/// 게이지: 배경 → 잔상(trailFill) → 실드(shieldFill) → 체력(hpFill) 순으로 겹친다.
/// 체력 바 아래 얇은 특성 게이지 줄(traitRoot)은 부모의 IOverheadTraitGauge 가 값·색·눈금을 공급하고, 없으면 숨긴다.
/// 위치 = 이 오브젝트 위치 + 월드 높이 → 로컬 Camera.main 스크린 좌표 + 화면 오프셋. 거리와 무관한 고정 크기.
/// 옮긴 비주얼은 이 컴포넌트가 꺼지면 숨기고, 파괴되면 같이 파괴한다.
/// </summary>
public class UnitOverheadHealthBar : MonoBehaviour
{
    [SerializeField] private GameObject barRoot;
    [SerializeField] private Image hpFill;
    [Tooltip("체력 뒤에 이어 붙는 실드 게이지. 비워도 동작한다.")]
    [SerializeField] private Image shieldFill;
    [Tooltip("체력 감소 잔상 게이지. 비워도 동작한다.")]
    [SerializeField] private Image trailFill;
    [Tooltip("규칙의 라벨(이름)을 띄울 텍스트. 비워도 동작한다.")]
    [SerializeField] private TMP_Text label;

    [Header("특성 게이지 줄 (체력 바 아래)")]
    [Tooltip("특성 게이지 줄 전체. 부모에 IOverheadTraitGauge 가 없으면 숨긴다. 비워도 동작한다.")]
    [SerializeField] private GameObject traitRoot;
    [SerializeField] private Image traitFill;
    [Tooltip("눈금(예: 암살자 최소 변신량). 가로 앵커를 눈금 위치로 옮긴다. 비워도 동작한다.")]
    [SerializeField] private RectTransform traitMarker;

    [Header("화면 배치")]
    [Tooltip("이 오브젝트 위치에서 월드 위쪽으로 올릴 높이(m). 바를 띄울 기준점.")]
    [SerializeField] private float worldHeightOffset = 1.84f;
    [Tooltip("기준점 화면 위치에서 더 옮길 오프셋. 1920×1080 기준 픽셀(세로 기준 스케일), 위 = +y.")]
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 80f);
    [Tooltip("바 비주얼 배율. 1 = 프리팹의 Bar 크기를 1080p 기준 픽셀로 그대로.")]
    [SerializeField, Min(0.01f)] private float screenScale = 0.9f;

    [Tooltip("규칙 컴포넌트가 없을 때의 체력 게이지 색")]
    [SerializeField] private Color defaultFillColor = new Color(0.2f, 0.8f, 0.25f, 1f);
    [Tooltip("체력이 줄어든 뒤 잔상이 머무는 시간(초)")]
    [SerializeField, Min(0f)] private float trailHoldDelay = 0.4f;
    [Tooltip("잔상이 내려오는 속도(게이지 전체 길이/초)")]
    [SerializeField, Min(0f)] private float trailSpeed = 0.8f;

    private Unit unit;
    private IOverheadHealthBarRule rule;
    private IOverheadTraitGauge traitGauge;
    private readonly OverheadHealthTrail trail = new OverheadHealthTrail();

    private RectTransform barRect;
    private CanvasGroup barGroup;
    private Canvas screenCanvas;

    private void Awake()
    {
        unit = GetComponentInParent<Unit>();
        rule = GetComponentInParent<IOverheadHealthBarRule>();
        traitGauge = GetComponentInParent<IOverheadTraitGauge>();
        if (traitRoot != null && traitGauge == null)
            traitRoot.SetActive(false);

        MoveBarToScreenCanvas();
    }

    // 바 크기는 옮기기 전 레이아웃 그대로(늘림 앵커여도 지금 rect 크기)를 고정 픽셀 크기로 쓴다.
    private void MoveBarToScreenCanvas()
    {
        if (barRoot == null)
            return;
        if (barRoot == gameObject || transform.IsChildOf(barRoot.transform) || !(barRoot.transform is RectTransform rect))
        {
            Debug.LogError("[OverheadHealthBar] barRoot 는 이 오브젝트 아래의 UI 자식이어야 합니다.", this);
            return;
        }

        Vector2 size = rect.rect.size;
        screenCanvas = OverheadHealthBarCanvas.Instance;
        rect.SetParent(screenCanvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * screenScale;
        barRoot.name = $"OverheadBar ({(unit != null ? unit.name : name)})";

        barRect = rect;
        if (!barRoot.TryGetComponent(out barGroup))
            barGroup = barRoot.AddComponent<CanvasGroup>();
        barGroup.blocksRaycasts = false;
        barGroup.interactable = false;
        barGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += ApplyScreenPosition;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ApplyScreenPosition;
        if (barRect != null && barRoot.activeSelf)
            barRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (barRect != null)
            Destroy(barRoot);
    }

    private void LateUpdate()
    {
        bool shouldShow = unit != null && (rule == null || rule.ShouldShow);
        if (barRoot != null && barRoot.activeSelf != shouldShow)
            barRoot.SetActive(shouldShow);
        if (label != null && !shouldShow && label.gameObject.activeSelf)
            label.gameObject.SetActive(false);

        if (!shouldShow)
            return;

        // 사망·Soul 에서도 숨기지 않고 체력 0 그대로 그린다.
        int health = unit.CurrentHealth;
        var layout = OverheadHealthBarLayout.Compute(health, unit.FinalMaxHp, unit.CurrentShield);
        float trailValue = trail.Step(Mathf.Max(0, health), Time.deltaTime, trailHoldDelay, trailSpeed * layout.Scale);

        if (hpFill != null)
        {
            hpFill.fillAmount = layout.HealthFill;
            hpFill.color = rule != null ? rule.FillColor : defaultFillColor;
        }
        if (shieldFill != null)
            shieldFill.fillAmount = layout.ShieldFill;
        if (trailFill != null)
            trailFill.fillAmount = layout.ToFill(trailValue);

        if (label != null)
        {
            string text = rule != null ? rule.Label : null;
            bool hasText = !string.IsNullOrEmpty(text);
            if (label.gameObject.activeSelf != hasText)
                label.gameObject.SetActive(hasText);
            if (hasText && label.text != text)
                label.text = text;
        }

        UpdateTraitGauge();
    }

    // 위치는 Canvas 렌더 직전에 반영한다 — 카메라가 LateUpdate 에서 움직인 뒤라 바가 유닛에서 밀리지 않는다.
    // 카메라 없음·카메라 뒤·화면 밖은 투명 처리(활성 토글은 이 시점에 그래픽 재빌드가 늦어 쓰지 않는다).
    private void ApplyScreenPosition()
    {
        if (barRect == null || !barRoot.activeSelf)
            return;

        Camera cam = Camera.main;
        bool visible = false;
        if (cam != null && screenCanvas != null)
        {
            Vector3 anchor = OverheadHealthBarScreenPlacement.AnchorWorld(transform.position, worldHeightOffset);
            Vector2 halfExtent = barRect.sizeDelta * (screenScale * 0.5f);
            visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
                cam.WorldToScreenPoint(anchor), new Vector2(Screen.width, Screen.height), screenCanvas.scaleFactor,
                screenOffset, halfExtent, out Vector2 position);
            if (visible)
            {
                barRect.anchoredPosition = position;
                barRect.localScale = Vector3.one * screenScale;
            }
        }

        barGroup.alpha = visible ? 1f : 0f;
    }

    // 0 이어도 빈 줄로 항상 그린다.
    private void UpdateTraitGauge()
    {
        if (traitGauge == null)
            return;

        if (traitFill != null)
        {
            traitFill.fillAmount = Mathf.Clamp01(traitGauge.Fill);
            traitFill.color = traitGauge.FillColor;
        }

        if (traitMarker != null)
        {
            float marker = traitGauge.MarkerPosition;
            bool showMarker = marker >= 0f;
            if (traitMarker.gameObject.activeSelf != showMarker)
                traitMarker.gameObject.SetActive(showMarker);
            if (showMarker)
            {
                float x = Mathf.Clamp01(marker);
                traitMarker.anchorMin = new Vector2(x, traitMarker.anchorMin.y);
                traitMarker.anchorMax = new Vector2(x, traitMarker.anchorMax.y);
            }
        }
    }
}
