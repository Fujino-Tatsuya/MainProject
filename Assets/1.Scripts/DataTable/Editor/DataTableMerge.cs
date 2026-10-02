using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// 병합 Export(PLAN-data-table.md D6-1·D7) — 배치는 새 템플릿대로 다시 짜고, <b>기획이 고친 값은 (대상, 필드) 로 보존</b>한다. 순수 함수.
/// <list type="number">
/// <item>옛 시트 전부(세로 표·행 테이블·키-값 어느 형식이든)의 값 칸을 (대상 Id, 필드) → 값 으로 뽑는다.</item>
/// <item>새 템플릿(현재 인스펙터 값)의 같은 칸을 그 값으로 덮는다 — 숫자는 원래 표기 그대로.</item>
/// <item>옛 시트 중 <c>#</c> 메모 시트, 코드가 모르는 시트는 그대로 남기고, 나머지는 새 배치로 대체한다.</item>
/// </list>
/// 코드에서 사라진 필드의 값은 버려지고 보고된다. 기획이 시트에 직접 넣은 # 메모 행·열은 남지 않는다(원본은 Export 백업).
/// 시트 이동(D6 묶음·D7 카테고리)도 이 한 가지로 된다 — 값은 시트가 아니라 (대상, 필드) 를 따라간다.
/// </summary>
public static class DataTableMerge
{
    private const string Comment = DataTableSchema.CommentPrefix;

    /// <param name="knownSheets">코드가 아는 시트 이름(타입 이름·묶음 이름) — 여기 있고 새 배치에 없는 옛 시트는 대체된 것으로 보고 뺀다.</param>
    public static List<XlsxWriteSheet> Merge(IReadOnlyList<XlsxSheet> existing, IReadOnlyList<XlsxWriteSheet> fresh, List<string> report,
        ISet<string> knownSheets = null)
    {
        var freshNames = new HashSet<string>(fresh.Select(s => s.Name), StringComparer.Ordinal);

        // 1) 옛 값 모으기.
        var oldValues = new Dictionary<string, (string value, string sheet)>(StringComparer.Ordinal);
        foreach (XlsxSheet old in existing.Where(s => !s.Name.StartsWith(Comment, StringComparison.Ordinal)))
        {
            foreach ((string asset, string field, string value) in Pairs(old))
            {
                oldValues[Key(asset, field)] = (value, old.Name);
            }
        }

        // 2) 새 배치에 덮기.
        var used = new HashSet<string>(StringComparer.Ordinal);
        int fromInspector = 0;
        foreach (XlsxWriteSheet sheet in fresh)
        {
            foreach ((object[] row, int column, string asset, string field) in ValueCells(sheet))
            {
                string key = Key(asset, field);
                if (oldValues.TryGetValue(key, out (string value, string sheet) old))
                {
                    row[column] = ToCell(old.value);
                    used.Add(key);
                }
                else if (!Equals(row[column], DataTableSchema.NotApplicable))
                {
                    fromInspector++;
                }
            }
        }

        report.Add($"기존 값 {used.Count}개 유지, 새 칸 {fromInspector}개는 현재 인스펙터 값.");
        foreach (IGrouping<string, KeyValuePair<string, (string value, string sheet)>> lost in oldValues
                     .Where(p => !used.Contains(p.Key))
                     .GroupBy(p => p.Value.sheet))
        {
            report.Add($"{lost.Key}: 값 {lost.Count()}개는 코드에 그 대상·필드가 없어 버려졌다" +
                       $"(예: {string.Join(", ", lost.Take(3).Select(p => p.Key.Replace("\n", ".")))}) — 원본은 Export 백업에.");
        }

        // 3) 옛 시트 정리: 메모 시트·모르는 시트는 남기고, 새 배치로 대체된 시트는 뺀다.
        var result = new List<XlsxWriteSheet>(fresh);
        foreach (XlsxSheet old in existing.Where(s => !freshNames.Contains(s.Name)))
        {
            bool memo = old.Name.StartsWith(Comment, StringComparison.Ordinal);
            if (memo || knownSheets == null || !knownSheets.Contains(old.Name))
            {
                if (!memo)
                {
                    report.Add($"{old.Name}: 코드가 모르는 시트 — 그대로 둠.");
                }

                result.Add(Copy(old));
            }
            else
            {
                report.Add($"{old.Name}: 새 배치로 옮겨 시트를 뺐다.");
            }
        }

        return result;
    }

    /// <summary>새 템플릿 시트의 값 칸 — (그 행, 열, 대상 Id, 필드). 세로 표·행 테이블·키-값 모두.</summary>
    private static IEnumerable<(object[] row, int column, string asset, string field)> ValueCells(XlsxWriteSheet sheet)
    {
        if (sheet.Rows.Count == 0)
        {
            yield break;
        }

        string kind = Text(sheet.Rows[0], 0);
        if (kind == DataTableSchema.AssetHeader)
        {
            foreach (object[] row in sheet.Rows.Skip(1).Where(r => r.Length >= 3))
            {
                string asset = Text(row, 0);
                if (asset.Length > 0 && !asset.StartsWith(Comment, StringComparison.Ordinal))
                {
                    yield return (row, 2, asset, Text(row, 1));
                }
            }

            yield break;
        }

        if (kind == DataTableSchema.IdHeader)
        {
            object[] header = sheet.Rows[0];
            foreach (object[] row in sheet.Rows.Skip(2))
            {
                for (int c = 1; c < row.Length && c < header.Length; c++)
                {
                    yield return (row, c, Text(row, 0), Text(header, c));
                }
            }

            yield break;
        }

        // 세로 표
        List<(int column, string id)> targets = null;
        foreach (object[] row in sheet.Rows)
        {
            string first = Text(row, 0);
            if (first == DataTableSchema.VerticalFieldHeader)
            {
                targets = Enumerable.Range(1, row.Length - 1)
                    .Select(c => (c, Text(row, c)))
                    .Where(t => t.Item2.Length > 0 && !t.Item2.StartsWith(Comment, StringComparison.Ordinal))
                    .ToList();
                continue;
            }

            if (targets == null || first.Length == 0 || first.StartsWith(Comment, StringComparison.Ordinal))
            {
                continue;
            }

            foreach ((int column, string id) in targets.Where(t => t.column < row.Length))
            {
                yield return (row, column, id, first);
            }
        }
    }

    /// <summary>읽은 시트의 값 칸 전부(세로 표·행 테이블·키-값) — # 행·열, 빈 칸, "-" 는 뺀다.</summary>
    private static IEnumerable<(string asset, string field, string value)> Pairs(XlsxSheet sheet)
    {
        string kind = sheet.Cell(0, 0).Trim();
        if (kind == DataTableSchema.AssetHeader)
        {
            for (int r = 1; r < sheet.Rows.Count; r++)
            {
                string asset = sheet.Cell(r, 0).Trim();
                string field = sheet.Cell(r, 1).Trim();
                string value = sheet.Cell(r, 2).Trim();
                if (asset.Length > 0 && !asset.StartsWith(Comment, StringComparison.Ordinal) && field.Length > 0 && IsValue(value))
                {
                    yield return (asset, field, value);
                }
            }

            yield break;
        }

        if (kind == DataTableSchema.IdHeader)
        {
            int width = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Length);
            for (int r = 2; r < sheet.Rows.Count; r++)
            {
                string id = sheet.Cell(r, 0).Trim();
                if (id.Length == 0 || id.StartsWith(Comment, StringComparison.Ordinal))
                {
                    continue;
                }

                for (int c = 1; c < width; c++)
                {
                    string field = sheet.Cell(0, c).Trim();
                    string value = sheet.Cell(r, c).Trim();
                    if (field.Length > 0 && !field.StartsWith(Comment, StringComparison.Ordinal) && IsValue(value))
                    {
                        yield return (id, field, value);
                    }
                }
            }

            yield break;
        }

        // 세로 표
        List<(int column, string id)> targets = null;
        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            string first = sheet.Cell(r, 0).Trim();
            if (first == DataTableSchema.VerticalFieldHeader)
            {
                targets = new List<(int, string)>();
                for (int c = 1; c < sheet.Rows[r].Length; c++)
                {
                    string id = sheet.Cell(r, c).Trim();
                    if (id.Length > 0 && !id.StartsWith(Comment, StringComparison.Ordinal))
                    {
                        targets.Add((c, id));
                    }
                }

                continue;
            }

            if (targets == null || first.Length == 0 || first.StartsWith(Comment, StringComparison.Ordinal))
            {
                continue;
            }

            foreach ((int column, string id) in targets)
            {
                string value = sheet.Cell(r, column).Trim();
                if (IsValue(value))
                {
                    yield return (id, first, value);
                }
            }
        }
    }

    private static bool IsValue(string text) => text.Length > 0 && text != DataTableSchema.NotApplicable;

    private static XlsxWriteSheet Copy(XlsxSheet old)
    {
        int frozen = old.Cell(0, 0).Trim() == DataTableSchema.IdHeader ? 2 : 1;
        return new XlsxWriteSheet(old.Name, old.Rows.Select(r => r.Select(ToCell).ToArray()).ToList(), frozenRows: frozen, boldRows: 1);
    }

    /// <summary>읽은 칸 텍스트 → 쓸 셀. 숫자는 표기 그대로 숫자 셀, TRUE/FALSE 는 불리언, 빈 칸은 null.</summary>
    private static object ToCell(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return new XlsxNumber(text);
        }

        if (text == "TRUE" || text == "FALSE")
        {
            return text == "TRUE";
        }

        return text;
    }

    private static string Text(object[] row, int column)
    {
        if (column >= row.Length || row[column] == null)
        {
            return string.Empty;
        }

        return row[column] is XlsxNumber number ? number.Text : row[column].ToString().Trim();
    }

    private static string Key(string asset, string field) => $"{asset}\n{field}";
}
