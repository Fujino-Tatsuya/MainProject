#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// bossroom.prefab에 BossArenaContext를 붙이고 부품 참조를 채워 저장한다.
//
// 왜 프리팹에 넣는가: 아레나 부품(착지점·BossArea·충전 기둥 4개·도착 지점)을 씬 참조로 들고 있으면
// 프리팹을 다른 씬에 인스턴스화하는 순간 전부 비고, 저작 도구가 기준점을 재생성하면 끊긴다.
// 프리팹이 자기 부품을 자기 안에서 들고 있어야 배선 없이 어디서든 동작한다.
//
// 참조는 프리팹 내부 오브젝트만 가리키므로 **절대좌표가 개입하지 않는다** — 아레나가 맵 밖
// 좌표(x≈+500)에 놓여도 전부 로컬 관계로 유지된다.
public static class BossArenaWiring
{
    const string BossRoomPath = "Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab";

    [MenuItem("Tools/Map/Authoring/Wire Boss Arena Context (bossroom)")]
    public static void WireBossArena()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BossRoomPath);

        try
        {
            var context = root.GetComponent<BossArenaContext>();
            bool added = context == null;
            if (added) context = root.AddComponent<BossArenaContext>();

            // 비어 있는 참조를 자기 계층에서 채운다(이미 채워진 값은 보존).
            context.Resolve();

            var so = new SerializedObject(context);
            var log = new StringBuilder($"[BossArenaWiring] {(added ? "컴포넌트 추가" : "기존 컴포넌트 갱신")}:\n");

            Transform landing = FindChild(root, "BossLandingPoint");
            Collider area = root.GetComponentsInChildren<Collider>(true)
                                .FirstOrDefault(c => c.CompareTag(BossArenaContext.BossAreaTag));
            List<ChargingObject> pillars = root.GetComponentsInChildren<ChargingObject>(true).ToList();
            List<Transform> arrivals = CollectArrivals(root);

            AssignObject(so, "bossLandingPoint", landing, log, "착지점");
            AssignObject(so, "bossArea", area, log, $"tag '{BossArenaContext.BossAreaTag}' 콜라이더");
            AssignList(so, "chargingPillars", pillars.Cast<Object>().ToList(), log, "충전 기둥");
            AssignList(so, "playerArrivalPoints", arrivals.Cast<Object>().ToList(), log, "도착 지점");

            so.ApplyModifiedPropertiesWithoutUndo();

            // 저장 전에 구성 문제를 먼저 알린다 — 저장 후에는 Play까지 가야 드러난다.
            ReportProblems(landing, area, pillars, arrivals, log);

            PrefabUtility.SaveAsPrefabAsset(root, BossRoomPath);
            log.AppendLine($"→ {BossRoomPath} 저장 완료.");
            log.AppendLine("씬의 bossroom 인스턴스는 프리팹 변경을 자동 반영한다. Director의 " +
                           "bossLandingPoint·chargingObjects는 비워도 되고, 채워져 있으면 그 값이 우선한다.");
            Debug.Log(log.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void ReportProblems(Transform landing, Collider area, List<ChargingObject> pillars,
                               List<Transform> arrivals, StringBuilder log)
    {
        if (landing == null)
            Debug.LogError("[BossArenaWiring] 'BossLandingPoint'가 없습니다 — 'Rebuild Boss Room Bounds'를 먼저 실행하세요.");

        if (area == null)
            Debug.LogError($"[BossArenaWiring] tag '{BossArenaContext.BossAreaTag}' 콜라이더가 없습니다 — " +
                           "'Rebuild Boss Room Bounds'가 BossArea를 만듭니다.");

        if (pillars.Count != BossArenaContext.ChargePillarCount)
            Debug.LogWarning($"[BossArenaWiring] 충전 기둥이 {pillars.Count}개입니다" +
                             $"(기대 {BossArenaContext.ChargePillarCount}).");

        foreach (ChargingObject pillar in pillars)
        {
            if (pillar.GetComponent<Collider>() == null)
                Debug.LogError($"[BossArenaWiring] 기둥 '{pillar.name}'에 Collider가 없습니다 — 피격 불가.");

            if (pillar.GetComponent<Unity.Netcode.NetworkObject>() == null)
                Debug.LogError($"[BossArenaWiring] 기둥 '{pillar.name}'에 NetworkObject가 없습니다 — " +
                               "OnNetworkSpawn이 돌지 않습니다.");
        }

        if (landing != null && area != null)
        {
            float gap = Vector3.Distance(landing.localPosition, area.transform.localPosition);
            log.AppendLine($"  착지점 localPos {landing.localPosition} / BossArea localPos " +
                           $"{area.transform.localPosition} — 간격 {gap:F3}m");
            if (gap > 0.5f)
                Debug.LogWarning($"[BossArenaWiring] 착지점과 BossArea 중심이 {gap:F2}m 떨어져 있습니다 — " +
                                 "보스가 아레나 중앙이 아닌 곳에 내려옵니다. 'Rebuild Boss Room Bounds'로 둘을 함께 재생성하세요.");
        }
    }

    static List<Transform> CollectArrivals(GameObject root)
    {
        var result = new List<Transform>();
        Transform arrivalRoot = FindChild(root, "PlayerArrivalPoints");
        if (arrivalRoot == null) return result;

        foreach (Transform child in arrivalRoot) result.Add(child);
        return result;
    }

    static Transform FindChild(GameObject root, string childName)
        => root.GetComponentsInChildren<Transform>(true)
               .FirstOrDefault(t => t != root.transform && t.name == childName);

    static void AssignObject(SerializedObject so, string path, Object value, StringBuilder log, string label)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null) return;

        p.objectReferenceValue = value;
        log.AppendLine($"  {label}: {(value != null ? value.name : "(없음)")}");
    }

    static void AssignList(SerializedObject so, string path, List<Object> values, StringBuilder log, string label)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null) return;

        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

        log.AppendLine($"  {label}: {values.Count}개" +
                       (values.Count > 0 ? $" ({string.Join(", ", values.Select(v => v.name))})" : ""));
    }
}
#endif
