using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Export 때 문자열 시트에 넣을 키 하나 — 새 키면 이 ko·메모 열로 행을 만든다.</summary>
public readonly struct StringKeyInfo
{
    public StringKeyInfo(string key, string description, string ko)
        : this(key, new[] { description }, ko)
    {
    }

    /// <summary>메모 열이 여럿인 시트용(ErrorCodes — <c>#분류 | #원인 | #조치</c>). 순서는 <see cref="StringsSheet.MemoHeaders"/> 와 같다.</summary>
    public StringKeyInfo(string key, IReadOnlyList<string> memos, string ko)
    {
        Key = key;
        Memos = memos ?? Array.Empty<string>();
        Ko = ko;
    }

    public string Key { get; }

    /// <summary>첫 메모 열(Strings 의 <c>#설명</c>, ErrorCodes 의 <c>#분류</c>).</summary>
    public string Description => Memos.Count > 0 ? Memos[0] : null;

    public IReadOnlyList<string> Memos { get; }
    public string Ko { get; }
}

/// <summary>
/// Export 가 문자열 시트에 넣을 키를 모으는 곳(<c>[DataTableText]</c> 자동 키는 <see cref="DataTableTextKeys"/> 가 따로 모은다).
/// 기획이 직접 적은 행은 병합 Export 가 그대로 남긴다. 코드에서 키를 내보내야 하면 <c>[InitializeOnLoad]</c> 에서
/// Strings 는 <see cref="Register"/>(예: <c>CharacterRosterStringKeys</c> — 캐릭터 이름), ErrorCodes 는 <see cref="RegisterErrorCodes"/>(<c>ErrorCodeKeys</c>).
/// </summary>
public static class StringKeyRegistry
{
    private static readonly List<Func<IEnumerable<StringKeyInfo>>> providers = new List<Func<IEnumerable<StringKeyInfo>>>();
    private static readonly List<Func<IEnumerable<StringKeyInfo>>> errorCodeProviders = new List<Func<IEnumerable<StringKeyInfo>>>();

    public static void Register(Func<IEnumerable<StringKeyInfo>> provider) => Add(providers, provider);

    public static void RegisterErrorCodes(Func<IEnumerable<StringKeyInfo>> provider) => Add(errorCodeProviders, provider);

    /// <summary>등록된 Strings 키 전부(같은 키는 처음 것만).</summary>
    public static List<StringKeyInfo> Collect() => Collect(providers);

    /// <summary>등록된 ErrorCodes 코드 전부(같은 코드는 처음 것만).</summary>
    public static List<StringKeyInfo> CollectErrorCodes() => Collect(errorCodeProviders);

    private static void Add(List<Func<IEnumerable<StringKeyInfo>>> list, Func<IEnumerable<StringKeyInfo>> provider)
    {
        if (provider != null && !list.Contains(provider))
        {
            list.Add(provider);
        }
    }

    private static List<StringKeyInfo> Collect(List<Func<IEnumerable<StringKeyInfo>>> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return list.SelectMany(p => p() ?? Enumerable.Empty<StringKeyInfo>())
            .Where(k => !string.IsNullOrEmpty(k.Key) && seen.Add(k.Key))
            .ToList();
    }
}

/// <summary>
/// xlsx 문자열 시트 — 1행 머리글 <c>키 | #메모… | ko | en</c>, 2행부터 키 하나에 한 행. 시트는 두 개다.
/// <list type="bullet">
/// <item><see cref="Strings"/> — <c>key | #설명 | ko | en</c>. UI·안내 문구.</item>
/// <item><see cref="ErrorCodes"/> — <c>code | #분류 | #원인(개발자용) | #조치(QA·CS용) | ko | en</c>. 로비·네트워크 오류 문구.</item>
/// </list>
/// 읽기·검증(<see cref="StringTableValidator"/>)·생성은 둘이 같다 — 시트 이름·키 열 이름·메모 열만 다르다.
/// <c>#</c> 로 시작하는 키 행은 메모, 완전히 빈 행은 건너뛴다. 머리글 열은 이름으로 찾는다(순서가 바뀌어도 된다).
/// 다른 시트와 달리 SO 에 쓰지 않고 <see cref="StringTable"/> 로 간다(<see cref="StringTableBuild"/>).
/// </summary>
public sealed class StringsSheet
{
    public const string KoHeader = "ko";
    public const string EnHeader = "en";

    public static readonly StringsSheet Strings = new StringsSheet("Strings", "key", DataTableSchema.DescriptionHeader);

    public static readonly StringsSheet ErrorCodes = new StringsSheet("ErrorCodes", "code", "#분류", "#원인(개발자용)", "#조치(QA·CS용)");

    public static readonly IReadOnlyList<StringsSheet> All = new[] { Strings, ErrorCodes };

    private StringsSheet(string name, string keyHeader, params string[] memoHeaders)
    {
        Name = name;
        KeyHeader = keyHeader;
        MemoHeaders = memoHeaders;
    }

    public string Name { get; }
    public string KeyHeader { get; }

    /// <summary>런타임에 안 들어가는 <c>#</c> 열. 첫 열이 <see cref="StringRow.Description"/>·<see cref="StringKeyInfo.Description"/> 자리.</summary>
    public IReadOnlyList<string> MemoHeaders { get; }

    /// <summary>Export 가 쓰는 머리글 순서.</summary>
    public string[] Headers => new[] { KeyHeader }.Concat(MemoHeaders).Concat(new[] { KoHeader, EnHeader }).ToArray();

    /// <summary>문자열 시트(어느 쪽이든)면 true — 수치 시트 파서가 건너뛴다.</summary>
    public static bool IsStringsSheet(string sheetName) => All.Any(s => s.Is(sheetName));

    public bool Is(string sheetName) => string.Equals(sheetName?.Trim(), Name, StringComparison.Ordinal);

    public List<StringRow> Parse(string fileName, IEnumerable<XlsxSheet> sheets, DataTableIssues issues)
    {
        var rows = new List<StringRow>();
        foreach (XlsxSheet sheet in sheets.Where(s => Is(s.Name)))
        {
            Parse(fileName, sheet, rows, issues);
        }

        return rows;
    }

    public void Parse(string fileName, XlsxSheet sheet, List<StringRow> into, DataTableIssues issues)
    {
        if (sheet.Rows.Count == 0 || sheet.Rows.All(r => r.All(string.IsNullOrWhiteSpace)))
        {
            return;
        }

        int keyColumn = FindHeader(sheet, KeyHeader);
        int descriptionColumn = FindHeader(sheet, MemoHeaders[0]);
        int koColumn = FindHeader(sheet, KoHeader);
        int enColumn = FindHeader(sheet, EnHeader);
        if (keyColumn < 0 || koColumn < 0 || enColumn < 0)
        {
            issues.Error(Where(fileName, 0, 0), $"머리글은 '{string.Join(" | ", Headers)}' 여야 한다(1행).");
            return;
        }

        for (int row = 1; row < sheet.Rows.Count; row++)
        {
            string key = sheet.Cell(row, keyColumn).Trim();
            if (key.StartsWith(DataTableSchema.CommentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string ko = sheet.Cell(row, koColumn).Trim();
            string en = sheet.Cell(row, enColumn).Trim();
            if (key.Length == 0)
            {
                if (ko.Length > 0 || en.Length > 0)
                {
                    issues.Error(Where(fileName, row, keyColumn), $"{KeyHeader} 가 비어 있는데 문구가 있다.");
                }

                continue;
            }

            string description = descriptionColumn < 0 ? string.Empty : sheet.Cell(row, descriptionColumn).Trim();
            into.Add(new StringRow(key, description, ko, en,
                Where(fileName, row, keyColumn), Where(fileName, row, koColumn), Where(fileName, row, enColumn)));
        }
    }

    /// <summary>
    /// Export 용 시트. 기존 시트의 행(메모 행 포함)은 순서·문구 그대로 두고, 등록된 키 중 없는 것만 뒤에 붙인다
    /// (ko = 공급자가 준 값, en 은 빈 칸). 기존 시트가 없거나 비어 있으면 머리글만 있는 시트가 된다.
    /// </summary>
    public XlsxWriteSheet BuildSheet(XlsxSheet existing, IEnumerable<StringKeyInfo> registered, List<string> report)
    {
        string[] headers = Headers;
        var rows = new List<object[]> { headers.Cast<object>().ToArray() };
        var present = new HashSet<string>(StringComparer.Ordinal);
        bool hasContent = existing != null && existing.Rows.Any(r => r.Any(c => !string.IsNullOrWhiteSpace(c)));

        if (hasContent)
        {
            int[] columns = headers.Select(h => FindHeader(existing, h)).ToArray();
            int keyColumn = columns[0];
            int koColumn = columns[headers.Length - 2];
            int enColumn = columns[headers.Length - 1];
            if (keyColumn < 0 || koColumn < 0 || enColumn < 0)
            {
                // 머리글을 못 읽으면 내용을 옮길 수 없다 — 원본은 Export 백업에 있다.
                report?.Add($"{Name}: 기존 시트 머리글을 읽지 못해 행을 옮기지 못했다 — 원본은 Export 백업에.");
            }
            else
            {
                for (int row = 1; row < existing.Rows.Count; row++)
                {
                    string[] values = columns.Select(c => c < 0 ? string.Empty : existing.Cell(row, c)).ToArray();
                    if (values.All(v => v.Length == 0))
                    {
                        continue;
                    }

                    present.Add(values[0].Trim());
                    rows.Add(values.Select(Cell).ToArray());
                }
            }
        }

        int added = 0;
        foreach (StringKeyInfo info in registered ?? Enumerable.Empty<StringKeyInfo>())
        {
            if (present.Add(info.Key))
            {
                var row = new object[headers.Length];
                row[0] = info.Key;
                for (int memo = 0; memo < MemoHeaders.Count && memo < info.Memos.Count; memo++)
                {
                    row[1 + memo] = Cell(info.Memos[memo]);
                }

                row[headers.Length - 2] = Cell(info.Ko);
                rows.Add(row);
                added++;
            }
        }

        report?.Add($"{Name}: 기존 행 {rows.Count - 1 - added}개 유지, 새 키 {added}개 추가.");
        return new XlsxWriteSheet(Name, rows, frozenRows: 1, boldRows: 1, frozenColumns: 1);
    }

    /// <summary>
    /// 기존 시트에만 있고 등록 목록에 없는 키(<c>#</c> 메모 행·빈 키 제외, 시트 순서). ErrorCodes 는 이걸 경고로 알린다 —
    /// 코드가 더 이상 내지 않는 오류 행이거나 코드 이름 오타일 수 있다(행은 지우지 않는다).
    /// </summary>
    public List<string> UnregisteredKeys(XlsxSheet existing, IEnumerable<StringKeyInfo> registered)
    {
        var result = new List<string>();
        if (existing == null || existing.Rows.Count == 0)
        {
            return result;
        }

        int keyColumn = FindHeader(existing, KeyHeader);
        if (keyColumn < 0)
        {
            return result;
        }

        var known = new HashSet<string>((registered ?? Enumerable.Empty<StringKeyInfo>()).Select(k => k.Key), StringComparer.Ordinal);
        for (int row = 1; row < existing.Rows.Count; row++)
        {
            string key = existing.Cell(row, keyColumn).Trim();
            if (key.Length > 0 && !key.StartsWith(DataTableSchema.CommentPrefix, StringComparison.Ordinal) && !known.Contains(key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static object Cell(string text) => string.IsNullOrEmpty(text) ? null : text;

    private static int FindHeader(XlsxSheet sheet, string header)
    {
        string[] first = sheet.Rows.Count == 0 ? Array.Empty<string>() : sheet.Rows[0];
        for (int column = 0; column < first.Length; column++)
        {
            if (string.Equals((first[column] ?? string.Empty).Trim(), header, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return -1;
    }

    private string Where(string fileName, int row, int column) =>
        $"{fileName} › {Name}!{XlsxSheet.Address(row, column)}";
}
