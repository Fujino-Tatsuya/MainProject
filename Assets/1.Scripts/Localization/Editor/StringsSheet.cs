using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Export 때 Strings 시트에 넣을 키 하나 — 새 키면 이 ko·설명으로 행을 만든다.</summary>
public readonly struct StringKeyInfo
{
    public StringKeyInfo(string key, string description, string ko)
    {
        Key = key;
        Description = description;
        Ko = ko;
    }

    public string Key { get; }
    public string Description { get; }
    public string Ko { get; }
}

/// <summary>
/// Export 가 Strings 시트에 넣을 키를 모으는 곳. L1 에는 등록된 공급자가 없다 — 시트는 머리글만 생기고,
/// 기획이 직접 적은 행은 병합 Export 가 그대로 남긴다. 코드에서 키를 내보내야 하면 <c>[InitializeOnLoad]</c> 에서 <see cref="Register"/>.
/// </summary>
public static class StringKeyRegistry
{
    private static readonly List<Func<IEnumerable<StringKeyInfo>>> providers = new List<Func<IEnumerable<StringKeyInfo>>>();

    public static void Register(Func<IEnumerable<StringKeyInfo>> provider)
    {
        if (provider != null && !providers.Contains(provider))
        {
            providers.Add(provider);
        }
    }

    /// <summary>등록된 키 전부(같은 키는 처음 것만).</summary>
    public static List<StringKeyInfo> Collect()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return providers.SelectMany(p => p() ?? Enumerable.Empty<StringKeyInfo>())
            .Where(k => !string.IsNullOrEmpty(k.Key) && seen.Add(k.Key))
            .ToList();
    }
}

/// <summary>
/// xlsx <c>Strings</c> 시트 — 1행 머리글 <c>key | #설명 | ko | en</c>, 2행부터 키 하나에 한 행.
/// <c>#</c> 로 시작하는 키 행은 메모, 완전히 빈 행은 건너뛴다. 머리글 열은 이름으로 찾는다(순서가 바뀌어도 된다).
/// 다른 시트와 달리 SO 에 쓰지 않고 <see cref="StringTable"/> 로 간다(<see cref="StringTableBuild"/>).
/// </summary>
public static class StringsSheet
{
    public const string SheetName = "Strings";
    public const string KeyHeader = "key";
    public const string DescriptionHeader = DataTableSchema.DescriptionHeader;
    public const string KoHeader = "ko";
    public const string EnHeader = "en";

    public static bool IsStringsSheet(string sheetName) =>
        string.Equals(sheetName?.Trim(), SheetName, StringComparison.Ordinal);

    public static List<StringRow> Parse(string fileName, IEnumerable<XlsxSheet> sheets, DataTableIssues issues)
    {
        var rows = new List<StringRow>();
        foreach (XlsxSheet sheet in sheets.Where(s => IsStringsSheet(s.Name)))
        {
            Parse(fileName, sheet, rows, issues);
        }

        return rows;
    }

    public static void Parse(string fileName, XlsxSheet sheet, List<StringRow> into, DataTableIssues issues)
    {
        if (sheet.Rows.Count == 0 || sheet.Rows.All(r => r.All(string.IsNullOrWhiteSpace)))
        {
            return;
        }

        int keyColumn = FindHeader(sheet, KeyHeader);
        int descriptionColumn = FindHeader(sheet, DescriptionHeader);
        int koColumn = FindHeader(sheet, KoHeader);
        int enColumn = FindHeader(sheet, EnHeader);
        if (keyColumn < 0 || koColumn < 0 || enColumn < 0)
        {
            issues.Error(Where(fileName, 0, 0), $"머리글은 '{KeyHeader} | {DescriptionHeader} | {KoHeader} | {EnHeader}' 여야 한다(1행).");
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
                    issues.Error(Where(fileName, row, keyColumn), "key 가 비어 있는데 문구가 있다.");
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
    /// (ko = 공급자가 준 값, en 은 빈 칸). 기존 시트가 없으면 머리글만 있는 시트가 된다.
    /// </summary>
    public static XlsxWriteSheet BuildSheet(XlsxSheet existing, IEnumerable<StringKeyInfo> registered, List<string> report)
    {
        var rows = new List<object[]> { new object[] { KeyHeader, DescriptionHeader, KoHeader, EnHeader } };
        var present = new HashSet<string>(StringComparer.Ordinal);

        if (existing != null)
        {
            int keyColumn = FindHeader(existing, KeyHeader);
            int descriptionColumn = FindHeader(existing, DescriptionHeader);
            int koColumn = FindHeader(existing, KoHeader);
            int enColumn = FindHeader(existing, EnHeader);
            if (keyColumn < 0 || koColumn < 0 || enColumn < 0)
            {
                // 머리글을 못 읽으면 내용을 옮길 수 없다 — 원본은 Export 백업에 있다.
                report?.Add($"{SheetName}: 기존 시트 머리글을 읽지 못해 행을 옮기지 못했다 — 원본은 Export 백업에.");
            }
            else
            {
                for (int row = 1; row < existing.Rows.Count; row++)
                {
                    string key = existing.Cell(row, keyColumn);
                    string description = descriptionColumn < 0 ? string.Empty : existing.Cell(row, descriptionColumn);
                    string ko = existing.Cell(row, koColumn);
                    string en = existing.Cell(row, enColumn);
                    if (key.Length == 0 && description.Length == 0 && ko.Length == 0 && en.Length == 0)
                    {
                        continue;
                    }

                    present.Add(key.Trim());
                    rows.Add(new object[] { Cell(key), Cell(description), Cell(ko), Cell(en) });
                }
            }
        }

        int added = 0;
        foreach (StringKeyInfo info in registered ?? Enumerable.Empty<StringKeyInfo>())
        {
            if (present.Add(info.Key))
            {
                rows.Add(new object[] { info.Key, Cell(info.Description), Cell(info.Ko), null });
                added++;
            }
        }

        report?.Add($"{SheetName}: 기존 행 {rows.Count - 1 - added}개 유지, 새 키 {added}개 추가.");
        return new XlsxWriteSheet(SheetName, rows, frozenRows: 1, boldRows: 1, frozenColumns: 1);
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

    private static string Where(string fileName, int row, int column) =>
        $"{fileName} › {SheetName}!{XlsxSheet.Address(row, column)}";
}
