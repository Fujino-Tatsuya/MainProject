using System;
using System.Collections.Generic;

/// <summary>
/// 스킬 툴팁 태그 칩 종류 — 태그 ID 목록은 이 enum 하나다(이름 = xlsx 에 적는 ID).
/// 새 태그 = 여기 한 줄 + <see cref="SkillTooltipTags"/> 의 ko 이름 한 줄.
/// </summary>
public enum SkillTooltipTag
{
    Knockback,
    Stun,
    Vulnerable,
    Shield,
    Heal,
    Overheat,
    Unstoppable,
    Invulnerable,
}

/// <summary>이 문자열 필드가 태그 ID 쉼표 목록이다 — 데이터 테이블 적용(<c>DataTableApplier.Bind</c>)이 <see cref="SkillTooltipTags.TryParse"/> 로 검증한다.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SkillTooltipTagListAttribute : Attribute
{
}

/// <summary>
/// 태그 칸(<c>tooltip.tags</c> — 예 <c>"Knockback, Stun"</c>) 파싱·검증과 화면 이름.
/// 쉼표 구분·앞뒤 공백 허용·대소문자 무관, 빈 값 = 태그 없음. 없는 ID·중복은 오류(테이블 Play·빌드 중단).
/// 화면 이름 키 = <c>UI.SkillTooltip.Tag.&lt;ID&gt;</c>, 시트에 행이 없으면 아래 ko.
/// </summary>
public static class SkillTooltipTags
{
    public const string KeyPrefix = "UI.SkillTooltip.Tag.";

    private static readonly Dictionary<SkillTooltipTag, string> KoNames = new Dictionary<SkillTooltipTag, string>
    {
        [SkillTooltipTag.Knockback] = "넉백",
        [SkillTooltipTag.Stun] = "기절",
        [SkillTooltipTag.Vulnerable] = "취약",
        [SkillTooltipTag.Shield] = "보호막",
        [SkillTooltipTag.Heal] = "회복",
        [SkillTooltipTag.Overheat] = "과열",
        [SkillTooltipTag.Unstoppable] = "저지불가",
        [SkillTooltipTag.Invulnerable] = "무적",
    };

    private static readonly Dictionary<SkillTooltipTag, UiString> NameStrings = BuildNames();

    /// <summary>Export 가 Strings 시트에 모으는 태그 이름 키 전부(<see cref="UiStrings.All"/> 에 들어간다).</summary>
    public static IEnumerable<UiString> Names => NameStrings.Values;

    public static UiString Name(SkillTooltipTag tag) => NameStrings[tag];

    /// <summary>
    /// <paramref name="ids"/> 를 태그 목록으로. 오류가 있어도 읽은 것은 <paramref name="tags"/> 에 담고(화면은 아는 태그만 그린다) 첫 오류를 돌려준다.
    /// </summary>
    public static bool TryParse(string ids, List<SkillTooltipTag> tags, out string error)
    {
        tags?.Clear();
        error = null;
        if (string.IsNullOrWhiteSpace(ids))
            return true;

        var seen = new HashSet<SkillTooltipTag>();
        foreach (string part in ids.Split(','))
        {
            string id = part.Trim();
            if (id.Length == 0)
            {
                error ??= $"태그 목록 '{ids}' 에 빈 칸이 있다(쉼표 사이를 비우지 말 것).";
                continue;
            }

            if (!TryParseId(id, out SkillTooltipTag tag))
            {
                error ??= $"없는 태그 ID '{id}' — 허용: {string.Join(", ", Enum.GetNames(typeof(SkillTooltipTag)))}";
                continue;
            }

            if (!seen.Add(tag))
            {
                error ??= $"태그 '{tag}' 가 두 번 있다.";
                continue;
            }

            tags?.Add(tag);
        }

        return error == null;
    }

    public static bool TryValidate(string ids, out string error) => TryParse(ids, null, out error);

    /// <summary>태그 ID 하나(앞뒤 공백 허용·대소문자 무관). 빈 값·없는 ID 는 오류.</summary>
    public static bool TryParseOne(string id, out SkillTooltipTag tag, out string error)
    {
        error = null;
        string trimmed = id?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            tag = default;
            error = "태그 ID 가 비어 있다.";
            return false;
        }

        if (TryParseId(trimmed, out tag))
            return true;

        error = $"없는 태그 ID '{trimmed}' — 허용: {string.Join(", ", Enum.GetNames(typeof(SkillTooltipTag)))}";
        return false;
    }

    // Enum.TryParse 는 "1" 같은 숫자도 받으므로 이름으로만 찾는다.
    private static bool TryParseId(string id, out SkillTooltipTag tag)
    {
        foreach (SkillTooltipTag candidate in (SkillTooltipTag[])Enum.GetValues(typeof(SkillTooltipTag)))
        {
            if (string.Equals(candidate.ToString(), id, StringComparison.OrdinalIgnoreCase))
            {
                tag = candidate;
                return true;
            }
        }

        tag = default;
        return false;
    }

    private static Dictionary<SkillTooltipTag, UiString> BuildNames()
    {
        var names = new Dictionary<SkillTooltipTag, UiString>();
        foreach (SkillTooltipTag tag in (SkillTooltipTag[])Enum.GetValues(typeof(SkillTooltipTag)))
        {
            KoNames.TryGetValue(tag, out string ko);
            names[tag] = new UiString(KeyPrefix + tag, ko ?? tag.ToString(), "CombatHUD › SkillTooltip — 태그 칩(코드)");
        }

        return names;
    }
}
