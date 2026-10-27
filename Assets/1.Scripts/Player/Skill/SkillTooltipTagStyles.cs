using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>이 문자열 필드가 태그 ID 하나다 — 데이터 테이블 적용(<c>DataTableApplier.Bind</c>)이 <see cref="SkillTooltipTags.TryParseOne"/> 로 검증한다.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SkillTooltipTagIdAttribute : Attribute
{
}

/// <summary>태그 칩 색 한 줄 — 태그 ID → 배경색·글자색(<c>#RRGGBB</c>·<c>#RRGGBBAA</c>).</summary>
[Serializable]
public sealed class SkillTooltipTagStyle
{
    [SerializeField, DataTableText(Localize = false), SkillTooltipTagId]
    [Tooltip("태그 ID(SkillTooltipTag 이름 — 예 Knockback). 목록에 없는 ID 는 오류")]
    private string tagId = string.Empty;

    [SerializeField, DataTableText(Localize = false), HexColorText]
    [Tooltip("칩 배경색 #RRGGBB 또는 #RRGGBBAA")]
    private string background = SkillTooltipTagStyles.DefaultBackgroundHex;

    [SerializeField, DataTableText(Localize = false), HexColorText]
    [Tooltip("칩 글자색 #RRGGBB 또는 #RRGGBBAA")]
    private string text = SkillTooltipTagStyles.DefaultTextHex;

    public SkillTooltipTagStyle()
    {
    }

    public SkillTooltipTagStyle(string tagId, string background, string text)
    {
        this.tagId = tagId;
        this.background = background;
        this.text = text;
    }

    public string TagId => tagId ?? string.Empty;
    public string Background => background ?? string.Empty;
    public string Text => text ?? string.Empty;
}

/// <summary>
/// 스킬 툴팁 태그 칩 색 표(할 일 114 후속). 원본 = <c>GameData.xlsx › UI</c> 시트(<c>SkillTooltipTagStyles</c> 열),
/// 에셋 = <c>Assets/2.Prefabs/UI/SkillTooltipGenerated/SkillTooltipTagStyles.asset</c>(<c>Tools/UI/스킬 툴팁 레이아웃만 갱신</c> 이 만들고 툴팁에 잇는다).
/// 표에 없는 태그는 기본색으로 그리고 검증 경고, 잘못된 색 문자열·없는 ID·중복은 테이블 Play·빌드 오류.
/// </summary>
[CreateAssetMenu(fileName = "SkillTooltipTagStyles", menuName = "UI/Skill Tooltip Tag Styles")]
[DataTableSheet("UI", Order = 0)]
public sealed class SkillTooltipTagStyles : ScriptableObject
{
    public const string DefaultBackgroundHex = "#2A2E36E6";
    public const string DefaultTextHex = "#C9CED6";

    /// <summary>색 문자열마저 틀렸을 때(인스펙터 모드) 쓰는 마지막 기본값.</summary>
    public static readonly Color32 FallbackBackground = new Color32(0x2A, 0x2E, 0x36, 0xE6);
    public static readonly Color32 FallbackText = new Color32(0xC9, 0xCE, 0xD6, 0xFF);

    [Header("기본색 — 표에 없는 태그")]
    [SerializeField, DataTableText(Localize = false), HexColorText]
    [Tooltip("표에 없는 태그의 칩 배경색 #RRGGBB(AA)")]
    private string defaultBackground = DefaultBackgroundHex;

    [SerializeField, DataTableText(Localize = false), HexColorText]
    [Tooltip("표에 없는 태그의 칩 글자색 #RRGGBB(AA)")]
    private string defaultText = DefaultTextHex;

    [Header("태그별 색")]
    [Tooltip("태그 ID → 칩 배경·글자색. 줄 수는 xlsx 에서 못 바꾼다 — 새 태그 줄은 프로그래머가 늘린다")]
    [SerializeField] private SkillTooltipTagStyle[] entries = Array.Empty<SkillTooltipTagStyle>();

    [NonSerialized] private Dictionary<SkillTooltipTag, (Color32 background, Color32 text)> cache;
    [NonSerialized] private SkillTooltipTagStyle[] cachedEntries;

    public IReadOnlyList<SkillTooltipTagStyle> Entries => entries;
    public string DefaultBackground => defaultBackground;
    public string DefaultText => defaultText;

    /// <summary>
    /// <paramref name="tag"/> 의 칩 색. 표에 없으면 기본색이고 false. 잘못된 색 문자열은 기본색(그것도 틀리면 <see cref="FallbackBackground"/>)으로 대신한다.
    /// </summary>
    public bool TryGetColors(SkillTooltipTag tag, out Color32 background, out Color32 text)
    {
        // 테이블 Play 적용·복구(FromJsonOverwrite)는 배열을 새로 만든다 — OnValidate 가 안 불려도 참조가 바뀌면 다시 읽는다.
        if (cache == null || !ReferenceEquals(cachedEntries, entries))
        {
            cache = Build(entries, defaultBackground, defaultText);
            cachedEntries = entries;
        }
        if (cache.TryGetValue(tag, out var colors))
        {
            background = colors.background;
            text = colors.text;
            return true;
        }

        ResolveDefaults(defaultBackground, defaultText, out background, out text);
        return false;
    }

    /// <summary>에디터·데이터 테이블이 값을 고친 뒤 다시 읽게 한다.</summary>
    public void InvalidateCache() => cache = null;

    private void OnEnable() => cache = null;
    private void OnValidate() => cache = null;

    /// <summary>저작 메뉴·테스트용 — 기본 색 표로 채운다.</summary>
    public void SetEntries(string background, string text, SkillTooltipTagStyle[] styles)
    {
        defaultBackground = background;
        defaultText = text;
        entries = styles ?? Array.Empty<SkillTooltipTagStyle>();
        cache = null;
    }

    /// <summary>
    /// 기본 색 초안(2026-10-27 2차, 참고 이미지 '넉백') — 배경 = 같은 계열 어두운 반투명(알파 E6), 글자·둥근 테두리 = 같은 계열 밝은 색.
    /// 테두리는 따로 칸이 없고 글자색을 쓴다. 대비는 툴팁 패널(<see cref="TooltipPanelHex"/>) 위에 합성한 배경 기준.
    /// </summary>
    public static SkillTooltipTagStyle[] DefaultStyles() => new[]
    {
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Knockback), "#3D2A12E6", "#F2B84B"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Stun), "#38330EE6", "#EEDC5A"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Vulnerable), "#3D1530E6", "#FF8FCF"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Shield), "#13294AE6", "#7FB8FF"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Heal), "#14351CE6", "#7EE08F"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Overheat), "#45160FE6", "#FF7B63"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Unstoppable), "#2A1A45E6", "#C29BFF"),
        new SkillTooltipTagStyle(nameof(SkillTooltipTag.Invulnerable), "#2E3238E6", "#E8ECF2"),
    };

    /// <summary>칩이 놓이는 툴팁 패널 색(CombatHUD › SkillTooltipHost/Panel, 알파 0.97 은 무시) — 반투명 배경 대비 계산용.</summary>
    public const string TooltipPanelHex = "#090B11";

    /// <summary>
    /// 표 전체 검사(순수 로직). 오류 = 빈·없는 태그 ID, 같은 ID 두 줄, 잘못된 색 문자열. 경고 = 표에 줄이 없는 태그(기본색으로 그린다).
    /// </summary>
    public static void Validate(
        IReadOnlyList<SkillTooltipTagStyle> styles, string background, string text, List<string> errors, List<string> warnings)
    {
        if (!HexColor.TryValidate(background, out string error))
            errors.Add($"defaultBackground — {error}");
        if (!HexColor.TryValidate(text, out error))
            errors.Add($"defaultText — {error}");

        var ids = new List<string>();
        for (int i = 0; styles != null && i < styles.Count; i++)
        {
            SkillTooltipTagStyle style = styles[i];
            ids.Add(style?.TagId);
            if (style == null)
                continue;
            if (!HexColor.TryValidate(style.Background, out error))
                errors.Add($"entries[{i}].background — {error}");
            if (!HexColor.TryValidate(style.Text, out error))
                errors.Add($"entries[{i}].text — {error}");
        }

        ValidateIds(ids, errors, warnings);
    }

    /// <summary>
    /// 태그 ID 줄만 검사 — 데이터 테이블 적용기가 테이블 값을 덮은 ID 목록으로 부른다
    /// (<paramref name="reportInvalidIds"/> = false: 빈·없는 ID 는 칸 검증이 이미 냈으니 중복·줄 없는 태그만).
    /// </summary>
    public static void ValidateIds(IReadOnlyList<string> ids, List<string> errors, List<string> warnings, bool reportInvalidIds = true)
    {
        var seen = new Dictionary<SkillTooltipTag, int>();
        for (int i = 0; i < ids.Count; i++)
        {
            if (!SkillTooltipTags.TryParseOne(ids[i], out SkillTooltipTag tag, out string error))
            {
                if (reportInvalidIds)
                    errors.Add($"entries[{i}].tagId — {error}");
                continue;
            }

            if (seen.TryGetValue(tag, out int first))
            {
                errors.Add($"entries[{i}].tagId — 태그 '{tag}' 가 entries[{first}] 에도 있다.");
                continue;
            }

            seen[tag] = i;
        }

        foreach (SkillTooltipTag tag in (SkillTooltipTag[])Enum.GetValues(typeof(SkillTooltipTag)))
        {
            if (!seen.ContainsKey(tag))
                warnings.Add($"태그 '{tag}' 의 색 줄이 없다 — 기본색으로 그린다(SkillTooltipTagStyles entries 에 줄을 늘릴 것).");
        }
    }

    /// <summary>ID → 색 사전(순수 로직). 틀린 ID·중복 뒤 줄은 건너뛰고, 틀린 색은 기본색으로 대신한다.</summary>
    public static Dictionary<SkillTooltipTag, (Color32 background, Color32 text)> Build(
        IReadOnlyList<SkillTooltipTagStyle> styles, string background, string text)
    {
        ResolveDefaults(background, text, out Color32 defaultBg, out Color32 defaultFg);
        var map = new Dictionary<SkillTooltipTag, (Color32, Color32)>();
        for (int i = 0; styles != null && i < styles.Count; i++)
        {
            SkillTooltipTagStyle style = styles[i];
            if (style == null || !SkillTooltipTags.TryParseOne(style.TagId, out SkillTooltipTag tag, out _) || map.ContainsKey(tag))
                continue;

            Color32 bg = HexColor.TryParse(style.Background, out Color32 parsedBg, out _) ? parsedBg : defaultBg;
            Color32 fg = HexColor.TryParse(style.Text, out Color32 parsedFg, out _) ? parsedFg : defaultFg;
            map[tag] = (bg, fg);
        }

        return map;
    }

    private static void ResolveDefaults(string background, string text, out Color32 bg, out Color32 fg)
    {
        bg = HexColor.TryParse(background, out Color32 parsedBg, out _) ? parsedBg : FallbackBackground;
        fg = HexColor.TryParse(text, out Color32 parsedFg, out _) ? parsedFg : FallbackText;
    }
}
