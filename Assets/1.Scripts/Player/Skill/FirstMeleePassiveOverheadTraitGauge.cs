using UnityEngine;

/// <summary>
/// 머리 위 특성 게이지 — 가붕이 패시브(불굴의 의지) 쿨타임 진행률. 충전 상태면 가득 찬 노랑, 차는 중 흰색.
/// 충전 여부(상태이상 PassiveCharge)와 쿨타임 끝 시각(readyServerTime)은 둘 다 전 피어에서 읽힌다.
/// </summary>
[DisallowMultipleComponent]
public sealed class FirstMeleePassiveOverheadTraitGauge : MonoBehaviour, IOverheadTraitGauge
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] private FirstMeleePassive passive;
    [SerializeField] private Color chargingColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private Color chargedColor = new Color(1f, 0.85f, 0.2f, 1f);

    private void Awake()
    {
        if (passive == null)
            passive = GetComponentInParent<FirstMeleePassive>();
    }

    public float Fill => passive != null
        ? OverheadTraitGaugeRules.FirstMeleePassiveFill(passive.IsReady, passive.RemainingCooldown, passive.CooldownTime)
        : 0f;

    public Color FillColor => passive != null && OverheadTraitGaugeRules.PassiveTone(passive.IsReady) == FirstMeleePassiveTone.Charged
        ? chargedColor
        : chargingColor;

    public float MarkerPosition => -1f;
}
