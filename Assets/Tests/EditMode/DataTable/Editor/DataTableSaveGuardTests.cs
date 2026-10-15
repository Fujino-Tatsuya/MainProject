using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 테이블 Play 중 저장 차단(PLAN R8) — 테이블이 덮어쓴 에셋만 빼고 나머지는 그대로 저장한다.
public sealed class DataTableSaveGuardTests
{
    [Test]
    public void FilterSaves_RemovesGuardedPaths_KeepsOthers()
    {
        var guarded = new HashSet<string> { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab" };
        string[] paths = { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab", "Assets/0.Scenes/Other.unity" };

        string[] allowed = DataSourcePlayMode.FilterSaves(paths, guarded, out string[] blocked);

        Assert.That(allowed, Is.EqualTo(new[] { "Assets/0.Scenes/Other.unity" }));
        Assert.That(blocked, Is.EqualTo(new[] { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab" }));
    }

    [Test]
    public void FilterSaves_NothingGuarded_PassesThrough()
    {
        string[] paths = { "Assets/A.asset" };

        string[] allowed = DataSourcePlayMode.FilterSaves(paths, new HashSet<string>(), out string[] blocked);

        Assert.That(allowed, Is.SameAs(paths));
        Assert.That(blocked, Is.Empty);
    }

    // 10-09 재발: Play 가 끝난 직후(EnteredEditMode) 다른 핸들러(DevBootLauncher)의 SaveAssets 가
    // 테이블 스냅샷 복구보다 먼저 돌면, Play 가 아니라서 가드가 꺼진 채 메모리의 테이블 값이 Variant 에 저장됐다.
    // 여기서는 Play 없이 같은 조건(테이블 값 적용 + 가드 설정 + Play 아님)을 만든다.
    [TestCase(false)] // base 의 컴포넌트를 Variant 가 상속(Player_Gunner maxHp·attackDamage)
    [TestCase(true)]  // Variant 가 직접 붙인 컴포넌트(Player_Paladin FirstMeleePassive)
    public void PrefabVariant_TableValuesInMemory_SaveAssetsOutsidePlay_FileUnchanged(bool componentOnVariant)
    {
        const string basePath = "Assets/Tests/EditMode/DataTable/Editor/__DataTableGuardBaseTemp.prefab";
        const string variantPath = "Assets/Tests/EditMode/DataTable/Editor/__DataTableGuardVariantTemp.prefab";
        var go = new GameObject("Bot");
        if (!componentOnVariant)
        {
            go.AddComponent<DataTableTestComponent>();
        }

        GameObject basePrefab = PrefabUtility.SaveAsPrefabAsset(go, basePath);
        Object.DestroyImmediate(go);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        if (componentOnVariant)
        {
            instance.AddComponent<DataTableTestComponent>();
        }

        GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        Object.DestroyImmediate(instance);
        DataTableSnapshot snapshot = null;
        try
        {
            byte[] variantBefore = System.IO.File.ReadAllBytes(variantPath);
            byte[] baseBefore = System.IO.File.ReadAllBytes(basePath);
            var component = variant.GetComponent<DataTableTestComponent>();
            var issues = new DataTableIssues();
            List<DataTableWrite> writes = DataTableApplier.Bind(
                new[]
                {
                    new DataTableEntry(nameof(DataTableTestComponent), "__DataTableGuardVariantTemp", "damage", "42", "T.xlsx"),
                    new DataTableEntry(nameof(DataTableTestComponent), "__DataTableGuardVariantTemp", "speed", "8.5", "T.xlsx"),
                },
                new TestLookup(("__DataTableGuardVariantTemp", (Object)component)), issues);
            Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));

            snapshot = DataTableApplier.ApplyInMemory(writes);
            DataSourcePlayMode.SetGuardedPaths(new[] { variantPath });
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);

            AssetDatabase.SaveAssets();

            Assert.That(System.IO.File.ReadAllBytes(variantPath), Is.EqualTo(variantBefore),
                "복구 전 저장이 메모리의 테이블 값을 Variant 파일에 썼다");
            Assert.That(System.IO.File.ReadAllBytes(basePath), Is.EqualTo(baseBefore));
        }
        finally
        {
            DataSourcePlayMode.SetGuardedPaths(null);
            if (snapshot != null)
            {
                DataTableApplier.Restore(snapshot);
            }

            AssetDatabase.DeleteAsset(variantPath);
            AssetDatabase.DeleteAsset(basePath);
        }
    }
}
