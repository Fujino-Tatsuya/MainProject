using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// 맵 생성 v2 셋업 보조 (에디터 메뉴):
//  ① 기존 ZoneVolume → ZoneSlot 앵커 자동 생성 (위치/Footprint/역할후보 복사, Size는 등급 추정).
//  ② 프로젝트의 ZoneLayout 프리팹 전체 스캔 → ZoneLayoutCatalog.asset 자동 구성.
public static class MapSlotSetup
{
    // ZoneWiring.CatalogPath 와 동일해야 함(이중 카탈로그 방지).
    private const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";

    [MenuItem("VeyTrace/Map/v2 ① Create ZoneSlots from ZoneVolumes")]
    public static void CreateZoneSlotsFromVolumes()
    {
        var volumes = Object.FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None);
        if (volumes.Length == 0)
        {
            Debug.LogWarning("[MapSlotSetup] 씬에 ZoneVolume이 없음 — 먼저 존 볼륨을 배치하거나 ZoneSlot을 수동 생성하세요.");
            return;
        }

        // 위치 결정성 위해 이름/위치 순으로 정렬 후 SlotID 부여
        System.Array.Sort(volumes, (a, b) =>
        {
            int c = Mathf.RoundToInt(a.transform.position.z * 10f).CompareTo(Mathf.RoundToInt(b.transform.position.z * 10f));
            if (c != 0) return c;
            return Mathf.RoundToInt(a.transform.position.x * 10f).CompareTo(Mathf.RoundToInt(b.transform.position.x * 10f));
        });

        Transform parent = GameObject.Find("ZoneSlots")?.transform;
        if (parent == null) parent = new GameObject("ZoneSlots").transform;

        int created = 0, id = 1;
        foreach (var vol in volumes)
        {
            var z = vol.Zone;
            string nm = z != null ? z.ZoneName : "Zone";
            var go = new GameObject($"ZoneSlot_{id}_{nm}");
            Undo.RegisterCreatedObjectUndo(go, "Create ZoneSlot");
            go.transform.SetParent(parent, true);
            go.transform.position = vol.transform.position;
            go.transform.rotation = vol.transform.rotation;

            var slot = go.AddComponent<ZoneSlot>();
            slot.SlotID = id++;
            slot.Footprint = new Vector2(vol.Size.x, vol.Size.z);

            // Size 추정(수동 보정 필요): 보스후보=소형, A등급=대형, 그 외=중형
            if (z != null && z.IsBossGateCandidate) slot.Size = ZoneSize.Small;
            else if (z != null && z.DefaultGrade == ZoneGrade.A_UpToTier1) slot.Size = ZoneSize.Large;
            else slot.Size = ZoneSize.Medium;

            if (z != null)
            {
                slot.IsQuestCandidate = z.IsQuestZoneCandidate;
                slot.IsBossCandidate = z.IsBossGateCandidate;
                slot.IsSpawnCandidate = z.IsPlayerSpawnCandidate;
            }
            created++;
        }

        EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        Debug.Log($"[MapSlotSetup] ZoneSlot {created}개 생성(ZoneVolume 기반). " +
                  "⚠️ 각 슬롯의 Size(대/중/소)는 추정값 — 인스펙터에서 확인·보정하세요. (기존 ZoneVolume은 지오메트리 저작용으로 남겨두거나 비활성)");
    }

    [MenuItem("VeyTrace/Map/v2 ② Build ZoneLayout Catalog from Prefabs")]
    public static void BuildCatalogFromPrefabs()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);
        if (catalog == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/MapGen")) AssetDatabase.CreateFolder("Assets/Resources", "MapGen");
            catalog = ScriptableObject.CreateInstance<ZoneLayoutCatalogSO>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            Debug.Log($"[MapSlotSetup] ZoneLayoutCatalog.asset 생성: {CatalogPath}");
        }

        catalog.Entries = new List<ZoneLayoutCatalogSO.Entry>();
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var layout = go != null ? go.GetComponent<ZoneLayout>() : null;
            if (layout == null) continue;
            catalog.Entries.Add(new ZoneLayoutCatalogSO.Entry
            {
                Prefab = go,
                Size = layout.Size,
                Role = layout.Role,
                Difficulty = layout.Difficulty,
            });
            n++;
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapSlotSetup] ZoneLayout 프리팹 {n}개를 카탈로그에 등록. (프리팹의 Size/Role/Difficulty 태그 기준 — 프리팹 추가/수정 후 재실행)");
    }
}
