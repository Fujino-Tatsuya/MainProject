using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DataTableApplierTests
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

    private static DataTableEntry Entry(string asset, string field, string value, string sheet = nameof(DataTableTestData)) =>
        new DataTableEntry(sheet, asset, field, value, $"T.xlsx › {sheet}!{asset}.{field}");

    private static List<DataTableWrite> Bind(DataTableIssues issues, IDataTableAssetLookup lookup, params DataTableEntry[] entries) =>
        DataTableApplier.Bind(entries, lookup, issues);

    [Test]
    public void Bind_ConvertsEverySupportedKind()
    {
        DataTableTestData data = Make("A");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = Bind(issues, new TestLookup(data),
            Entry("A", "maxHp", "300"),
            Entry("A", "moveSpeed", "1.25"),
            Entry("A", "canDash", "FALSE"),
            Entry("A", "title", "보스"),
            Entry("A", "mode", "run"),
            Entry("A", "smallCount", "200"),
            Entry("A", "charge.speed", "7.5"),
            Entry("A", "phases[1]", "99"),
            Entry("A", "cooldown", "0.4"));

        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        DataTableApplier.ApplyInMemory(writes);

        Assert.That(data.maxHp, Is.EqualTo(300));
        Assert.That(data.moveSpeed, Is.EqualTo(1.25f));
        Assert.That(data.canDash, Is.False);
        Assert.That(data.title, Is.EqualTo("보스"));
        Assert.That(data.mode, Is.EqualTo(DataTableTestData.Mode.Run));
        Assert.That(data.smallCount, Is.EqualTo(200));
        Assert.That(data.charge.speed, Is.EqualTo(7.5f));
        Assert.That(data.phases, Is.EqualTo(new[] { 10, 99 }));
        Assert.That(data.Cooldown, Is.EqualTo(0.4f));
    }

    [TestCase("maxHp", "1.5")]          // 정수 칸에 소수
    [TestCase("maxHp", "abc")]
    [TestCase("maxHp", "3000000000")]   // int 범위 밖
    [TestCase("smallCount", "300")]     // byte 범위 밖
    [TestCase("moveSpeed", "1,5")]      // 쉼표 소수점
    [TestCase("canDash", "yes")]
    [TestCase("mode", "Fly")]
    [TestCase("prefab", "Boss")]        // 참조는 테이블 대상 아님
    [TestCase("charge", "1")]           // 구조체 통째로는 안 됨
    [TestCase("phases[5]", "1")]        // 배열 범위 밖 — 크기를 바꾸지 않는다
    [TestCase("noSuchField", "1")]
    public void Bind_BadValueOrField_IsError(string field, string value)
    {
        DataTableTestData data = Make("A");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = Bind(issues, new TestLookup(data), Entry("A", field, value));

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(writes, Is.Empty);
    }

    [Test]
    public void Bind_UnknownSheetAssetAndDoubleWrite_AreErrors_UnmentionedAssetIsWarning()
    {
        DataTableTestData a = Make("A");
        DataTableTestData b = Make("B");
        var issues = new DataTableIssues();

        Bind(issues, new TestLookup(a, b),
            Entry("A", "maxHp", "1"),
            Entry("A", "maxHp", "2"),            // 같은 필드를 두 번
            Entry("Ghost", "maxHp", "1"),        // 없는 에셋
            Entry("X", "maxHp", "1", "NoSuchSO")); // 없는 타입

        Assert.That(issues.Items.Count(i => i.Kind == DataTableIssueKind.Error), Is.EqualTo(3));
        Assert.That(issues.Items.Count(i => i.Kind == DataTableIssueKind.Warning), Is.EqualTo(1)); // B 의 행이 없다
    }

    [Test]
    public void ApplyInMemory_ThenRestore_ReturnsOriginalValues()
    {
        DataTableTestData data = Make("A");
        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(issues, new TestLookup(data),
            Entry("A", "maxHp", "1"), Entry("A", "charge.damage", "77"), Entry("A", "phases[0]", "5"));

        DataTableSnapshot snapshot = DataTableApplier.ApplyInMemory(writes);
        Assert.That(data.maxHp, Is.EqualTo(1));

        // 스냅샷은 SessionState 에 문자열로 넣었다 꺼낸다(D1.5) — 왕복해도 복구돼야 한다.
        DataTableSnapshot roundTrip = JsonUtility.FromJson<DataTableSnapshot>(JsonUtility.ToJson(snapshot));
        int missing = DataTableApplier.Restore(roundTrip);

        Assert.That(missing, Is.Zero);
        Assert.That(data.maxHp, Is.EqualTo(100));
        Assert.That(data.charge.damage, Is.EqualTo(5));
        Assert.That(data.phases, Is.EqualTo(new[] { 10, 20 }));
    }

    [Test]
    public void ApplyInMemory_DoesNotLeaveCleanAssetDirty()
    {
        DataTableTestData data = Make("A");
        EditorUtility.ClearDirty(data);
        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(issues, new TestLookup(data), Entry("A", "maxHp", "1"));

        DataTableApplier.ApplyInMemory(writes);

        Assert.That(EditorUtility.IsDirty(data), Is.False);
    }

    [Test]
    public void Diff_ReportsOnlyChangedFields()
    {
        DataTableTestData data = Make("A");
        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(issues, new TestLookup(data),
            Entry("A", "maxHp", "100"),      // 같음
            Entry("A", "moveSpeed", "2.5"),  // 같음(실수 정확 비교)
            Entry("A", "title", "다름"),
            Entry("A", "mode", "Walk"));     // 같음

        List<DataTableDifference> differences = DataTableApplier.Diff(writes);

        Assert.That(differences.Select(d => d.Write.Source.Field), Is.EqualTo(new[] { "title" }));
        Assert.That(differences[0].CurrentValue, Is.EqualTo("base"));
    }

    [Test]
    public void ApplyToDisk_ThenRestoreFromBackup_ScriptableObjectFileIsByteIdentical()
    {
        // 빌드 훅(D1.6)이 빌드 후 git diff 0 을 지키는지 — 실제 에셋 파일로 확인한다.
        const string path = "Assets/Tests/EditMode/DataTable/Editor/__DataTableDiskTemp.asset";
        var data = ScriptableObject.CreateInstance<DataTableTestData>();
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        try
        {
            byte[] before = System.IO.File.ReadAllBytes(path);
            var issues = new DataTableIssues();
            List<DataTableWrite> writes = Bind(issues, new TestLookup(data),
                Entry(data.name, "maxHp", "777"), Entry(data.name, "phases[1]", "3"));

            DataTableDiskBackup backup = DataTableApplier.ApplyToDisk(writes);
            Assert.That(System.IO.File.ReadAllBytes(path), Is.Not.EqualTo(before), "디스크에 써져야 한다");
            Assert.That(DataTableApplier.PendingBackup(), Is.Not.Null, "빌드 중 크래시 대비 백업 목록이 디스크에 있어야 한다");

            int failed = DataTableApplier.RestoreFromBackup(backup);

            Assert.That(failed, Is.Zero);
            Assert.That(System.IO.File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(AssetDatabase.LoadAssetAtPath<DataTableTestData>(path).maxHp, Is.EqualTo(100), "메모리도 원래 값");
            Assert.That(DataTableApplier.PendingBackup(), Is.Null, "다 되돌리면 백업 목록을 지운다");
        }
        finally
        {
            AssetDatabase.DeleteAsset(path);
        }
    }

    [Test]
    public void ApplyToDisk_WithoutBackup_LeavesNoPendingBackup()
    {
        // 수동 "인스펙터 값을 테이블 값으로 덮어쓰기" — 백업이 남으면 다음 에디터 시작 때 크래시 복구가 되돌려 버린다.
        const string path = "Assets/Tests/EditMode/DataTable/Editor/__DataTableDiskTemp2.asset";
        var data = ScriptableObject.CreateInstance<DataTableTestData>();
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        try
        {
            var issues = new DataTableIssues();
            List<DataTableWrite> writes = Bind(issues, new TestLookup(data), Entry(data.name, "maxHp", "5"));

            DataTableApplier.ApplyToDisk(writes, keepBackup: false);

            Assert.That(DataTableApplier.PendingBackup(), Is.Null);
            Assert.That(System.IO.File.ReadAllText(path), Does.Contain("maxHp: 5"));
        }
        finally
        {
            AssetDatabase.DeleteAsset(path);
        }
    }

    [Test]
    public void PrefabComponent_InMemoryApplyAndRestore()
    {
        var go = new GameObject("Bot");
        var component = go.AddComponent<DataTableTestComponent>();
        try
        {
            var issues = new DataTableIssues();
            List<DataTableWrite> writes = DataTableApplier.Bind(
                new[]
                {
                    Entry("BotPrefab", "speed", "8.5", nameof(DataTableTestComponent)),
                    Entry("BotPrefab", "damage", "42", nameof(DataTableTestComponent)),
                },
                new TestLookup(("BotPrefab", (Object)component)), issues);
            Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));

            DataTableSnapshot snapshot = DataTableApplier.ApplyInMemory(writes);
            Assert.That(component.speed, Is.EqualTo(8.5f));
            Assert.That(component.damage, Is.EqualTo(42));

            DataTableApplier.Restore(snapshot);
            Assert.That(component.speed, Is.EqualTo(3f));
            Assert.That(component.damage, Is.EqualTo(10));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PrefabComponent_ApplyToDisk_ThenRestore_PrefabFileIsByteIdentical()
    {
        const string path = "Assets/Tests/EditMode/DataTable/Editor/__DataTablePrefabTemp.prefab";
        var go = new GameObject("Bot");
        go.AddComponent<DataTableTestComponent>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        try
        {
            byte[] before = System.IO.File.ReadAllBytes(path);
            var component = prefab.GetComponent<DataTableTestComponent>();
            var issues = new DataTableIssues();
            List<DataTableWrite> writes = DataTableApplier.Bind(
                new[] { Entry("__DataTablePrefabTemp", "speed", "9", nameof(DataTableTestComponent)) },
                new TestLookup(("__DataTablePrefabTemp", (Object)component)), issues);

            DataTableDiskBackup backup = DataTableApplier.ApplyToDisk(writes);
            Assert.That(System.IO.File.ReadAllText(path), Does.Contain("speed: 9"), "프리팹 파일에 써져야 한다");

            int failed = DataTableApplier.RestoreFromBackup(backup);

            Assert.That(failed, Is.Zero);
            Assert.That(System.IO.File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<DataTableTestComponent>().speed, Is.EqualTo(3f));
        }
        finally
        {
            AssetDatabase.DeleteAsset(path);
        }
    }

    [TestCase("ratio", "1.5")]       // [Range(0,1)] 위
    [TestCase("ratio", "-0.1")]      // [Range(0,1)] 아래
    [TestCase("count", "-1")]        // [Min(0)]
    [TestCase("stages[1]", "11")]    // 배열 필드의 [Range] 는 원소마다
    public void Bind_OutsideInspectorRange_IsError(string field, string value)
    {
        DataTableTestData data = Make("A");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = Bind(issues, new TestLookup(data), Entry("A", field, value));

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(writes, Is.Empty);
    }

    [Test]
    public void Bind_InsideInspectorRange_IsAccepted()
    {
        DataTableTestData data = Make("A");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = Bind(issues, new TestLookup(data),
            Entry("A", "ratio", "1"), Entry("A", "count", "0"), Entry("A", "stages[0]", "10"));

        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(writes, Has.Count.EqualTo(3));
    }

    [TestCase("phases[0]", "phases.Array.data[0]")]
    [TestCase("waves[2].enemies[10].hp", "waves.Array.data[2].enemies.Array.data[10].hp")]
    [TestCase("charge.speed", "charge.speed")]
    public void ToPropertyPath_ConvertsArrayIndices(string field, string expected)
    {
        Assert.That(DataTableApplier.ToPropertyPath(field), Is.EqualTo(expected));
    }
}
