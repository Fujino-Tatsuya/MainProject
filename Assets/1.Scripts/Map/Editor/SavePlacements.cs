#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// v11 벌크 저장: 현재 GeneratedMap 클론들의 (SlotID, 원본 프리팹) → 슬롯 위치 + YawSteps 일괄 저장.
// GeneratedZoneIdentity로 정확 식별(근접매칭 아님). 조합 커버리지/개별 저작은 'Zone Rotation Authoring' 창을 쓴다.
public static class SavePlacements
{
    [MenuItem("Tools/MapGen/Save Placements (clones -> slots)")]
    static void Save()
    {
        var gen = GameObject.Find(MapContentSpawner.RootName);
        if (gen == null) { Debug.LogError("[SavePlacements] GeneratedMap 없음 — 먼저 Test Generate"); return; }

        var slots = Object.FindObjectsByType<ZoneSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .ToDictionary(s => s.SlotID, s => s);
        if (slots.Count == 0) { Debug.LogError("[SavePlacements] ZoneSlot 없음 — 먼저 Wire"); return; }

        var log = new StringBuilder("[SavePlacements] 저장:\n");
        int n = 0;
        foreach (Transform c in gen.transform)
        {
            var idc = c.GetComponent<GeneratedZoneIdentity>();
            if (idc == null || idc.SourcePrefab == null)
            { log.AppendLine($"  {c.name}: GeneratedZoneIdentity/프리팹 없음, 스킵"); continue; }
            if (!slots.TryGetValue(idc.SlotID, out var slot))
            { log.AppendLine($"  {c.name}: SlotID {idc.SlotID} 슬롯 없음, 스킵"); continue; }

            int yaw = Mathf.RoundToInt(c.eulerAngles.y / 90f) % 4;
            if (yaw < 0) yaw += 4;

            Undo.RecordObject(slot, "Save Placements");
            slot.SetPlacement(idc.SourcePrefab, yaw, c.position);   // 조합별 위치+회전 저장
            EditorUtility.SetDirty(slot);
            log.AppendLine($"  Slot {idc.SlotID} × {idc.SourcePrefab.name}: pos({c.position.x:F1},{c.position.y:F1},{c.position.z:F1}) yaw={yaw * 90}");
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gen.scene);
        log.AppendLine($"완료: {n}개 저장(미저장 — 씬 저장 필요). 커버리지는 'Tools/MapGen/Zone Rotation Authoring' 창 참조.");
        Debug.Log(log.ToString());
    }
}
#endif
