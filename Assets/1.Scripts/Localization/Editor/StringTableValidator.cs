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
/// <para>
/// <paramref name="ownerOf"/> 를 주면 자동 키 행(<c>주인ID/필드경로</c> — <c>[DataTableText]</c> 툴팁 문구)은 ko·en <b>각각</b>
/// <see cref="SkillTooltipFormatter.TryValidate"/> 로 그 키 주인(SO·컴포넌트)의 실제 필드를 가리키는지 본다 — 틀리면 오류.
/// 주인이나 그 문구 필드를 못 찾으면(이름이 바뀐 고아 행) 경고만 한다(화면은 인라인 값으로 돈다).
/// </para>
/// </summary>
public static class StringTableValidator
{
    /// <param name="ownerOf">자동 키 주인 ID → 주인(SO·컴포넌트), 없으면 null. null 을 주면 주인 필드 검사를 건너뛴다(ErrorCodes).</param>
    public static List<LocalizedString> Validate(IEnumerable<StringRow> rows, DataTableIssues issues, Func<string, object> ownerOf = null)
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

            bool koSyntax = CheckSyntax(ko, row.KoLocation, row.Key, issues);
            ok &= koSyntax;
            bool enSyntax = false;

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
                enSyntax = CheckSyntax(en, row.EnLocation, row.Key, issues);
                ok &= enSyntax;

                if (!LocText.SamePlaceholders(ko, en))
                {
                    issues.Error(row.EnLocation,
                        $"'{row.Key}' 의 자리표시자가 언어마다 다르다 — ko {Describe(LocText.Placeholders(ko))} / en {Describe(LocText.Placeholders(en))}.");
                    ok = false;
                }
            }

            if (ownerOf != null && LocKey.TrySplit(row.Key, out string ownerId, out string fieldPath))
            {
                object owner = ownerOf(ownerId);
                if (owner == null || !DataTableFields.IsLocalizedText(
                        DataTableFields.Resolve(owner.GetType(), DataTableApplier.ToPropertyPath(fieldPath))))
                {
                    issues.Warning(row.KeyLocation, owner == null
                        ? $"'{row.Key}' 의 주인 '{ownerId}' 를 데이터 테이블 대상에서 찾지 못해 {{필드}} 를 검사하지 못했다(이름이 바뀐 고아 행이면 key 를 고칠 것)."
                        : $"'{row.Key}' — {owner.GetType().Name} 에 문구 필드 '{fieldPath}' 가 없어 {{필드}} 를 검사하지 못했다(고아 행이면 key 를 고칠 것).");
                }
                else
                {
                    // 문법 오류는 위에서 이미 보고했다 — 같은 이유를 두 번 내지 않는다.
                    if (koSyntax && ko.Length > 0)
                    {
                        ok &= CheckOwnerFields(ko, owner, row.KoLocation, row.Key, issues);
                    }

                    if (enSyntax)
                    {
                        ok &= CheckOwnerFields(en, owner, row.EnLocation, row.Key, issues);
                    }
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

    // 자동 키 문구의 {필드} 가 주인의 실제 필드·형식인지 — 수치 시트 툴팁 칸에 쓰던 SkillTooltipFormatter 규칙 그대로.
    private static bool CheckOwnerFields(string text, object owner, string location, string key, DataTableIssues issues)
    {
        if (SkillTooltipFormatter.TryValidate(text, owner, out string error))
        {
            return true;
        }

        issues.Error(location, $"'{key}': {error}");
        return false;
    }

    private static string Describe(HashSet<string> names) =>
        names.Count == 0 ? "(없음)" : string.Join(" ", names.OrderBy(n => n, StringComparer.Ordinal).Select(n => "{" + n + "}"));
}
