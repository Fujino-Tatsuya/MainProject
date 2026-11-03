using UnityEngine;

/// <summary>
/// 한계 돌파(GUN_2_OVERDRIVE) 과열 단계별 거너 공격 이펙트 색(2026-11-03 민경 grill — PLAN.md 「한계 돌파 VFX」).
/// 공용 1개(<c>9.ScriptableObject/Effects/GunnerOverdriveVfxColors.asset</c>)를 <see cref="GunnerBeamView"/>(기본 공격·Q·R 시전)와
/// <see cref="GunnerTrackingLaserView"/>(R 영역·낙뢰)가 읽는다.
///
/// 색은 <b>색상만</b> 쓴다(<see cref="EffectTint.FromColor"/>) — 원래 파랑 이펙트의 밝기·흐름은 그대로 두고 색상만 이 색으로 바꾼다.
/// 단계마다 정해진 색으로 바로 바꾼다(파랑 → 빨강을 섞으면 중간이 보라가 된다). 0단계·한계 돌파 없음 = 원래 색.
/// </summary>
[CreateAssetMenu(fileName = "GunnerOverdriveVfxColors", menuName = "Effects/Gunner Overdrive Vfx Colors")]
public sealed class GunnerOverdriveVfxColors : ScriptableObject
{
    [Tooltip("과열 1단계")]
    [SerializeField] private Color stage1 = new Color(1f, 0.5f, 0.1f);
    [Tooltip("과열 2단계")]
    [SerializeField] private Color stage2 = new Color(1f, 0.28f, 0.06f);
    [Tooltip("과열 3단계")]
    [SerializeField] private Color stage3 = new Color(0.9f, 0.05f, 0.08f);
    [Tooltip("원래 채도에 곱하는 값(1 = 그대로). 흰 심은 이 값과 무관하게 흰 채로 남는다")]
    [SerializeField, Min(0f)] private float saturation = 1f;

    /// <summary>단계(1~3)의 색. 0 이하 = 원래 색(<see cref="EffectTint.None"/>).</summary>
    public EffectTint TintFor(int stage)
    {
        switch (stage)
        {
            case 1: return EffectTint.FromColor(stage1, saturation);
            case 2: return EffectTint.FromColor(stage2, saturation);
            case >= 3: return EffectTint.FromColor(stage3, saturation);
            default: return EffectTint.None;
        }
    }
}
