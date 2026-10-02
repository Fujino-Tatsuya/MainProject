using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// 병합 Export(PLAN-data-table.md D6-1) — 기획이 쓰던 xlsx 에 새로 생긴 시트·열·행만 더한다. 순수 함수(테스트 가능).
/// <list type="bullet">
/// <item><b>기존 칸 값은 그대로</b>(숫자는 원래 표기 그대로 다시 쓴다).</item>
/// <item>새 시트 → 통째로 추가. 새 필드(열·키-값 행)·새 대상(행) → 현재 인스펙터 값으로 추가.</item>
/// <item>코드에서 사라진 필드·대상 → 지우지 않고 <c>#</c> 를 붙여 메모로(그대로 두면 가져오기 오류, 지우면 기획 값 유실).</item>
/// <item>형식(행 테이블 ↔ 키-값)이 바뀐 시트·표시가 사라진 시트 → 손대지 않고 보고만.</item>
/// </list>
/// 서식·수식·열 너비는 보존하지 않는다(xlsx 를 새로 쓴다 — 수식은 저장된 결과값이 남는다).
/// </summary>
public static class DataTableMerge
{
    private const string Comment = DataTableSchema.CommentPrefix;

    /// <param name="movedSheets">옛 시트 이름 → 새 묶음 시트 이름(타입을 <c>[DataTableSheet("묶음")]</c> 으로 옮긴 경우).
    /// 옛 시트의 값을 새 시트의 같은 (Asset, Field) 칸으로 옮기고 옛 시트는 뺀다.</param>
    public static List<XlsxWriteSheet> Merge(IReadOnlyList<XlsxSheet> existing, IReadOnlyList<XlsxWriteSheet> fresh, List<string> report,
        IReadOnlyDictionary<string, string> movedSheets = null)
    {
        var freshByName = fresh.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var result = new List<XlsxWriteSheet>();

        // 옮겨 갈 값 모으기: 새 시트 이름 → (Asset, Field) → 옛 칸 값.
        var carried = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        var movedAway = new HashSet<string>(StringComparer.Ordinal);
        if (movedSheets != null)
        {
            foreach (XlsxSheet old in existing)
            {
                if (movedSheets.TryGetValue(old.Name, out string target) && freshByName.ContainsKey(target) && !freshByName.ContainsKey(old.Name))
                {
                    if (!carried.TryGetValue(target, out Dictionary<string, object> values))
                    {
                        values = new Dictionary<string, object>(StringComparer.Ordinal);
                        carried[target] = values;
                    }

                    int count = 0;
                    foreach ((string asset, string field, string value) in Pairs(old))
                    {
                        values[Key(asset, field)] = ToCell(value);
                        count++;
                    }

                    movedAway.Add(old.Name);
                    report.Add($"{old.Name}: '{target}' 시트로 옮김(값 {count}개) — 옛 시트는 뺐다.");
                }
            }
        }

        // 옮겨 온 값을 새 묶음 시트(키-값)의 같은 칸에 덮는다 — 아래 병합에서 기존 시트가 없으면 이 값으로 추가된다.
        foreach (KeyValuePair<string, Dictionary<string, object>> pair in carried)
        {
            XlsxWriteSheet target = freshByName[pair.Key];
            int applied = 0;
            foreach (object[] row in target.Rows.Skip(1))
            {
                if (row.Length >= 3 && pair.Value.TryGetValue(Key(row[0], row[1]), out object value))
                {
                    row[2] = value;
                    applied++;
                }
            }

            int lost = pair.Value.Count - applied;
            if (lost > 0)
            {
                report.Add($"{pair.Key}: 옮긴 값 중 {lost}개는 코드에 그 필드가 없어 버려졌다(원본은 Export 백업에).");
            }
        }

        foreach (XlsxSheet old in existing.Where(s => !movedAway.Contains(s.Name)))
        {
            if (!freshByName.TryGetValue(old.Name, out XlsxWriteSheet now))
            {
                if (!old.Name.StartsWith(Comment, StringComparison.Ordinal))
                {
                    report.Add($"{old.Name}: 코드에 [DataTableSheet] 대상이 없다 — 시트는 그대로 둠.");
                }

                result.Add(Copy(old));
                continue;
            }

            freshByName.Remove(old.Name);
            string oldKind = old.Cell(0, 0).Trim();
            string newKind = now.Rows.Count > 0 ? now.Rows[0][0] as string : null;
            if (oldKind != newKind)
            {
                report.Add($"{old.Name}: 형식이 바뀌었다({oldKind} → {newKind}, 대상 수가 1 ↔ 여럿) — 시트는 그대로 둠, 직접 옮길 것.");
                result.Add(Copy(old));
            }
            else if (oldKind == DataTableSchema.IdHeader)
            {
                result.Add(MergeRowTable(old, now, report));
            }
            else
            {
                result.Add(MergeKeyValue(old, now, report));
            }
        }

        foreach (XlsxWriteSheet added in fresh.Where(s => freshByName.ContainsKey(s.Name)))
        {
            report.Add($"{added.Name}: 새 시트 추가(현재 인스펙터 값).");
            result.Add(added);
        }

        return result;
    }

    private static XlsxWriteSheet MergeKeyValue(XlsxSheet old, XlsxWriteSheet now, List<string> report)
    {
        var freshRows = now.Rows.Skip(1).ToDictionary(r => Key(r[0], r[1]), r => r);
        var rows = old.Rows.Select(ToCells).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int r = 1; r < rows.Count; r++)
        {
            string asset = Text(rows[r], 0);
            string field = Text(rows[r], 1);
            if (asset.Length == 0 || asset.StartsWith(Comment, StringComparison.Ordinal))
            {
                continue;
            }

            string key = Key(asset, field);
            seen.Add(key);
            if (!freshRows.ContainsKey(key))
            {
                rows[r][0] = Comment + asset;
                report.Add($"{old.Name}: '{asset}.{field}' 가 코드에 없다 — 행을 메모(#)로 바꿈.");
            }
        }

        int added = 0;
        foreach (KeyValuePair<string, object[]> pair in freshRows.Where(p => !seen.Contains(p.Key)))
        {
            rows.Add(pair.Value);
            added++;
        }

        if (added > 0)
        {
            report.Add($"{old.Name}: 새 행 {added}개 추가.");
        }

        return new XlsxWriteSheet(old.Name, rows, frozenRows: 1, boldRows: 1);
    }

    private static XlsxWriteSheet MergeRowTable(XlsxSheet old, XlsxWriteSheet now, List<string> report)
    {
        var rows = old.Rows.Select(ToCells).ToList();
        while (rows.Count < 2)
        {
            rows.Add(Array.Empty<object>());
        }

        object[] newHeader = now.Rows[0];
        object[] newDescription = now.Rows[1];
        var freshById = now.Rows.Skip(2).ToDictionary(r => (string)r[0], r => r, StringComparer.Ordinal);
        var freshColumn = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 1; c < newHeader.Length; c++)
        {
            freshColumn[(string)newHeader[c]] = c;
        }

        // 열: 기존 머리글 유지, 코드에서 사라진 필드는 #, 새 필드는 끝에 추가.
        var header = new List<object>(rows[0]);
        var description = new List<object>(rows[1]);
        var existingFields = new HashSet<string>(StringComparer.Ordinal);
        for (int c = 1; c < header.Count; c++)
        {
            string name = header[c] as string ?? string.Empty;
            if (name.Length == 0 || name.StartsWith(Comment, StringComparison.Ordinal))
            {
                continue;
            }

            existingFields.Add(name);
            if (!freshColumn.ContainsKey(name))
            {
                header[c] = Comment + name;
                report.Add($"{old.Name}: 열 '{name}' 이 코드에 없다 — 머리글을 메모(#)로 바꿈.");
            }
        }

        var addedColumns = freshColumn.Keys.Where(f => !existingFields.Contains(f)).OrderBy(f => freshColumn[f]).ToList();
        foreach (string field in addedColumns)
        {
            Pad(description, header.Count);
            header.Add(field);
            description.Add(newDescription[freshColumn[field]]);
        }

        if (addedColumns.Count > 0)
        {
            report.Add($"{old.Name}: 새 열 {addedColumns.Count}개 추가({string.Join(", ", addedColumns)}).");
        }

        rows[0] = header.ToArray();
        rows[1] = description.ToArray();

        // 기존 행: 새 열 값을 채운다. 코드에서 사라진 대상은 Id 를 #.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        int missingValues = 0;
        for (int r = 2; r < rows.Count; r++)
        {
            string id = Text(rows[r], 0);
            if (id.Length == 0 || id.StartsWith(Comment, StringComparison.Ordinal))
            {
                continue;
            }

            seenIds.Add(id);
            if (!freshById.TryGetValue(id, out object[] freshRow))
            {
                var cells = new List<object>(rows[r]);
                cells[0] = Comment + id;
                rows[r] = cells.ToArray();
                report.Add($"{old.Name}: 대상 '{id}' 가 없다(에셋·프리팹 삭제·이름 변경?) — 행을 메모(#)로 바꿈.");
                continue;
            }

            if (addedColumns.Count > 0)
            {
                var cells = new List<object>(rows[r]);
                Pad(cells, header.Count - addedColumns.Count);
                foreach (string field in addedColumns)
                {
                    cells.Add(freshRow[freshColumn[field]]);
                }

                rows[r] = cells.ToArray();
            }
        }

        // 새 대상 행: 머리글 순서대로 현재 값. 그 대상에 없는 열(배열 길이 차이 등)은 빈 칸 → 가져오기 오류로 드러난다.
        int addedRows = 0;
        foreach (KeyValuePair<string, object[]> pair in freshById.Where(p => !seenIds.Contains(p.Key)))
        {
            var cells = new object[header.Count];
            cells[0] = pair.Key;
            for (int c = 1; c < header.Count; c++)
            {
                string name = header[c] as string ?? string.Empty;
                if (freshColumn.TryGetValue(name, out int source))
                {
                    cells[c] = pair.Value[source];
                }
                else if (name.Length > 0 && !name.StartsWith(Comment, StringComparison.Ordinal))
                {
                    missingValues++;
                }
            }

            rows.Add(cells);
            addedRows++;
        }

        if (addedRows > 0)
        {
            report.Add($"{old.Name}: 새 행 {addedRows}개 추가.");
        }

        if (missingValues > 0)
        {
            report.Add($"{old.Name}: 새 행에 채울 값이 없는 칸 {missingValues}개 — 직접 채울 것(빈 칸은 가져오기 오류).");
        }

        return new XlsxWriteSheet(old.Name, rows, frozenRows: 2, boldRows: 1);
    }

    /// <summary>시트의 값 칸 전부(행 테이블·키-값 모두) — # 행·열, 빈 칸, "-" 는 뺀다.</summary>
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
                if (asset.Length > 0 && !asset.StartsWith(Comment, StringComparison.Ordinal) && field.Length > 0 && value.Length > 0)
                {
                    yield return (asset, field, value);
                }
            }
        }
        else if (kind == DataTableSchema.IdHeader)
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
                    if (field.Length > 0 && !field.StartsWith(Comment, StringComparison.Ordinal)
                        && value.Length > 0 && value != DataTableSchema.NotApplicable)
                    {
                        yield return (id, field, value);
                    }
                }
            }
        }
    }

    private static XlsxWriteSheet Copy(XlsxSheet old)
    {
        int frozen = old.Cell(0, 0).Trim() == DataTableSchema.IdHeader ? 2 : 1;
        return new XlsxWriteSheet(old.Name, old.Rows.Select(ToCells).ToList(), frozenRows: frozen, boldRows: 1);
    }

    /// <summary>읽은 칸 텍스트 → 쓸 셀. 숫자는 표기 그대로 숫자 셀, TRUE/FALSE 는 불리언, 빈 칸은 null.</summary>
    private static object[] ToCells(string[] row) => row.Select(ToCell).ToArray();

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

    private static void Pad(List<object> cells, int count)
    {
        while (cells.Count < count)
        {
            cells.Add(null);
        }
    }

    private static string Key(object asset, object field) => $"{asset}\n{field}";
}
