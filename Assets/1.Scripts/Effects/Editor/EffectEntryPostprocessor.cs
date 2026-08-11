using UnityEditor;
using UnityEngine;

/// <summary>
/// 파티클 프리팹이 바뀌면 그걸 참조하는 <see cref="EffectEntry"/>의 자동 수명을 다시 계산한다.
///
/// <see cref="EffectEntry"/>의 <c>OnValidate</c>는 <b>그 엔트리 에셋을 건드릴 때만</b> 돈다.
/// 프리팹의 startLifetime을 튜닝하는 건 엔트리를 건드리는 게 아니라서, 이게 없으면
/// 계산값이 옛날 값인 채 남아 이펙트가 조용히 잘린다 — 손으로 적는 것과 같은 실패다.
/// </summary>
public class EffectEntryPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
                                               string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (!ContainsPrefab(importedAssets)) return;

        string[] guids = AssetDatabase.FindAssets("t:EffectEntry");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var entry = AssetDatabase.LoadAssetAtPath<EffectEntry>(path);
            if (entry == null) continue;

            if (!entry.RecomputeLifetimes()) continue;

            EditorUtility.SetDirty(entry);
            Debug.Log($"[Effect] '{entry.name}'의 자동 수명을 프리팹 변경에 맞춰 갱신했다 " +
                      $"(duration {entry.ResolvedDuration:F2}s).", entry);
        }
    }

    private static bool ContainsPrefab(string[] paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i].EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
