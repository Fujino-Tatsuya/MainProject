using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 보스 등장 오케스트레이터(BossEncounterDirector) 씬 배선 도구. (승인 계획 Task 3)
//
// MapScene에는 보스를 스폰하는 주체가 없었다 — TwentyThreeArenaContext는 BossScene·PlayerBossTest
// 전용이라 정식 플로우에서는 보스가 등장하지 않았다. 이 도구가 Director를 씬 상주 NetworkObject로
// 배치하고, 보스 프리팹·착지점·충전 기둥 4개·씬 매니저 참조를 채운다.
//
// 재실행 안전: 이미 있으면 참조만 갱신한다.
public static class BossEncounterWiring
{
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    const string BossPrefabPath = "Assets/2.Prefabs/Wells&No.23/TwentyThree.prefab";
    const string DirectorObjectName = "BossEncounterDirector";
    const string LandingPointName = "BossLandingPoint";

    [MenuItem("Tools/Map/Authoring/Wire Boss Encounter (MapScene)")]
    public static void WireBossEncounter()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MapScenePath)
        {
            Debug.LogError(
                $"[BossEncounterWiring] MapScene을 먼저 열어야 한다. 현재: {scene.path}\n" +
                $"기대: {MapScenePath}");
            return;
        }

        GameObject bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        if (bossPrefab == null)
        {
            Debug.LogError($"[BossEncounterWiring] 보스 프리팹을 찾지 못했다: {BossPrefabPath}");
            return;
        }

        BossEncounterDirector director = FindOrCreateDirector();
        Transform landingPoint = FindLandingPoint();
        List<ChargingObject> pillars = FindChargePillars();
        MapSceneManager sceneManager = Object.FindFirstObjectByType<MapSceneManager>();
        BossTeleportManager teleportManager = Object.FindFirstObjectByType<BossTeleportManager>();

        if (landingPoint == null)
            Debug.LogError($"[BossEncounterWiring] '{LandingPointName}'을 씬에서 찾지 못했다 — bossroom 저작을 먼저 실행할 것.");

        if (pillars.Count == 0)
        {
            Debug.LogError(
                "[BossEncounterWiring] ChargingObject를 찾지 못했다 — " +
                "'Tools/Map/Authoring/Setup Boss Charge Pillars'를 먼저 실행할 것.");
        }

        if (teleportManager == null)
            Debug.LogError("[BossEncounterWiring] BossTeleportManager가 씬에 없다 — 도착 신호를 받을 수 없다.");

        var serialized = new SerializedObject(director);
        serialized.FindProperty("bossPrefab").objectReferenceValue = bossPrefab;
        serialized.FindProperty("bossLandingPoint").objectReferenceValue = landingPoint;
        serialized.FindProperty("mapSceneManager").objectReferenceValue = sceneManager;

        SerializedProperty list = serialized.FindProperty("chargingObjects");
        list.arraySize = pillars.Count;
        for (int i = 0; i < pillars.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = pillars[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(director);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"[BossEncounterWiring] 배선 완료 — 보스 {bossPrefab.name}, 착지점 " +
            $"{(landingPoint != null ? landingPoint.position.ToString() : "없음")}, " +
            $"기둥 {pillars.Count}개, MapSceneManager {(sceneManager != null ? "연결" : "없음")}. 씬 저장됨.");
    }

    static BossEncounterDirector FindOrCreateDirector()
    {
        BossEncounterDirector existing = Object.FindFirstObjectByType<BossEncounterDirector>();
        if (existing != null)
        {
            EnsureNetworkObject(existing.gameObject);
            return existing;
        }

        var go = new GameObject(DirectorObjectName);
        EnsureNetworkObject(go);
        BossEncounterDirector created = go.AddComponent<BossEncounterDirector>();
        Debug.Log($"[BossEncounterWiring] {DirectorObjectName} 생성 — 씬 상주 NetworkObject.");
        return created;
    }

    static void EnsureNetworkObject(GameObject go)
    {
        if (go.GetComponent<NetworkObject>() == null)
            go.AddComponent<NetworkObject>();
    }

    static Transform FindLandingPoint()
    {
        foreach (Transform transform in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (transform.name == LandingPointName)
                return transform;
        }

        return null;
    }

    /// <summary>
    /// 씬의 충전 기둥을 수집한다. 인원수별 활성 순서가 실행마다 흔들리지 않도록 위치로 정렬한다.
    /// </summary>
    static List<ChargingObject> FindChargePillars()
    {
        var pillars = new List<ChargingObject>(
            Object.FindObjectsByType<ChargingObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        pillars.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;
            int compareZ = pa.z.CompareTo(pb.z);
            return compareZ != 0 ? compareZ : pa.x.CompareTo(pb.x);
        });

        return pillars;
    }
}
