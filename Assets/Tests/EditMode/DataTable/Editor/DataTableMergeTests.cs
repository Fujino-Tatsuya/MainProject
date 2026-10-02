using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

public sealed class DataTableMergeTests
{
    private static XlsxSheet Old(string name, params string[][] rows) => new XlsxSheet(name, rows);

    private static XlsxWriteSheet Fresh(string name, params object[][] rows) => new XlsxWriteSheet(name, rows.ToList());

    // 병합 결과를 실제 xlsx 로 썼다 다시 읽는다 — 셀 텍스트로 비교하려고.
    private static List<XlsxSheet> RoundTrip(List<XlsxWriteSheet> sheets)
    {
        var stream = new MemoryStream();
        XlsxWriter.Write(stream, sheets);
        stream.Position = 0;
        return XlsxReader.Read(stream).ToList();
    }

    [Test]
    public void KeyValue_KeepsDesignerValues_AddsNewRows_CommentsRemovedFields()
    {
        var existing = new[]
        {
            Old("PlayerDashData",
                new[] { "Asset", "Field", "Value", "#설명" },
                new[] { "PlayerDashData", "dashSpeed", "25", "기획이 바꾼 값" },
                new[] { "PlayerDashData", "oldField", "1", "" },
                new[] { "#PlayerDashData", "memo", "x", "" }),
        };
        var fresh = new[]
        {
            Fresh("PlayerDashData",
                new object[] { "Asset", "Field", "Value", "#설명" },
                new object[] { "PlayerDashData", "dashSpeed", 20f, "" },
                new object[] { "PlayerDashData", "maxCharge", 2L, "충전 수" }),
        };
        var report = new List<string>();

        XlsxSheet merged = RoundTrip(DataTableMerge.Merge(existing, fresh, report))[0];

        Assert.That(merged.Rows[1], Is.EqualTo(new[] { "PlayerDashData", "dashSpeed", "25", "기획이 바꾼 값" }), "기획 값 유지");
        Assert.That(merged.Cell(2, 0), Is.EqualTo("#PlayerDashData"), "코드에 없는 필드는 메모로");
        Assert.That(merged.Cell(3, 0), Is.EqualTo("#PlayerDashData"), "원래 메모 행은 그대로");
        Assert.That(merged.Rows[4], Is.EqualTo(new[] { "PlayerDashData", "maxCharge", "2", "충전 수" }), "새 필드는 현재 값으로 추가");
        Assert.That(report, Has.Count.EqualTo(2));
    }

    [Test]
    public void RowTable_KeepsValues_AddsColumnsAndRows_CommentsRemoved()
    {
        var existing = new[]
        {
            Old("MonsterDataSO",
                new[] { "Id", "maxHp", "oldStat", "#메모" },
                new[] { "설명 →", "체력", "옛 값", "" },
                new[] { "Goblin", "999", "3", "기획 메모" },
                new[] { "Removed", "10", "1", "" }),
        };
        var fresh = new[]
        {
            Fresh("MonsterDataSO",
                new object[] { "Id", "maxHp", "moveSpeed" },
                new object[] { "설명 →", "체력", "이동" },
                new object[] { "Goblin", 100L, 2.5f },
                new object[] { "Orc", 300L, 1.5f }),
        };
        var report = new List<string>();

        XlsxSheet merged = RoundTrip(DataTableMerge.Merge(existing, fresh, report))[0];

        Assert.That(merged.Rows[0], Is.EqualTo(new[] { "Id", "maxHp", "#oldStat", "#메모", "moveSpeed" }));
        Assert.That(merged.Cell(1, 4), Is.EqualTo("이동"), "새 열 설명");
        Assert.That(merged.Rows[2], Is.EqualTo(new[] { "Goblin", "999", "3", "기획 메모", "2.5" }), "기획 값 유지 + 새 열 값");
        Assert.That(merged.Cell(3, 0), Is.EqualTo("#Removed"), "없는 대상은 메모로");
        Assert.That(merged.Rows[4], Is.EqualTo(new[] { "Orc", "300", "", "", "1.5" }), "새 대상 행");

        // 병합 결과는 그대로 가져오기가 된다(# 열·행은 무시, 빈 칸 없음).
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = DataTableSchema.Parse("T.xlsx", new[] { merged }, issues);
        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(entries.Select(e => $"{e.AssetId}.{e.Field}={e.Value}"), Is.EqualTo(new[]
        {
            "Goblin.maxHp=999", "Goblin.moveSpeed=2.5", "Orc.maxHp=300", "Orc.moveSpeed=1.5",
        }));
    }

    [Test]
    public void NewSheetAdded_UnknownAndChangedFormatSheetsKept()
    {
        var existing = new[]
        {
            Old("#기획메모", new[] { "아무거나" }),
            Old("GoneType", new[] { "Asset", "Field", "Value" }, new[] { "A", "x", "1" }),
            Old("Flipped", new[] { "Asset", "Field", "Value" }, new[] { "A", "x", "1" }),
        };
        var fresh = new[]
        {
            Fresh("Flipped", new object[] { "Id", "x" }, new object[] { "설명 →", "" }, new object[] { "A", 1L }, new object[] { "B", 2L }),
            Fresh("Brand", new object[] { "Asset", "Field", "Value" }, new object[] { "Brand", "y", 5L }),
        };
        var report = new List<string>();

        List<XlsxSheet> merged = RoundTrip(DataTableMerge.Merge(existing, fresh, report));

        Assert.That(merged.Select(s => s.Name), Is.EqualTo(new[] { "#기획메모", "GoneType", "Flipped", "Brand" }));
        Assert.That(merged[2].Cell(0, 0), Is.EqualTo("Asset"), "형식이 바뀐 시트는 손대지 않음");
        Assert.That(merged[3].Cell(1, 2), Is.EqualTo("5"));
        Assert.That(report.Count(r => r.Contains("형식이 바뀌었다")), Is.EqualTo(1));
        Assert.That(report.Count(r => r.Contains("GoneType")), Is.EqualTo(1));
    }

    [Test]
    public void ExistingNumberText_IsWrittenBackUnchanged()
    {
        // 0.1 같은 값이 float 왕복으로 0.100000001 이 되면 안 된다 — 기존 칸은 텍스트 그대로.
        var existing = new[] { Old("S", new[] { "Asset", "Field", "Value" }, new[] { "S", "x", "0.1" }, new[] { "S", "y", "1E-05" }) };
        var fresh = new[] { Fresh("S", new object[] { "Asset", "Field", "Value" }, new object[] { "S", "x", 0f }, new object[] { "S", "y", 0f }) };

        XlsxSheet merged = RoundTrip(DataTableMerge.Merge(existing, fresh, new List<string>()))[0];

        Assert.That(merged.Cell(1, 2), Is.EqualTo("0.1"));
        Assert.That(merged.Cell(2, 2), Is.EqualTo("1E-05"));
    }
}
