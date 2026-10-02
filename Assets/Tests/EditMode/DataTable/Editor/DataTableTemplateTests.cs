using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class DataTableTemplateTests
{
    private readonly List<ScriptableObject> created = new List<ScriptableObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (ScriptableObject so in created)
        {
            UnityEngine.Object.DestroyImmediate(so);
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
    public void RowSheet_ArrayLengthDiffers_DropsUncommonElementsWithWarning()
    {
        DataTableTestData a = Make("A");
        DataTableTestData b = Make("B");
        b.phases = new[] { 1, 2, 3 };
        var warnings = new List<string>();

        List<XlsxWriteSheet> sheets = DataTableTemplate.BuildSheets(
            new[] { (typeof(DataTableTestData), (IReadOnlyList<ScriptableObject>)new ScriptableObject[] { a, b }) }, warnings);

        Assert.That(sheets[0].Rows[0], Does.Not.Contain("phases[2]"));
        Assert.That(warnings, Has.Count.EqualTo(1).And.Some.Contains("phases[2]"));
    }

    [TestCase("phases.Array.data[0]", "phases[0]")]
    [TestCase("waves.Array.data[2].hp", "waves[2].hp")]
    public void ToTablePath_IsInverseOfToPropertyPath(string propertyPath, string tablePath)
    {
        Assert.That(DataTableTemplate.ToTablePath(propertyPath), Is.EqualTo(tablePath));
        Assert.That(DataTableApplier.ToPropertyPath(tablePath), Is.EqualTo(propertyPath));
    }

    private static List<DataTableDifference> ExportImportDiff(
        ScriptableObject[] assets, out DataTableIssues issues, out List<XlsxSheet> sheets)
    {
        var warnings = new List<string>();
        List<XlsxWriteSheet> written = DataTableTemplate.BuildSheets(
            new[] { (typeof(DataTableTestData), (IReadOnlyList<ScriptableObject>)assets) }, warnings);
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
