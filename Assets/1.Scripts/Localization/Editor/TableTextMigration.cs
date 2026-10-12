using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 병합 Export 때 옛 수치 시트(Paladin·Gunner 등)의 번역 문구 칸(<c>tooltip.*</c> — <see cref="DataTableFields.IsLocalizedText"/>)을
/// Strings 시트의 자동 키 행 ko 로 옮기는 계획(순수 함수). 툴팁 문구 원본은 Strings 하나다(localization.md §5).
/// <list type="number">
/// <item>옛 시트(문자열 시트·<c>#</c> 메모 시트 제외)의 값 칸 중 (대상 Id, 필드)가 지금 코드의 문구 칸(<see cref="DataTableTextKeys.Slot"/>)인 것만 본다.</item>
/// <item>Strings 에 그 키 행이 없으면 → 새 행(ko = 수치 시트 값, en 빈 칸). 기획이 수치 시트에서 고친 문구가 인라인 SO 값보다 이긴다.</item>
/// <item>Strings 에 이미 행이 있으면 → <b>Strings 행 유지</b>(L2 뒤 기획이 고친 곳은 Strings — en 과 짝이 맞는 쪽). 값이 다르면 경고(버린 값은 Export 백업에).</item>
/// <item>같은 키로 두 칸이 오면(같은 컴포넌트 타입이 두 프리팹에) 처음 값만 — 같은 값이면 조용히, 다르면 경고.</item>
/// </list>
/// 옮긴(또는 Strings 에 이미 있는) 칸만 <see cref="Moved"/> 에 담긴다 — 수치 시트에서 빠져도 되는 칸이다.
/// Strings 머리글을 못 읽어 옮길 수 없으면 <see cref="Error"/> — 호출자(Export)는 파일을 쓰지 않는다(수치 시트 칸이 그대로 남는다).
/// 두 번 돌려도 같다 — 두 번째엔 Strings 에 행이 있으므로 새 행이 없다.
/// </summary>
public sealed class TableTextMigration
{
    private TableTextMigration()
    {
    }

    /// <summary>Strings 시트에 새로 붙일 행(<see cref="StringsSheet.BuildSheet"/> 의 등록 키 앞에 넣는다 — 같은 키면 이쪽이 먼저).</summary>
    public List<StringKeyInfo> Rows { get; } = new List<StringKeyInfo>();

    /// <summary>수치 시트에서 빼도 되는 칸 (대상 Id, 필드).</summary>
    public List<(string id, string field)> Moved { get; } = new List<(string id, string field)>();

    public List<string> Warnings { get; } = new List<string>();

    /// <summary>null 이 아니면 옮기지 못했다 — 아무 칸도 빼면 안 된다.</summary>
    public string Error { get; private set; }

    public bool Ok => Error == null;

    public static TableTextMigration Plan(IReadOnlyList<XlsxSheet> existing, IEnumerable<DataTableTextKeys.Slot> slots)
    {
        var plan = new TableTextMigration();
        var slotOf = new Dictionary<(string, string), DataTableTextKeys.Slot>();
        foreach (DataTableTextKeys.Slot slot in slots)
        {
            if (!slotOf.ContainsKey((slot.Id, slot.FieldPath)))
            {
                slotOf[(slot.Id, slot.FieldPath)] = slot;
            }
        }

        var cells = new List<(string sheet, DataTableTextKeys.Slot slot, string value)>();
        foreach (XlsxSheet sheet in existing.Where(s => !StringsSheet.IsStringsSheet(s.Name) &&
                                                         !s.Name.StartsWith(DataTableSchema.CommentPrefix, StringComparison.Ordinal)))
        {
            foreach ((string asset, string field, string value) in DataTableMerge.Pairs(sheet))
            {
                if (slotOf.TryGetValue((asset, field), out DataTableTextKeys.Slot slot))
                {
                    cells.Add((sheet.Name, slot, value));
                }
            }
        }

        if (cells.Count == 0)
        {
            return plan;
        }

        XlsxSheet stringsSheet = existing.FirstOrDefault(s => StringsSheet.Strings.Is(s.Name));
        Dictionary<string, string> sheetKo = StringsSheet.Strings.ReadKo(stringsSheet);
        if (sheetKo == null)
        {
            plan.Error = $"{StringsSheet.Strings.Name} 시트 머리글을 읽지 못해 수치 시트 문구 {cells.Count}칸을 옮길 수 없다 — " +
                         $"머리글을 '{string.Join(" | ", StringsSheet.Strings.Headers)}' 로 고친 뒤 다시 Export.";
            return plan;
        }

        var planned = new Dictionary<string, (string value, string from)>(StringComparer.Ordinal);
        foreach ((string sheet, DataTableTextKeys.Slot slot, string value) in cells)
        {
            string from = $"{sheet} {slot.Id}.{slot.FieldPath}";
            plan.Moved.Add((slot.Id, slot.FieldPath));
            if (sheetKo.TryGetValue(slot.Key, out string ko))
            {
                if (ko != value)
                {
                    plan.Warnings.Add($"'{slot.Key}': Strings 행 문구를 유지한다 — 수치 시트 {from} 값은 다르다(버림, Export 백업에 있다). " +
                                      $"Strings ko '{Short(ko)}' / 수치 시트 '{Short(value)}'.");
                }

                continue;
            }

            if (planned.TryGetValue(slot.Key, out (string value, string from) first))
            {
                if (first.value != value)
                {
                    plan.Warnings.Add($"'{slot.Key}': {first.from} 와 {from} 의 문구가 다르다 — {first.from} 것만 옮겼다.");
                }

                continue;
            }

            planned[slot.Key] = (value, from);
            plan.Rows.Add(new StringKeyInfo(slot.Key, slot.Description, value));
        }

        return plan;
    }

    private static string Short(string text)
    {
        string oneLine = text.Replace("\r", string.Empty).Replace('\n', ' ');
        return oneLine.Length <= 40 ? oneLine : oneLine.Substring(0, 40) + "…";
    }
}
