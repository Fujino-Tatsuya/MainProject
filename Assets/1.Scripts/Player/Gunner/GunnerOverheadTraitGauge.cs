using UnityEngine;

/// <summary>
/// 머리 위 특성 게이지 — 거너 과열. GunnerHeat 는 전 피어 복제라 원격 플레이어 게이지도 같은 값을 그린다.
/// 0단계 흰색 / 1~2단계 주황 / 3단계·과열 빨강, 과열 중 깜빡임(GunnerHeatGauge 와 같은 규칙).
/// </summary>
[DisallowMultipleComponent]
public sealed class GunnerOverheadTraitGauge : MonoBehaviour, IOverheadTraitGauge
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] private GunnerHeat heat;
    [SerializeField] private Color coolColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private Color warmColor = new Color(1f, 0.55f, 0.1f, 1f);
    [SerializeField] private Color hotColor = new Color(1f, 0.15f, 0.1f, 1f);
    [SerializeField] private Color overheatBlinkColor = Color.white;
    [SerializeField, Min(0.05f)] private float overheatBlinkPeriod = 0.4f;

    private void Awake()
    {
        if (heat == null)
            heat = GetComponentInParent<GunnerHeat>();
    }

    public float Fill => heat != null ? heat.Normalized : 0f;

    public Color FillColor
    {
        get
        {
            if (heat == null)
                return coolColor;

            bool overheated = heat.IsOverheated;
            if (OverheadTraitGaugeRules.IsBlinkOn(overheated, Time.unscaledTime, overheatBlinkPeriod))
                return overheatBlinkColor;

            switch (OverheadTraitGaugeRules.GunnerTone(heat.CurrentStage, overheated))
            {
                case GunnerHeatTone.Hot: return hotColor;
                case GunnerHeatTone.Warm: return warmColor;
                default: return coolColor;
            }
        }
    }

    public float MarkerPosition => -1f;
}
