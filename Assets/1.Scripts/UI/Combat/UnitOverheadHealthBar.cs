using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유닛 머리 위 월드스페이스 체력바. 대상 Unit 의 자식(월드스페이스 캔버스)에 부착.
/// 이 컴포넌트는 Unit 의 체력·실드를 읽어 게이지만 그린다. 색·표시·라벨은 자기/부모의
/// IOverheadHealthBarRule 이 공급하고(플레이어 = PlayerOverheadHealthBarRule), 규칙이 없으면
/// 항상 표시 + defaultFillColor + 라벨 없음.
/// 게이지: 배경 → 잔상(trailFill) → 실드(shieldFill) → 체력(hpFill) 순으로 겹친다.
/// LateUpdate에서 카메라 회전을 그대로 따라가는 화면 정렬 빌보드.
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

    [Tooltip("규칙 컴포넌트가 없을 때의 체력 게이지 색")]
    [SerializeField] private Color defaultFillColor = new Color(0.2f, 0.8f, 0.25f, 1f);
    [Tooltip("체력이 줄어든 뒤 잔상이 머무는 시간(초)")]
    [SerializeField, Min(0f)] private float trailHoldDelay = 0.4f;
    [Tooltip("잔상이 내려오는 속도(게이지 전체 길이/초)")]
    [SerializeField, Min(0f)] private float trailSpeed = 0.8f;

    private Unit unit;
    private IOverheadHealthBarRule rule;
    private readonly OverheadHealthTrail trail = new OverheadHealthTrail();

    private void Awake()
    {
        unit = GetComponentInParent<Unit>();
        rule = GetComponentInParent<IOverheadHealthBarRule>();
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

        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

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
    }
}
