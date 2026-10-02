using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>시트의 값 한 칸 = "어떤 SO 의 어떤 필드에 무슨 값". 아직 SO 와 연결되지 않은 텍스트 상태.</summary>
public readonly struct DataTableEntry
{
    public DataTableEntry(string sheet, string assetId, string field, string value, string location)
    {
        Sheet = sheet;
        AssetId = assetId;
        Field = field;
        Value = value;
        Location = location;
    }

    /// <summary>시트 이름 = SO 타입 이름.</summary>
    public string Sheet { get; }

    /// <summary>SO 에셋 파일 이름(확장자 없이).</summary>
    public string AssetId { get; }

    /// <summary>필드 경로. 중첩 "charge.speed", 배열 "phases[0].hp".</summary>
    public string Field { get; }

    public string Value { get; }

    /// <summary>오류 표시용 "Player.xlsx › PlayerDashData!C3".</summary>
    public string Location { get; }
}

public enum DataTableIssueKind
{
    Error,
    Warning,
}

public readonly struct DataTableIssue
{
    public DataTableIssue(DataTableIssueKind kind, string location, string message)
    {
        Kind = kind;
        Location = location;
        Message = message;
    }

    public DataTableIssueKind Kind { get; }
    public string Location { get; }
    public string Message { get; }

    public override string ToString() => $"{Location}: {Message}";
}

/// <summary>오류·경고를 모은다. 오류가 하나라도 있으면 어떤 SO 에도 쓰지 않는다.</summary>
public sealed class DataTableIssues
{
    private readonly List<DataTableIssue> items = new List<DataTableIssue>();

    public IReadOnlyList<DataTableIssue> Items => items;
    public bool HasErrors => items.Any(i => i.Kind == DataTableIssueKind.Error);

    public void Error(string location, string message) => items.Add(new DataTableIssue(DataTableIssueKind.Error, location, message));
    public void Warning(string location, string message) => items.Add(new DataTableIssue(DataTableIssueKind.Warning, location, message));
}

/// <summary>
/// xlsx 시트 → <see cref="DataTableEntry"/> 목록. 시트 형식은 두 가지(PLAN-data-table.md §4-2):
/// <list type="bullet">
/// <item><b>행 테이블</b> — A1 = "Id". 1행 = 필드 이름, 2행 = 설명(무시), 3행부터 데이터. 행 하나 = SO 하나.</item>
/// <item><b>키-값 테이블</b> — A1:C1 = "Asset | Field | Value". 2행부터 데이터. 하나뿐인 SO 용.</item>
/// </list>
/// 공통: 시트 이름 = SO 타입 이름. <c>#</c> 로 시작하는 시트·열·Id(Asset) 는 기획 메모로 보고 무시한다. 완전히 빈 행은 건너뛴다.
/// 빈 값 칸은 오류다 — "안 채움" 과 "0" 을 구분하지 못하면 조용히 틀린다.
/// </summary>
public static class DataTableSchema
{
    public const string IdHeader = "Id";
    public const string AssetHeader = "Asset";
    public const string FieldHeader = "Field";
    public const string ValueHeader = "Value";
    public const string CommentPrefix = "#";

    /// <summary>
    /// 행 테이블에서 "이 대상에는 없는 칸"(예: 23호 Solo 는 공격이 6개라 attacks[6]·[7] 이 없다).
    /// 빈 칸은 "채우는 걸 잊음" 이라 오류로 남기고, 없는 칸은 이 표시로 구분한다.
    /// </summary>
    public const string NotApplicable = "-";

    private const int RowTableFirstDataRow = 2; // 0: 필드 이름, 1: 설명

    public static List<DataTableEntry> Parse(string fileName, IEnumerable<XlsxSheet> sheets, DataTableIssues issues)
    {
        var entries = new List<DataTableEntry>();
        foreach (XlsxSheet sheet in sheets)
        {
            Parse(fileName, sheet, entries, issues);
        }

        return entries;
    }

    public static void Parse(string fileName, XlsxSheet sheet, List<DataTableEntry> into, DataTableIssues issues)
    {
        string sheetName = sheet.Name?.Trim() ?? string.Empty;
        if (IsComment(sheetName))
        {
            return;
        }

        string first = sheet.Cell(0, 0).Trim();
        if (first == IdHeader)
        {
            ParseRowTable(fileName, sheetName, sheet, into, issues);
        }
        else if (first == AssetHeader)
        {
            ParseKeyValueTable(fileName, sheetName, sheet, into, issues);
        }
        else if (!IsEmptySheet(sheet))
        {
            issues.Error(Where(fileName, sheetName, 0, 0),
                $"A1 이 '{IdHeader}'(행 테이블) 도 '{AssetHeader}'(키-값 테이블) 도 아니다. 메모 시트면 이름 앞에 '{CommentPrefix}' 를 붙일 것.");
        }
    }

    private static void ParseRowTable(string fileName, string sheetName, XlsxSheet sheet, List<DataTableEntry> into, DataTableIssues issues)
    {
        // 필드 열 수집. # 열은 무시, 빈 머리글은 아래에 데이터가 없을 때만 허용.
        var fields = new List<(int column, string name)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int width = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Length);
        for (int column = 1; column < width; column++)
        {
            string header = sheet.Cell(0, column).Trim();
            if (IsComment(header))
            {
                continue;
            }

            if (header.Length == 0)
            {
                if (ColumnHasData(sheet, column, RowTableFirstDataRow))
                {
                    issues.Error(Where(fileName, sheetName, 0, column), "머리글(필드 이름)이 빈 열에 값이 있다.");
                }

                continue;
            }

            if (!seen.Add(header))
            {
                issues.Error(Where(fileName, sheetName, 0, column), $"필드 '{header}' 가 두 번 나온다.");
                continue;
            }

            fields.Add((column, header));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int row = RowTableFirstDataRow; row < sheet.Rows.Count; row++)
        {
            string id = sheet.Cell(row, 0).Trim();
            if (IsComment(id))
            {
                continue;
            }

            bool anyValue = fields.Any(f => sheet.Cell(row, f.column).Trim().Length > 0);
            if (id.Length == 0)
            {
                if (anyValue)
                {
                    issues.Error(Where(fileName, sheetName, row, 0), "Id 가 비어 있는데 값이 있다.");
                }

                continue;
            }

            if (!ids.Add(id))
            {
                issues.Error(Where(fileName, sheetName, row, 0), $"Id '{id}' 가 두 번 나온다.");
                continue;
            }

            foreach ((int column, string name) in fields)
            {
                string value = sheet.Cell(row, column).Trim();
                string location = Where(fileName, sheetName, row, column);
                if (value.Length == 0)
                {
                    issues.Error(location, $"'{id}.{name}' 값이 비어 있다(이 대상에 없는 칸이면 '{NotApplicable}').");
                    continue;
                }

                if (value == NotApplicable)
                {
                    continue; // 이 대상엔 없는 칸(배열 길이가 다름 등)
                }

                into.Add(new DataTableEntry(sheetName, id, name, value, location));
            }
        }
    }

    private static void ParseKeyValueTable(string fileName, string sheetName, XlsxSheet sheet, List<DataTableEntry> into, DataTableIssues issues)
    {
        if (sheet.Cell(0, 1).Trim() != FieldHeader || sheet.Cell(0, 2).Trim() != ValueHeader)
        {
            issues.Error(Where(fileName, sheetName, 0, 0),
                $"키-값 테이블 머리글은 '{AssetHeader} | {FieldHeader} | {ValueHeader}' 순서여야 한다.");
            return;
        }

        int width = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Length);
        for (int column = 3; column < width; column++)
        {
            string header = sheet.Cell(0, column).Trim();
            if (!IsComment(header) && (header.Length > 0 || ColumnHasData(sheet, column, 1)))
            {
                issues.Error(Where(fileName, sheetName, 0, column),
                    $"키-값 테이블의 4열부터는 메모 열이어야 한다(머리글 앞에 '{CommentPrefix}').");
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int row = 1; row < sheet.Rows.Count; row++)
        {
            string asset = sheet.Cell(row, 0).Trim();
            string field = sheet.Cell(row, 1).Trim();
            string value = sheet.Cell(row, 2).Trim();
            if (IsComment(asset) || (asset.Length == 0 && field.Length == 0 && value.Length == 0))
            {
                continue;
            }

            if (asset.Length == 0 || field.Length == 0)
            {
                issues.Error(Where(fileName, sheetName, row, asset.Length == 0 ? 0 : 1), "Asset 과 Field 를 모두 채워야 한다.");
                continue;
            }

            if (!keys.Add(asset + "\n" + field))
            {
                issues.Error(Where(fileName, sheetName, row, 1), $"'{asset}.{field}' 가 두 번 나온다.");
                continue;
            }

            string location = Where(fileName, sheetName, row, 2);
            if (value.Length == 0)
            {
                issues.Error(location, $"'{asset}.{field}' 값이 비어 있다.");
                continue;
            }

            into.Add(new DataTableEntry(sheetName, asset, field, value, location));
        }
    }

    private static bool IsComment(string text) => text.StartsWith(CommentPrefix, StringComparison.Ordinal);

    private static bool IsEmptySheet(XlsxSheet sheet) =>
        sheet.Rows.All(r => r.All(c => string.IsNullOrWhiteSpace(c)));

    private static bool ColumnHasData(XlsxSheet sheet, int column, int fromRow)
    {
        for (int row = fromRow; row < sheet.Rows.Count; row++)
        {
            if (sheet.Cell(row, column).Trim().Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string Where(string fileName, string sheetName, int row, int column) =>
        $"{fileName} › {sheetName}!{XlsxSheet.Address(row, column)}";
}
