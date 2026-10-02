using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

// 묶음 시트([DataTableSheet("Paladin")]) — 여러 타입이 한 키-값 시트를 같이 쓴다(2026-10-02 캐릭터별 시트).
public sealed class DataTableSheetGroupTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
        {
            Object.DestroyImmediate(o);
        }

        created.Clear();
    }

    private static DataTableEntry Entry(string sheet, string asset, string field, string value) =>
        new DataTableEntry(sheet, asset, field, value, $"T.xlsx › {sheet}!{asset}.{field}");

    private (DataTableTestData data, DataTableTestComponent component) MakePair()
    {
        var data = ScriptableObject.CreateInstance<DataTableTestData>();
        created.Add(data);
        var go = new GameObject("Bot");
        created.Add(go);
        return (data, go.AddComponent<DataTableTestComponent>());
    }

    [Test]
    public void GroupSheet_WritesToEveryMemberType()
    {
        (DataTableTestData data, DataTableTestComponent component) = MakePair();
        var lookup = new TestLookup(("MainSkill", (Object)data), ("Player_Paladin", component)) { Group = "Paladin" };
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = DataTableApplier.Bind(new[]
        {
            Entry("Paladin", "MainSkill", "maxHp", "321"),
            Entry("Paladin", "Player_Paladin", "speed", "7"),
        }, lookup, issues);

        Assert.That(issues.HasErrors, Is.False, string.Join("\n", issues.Items));
        DataTableApplier.ApplyInMemory(writes);
        Assert.That(data.maxHp, Is.EqualTo(321));
        Assert.That(component.speed, Is.EqualTo(7f));
    }

    [Test]
    public void GroupSheet_SameIdOnTwoTypes_ResolvedByField()
    {
        // 같은 Id(예: 프리팹 Player_Paladin)를 두 타입이 가지면 필드를 실제로 가진 쪽으로.
        (DataTableTestData data, DataTableTestComponent component) = MakePair();
        var lookup = new TestLookup(("Same", (Object)data), ("Same", component)) { Group = "Paladin" };
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = DataTableApplier.Bind(new[]
        {
            Entry("Paladin", "Same", "maxHp", "5"),   // DataTableTestData 에만
            Entry("Paladin", "Same", "speed", "9"),   // DataTableTestComponent 에만
            Entry("Paladin", "Same", "nothing", "1"), // 둘 다 없음 → 오류
        }, lookup, issues);

        Assert.That(writes.Select(w => w.Target), Is.EqualTo(new Object[] { data, component }));
        Assert.That(issues.Items.Count(i => i.Kind == DataTableIssueKind.Error), Is.EqualTo(1));
    }

    [Test]
    public void GroupSheet_MemberTypeNameSheet_IsNotFound()
    {
        // 묶음으로 옮긴 타입을 옛 "타입 이름" 시트로 쓰면 오류 — 값이 두 곳에 있으면 안 된다.
        (DataTableTestData data, _) = MakePair();
        var lookup = new TestLookup(("MainSkill", (Object)data)) { Group = "Paladin" };
        var issues = new DataTableIssues();

        DataTableApplier.Bind(new[] { Entry(nameof(DataTableTestData), "MainSkill", "maxHp", "1") }, lookup, issues);

        Assert.That(issues.HasErrors, Is.True);
    }

    [Test]
    public void Merge_MovedSheets_CarryDesignerValuesIntoGroupSheet_AndDropOldSheets()
    {
        var existing = new[]
        {
            new XlsxSheet("FirstMeleeMainSkillData", new[]
            {
                new[] { "Asset", "Field", "Value" },
                new[] { "FirstMeleeMainSkillData", "cooldownTime", "4.5" },   // 기획이 고친 값
                new[] { "FirstMeleeMainSkillData", "removedField", "1" },     // 코드에서 사라짐
            }),
            new XlsxSheet("PlayerDashData", new[] { new[] { "Asset", "Field", "Value" }, new[] { "PlayerDashData", "dashSpeed", "20" } }),
        };
        var fresh = new[]
        {
            new XlsxWriteSheet("Paladin", new List<object[]>
            {
                new object[] { "Asset", "Field", "Value", "#설명" },
                new object[] { "#── FirstMeleeMainSkillData ──" },
                new object[] { "FirstMeleeMainSkillData", "cooldownTime", 8f, "" },
                new object[] { "FirstMeleeMainSkillData", "advanceSpeed", 6f, "" },
            }),
            new XlsxWriteSheet("PlayerDashData", new List<object[]>
            {
                new object[] { "Asset", "Field", "Value" }, new object[] { "PlayerDashData", "dashSpeed", 20L },
            }),
        };
        var moved = new Dictionary<string, string> { ["FirstMeleeMainSkillData"] = "Paladin" };
        var report = new List<string>();

        List<XlsxWriteSheet> merged = DataTableMerge.Merge(existing, fresh, report, moved);

        var stream = new MemoryStream();
        XlsxWriter.Write(stream, merged);
        stream.Position = 0;
        List<XlsxSheet> sheets = XlsxReader.Read(stream).ToList();

        Assert.That(sheets.Select(s => s.Name), Is.EquivalentTo(new[] { "PlayerDashData", "Paladin" }), "옛 시트는 빠진다");
        XlsxSheet paladin = sheets.Single(s => s.Name == "Paladin");
        Assert.That(paladin.Rows[2].Take(3), Is.EqualTo(new[] { "FirstMeleeMainSkillData", "cooldownTime", "4.5" }), "기획 값 이동");
        Assert.That(paladin.Cell(3, 2), Is.EqualTo("6"), "옛 시트에 없던 필드는 현재 값");
        Assert.That(report.Any(r => r.Contains("1개는 코드에 그 필드가 없어")), Is.True, "사라진 필드는 보고");
    }
}
