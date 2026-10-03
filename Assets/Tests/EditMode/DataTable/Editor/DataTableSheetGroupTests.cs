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

}
