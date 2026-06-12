using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

// 맵 시스템이 '실제로 사용하는' 아트 에셋만 추려 50.Art(SVN 영역)로 이동한다.
//  - 루트(카탈로그/Stage1/MapScene)의 의존성을 재귀 수집 → Synty/POLYBOX 에셋 + 1티어 FBX만 이동
//  - AssetDatabase.MoveAsset = GUID 보존 → 카탈로그/프리팹/씬 참조가 깨지지 않음
//  - 이동 후 남은 Assets/Synty, Assets/POLYBOX 원본 팩은 SVN에 올릴 필요 없음
public static class MapArtCollector
{
    private const string DestRoot = "Assets/50.Art/MapGen";

    private static readonly string[] Roots =
    {
        "Assets/Resources/MapGen/MapPrefabCatalog.asset",
        "Assets/2.Prefabs/Map/Stage1.prefab",
        "Assets/0.Scenes/MapScene.unity",
    };

    [MenuItem("VeyTrace/Map/Collect Used Art To 50.Art")]
    public static void Collect()
    {
        string[] deps = AssetDatabase.GetDependencies(Roots, true);

        int moved = 0, failed = 0, skipped = 0;
        var log = new StringBuilder();

        foreach (string dep in deps)
        {
            bool isPackArt = dep.StartsWith("Assets/Synty/") || dep.StartsWith("Assets/POLYBOX/");
            bool isNodeFbx = dep.StartsWith("Assets/Resources/MapGen/Prefabs/") && dep.EndsWith(".fbx");
            if (!isPackArt && !isNodeFbx) { skipped++; continue; }

            string dest = isNodeFbx
                ? $"{DestRoot}/Nodes/{Path.GetFileName(dep)}"
                : $"{DestRoot}/{dep.Substring("Assets/".Length)}"; // Synty/... 구조 유지

            EnsureFolder(Path.GetDirectoryName(dest).Replace('\\', '/'));

            string error = AssetDatabase.MoveAsset(dep, dest);
            if (string.IsNullOrEmpty(error))
            {
                moved++;
                log.AppendLine($"  {dep} -> {dest}");
            }
            else
            {
                failed++;
                Debug.LogWarning($"[ArtCollect] 이동 실패: {dep} — {error}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ArtCollect] 완료 — 이동 {moved} / 실패 {failed} (비대상 {skipped})\n{log}");
    }

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
