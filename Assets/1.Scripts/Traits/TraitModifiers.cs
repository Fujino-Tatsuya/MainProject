using System;
using System.Collections.Generic;

/// <summary>특성 수치 배율의 종류(할 일 125 2/5). 값의 의미는 <see cref="TraitModifier"/> 참고.</summary>
public enum TraitModifierKind : byte
{
    /// <summary>공격 계열 원래 피해 배율 — 기준 피해(Dref)에 한 번 곱한다. 값 = 배율(1.2 = +20%, 0.85 = −15%). 서로 곱한다.</summary>
    DamageMultiplier = 0,

    /// <summary>슬롯 전체 쿨타임 배율. 값 = 배율(0.85 = 15% 감소, 1.2 = 20% 증가). 서로 곱한다(명세 §6).</summary>
    CooldownMultiplier = 1,

    /// <summary>받는/주는 회복량 배율. 값 = 배율. 서로 곱한다. 소비처는 3/5(HealHp 실제 회복량)에서 연결한다.</summary>
    HealMultiplier = 2,

    /// <summary>PlayerStats 기본% 단계 몫. 값 = %(0.2 = +20%). 같은 단계끼리 더한다(player-stats.md §2).</summary>
    StatBasePercent = 3,

    /// <summary>PlayerStats 전체% 단계 몫. 값 = %(0.2 = +20%). 같은 단계끼리 더한다.</summary>
    StatTotalPercent = 4,

    /// <summary>PlayerStats 가하는 피해% 몫. 값 = %. 같은 단계끼리 더한다.</summary>
    DealtDamagePercent = 5,
}

/// <summary>
/// 공격의 형태 — 같은 <see cref="AttackType"/> 안에서 특성이 갈라 보는 구분(어쌔신 일반/변신 Q·E, 강타).
/// Alternate = 슬롯 대체 스킬·변신 기본 공격(<see cref="PlayerSkillController"/> 대체 세트). Enhanced = 일반 E 로 강화한 기본 공격(강타).
/// </summary>
public enum TraitAttackForm : byte
{
    Normal = 0,
    Alternate = 1,
    Enhanced = 2,
}

/// <summary>
/// 피해 배율 질의 키 = (공격 종류, 형태). 공격 종류는 기존 <see cref="AttackType"/> 그대로(평타 = Default, Q/E/R = SkillQ/E/R, 우클릭 = Skill).
/// 가붕이 패시브 추가피해는 <see cref="AttackType.None"/>(공격 계열 미지정)으로 묻는다.
/// </summary>
public readonly struct TraitAttackKey : IEquatable<TraitAttackKey>
{
    public readonly AttackType Type;
    public readonly TraitAttackForm Form;

    public TraitAttackKey(AttackType type, TraitAttackForm form = TraitAttackForm.Normal)
    {
        Type = type;
        Form = form;
    }

    /// <summary>스킬 슬롯 → 공격 종류(<see cref="PlayerSkillBase"/> 의 DamageAttackType 과 같은 매핑).</summary>
    public static AttackType AttackTypeOf(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => AttackType.SkillQ,
        PlayerSkillSlot.Sub => AttackType.SkillE,
        PlayerSkillSlot.Ultimate => AttackType.SkillR,
        _ => AttackType.Skill,
    };

    public static TraitAttackKey ForSkill(PlayerSkillSlot slot, bool alternate) =>
        new TraitAttackKey(AttackTypeOf(slot), alternate ? TraitAttackForm.Alternate : TraitAttackForm.Normal);

    public bool Equals(TraitAttackKey other) => Type == other.Type && Form == other.Form;
    public override bool Equals(object obj) => obj is TraitAttackKey other && Equals(other);
    public override int GetHashCode() => ((int)Type << 8) | (int)Form;
    public override string ToString() => $"{Type}/{Form}";
}

/// <summary>
/// 특성 1장이 기여하는 수치 1건. 대상 거름은 비트 마스크이고 0 = 전부(공격 종류 = 1 &lt;&lt; AttackType, 형태 = 1 &lt;&lt; TraitAttackForm, 슬롯 = 1 &lt;&lt; PlayerSkillSlot).
/// 데이터는 ratio 로 보관한다(명세 §9) — 배율 종류는 배율(1.2), % 종류는 %(0.2).
/// </summary>
public readonly struct TraitModifier
{
    public readonly TraitModifierKind Kind;
    public readonly float Value;
    public readonly int AttackTypeMask;
    public readonly int FormMask;
    public readonly int SlotMask;
    public readonly PlayerStatKind Stat;

    private TraitModifier(TraitModifierKind kind, float value, int attackTypeMask, int formMask, int slotMask, PlayerStatKind stat)
    {
        Kind = kind;
        Value = value;
        AttackTypeMask = attackTypeMask;
        FormMask = formMask;
        SlotMask = slotMask;
        Stat = stat;
    }

    public static int MaskOf(AttackType type) => 1 << (int)type;
    public static int MaskOf(TraitAttackForm form) => 1 << (int)form;
    public static int MaskOf(PlayerSkillSlot slot) => 1 << (int)slot;

    /// <summary>피해 배율. 마스크 0 = 모든 공격 종류·형태.</summary>
    public static TraitModifier Damage(float multiplier, int attackTypeMask = 0, int formMask = 0) =>
        new TraitModifier(TraitModifierKind.DamageMultiplier, multiplier, attackTypeMask, formMask, 0, default);

    /// <summary>쿨 배율. 형태 마스크로 일반/대체(변신) 스킬을 가른다. 마스크 0 = 전부.</summary>
    public static TraitModifier Cooldown(float multiplier, int slotMask = 0, int formMask = 0) =>
        new TraitModifier(TraitModifierKind.CooldownMultiplier, multiplier, 0, formMask, slotMask, default);

    public static TraitModifier Heal(float multiplier) =>
        new TraitModifier(TraitModifierKind.HealMultiplier, multiplier, 0, 0, 0, default);

    public static TraitModifier StatBase(PlayerStatKind stat, float percent) =>
        new TraitModifier(TraitModifierKind.StatBasePercent, percent, 0, 0, 0, stat);

    public static TraitModifier StatTotal(PlayerStatKind stat, float percent) =>
        new TraitModifier(TraitModifierKind.StatTotalPercent, percent, 0, 0, 0, stat);

    public static TraitModifier DealtDamage(float percent) =>
        new TraitModifier(TraitModifierKind.DealtDamagePercent, percent, 0, 0, 0, default);

    public bool Matches(TraitAttackKey key) => Covers(AttackTypeMask, (int)key.Type) && Covers(FormMask, (int)key.Form);

    public bool Matches(PlayerSkillSlot slot, TraitAttackForm form) => Covers(SlotMask, (int)slot) && Covers(FormMask, (int)form);

    private static bool Covers(int mask, int bit) => mask == 0 || (mask & (1 << bit)) != 0;

    public override string ToString() => $"{Kind} {Value} (공격 {AttackTypeMask:X}·형태 {FormMask:X}·슬롯 {SlotMask:X}·스탯 {Stat})";
}

/// <summary>
/// 특성이 수치를 기여하는 창구. 특성 SO(지금은 <see cref="TraitDefinition"/>, 3/5 부터 효과 클래스 조합)가 구현한다.
/// 같은 입력이면 모든 피어에서 같은 목록을 내야 한다(SO 수치만 읽는다).
/// </summary>
public interface ITraitModifierContributor
{
    void GetModifiers(List<TraitModifier> result);
}

/// <summary>
/// 한 플레이어가 고른 특성들의 수치 배율 합성(할 일 125 2/5) — 순수 로직. 서버·클라가 같은 획득 해시 목록으로 각자 계산해 같은 값을 낸다.
/// 상태이상을 쓰지 않으므로 보스방 입장 디버프 정리·연출 잠금과 무관하고, 1 미만 배율(페널티)도 그대로 남는다.
///   피해·쿨·회복 배율 = 맞는 기여의 곱(서로 다른 특성 곱 — 명세 §5·§6), 없으면 1.
///   스탯 기본%·전체%·가하는 피해% = 같은 단계 합(player-stats.md §2), 없으면 0.
/// 같은 특성 해시가 두 번 와도 한 번만 센다(같은 ID 중복 적용 금지 — 명세 §6). 잘못된 값(NaN·무한·음수 배율·0 이하 쿨 배율)은 버리고 오류로 남긴다.
/// </summary>
public sealed class TraitModifierSet
{
    private readonly List<int> traitHashes = new List<int>();
    private readonly List<TraitModifier> modifiers = new List<TraitModifier>();
    private readonly List<string> errors = new List<string>();
    private readonly List<TraitModifier> scratch = new List<TraitModifier>();
    private bool built;

    /// <summary>지금 반영된 특성 해시(중복 제거, 획득 순서).</summary>
    public IReadOnlyList<int> TraitHashes => traitHashes;
    public IReadOnlyList<TraitModifier> Modifiers => modifiers;
    /// <summary>마지막 <see cref="Rebuild"/> 의 검증 오류(정의 없음·잘못된 값).</summary>
    public IReadOnlyList<string> Errors => errors;

    /// <summary>
    /// 획득 해시 목록으로 다시 합성한다. 목록(중복 제거 후 순서 포함)이 이전과 같으면 아무것도 안 하고 false.
    /// resolve = 해시 → 기여자(정의 없음 = null, 오류로 남기고 건너뜀).
    /// </summary>
    public bool Rebuild(IReadOnlyList<int> acquiredHashes, Func<int, ITraitModifierContributor> resolve)
    {
        // 첫 합성 전에는 빈 목록이라도 "같다"로 보지 않는다 — 처음 한 번은 반드시 합성한다.
        if (built && SameHashes(acquiredHashes))
            return false;

        built = true;
        traitHashes.Clear();
        modifiers.Clear();
        errors.Clear();
        if (acquiredHashes == null)
            return true;

        for (int i = 0; i < acquiredHashes.Count; i++)
        {
            int hash = acquiredHashes[i];
            if (traitHashes.Contains(hash))
                continue;

            traitHashes.Add(hash);
            ITraitModifierContributor contributor = resolve != null ? resolve(hash) : null;
            if (contributor == null)
            {
                errors.Add($"특성 {hash} 정의가 없다 — 수치 기여 없음.");
                continue;
            }

            scratch.Clear();
            contributor.GetModifiers(scratch);
            for (int m = 0; m < scratch.Count; m++)
            {
                if (TraitModifierRules.IsValid(scratch[m], out string reason))
                    modifiers.Add(scratch[m]);
                else
                    errors.Add($"특성 {hash} 기여 무시 — {reason}: {scratch[m]}");
            }
        }

        scratch.Clear();
        return true;
    }

    public float DamageMultiplier(TraitAttackKey key)
    {
        float product = 1f;
        for (int i = 0; i < modifiers.Count; i++)
        {
            TraitModifier m = modifiers[i];
            if (m.Kind == TraitModifierKind.DamageMultiplier && m.Matches(key))
                product *= m.Value;
        }

        return product;
    }

    public float CooldownMultiplier(PlayerSkillSlot slot, TraitAttackForm form)
    {
        float product = 1f;
        for (int i = 0; i < modifiers.Count; i++)
        {
            TraitModifier m = modifiers[i];
            if (m.Kind == TraitModifierKind.CooldownMultiplier && m.Matches(slot, form))
                product *= m.Value;
        }

        return product;
    }

    public float HealMultiplier => Product(TraitModifierKind.HealMultiplier);

    public float BasePercent(PlayerStatKind stat) => StatSum(TraitModifierKind.StatBasePercent, stat);

    public float TotalPercent(PlayerStatKind stat) => StatSum(TraitModifierKind.StatTotalPercent, stat);

    public float DealtDamagePercent
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Kind == TraitModifierKind.DealtDamagePercent)
                    sum += modifiers[i].Value;
            }

            return sum;
        }
    }

    private float Product(TraitModifierKind kind)
    {
        float product = 1f;
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].Kind == kind)
                product *= modifiers[i].Value;
        }

        return product;
    }

    private float StatSum(TraitModifierKind kind, PlayerStatKind stat)
    {
        float sum = 0f;
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].Kind == kind && modifiers[i].Stat == stat)
                sum += modifiers[i].Value;
        }

        return sum;
    }

    private bool SameHashes(IReadOnlyList<int> acquired)
    {
        int count = 0;
        if (acquired != null)
        {
            for (int i = 0; i < acquired.Count; i++)
            {
                if (IndexOfBefore(acquired, i) >= 0)
                    continue; // 중복 — Rebuild 도 건너뛴다

                if (count >= traitHashes.Count || traitHashes[count] != acquired[i])
                    return false;
                count++;
            }
        }

        return count == traitHashes.Count;
    }

    private static int IndexOfBefore(IReadOnlyList<int> list, int index)
    {
        for (int i = 0; i < index; i++)
        {
            if (list[i] == list[index])
                return i;
        }

        return -1;
    }
}

/// <summary>특성 수치 공용 규칙(할 일 125 2/5) — 값 검증·쿨 진행률 보존.</summary>
public static class TraitModifierRules
{
    /// <summary>
    /// 기여 1건 검증(명세 §9) — 무한/NaN 금지. 피해·회복 배율 ≥ 0, 쿨 배율 &gt; 0(감소율 1 미만). % 종류는 유한값이면 된다(페널티 % 허용).
    /// </summary>
    public static bool IsValid(TraitModifier modifier, out string reason)
    {
        float v = modifier.Value;
        if (float.IsNaN(v) || float.IsInfinity(v))
        {
            reason = "NaN·무한";
            return false;
        }

        switch (modifier.Kind)
        {
            case TraitModifierKind.DamageMultiplier:
            case TraitModifierKind.HealMultiplier:
                if (v < 0f)
                {
                    reason = "배율 < 0";
                    return false;
                }
                break;
            case TraitModifierKind.CooldownMultiplier:
                if (v <= 0f)
                {
                    reason = "쿨 배율 ≤ 0(감소율 1 이상)";
                    return false;
                }
                break;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// 전체 쿨이 바뀔 때 진행률 보존(명세 §6): 새 남은 = 기존 남은 / 기존 전체 × 새 전체.
    /// 기존 전체가 0 이하이거나 이미 준비(남은 0)면 그대로 둔다(나누지 않는다).
    /// </summary>
    public static float RescaleRemaining(float remaining, float oldFull, float newFull)
    {
        if (remaining <= 0f || oldFull <= 0f)
            return Math.Max(0f, remaining);

        return Math.Max(0f, remaining / oldFull * Math.Max(0f, newFull));
    }
}
