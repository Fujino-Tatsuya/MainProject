using UnityEngine;

/// <summary>
/// 머리 위 특성 게이지 — 암살자 분노 게이지. AssassinState 는 전 피어 복제라 원격 플레이어 게이지도 같은 값을 그린다.
/// 최소 변신량 미만 흰색 / 이상 초록 / 변신 중 보라(줄어드는 줄). 최소 변신량 위치에 눈금.
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

    public float Fill => state != null ? OverheadTraitGaugeRules.Normalize(state.Rage, state.MaxRage) : 0f;

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

    public float MarkerPosition => state != null ? OverheadTraitGaugeRules.Normalize(state.MinTransformRage, state.MaxRage) : -1f;
}
