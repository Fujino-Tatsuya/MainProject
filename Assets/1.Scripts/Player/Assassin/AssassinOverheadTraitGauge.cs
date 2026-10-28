using UnityEngine;

/// <summary>
/// 머리 위 특성 게이지 — 암살자 분노 게이지. AssassinState 는 전 피어 복제라 원격 플레이어 게이지도 같은 값을 그린다.
/// 최소 변신량 미만 흰색 / 이상 초록 / 변신 중 보라(줄어드는 줄). 최소 변신량 위치에 눈금. 절제된 분노면 줄을 숨긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class AssassinOverheadTraitGauge : MonoBehaviour, IOverheadTraitGauge
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] private AssassinState state;
    [SerializeField] private Color buildingColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private Color readyColor = new Color(0.3f, 0.9f, 0.35f, 1f);
    [SerializeField] private Color transformedColor = new Color(0.65f, 0.35f, 1f, 1f);

    private void Awake()
    {
        if (state == null)
            state = GetComponentInParent<AssassinState>();
    }

    // 절제된 분노(R·분노 없음)면 빈 줄·눈금 없음 + 줄 자체도 숨긴다(Visible, 할 일 133).
    private bool Active => state != null && !state.IsRestrained;

    public bool Visible => state == null || AssassinRestraintRules.ShowsRageGauge(state.IsRestrained);

    public float Fill => Active ? OverheadTraitGaugeRules.Normalize(state.Rage, state.MaxRage) : 0f;

    public Color FillColor
    {
        get
        {
            if (state == null)
                return buildingColor;

            switch (OverheadTraitGaugeRules.AssassinTone(state.Rage, state.MinTransformRage, state.IsTransformed))
            {
                case AssassinRageTone.Transformed: return transformedColor;
                case AssassinRageTone.Ready: return readyColor;
                default: return buildingColor;
            }
        }
    }

    public float MarkerPosition => Active ? OverheadTraitGaugeRules.Normalize(state.MinTransformRage, state.MaxRage) : -1f;
}
