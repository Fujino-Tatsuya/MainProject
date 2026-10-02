using System;
using UnityEngine;
using Object = UnityEngine.Object;

[Serializable]
public struct SkillTooltipText
{
    [DataTableText, SerializeField] private string displayName;
    [DataTableText, SerializeField] private string subtitle;
    [DataTableText, TextArea(3, 10), SerializeField] private string description;
    [SerializeField] private Sprite icon;

    public string DisplayName => displayName ?? string.Empty;
    public string Subtitle => subtitle ?? string.Empty;
    public string Description => description ?? string.Empty;
    public Sprite Icon => icon;
}

/// <summary>툴팁 피해의 실제 결과와 Shift 계산식에 표시할 항.</summary>
public readonly struct SkillTooltipDamage
{
    public SkillTooltipDamage(
        int minimum, int maximum,
        float minimumCoefficient, float maximumCoefficient,
        int minimumFlatBonus = 0, int maximumFlatBonus = 0)
    {
        HasValue = true;
        Minimum = Mathf.Max(0, minimum);
        Maximum = Mathf.Max(Minimum, maximum);
        MinimumCoefficient = Mathf.Max(0f, minimumCoefficient);
        MaximumCoefficient = Mathf.Max(MinimumCoefficient, maximumCoefficient);
        MinimumFlatBonus = minimumFlatBonus;
        MaximumFlatBonus = maximumFlatBonus;
    }

    public bool HasValue { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public float MinimumCoefficient { get; }
    public float MaximumCoefficient { get; }
    public int MinimumFlatBonus { get; }
    public int MaximumFlatBonus { get; }
    public bool IsRange => Minimum != Maximum;

    public static SkillTooltipDamage FromLinear(Player player, float coefficient, int flatBonus)
    {
        if (player == null)
            return default;

        return FromLinear(player.FinalAttackDamage, coefficient, flatBonus);
    }

    public static SkillTooltipDamage FromLinear(float finalAttackDamage, float coefficient, int flatBonus)
    {
        int damage = Mathf.Max(0, Mathf.RoundToInt(finalAttackDamage * coefficient) + flatBonus);
        return new SkillTooltipDamage(damage, damage, coefficient, coefficient, flatBonus, flatBonus);
    }

    /// <summary>스킬 컨트롤러의 1차 스냅샷을 만든 뒤 차지·과열 배율을 곱하는 판정식.</summary>
    public static SkillTooltipDamage FromScaledSnapshot(
        float finalAttackDamage, float baseCoefficient, int flatBonus, float minimumScale, float maximumScale)
    {
        int snapshot = Mathf.Max(0, Mathf.RoundToInt(finalAttackDamage * baseCoefficient) + flatBonus);
        int minimum = Mathf.Max(0, Mathf.RoundToInt(snapshot * minimumScale));
        int maximum = Mathf.Max(0, Mathf.RoundToInt(snapshot * maximumScale));
        return new SkillTooltipDamage(
            minimum, maximum,
            baseCoefficient * minimumScale, baseCoefficient * maximumScale,
            Mathf.RoundToInt(flatBonus * minimumScale), Mathf.RoundToInt(flatBonus * maximumScale));
    }
}

/// <summary>HUD가 캐릭터 구현을 모르고 툴팁을 읽는 공용 계약.</summary>
public interface ISkillTooltipSource
{
    SkillTooltipText Tooltip { get; }
    Object TooltipValueSource { get; }
    SkillTooltipDamage GetTooltipDamage(Player player);
}
