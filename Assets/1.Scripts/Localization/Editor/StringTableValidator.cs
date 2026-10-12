using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>문자열 시트(Strings·ErrorCodes)의 값 행 하나(아직 검증 전). 위치는 오류 표시용 "GameData.xlsx › Strings!C3".</summary>
public readonly struct StringRow
{
    public StringRow(string key, string description, string ko, string en, string keyLocation, string koLocation, string enLocation)
    {
        Key = key;
        Description = description;
        Ko = ko;
        En = en;
        KeyLocation = keyLocation;
        KoLocation = koLocation;
        EnLocation = enLocation;
    }

    public string Key { get; }
    public string Description { get; }
    public string Ko { get; }
    public string En { get; }
    public string KeyLocation { get; }
    public string KoLocation { get; }
    public string EnLocation { get; }
}

/// <summary>검증을 통과한 문구 하나 — en 빈 칸은 이미 ko 로 채워져 있다.</summary>
public readonly struct LocalizedString
{
    public LocalizedString(string key, string ko, string en)
    {
        Key = key;
        Ko = ko;
        En = en;
    }

    public string Key { get; }
    public string Ko { get; }
    public string En { get; }
}

/// <summary>
/// 문자열 시트 검증(순수 함수, Unity 의존 없음) — Strings·ErrorCodes 둘 다 같은 규칙, 시트마다 따로 부른다(키 공간이 따로).
/// 오류가 하나라도 있으면 호출자는 표를 갱신하지 않는다.
/// <list type="bullet">
/// <item>키 중복 = 오류(첫 행만 남긴다)</item>
/// <item>ko 빈 칸 = 오류</item>
/// <item>en 빈 칸 = 경고 + ko 로 채움</item>
/// <item>ko·en 의 자리표시자(<c>{이름}</c>) 집합이 다르면 오류</item>
/// <item><c>&lt;style=…&gt;</c>·<c>&lt;/style&gt;</c> 짝이 안 맞으면 오류(언어마다)</item>
/// <item>중괄호 문법(짝 없는 <c>}</c>·닫히지 않은 <c>{</c>·빈 <c>{}</c>)이 틀리면 오류(언어마다)</item>
/// </list>
/// 문법·자리표시자 규칙은 <see cref="LocText"/> — 스킬 툴팁(<see cref="SkillTooltipFormatter"/>)과 같은 것이다.
/// </summary>
public static class StringTableValidator
{
    public static List<LocalizedString> Validate(IEnumerable<StringRow> rows, DataTableIssues issues)
    {
        var result = new List<LocalizedString>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (StringRow row in rows)
        {
            if (seen.TryGetValue(row.Key, out string firstLocation))
            {
                issues.Error(row.KeyLocation, $"키 '{row.Key}' 가 두 번 나온다(처음: {firstLocation}).");
                continue;
            }

            seen[row.Key] = row.KeyLocation;

            string ko = row.Ko ?? string.Empty;
            string en = row.En ?? string.Empty;
            bool ok = true;

            if (ko.Length == 0)
            {
                issues.Error(row.KoLocation, $"'{row.Key}' 의 ko 가 비어 있다 — ko 는 반드시 채운다.");
                ok = false;
            }

            ok &= CheckSyntax(ko, row.KoLocation, row.Key, issues);

            if (en.Length == 0)
            {
                if (ko.Length > 0)
                {
                    issues.Warning(row.EnLocation, $"'{row.Key}' 의 en 이 비어 있어 ko 문구를 쓴다.");
                }

                en = ko;
            }
            else
            {
                ok &= CheckSyntax(en, row.EnLocation, row.Key, issues);

                if (!LocText.SamePlaceholders(ko, en))
                {
                    issues.Error(row.EnLocation,
                        $"'{row.Key}' 의 자리표시자가 언어마다 다르다 — ko {Describe(LocText.Placeholders(ko))} / en {Describe(LocText.Placeholders(en))}.");
                    ok = false;
                }
            }

            if (ok)
            {
                result.Add(new LocalizedString(row.Key, ko, en));
            }
        }

        return result;
    }

    // 문법 규칙은 SkillTooltipFormatter 와 같은 LocText 하나 — style 짝·중괄호.
    private static bool CheckSyntax(string text, string location, string key, DataTableIssues issues)
    {
        string error = LocText.StyleTagError(text) ?? LocText.BraceError(text);
        if (error == null)
        {
            return true;
        }

        issues.Error(location, $"'{key}': {error}");
        return false;
    }

    private static string Describe(HashSet<string> names) =>
        names.Count == 0 ? "(없음)" : string.Join(" ", names.OrderBy(n => n, StringComparer.Ordinal).Select(n => "{" + n + "}"));
}
