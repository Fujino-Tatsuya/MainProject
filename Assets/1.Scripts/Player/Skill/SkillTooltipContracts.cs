using System;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 스킬 툴팁 문구. 인라인 값은 ko 원문이자 폴백 — 주인이 <see cref="Keyed"/> 로 키를 붙여 내보내면 getter 가
/// <c>Loc.Get(주인ID/필드.displayName, 인라인)</c> 을 돌려준다(Strings 시트에 행이 없으면 인라인 그대로).
/// 키 규칙은 <see cref="LocKey"/>, 상세는 Docs/tech/localization.md §5.
/// </summary>
[Serializable]
public struct SkillTooltipText
{
    [DataTableText, SerializeField] private string displayName;
    [DataTableText, SerializeField] private string subtitle;
    [DataTableText, TextArea(3, 10), SerializeField] private string description;
    [SerializeField] private Sprite icon;

    // 주인ID/필드경로(예 "FirstMeleeMainSkillData/tooltip"). 직렬화하지 않는다 — 주인이 getter 에서 매번 붙인다.
    [NonSerialized] private string keyPrefix;

    public string DisplayName => Localize(nameof(displayName), displayName);
    public string Subtitle => Localize(nameof(subtitle), subtitle);
    public string Description => Localize(nameof(description), description);
    public Sprite Icon => icon;

    /// <summary>키를 붙인 사본. 주인은 <c>Tooltip => tooltip.Keyed(this, nameof(tooltip))</c> 로 내보낸다.</summary>
    public SkillTooltipText Keyed(UnityEngine.Object owner, string fieldPath)
    {
        SkillTooltipText copy = this;
        copy.keyPrefix = LocKey.ForField(owner, fieldPath);
        return copy;
    }

    private string Localize(string field, string inline)
    {
        inline ??= string.Empty;
        return keyPrefix == null ? inline : Loc.Get(keyPrefix + "." + field, inline);
    }
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

        return FromLinear(player.FinalAttackDamage, coefficient, flatBonus, player.DealtDamagePercent);
    }

    /// <summary>판정 스냅샷과 같은 식(AttackStatFormula.Snapshot). 가하는 피해% 는 결과에만 들어가고 Shift 계산식 항(계수·고정값)엔 표시하지 않는다.</summary>
    public static SkillTooltipDamage FromLinear(float finalAttackDamage, float coefficient, int flatBonus, float dealtDamagePercent = 0f)
    {
        int damage = AttackStatFormula.Snapshot(Mathf.RoundToInt(finalAttackDamage), coefficient, flatBonus, dealtDamagePercent);
        return new SkillTooltipDamage(damage, damage, coefficient, coefficient, flatBonus, flatBonus);
    }

    /// <summary>스킬 컨트롤러의 1차 스냅샷을 만든 뒤 차지·과열 배율을 곱하는 판정식.</summary>
    public static SkillTooltipDamage FromScaledSnapshot(
        float finalAttackDamage, float baseCoefficient, int flatBonus, float minimumScale, float maximumScale,
        float dealtDamagePercent = 0f)
    {
        int snapshot = AttackStatFormula.Snapshot(Mathf.RoundToInt(finalAttackDamage), baseCoefficient, flatBonus, dealtDamagePercent);
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
