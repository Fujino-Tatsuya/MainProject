using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 특성 디버그 랩(할 일 135)이 DebugTraitLab 씬 밖에 새지 않는다는 증명 — <see cref="DebugInterruptLabIsolationTests"/> 와 같은 방식
/// (씬·프리팹은 열지 않고 YAML 텍스트에서 패널 스크립트 guid 를 찾는다).
/// 실행은 Dev Boot 드롭다운(Play 동안만 빌드 목록에 넣고 뺀다) — 빌드 목록에는 없어야 한다.
/// </summary>
public sealed class DebugTraitLabIsolationTests
{
    const string LabScenePath = "Assets/0.Scenes/Debug/DebugTraitLab.unity";

    static string PanelGuid()
    {
        string path = AssetDatabase.FindAssets($"t:MonoScript {nameof(DebugTraitLab)}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Single(p => Path.GetFileNameWithoutExtension(p) == nameof(DebugTraitLab));
        return AssetDatabase.AssetPathToGUID(path);
    }

    static bool UsesPanel(string assetPath, string guid) => File.ReadAllText(assetPath).Contains("guid: " + guid);

    static string Normalize(string path) => path.Replace('\\', '/');

    [Test]
    public void 랩_씬이_있고_패널이_붙어_있다()
    {
        Assert.IsTrue(File.Exists(LabScenePath));
        Assert.IsTrue(UsesPanel(LabScenePath, PanelGuid()));
    }

    [Test]
    public void 랩_씬은_Dev_Boot_드롭다운에_보인다()
    {
        CollectionAssert.Contains(DevBootSceneCatalog.GetAllScenePaths().Select(Normalize).ToArray(), LabScenePath);
    }

    [Test]
    public void 빌드_목록에_랩_씬이_없다()
    {
        string[] buildScenes = EditorBuildSettings.scenes.Select(s => Normalize(s.path)).ToArray();
        CollectionAssert.DoesNotContain(buildScenes, LabScenePath);
        Assert.IsFalse(buildScenes.Any(p => p.Contains("DebugTraitLab")));
    }

    [Test]
    public void 랩_씬_밖의_모든_씬과_프리팹에_패널이_없다()
    {
        string guid = PanelGuid();
        string[] leaks = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories))
            .Select(Normalize)
            .Where(p => p != LabScenePath && UsesPanel(p, guid))
            .ToArray();
        CollectionAssert.IsEmpty(leaks);
    }

    [Test]
    public void 랩_씬에_특성_디렉터가_있다()
    {
        // TraitDirector.prefab 인스턴스(4.MapScene 과 같은 프리팹) — 없으면 패널이 아무것도 못 한다.
        string directorPrefabGuid = AssetDatabase.AssetPathToGUID("Assets/2.Prefabs/Managers/TraitDirector.prefab");
        Assert.IsNotEmpty(directorPrefabGuid);
        Assert.IsTrue(UsesPanel(LabScenePath, directorPrefabGuid));
    }
}
