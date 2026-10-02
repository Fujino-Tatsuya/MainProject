using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DataTableTemplateTests
{
    private readonly List<ScriptableObject> created = new List<ScriptableObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (ScriptableObject so in created)
        {
            Object.DestroyImmediate(so);
        }

        created.Clear();
    }

    private DataTableTestData Make(string name)
    {
        var data = ScriptableObject.CreateInstance<DataTableTestData>();
        data.name = name;
        created.Add(data);
        return data;
    }

    [Test]
    public void CollectFields_NumbersOnly_SkipsIgnoredAndNonNumeric()
    {
        DataTableTestData data = Make("A");

        List<DataTableTemplate.Field> fields = DataTableTemplate.CollectFields(data);

        // bool·string·enum·참조 없음, [DataTableIgnore] 필드와 그 하위 없음, 배열 크기 없음.
        Assert.That(fields.Select(f => f.Path), Is.EqualTo(new[]
        {
            "maxHp", "moveSpeed", "smallCount", "charge.speed", "charge.damage", "phases[0]", "phases[1]", "cooldown",
            "ratio", "count", "stages[0]", "stages[1]",
        }));
        Assert.That(fields[0].Description, Is.EqualTo("최대 체력"));
    }

    [Test]
    public void SingleAsset_KeyValueSheet_RoundTripsWithZeroDifferences()
    {
        DataTableTestData data = Make("Only");
        data.moveSpeed = 0.1f; // float 표기가 번지지 않는지
        data.phases = new[] { 7, 8 };

        List<DataTableDifference> differences = ExportImportDiff(new[] { data }, out DataTableIssues issues, out List<XlsxSheet> sheets);

        Assert.That(sheets[0].Cell(0, 0), Is.EqualTo(DataTableSchema.AssetHeader));
        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(differences, Is.Empty, string.Join("\n", differences));
    }

    [Test]
    public void ManyAssets_RowSheet_RoundTripsWithZeroDifferences()
    {
        DataTableTestData a = Make("A");
        DataTableTestData b = Make("B");
        b.maxHp = 300;
        b.charge.speed = 1.75f;

        List<DataTableDifference> differences = ExportImportDiff(new[] { a, b }, out DataTableIssues issues, out List<XlsxSheet> sheets);

        Assert.That(sheets[0].Cell(0, 0), Is.EqualTo(DataTableSchema.IdHeader));
        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(differences, Is.Empty, string.Join("\n", differences));
    }

    [Test]
    public void RowSheet_ArrayLengthDiffers_KeepsEveryElement_MarksMissingWithDash()
    {
        // 23호 No23(공격 8) / No23_Solo(공격 6) 같은 경우 — 한쪽에만 있는 원소도 열로 싣고 없는 쪽은 "-".
        DataTableTestData a = Make("A");
        DataTableTestData b = Make("B");
        b.phases = new[] { 1, 2, 3 };
        var warnings = new List<string>();

        List<XlsxWriteSheet> sheets = DataTableTemplate.BuildSheets(
            new[] { (typeof(DataTableTestData), Targets(a, b)) }, warnings);

        object[] header = sheets[0].Rows[0];
        int column = Array.IndexOf(header, "phases[2]");
        Assert.That(column, Is.EqualTo(Array.IndexOf(header, "phases[1]") + 1), "phases[1] 바로 뒤");
        Assert.That(sheets[0].Rows[2][column], Is.EqualTo(DataTableSchema.NotApplicable), "A 에는 없다");
        Assert.That(sheets[0].Rows[3][column], Is.EqualTo(3L), "B 의 값");
        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public void RowSheet_ArrayLengthDiffers_RoundTripsWithZeroDifferences()
    {
        DataTableTestData a = Make("A");
        DataTableTestData b = Make("B");
        b.phases = new[] { 1, 2, 3 };

        List<DataTableDifference> differences = ExportImportDiff(new[] { a, b }, out DataTableIssues issues, out _);

        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(differences, Is.Empty, string.Join("\n", differences));
    }

    [TestCase("phases.Array.data[0]", "phases[0]")]
    [TestCase("waves.Array.data[2].hp", "waves[2].hp")]
    public void ToTablePath_IsInverseOfToPropertyPath(string propertyPath, string tablePath)
    {
        Assert.That(DataTableTemplate.ToTablePath(propertyPath), Is.EqualTo(tablePath));
        Assert.That(DataTableApplier.ToPropertyPath(tablePath), Is.EqualTo(propertyPath));
    }

    private static IReadOnlyList<(string id, Object target)> Targets(params ScriptableObject[] assets) =>
        assets.Select(a => (a.name, (Object)a)).ToList();

    [Test]
    public void CollectFields_Component_SkipsNetworkVariableAndIgnored()
    {
        var go = new GameObject("Bot");
        try
        {
            var component = go.AddComponent<DataTableTestComponent>();

            List<DataTableTemplate.Field> fields = DataTableTemplate.CollectFields(component);

            // m_Enabled 같은 Unity 내부 값·[DataTableIgnore]·NetworkVariable 안쪽 없음.
            Assert.That(fields.Select(f => f.Path), Is.EqualTo(new[] { "speed", "damage" }));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static List<DataTableDifference> ExportImportDiff(
        ScriptableObject[] assets, out DataTableIssues issues, out List<XlsxSheet> sheets)
    {
        var warnings = new List<string>();
        List<XlsxWriteSheet> written = DataTableTemplate.BuildSheets(
            new[] { (typeof(DataTableTestData), Targets(assets)) }, warnings);
        Assert.That(warnings, Is.Empty);

        var stream = new MemoryStream();
        XlsxWriter.Write(stream, written);
        stream.Position = 0;
        sheets = XlsxReader.Read(stream).ToList();

        issues = new DataTableIssues();
        List<DataTableEntry> entries = DataTableSchema.Parse("T.xlsx", sheets, issues);
        List<DataTableWrite> writes = DataTableApplier.Bind(entries, new TestLookup(assets), issues);
        Assert.That(writes, Is.Not.Empty);
        return DataTableApplier.Diff(writes);
    }
}
