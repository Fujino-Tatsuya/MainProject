using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

// 병합 Export(D7) — 배치는 새로 짜고 기획 값은 (대상, 필드) 로 보존한다.
public sealed class DataTableMergeTests
{
    private static XlsxSheet Old(string name, params string[][] rows) => new XlsxSheet(name, rows);

    private static List<XlsxSheet> RoundTrip(List<XlsxWriteSheet> sheets)
    {
        var stream = new MemoryStream();
        XlsxWriter.Write(stream, sheets);
        stream.Position = 0;
        return XlsxReader.Read(stream).ToList();
    }

    // 새 템플릿 모양(세로 표): Monster 시트에 MonsterDataSO 구역.
    private static XlsxWriteSheet FreshMonster() => new XlsxWriteSheet("Monster", new List<object[]>
    {
        new object[] { "#■ MonsterDataSO" },
        new object[] { "필드", "#설명", "ChompBotData", "GauntletBotData" },
        new object[] { "#  ─ 스탯" },
        new object[] { "maxHp", "최대 체력", 100L, 300L },
        new object[] { "moveSpeed", "", 2.5f, 3f },
    }, frozenRows: 0, boldRows: 0, frozenColumns: 1);

    private static readonly HashSet<string> Known = new HashSet<string> { "MonsterDataSO", "Monster", "GauntletBot" };

    [Test]
    public void OldRowAndKeyValueSheets_ValuesMoveIntoNewVerticalLayout()
    {
        var existing = new[]
        {
            Old("MonsterDataSO",
                new[] { "Id", "maxHp", "removedStat" },
                new[] { "설명 →", "", "" },
                new[] { "ChompBotData", "120", "9" },     // 기획이 고친 값 + 코드에서 사라진 필드
                new[] { "GauntletBotData", "300", "9" }),
            Old("GauntletBot", new[] { "Asset", "Field", "Value" }, new[] { "GauntletBot", "punch01Weight", "40" }),
            Old("#기획메모", new[] { "아무거나" }),
            Old("SomethingElse", new[] { "Asset", "Field", "Value" }, new[] { "X", "y", "1" }),
        };
        var report = new List<string>();

        List<XlsxSheet> merged = RoundTrip(DataTableMerge.Merge(existing, new[] { FreshMonster() }, report, Known));

        Assert.That(merged.Select(s => s.Name), Is.EqualTo(new[] { "Monster", "#기획메모", "SomethingElse" }),
            "새 배치 + 메모 시트 + 코드가 모르는 시트. 옛 MonsterDataSO·GauntletBot 시트는 빠진다");
        XlsxSheet monster = merged[0];
        Assert.That(monster.Rows[3], Is.EqualTo(new[] { "maxHp", "최대 체력", "120", "300" }), "기획 값(120) 유지");
        Assert.That(monster.Rows[4], Is.EqualTo(new[] { "moveSpeed", "", "2.5", "3" }), "옛 시트에 없던 필드는 현재 값");
        Assert.That(report.Any(r => r.Contains("MonsterDataSO: 값 2개는 코드에 그 대상·필드가 없어")), Is.True, "removedStat 2칸 보고");
        Assert.That(report.Any(r => r.Contains("GauntletBot: 값 1개")), Is.True, "MidBoss 가 새 배치에 없으면 그 값도 보고");
    }

    [Test]
    public void VerticalToVertical_KeepsDesignerValues_AndDash()
    {
        var existing = new[]
        {
            Old("Monster",
                new[] { "#■ MonsterDataSO" },
                new[] { "필드", "#설명", "ChompBotData", "GauntletBotData" },
                new[] { "maxHp", "옛 설명", "150", "-" }),
        };

        XlsxSheet monster = RoundTrip(DataTableMerge.Merge(existing, new[] { FreshMonster() }, new List<string>(), Known))[0];

        Assert.That(monster.Rows[3], Is.EqualTo(new[] { "maxHp", "최대 체력", "150", "300" }),
            "값은 유지, 설명은 새 툴팁, 옛 '-' 는 값이 아니라 현재 값으로");
    }

    [Test]
    public void ExistingNumberText_IsWrittenBackUnchanged()
    {
        // 0.1 같은 값이 float 왕복으로 0.100000001 이 되면 안 된다 — 기존 칸은 텍스트 그대로.
        var existing = new[] { Old("MonsterDataSO", new[] { "Asset", "Field", "Value" }, new[] { "ChompBotData", "moveSpeed", "1E-05" }) };

        XlsxSheet monster = RoundTrip(DataTableMerge.Merge(existing, new[] { FreshMonster() }, new List<string>(), Known))[0];

        Assert.That(monster.Cell(4, 2), Is.EqualTo("1E-05"));
    }

    [Test]
    public void MergedSheet_ImportsWithoutIssues()
    {
        var existing = new[] { Old("MonsterDataSO", new[] { "Asset", "Field", "Value" }, new[] { "ChompBotData", "maxHp", "120" }) };

        XlsxSheet monster = RoundTrip(DataTableMerge.Merge(existing, new[] { FreshMonster() }, new List<string>(), Known))[0];
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = DataTableSchema.Parse("T.xlsx", new[] { monster }, issues);

        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(entries.Select(e => $"{e.AssetId}.{e.Field}={e.Value}"), Is.EqualTo(new[]
        {
            "ChompBotData.maxHp=120", "GauntletBotData.maxHp=300", "ChompBotData.moveSpeed=2.5", "GauntletBotData.moveSpeed=3",
        }));
    }
}
