using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 특성 카드 툴팁 값 소스(할 일 125 5/5, 명세 §8 TraitTooltipValueSource) — 설명 템플릿의 <c>{이름}</c> 첫 칸을 이 순서로 푼다.
/// <list type="number">
/// <item>등록부 별칭(<see cref="TraitContentRegistry.Registrar.Value"/>) — 다른 SO 값·표시용 파생 값</item>
/// <item>정의 자신의 직렬화 필드(<c>{effects[0].damageIncrease}</c> 처럼 경로로도 쓸 수 있다)</item>
/// <item>효과 목록 안 필드 이름(<c>{damageIncrease}</c>) 또는 이름 붙은 칸(⑩ 스킬 수치 칸 name — <see cref="TraitEffect.TryGetNamedValue"/>) — 그 이름을 가진 효과가 정확히 하나여야 한다(둘 이상 = 오류, 경로나 별칭을 쓸 것)</item>
/// </list>
/// 어디에도 없으면 오류(포매터가 다른 데서 찾지 않는다 — 조용히 0 으로 보이지 않게).
/// </summary>
public sealed class TraitTooltipValueSource : ISkillTooltipValueSource
{
    private readonly TraitDefinition trait;
    private readonly TraitContentRegistry registry;

    public TraitTooltipValueSource(TraitDefinition trait, TraitContentRegistry registry)
    {
        this.trait = trait;
        this.registry = registry;
    }

    public bool TryGetTooltipValue(string name, out object value, out string error)
    {
        value = null;
        error = null;
        if (trait == null)
        {
            error = "특성 정의가 없다.";
            return false;
        }

        if (registry != null && registry.TryGetValue(trait.Id, name, out Func<TraitDefinition, object> getter))
        {
            value = getter(trait);
            return true;
        }

        FieldInfo own = FindField(trait.GetType(), name, typeof(UnityEngine.Object));
        if (own != null)
        {
            value = own.GetValue(trait);
            return true;
        }

        int found = -1;
        IReadOnlyList<TraitEffect> effects = trait.Effects;
        for (int i = 0; effects != null && i < effects.Count; i++)
        {
            if (effects[i] == null)
                continue;

            FieldInfo field = FindField(effects[i].GetType(), name, typeof(object));
            object named = null;
            if (field == null && !effects[i].TryGetNamedValue(name, out named))
                continue;

            if (found >= 0)
            {
                error = $"효과 칸 {found}·{i} 에 다 있다 — {{effects[{found}].{name}}} 처럼 경로로 쓰거나 별칭을 등록할 것.";
                return false;
            }

            found = i;
            value = field != null ? field.GetValue(effects[i]) : named;
        }

        if (found >= 0)
            return true;

        error = $"'{trait.Id}' 에 값 '{name}' 이 없다(별칭·정의 필드·효과 필드 어디에도).";
        return false;
    }

    private static FieldInfo FindField(Type type, string name, Type stopAt)
    {
        for (Type current = type; current != null && current != stopAt && current != typeof(object); current = current.BaseType)
        {
            FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }
}

/// <summary>
/// 특성 카드 설명 = 템플릿(<see cref="TraitDefinition.Description"/>) → <see cref="SkillTooltipFormatter"/>(할 일 125 5/5).
/// <c>{필드}</c>·<c>{필드:%}</c> 는 <see cref="TraitTooltipValueSource"/>, <c>{dmg}</c> 는 등록부의 특성별 계산식(캐릭터 레인).
/// 검증(<see cref="Validate"/>)은 Play 없이 돈다 — 풀리지 않는 토큰·계산식 없는 <c>{dmg}</c> = 오류(EditMode <c>TraitTooltipTests</c> 가 프로젝트 정의 전부를 본다).
/// </summary>
public static class TraitTooltip
{
    /// <summary>화면 설명. 실패하면 false + 오류(화면은 <see cref="UiStrings.SkillTooltipError"/>).</summary>
    public static bool FormatDescription(TraitDefinition trait, Player player, bool showCalculation, out string text, out string error) =>
        Format(trait, trait != null ? trait.Description : null, player, showCalculation, TraitContent.Registry, out text, out error);

    /// <summary>템플릿 하나를 푼다(등록부를 바꿔 끼울 수 있는 순수 판 — 테스트용).</summary>
    public static bool Format(TraitDefinition trait, string template, Player player, bool showCalculation,
        TraitContentRegistry registry, out string text, out string error)
    {
        text = string.Empty;
        if (trait == null)
        {
            error = "특성 정의가 없다.";
            return false;
        }

        TraitDamageCalculator calculator = null;
        bool hasCalculator = registry != null && registry.TryGetDamage(trait.Id, out calculator);
        SkillTooltipDamage damage = hasCalculator && player != null ? calculator(trait, player) : default;

        if (!SkillTooltipFormatter.TryFormat(template, new TraitTooltipValueSource(trait, registry), damage, showCalculation,
                out text, out bool hasDamage, out error))
            return false;

        if (hasDamage && !hasCalculator)
        {
            error = MissingDamage(trait);
            text = string.Empty;
            return false;
        }

        // 계산할 플레이어가 없으면(로비 미리보기 등) 0 이 아니라 '알 수 없음' 표시.
        if (hasDamage && !damage.HasValue)
            text = text.Replace("{dmg}", TraitUiStrings.DamageUnknown.Text);

        return true;
    }

    /// <summary>템플릿 검증 — 문법·풀리지 않는 토큰·계산식 없는 {dmg}.</summary>
    public static bool Validate(TraitDefinition trait, string template, TraitContentRegistry registry, out string error)
    {
        if (trait == null)
        {
            error = "특성 정의가 없다.";
            return false;
        }

        if (!SkillTooltipFormatter.TryFormat(template, new TraitTooltipValueSource(trait, registry), default, false,
                out _, out bool hasDamage, out error))
            return false;

        if (hasDamage && (registry == null || !registry.TryGetDamage(trait.Id, out _)))
        {
            error = MissingDamage(trait);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 정의 하나의 카드 문구 전체 검증 — ID 키 규칙·이름 비었음·인라인 설명·현재 언어 설명(Strings 행이 있으면 그 문구).
    /// 오류 문장을 <paramref name="errors"/> 에 덧붙인다.
    /// </summary>
    public static void ValidateDefinition(TraitDefinition trait, TraitContentRegistry registry, List<string> errors)
    {
        if (trait == null)
            return;

        string id = trait.Id;
        if (!TraitStringKeys.IsValidId(id))
        {
            errors.Add($"'{trait.name}': ID '{id}' 는 Strings 키에 쓸 수 없다(빈 칸·공백·'.'·'/'·중괄호).");
            return;
        }

        if (string.IsNullOrWhiteSpace(trait.DisplayName))
            errors.Add($"'{id}' 이름이 비었다(인라인 displayName 또는 {TraitStringKeys.Name(id)}).");

        if (!Validate(trait, trait.InlineDescription, registry, out string inlineError))
            errors.Add($"'{id}' 설명(인라인): {inlineError}");

        string current = trait.Description;
        if (!string.Equals(current, trait.InlineDescription, StringComparison.Ordinal) && !Validate(trait, current, registry, out string sheetError))
            errors.Add($"'{id}' 설명({TraitStringKeys.Description(id)}): {sheetError}");
    }

    private static string MissingDamage(TraitDefinition trait) =>
        $"'{trait.Id}' 설명에 {{dmg}} 가 있는데 피해 계산식이 등록되지 않았다(캐릭터 파일 TraitContent.<캐릭터>.cs 에서 r.Damage).";
}

/// <summary>
/// 2차 상충 확인창 규칙(할 일 125 5/5, 명세 §2) — 카드 클릭 1번이 무엇이 되는지만 정하는 순수 상태. UI(<see cref="TraitSelectionView"/>)가 쓴다.
/// <list type="bullet">
/// <item>고른 특성이 보유 특성의 효과를 없애면(<see cref="TraitContentRegistry.GetDisabledBy"/>) 확인창 → [확인] 이면 그대로 선택 요청, [취소] 면 아무것도 보내지 않는다.</item>
/// <item>상충 없으면 바로 선택 요청.</item>
/// <item>60초 출구 자동 선택은 서버가 첫 후보를 고른다 — 확인창 없음. 창이 닫히거나(자동 선택·확정) 제시가 바뀌면 열린 확인창은 보내지 않고 닫는다(<see cref="Reset"/>).</item>
/// </list>
/// </summary>
public sealed class TraitPickConfirmModel
{
    private readonly List<string> disabled = new List<string>();

    /// <summary>확인을 기다리는 특성 해시. 없으면 <see cref="TraitIdHash.None"/>.</summary>
    public int PendingHash { get; private set; } = TraitIdHash.None;

    public bool IsAsking => PendingHash != TraitIdHash.None;

    /// <summary>확인창에 이름을 띄울, 효과를 잃는 보유 특성 ID(보유 순서).</summary>
    public IReadOnlyList<string> DisabledIds => disabled;

    /// <summary>카드 클릭. 반환 = 지금 서버에 보낼 해시(확인창을 열었거나 무시하면 <see cref="TraitIdHash.None"/>).</summary>
    public int Click(int traitHash, string traitId, IReadOnlyList<string> ownedIds, TraitContentRegistry registry)
    {
        if (traitHash == TraitIdHash.None || IsAsking)
            return TraitIdHash.None; // 확인창이 떠 있는 동안 뒤 카드는 못 누른다(모달) — 와도 무시

        if (registry != null)
            registry.GetDisabledBy(traitId, ownedIds, disabled);
        else
            disabled.Clear();

        if (disabled.Count == 0)
            return traitHash;

        PendingHash = traitHash;
        return TraitIdHash.None;
    }

    /// <summary>[확인] — 보낼 해시(한 번만). 확인창이 없으면 None.</summary>
    public int Confirm()
    {
        int hash = PendingHash;
        Reset();
        return hash;
    }

    /// <summary>[취소] — 아무것도 보내지 않는다. 다시 고를 수 있다.</summary>
    public void Cancel() => Reset();

    /// <summary>창 닫힘·새 제시 — 열린 확인창을 버린다(보내지 않음).</summary>
    public void Reset()
    {
        PendingHash = TraitIdHash.None;
        disabled.Clear();
    }

    /// <summary>확인창 문구 — Strings <c>Trait.&lt;ID&gt;.Conflict</c> → 등록 ko → 기본 문구. <c>{traits}</c> = 이름들.</summary>
    public static string FormatMessage(string chosenId, IReadOnlyList<string> disabledNames, TraitContentRegistry registry)
    {
        string names = disabledNames == null ? string.Empty : string.Join(TraitUiStrings.NameSeparator.Text, disabledNames);
        var args = new Dictionary<string, string> { [TraitStringKeys.TraitsToken] = names };
        string key = TraitStringKeys.Conflict(chosenId);
        if (registry != null && registry.TryGetConflictText(chosenId, out string ko))
            return Loc.Format(key, ko, args);

        return key != null && Loc.Get(key, null) != null
            ? Loc.Format(key, null, args)
            : TraitUiStrings.ConflictDefault.Format(args);
    }
}
