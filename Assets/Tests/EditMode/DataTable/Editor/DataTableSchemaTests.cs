using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

public sealed class DataTableSchemaTests
{
    private static List<DataTableEntry> Parse(XlsxSheet sheet, DataTableIssues issues)
    {
        var entries = new List<DataTableEntry>();
        DataTableSchema.Parse("T.xlsx", sheet, entries, issues);
        return entries;
    }

    private static XlsxSheet Sheet(string name, params string[][] rows) => new XlsxSheet(name, rows);

    [Test]
    public void RowTable_SkipsDescriptionRow_CommentColumnsAndRows()
    {
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = Parse(Sheet("MonsterDataSO",
            new[] { "Id", "maxHp", "#메모", "moveSpeed" },
            new[] { "", "최대 체력", "", "속도" },
            new[] { "Goblin", "100", "약함", "2" },
            new[] { "#Orc", "999", "", "9" },
            new string[0],
            new[] { "Troll", "300", "", "1.5" }), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(entries.Select(e => $"{e.AssetId}.{e.Field}={e.Value}"), Is.EqualTo(new[]
        {
            "Goblin.maxHp=100", "Goblin.moveSpeed=2", "Troll.maxHp=300", "Troll.moveSpeed=1.5",
        }));
        Assert.That(entries[0].Location, Is.EqualTo("T.xlsx › MonsterDataSO!B3"));
    }

    [Test]
    public void RowTable_EmptyValue_IsError()
    {
        var issues = new DataTableIssues();
        Parse(Sheet("S", new[] { "Id", "maxHp" }, new[] { "", "" }, new[] { "Goblin", "" }), issues);

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(issues.Items[0].Location, Is.EqualTo("T.xlsx › S!B3"));
    }

    [Test]
    public void VerticalTable_SectionsWithOwnHeaders_CommentsAndDash()
    {
        // D7 세로 표 — 구역마다 머리글이 다시 나오고 대상(열)이 달라도 된다.
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = Parse(Sheet("Player",
            new[] { "#■ Player" },
            new[] { "필드", "#설명", "Player", "Player_Gunner" },
            new[] { "#  ─ 스탯" },
            new[] { "maxHp", "최대 체력", "100", "50" },
            new string[0],
            new[] { "#■ PlayerDashData" },
            new[] { "필드", "#설명", "PlayerDashData", "#메모" },
            new[] { "dashSpeed", "", "20", "기획 메모" },
            new[] { "#dashDuration", "", "0.3" },
            new[] { "charge", "", "-" }), issues);

        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(entries.Select(e => $"{e.AssetId}.{e.Field}={e.Value}"), Is.EqualTo(new[]
        {
            "Player.maxHp=100", "Player_Gunner.maxHp=50", "PlayerDashData.dashSpeed=20",
        }));
        Assert.That(entries[1].Location, Is.EqualTo("T.xlsx › Player!D4"));
    }

    [Test]
    public void VerticalTable_ValueBeforeHeader_IsError_EmptyValuePassesToApplier()
    {
        // 빈 칸 판정은 필드 타입을 아는 Applier 몫([DataTableText] 문자열만 빈 값 허용) — 스키마는 빈 값 그대로 넘긴다.
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = Parse(Sheet("S",
            new[] { "maxHp", "", "1" },                 // 머리글 전
            new[] { "필드", "#설명", "A", "B" },
            new[] { "maxHp", "", "1", "" }), issues);   // B 빈 칸

        Assert.That(issues.Items.Select(i => i.Location), Is.EquivalentTo(new[] { "T.xlsx › S!A1" }));
        Assert.That(entries.Select(e => $"{e.AssetId}.{e.Field}={e.Value}"), Is.EqualTo(new[] { "A.maxHp=1", "B.maxHp=" }));
    }

    [Test]
    public void RowTable_DashMeansNotApplicable_IsSkippedWithoutError()
    {
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = Parse(Sheet("S",
            new[] { "Id", "attacks[6].damage" },
            new[] { "", "" },
            new[] { "No23", "40" },
            new[] { "No23_Solo", "-" }), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(entries.Select(e => e.AssetId), Is.EqualTo(new[] { "No23" }));
    }

    [Test]
    public void RowTable_DuplicateIdAndHeader_AreErrors()
    {
        var issues = new DataTableIssues();
        Parse(Sheet("S",
            new[] { "Id", "a", "a" },
            new[] { "", "", "" },
            new[] { "X", "1", "2" },
            new[] { "X", "1", "2" }), issues);

        Assert.That(issues.Items.Count(i => i.Kind == DataTableIssueKind.Error), Is.EqualTo(2));
    }

    [Test]
    public void RowTable_ValuesWithoutIdOrHeader_AreErrors()
    {
        var issues = new DataTableIssues();
        Parse(Sheet("S",
            new[] { "Id", "a", "" },
            new[] { "", "", "" },
            new[] { "", "1", "" },
            new[] { "X", "1", "5" }), issues);

        Assert.That(issues.Items.Select(i => i.Location), Is.EquivalentTo(new[] { "T.xlsx › S!C1", "T.xlsx › S!A3" }));
    }

    [Test]
    public void KeyValueTable_ReadsRows_IgnoresCommentColumn()
    {
        var issues = new DataTableIssues();
        List<DataTableEntry> entries = Parse(Sheet("PlayerDashData",
            new[] { "Asset", "Field", "Value", "#설명" },
            new[] { "PlayerDashData", "cooldown", "1.2", "대시 쿨다운" },
            new[] { "#PlayerDashData", "speed", "99", "" },
            new[] { "PlayerDashData", "charge.speed", "3", "" }), issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(entries.Select(e => $"{e.AssetId}.{e.Field}={e.Value}"),
            Is.EqualTo(new[] { "PlayerDashData.cooldown=1.2", "PlayerDashData.charge.speed=3" }));
        Assert.That(entries[0].Location, Is.EqualTo("T.xlsx › PlayerDashData!C2"));
    }

    [Test]
    public void KeyValueTable_DuplicateKeyMissingPartsAndExtraColumn_AreErrors()
    {
        var issues = new DataTableIssues();
        Parse(Sheet("S",
            new[] { "Asset", "Field", "Value", "Note" },
            new[] { "A", "x", "1" },
            new[] { "A", "x", "2" },
            new[] { "A", "", "3" },
            new[] { "A", "y", "" }), issues);

        Assert.That(issues.Items.Select(i => i.Location), Is.EquivalentTo(new[]
        {
            "T.xlsx › S!D1", "T.xlsx › S!B3", "T.xlsx › S!B4", "T.xlsx › S!C5",
        }));
    }

    [Test]
    public void UnknownHeader_IsError_CommentAndEmptySheetsAreIgnored()
    {
        var issues = new DataTableIssues();
        Parse(Sheet("S", new[] { "이름", "hp" }), issues);
        Parse(Sheet("#메모", new[] { "아무거나" }), issues);
        Parse(Sheet("Sheet3"), issues);

        Assert.That(issues.Items.Select(i => i.Location), Is.EqualTo(new[] { "T.xlsx › S!A1" }));
    }
}
