using System;
using System.Collections.Generic;

/// <summary>
/// 특성 문구 Strings 키 규칙(할 일 125 5/5) — 특성마다 <c>Trait.&lt;ID&gt;.&lt;칸&gt;</c>. ID = 영구 문자열 ID(<c>PAL_1_Q_DAMAGE</c>).
/// 에셋 이름이 아니라 ID 기준이라 정의 에셋을 옮기거나 이름을 바꿔도 번역이 따라간다.
/// <list type="bullet">
/// <item><c>.Name</c> 이름 · <c>.Category</c> 짧은 분류(명세 '분류:') · <c>.Desc</c> 설명 템플릿(<c>{필드}</c>·<c>{필드:%}</c>·<c>{dmg}</c>)</item>
/// <item><c>.Conflict</c> 2차 상충 확인창 문구(<c>{traits}</c> = 효과를 잃는 보유 특성 이름) — 상충 규칙이 있는 특성만(<see cref="TraitContentRegistry.Conflict"/>)</item>
/// </list>
/// 시트에 행이 없으면 정의 인라인 ko(상충 문구는 등록한 ko)가 화면 폴백이다 — <c>#키</c> 를 내지 않는다.
/// Export 는 <c>TraitStringKeyCollector</c>(에디터)가 이 키들을 Strings 시트 새 행으로 붙인다(ko = 인라인, en 빈 칸).
/// </summary>
public static class TraitStringKeys
{
    public const string Prefix = "Trait.";
    public const string NameSuffix = ".Name";
    public const string CategorySuffix = ".Category";
    public const string DescriptionSuffix = ".Desc";
    public const string ConflictSuffix = ".Conflict";

    /// <summary>상충 확인창 문구에서 효과를 잃는 보유 특성 이름(여럿이면 쉼표로 잇는다)이 들어가는 자리.</summary>
    public const string TraitsToken = "traits";

    public static string Name(string traitId) => Key(traitId, NameSuffix);
    public static string Category(string traitId) => Key(traitId, CategorySuffix);
    public static string Description(string traitId) => Key(traitId, DescriptionSuffix);
    public static string Conflict(string traitId) => Key(traitId, ConflictSuffix);

    /// <summary>키에 쓸 수 있는 ID 인가 — 비지 않고, 공백·점·중괄호가 없다(키 구분자·자리표시자와 겹치지 않게).</summary>
    public static bool IsValidId(string traitId)
    {
        if (string.IsNullOrEmpty(traitId))
            return false;

        foreach (char c in traitId)
        {
            if (char.IsWhiteSpace(c) || c == '.' || c == '{' || c == '}' || c == '/')
                return false;
        }

        return true;
    }

    /// <summary>키 → (ID, 칸 접미사). 특성 키가 아니면 false.</summary>
    public static bool TryParse(string key, out string traitId, out string suffix)
    {
        traitId = null;
        suffix = null;
        if (string.IsNullOrEmpty(key) || !key.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        foreach (string candidate in Suffixes)
        {
            if (key.Length > Prefix.Length + candidate.Length && key.EndsWith(candidate, StringComparison.Ordinal))
            {
                string id = key.Substring(Prefix.Length, key.Length - Prefix.Length - candidate.Length);
                if (!IsValidId(id))
                    return false;

                traitId = id;
                suffix = candidate;
                return true;
            }
        }

        return false;
    }

    public static readonly string[] Suffixes = { NameSuffix, CategorySuffix, DescriptionSuffix, ConflictSuffix };

    /// <summary>시트 문구 → 없으면 <paramref name="fallback"/>(정의 인라인 ko). ID 가 키로 못 쓰는 값이면 폴백만.</summary>
    public static string Resolve(string key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback ?? string.Empty : Loc.Get(key, fallback ?? string.Empty);

    private static string Key(string traitId, string suffix) => IsValidId(traitId) ? Prefix + traitId + suffix : null;
}

/// <summary>
/// 특성 선택 화면 고정 문구 키(할 일 125 5/5 — 분류 줄·2차 상충 확인창·도움말 줄). <see cref="UiStrings.All"/> 이 모아 Export 가 Strings 행을 붙인다.
/// en 초안은 <c>_lane-artifacts/common/125/125-캐릭터레인-가이드.md</c>·<c>Docs/tech/tutorial-traits.md</c> — 시트에 넣는 건 병합 때 메인.
/// </summary>
public static class TraitUiStrings
{
    private const string Where = "TraitUI.prefab › ";

    /// <summary>카드 분류 줄({category} = 특성 분류 — Trait.&lt;ID&gt;.Category). 앞뒤 장식이 필요하면 문구만 고친다.</summary>
    public static readonly UiString CardCategory = new UiString("UI.Trait.Card.Category", "{category}", Where + "카드 분류 줄({category} = Trait.<ID>.Category)(코드)");

    /// <summary>선택 창 도움말 한 줄(명세 §2 — CC 공통 안내는 카드마다 반복하지 않는다).</summary>
    public static readonly UiString Help = new UiString("UI.Trait.Help", "보스는 취약할 때만 밀치기와 기절이 적용됩니다.", Where + "선택 창 도움말 줄(코드)");

    /// <summary>상충 확인창 기본 문구 — 그 특성의 Trait.&lt;ID&gt;.Conflict 가 없을 때({traits} = 효과를 잃는 보유 특성 이름).</summary>
    public static readonly UiString ConflictDefault = new UiString("UI.Trait.Conflict.Default", "‘{traits}’의 효과를 받을 수 없게 됩니다. 선택할까요?", Where + "2차 상충 확인창 기본 문구({traits} = 보유 특성 이름)(코드)");

    public static readonly UiString ConflictConfirm = new UiString("UI.Trait.Conflict.Confirm", "확인", Where + "2차 상충 확인창 — 확인 버튼(코드)");
    public static readonly UiString ConflictCancel = new UiString("UI.Trait.Conflict.Cancel", "취소", Where + "2차 상충 확인창 — 취소 버튼(코드)");

    /// <summary>카드 설명의 {dmg} 를 계산할 플레이어가 없을 때 그 자리(0 으로 보이지 않게).</summary>
    public static readonly UiString DamageUnknown = new UiString("UI.Trait.Card.DamageUnknown", "-", Where + "카드 설명 {dmg} — 계산할 플레이어가 없을 때(코드)");

    /// <summary>상충 확인창에서 보유 특성 이름 여럿을 잇는 구분자.</summary>
    public static readonly UiString NameSeparator = new UiString("UI.Trait.Conflict.NameSeparator", ", ", Where + "2차 상충 확인창 — 특성 이름 구분자(코드)");

    /// <summary>{percent} — 계수 퍼센트('150%' 또는 '100~150%').</summary>
    public const string PercentToken = "percent";

    /// <summary>{flat} — 고정 추가 피해('20' 또는 '10~20').</summary>
    public const string FlatToken = "flat";

    /// <summary>카드·획득 툴팁 설명 {dmg} 평소 표시(할 일 171) — Shift 누르는 동안만 계산 숫자.</summary>
    public static readonly UiString DamagePercent = new UiString("UI.Trait.Card.DamagePercent", "공격력 {percent}", Where + "카드 설명 {dmg} 평소 표시({percent} = 150% 또는 100~150%)(코드)");

    /// <summary><see cref="DamagePercent"/> + 고정 추가 피해가 0 이 아닐 때.</summary>
    public static readonly UiString DamagePercentFlat = new UiString("UI.Trait.Card.DamagePercentFlat", "{flat} + 공격력 {percent}", Where + "카드 설명 {dmg} 평소 표시 — 고정 추가 피해 있음({flat} = 고정값, {percent} = 계수)(코드)");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        CardCategory, Help, ConflictDefault, ConflictConfirm, ConflictCancel, DamageUnknown, NameSeparator,
        DamagePercent, DamagePercentFlat,
    };

    /// <summary>분류 줄 문구. 분류가 비면 빈 문자열(줄을 숨긴다).</summary>
    public static string FormatCategory(string category) =>
        string.IsNullOrEmpty(category)
            ? string.Empty
            : CardCategory.Format(new Dictionary<string, string> { ["category"] = category });
}
