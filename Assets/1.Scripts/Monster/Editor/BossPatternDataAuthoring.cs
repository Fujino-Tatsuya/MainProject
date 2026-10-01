using UnityEditor;
using UnityEngine;

/// <summary>
/// 전기 장판 · 자폭 드론 데이터 에셋을 만들고 23호 BossDataSO(No23 · No23_Solo)에 연결한다. 재실행 안전 —
/// 이미 있는 에셋·이미 채워진 칸은 건드리지 않는다. PLAN-boss-electric-drone.
/// </summary>
static class BossPatternDataAuthoring
{
    const string Dir = "Assets/2.Prefabs/Monster/Data";
    const string FloorPath = Dir + "/BossElectricFloorData.asset";
    const string DronePath = Dir + "/WellsDroneData.asset";
    const string DroneModelPath = "Assets/50.Art/Char/Drone/Models/DRONE.fbx";
    static readonly string[] BossData = { Dir + "/No23.asset", Dir + "/No23_Solo.asset" };

    [MenuItem("Tools/Boss/전기 장판·자폭 드론 데이터 만들기·연결")]
    static void Run()
    {
        var floor = LoadOrCreate<BossElectricFloorDataSO>(FloorPath);
        var drone = LoadOrCreate<WellsDroneDataSO>(DronePath);

        if (drone.droneModel == null)
        {
            drone.droneModel = AssetDatabase.LoadAssetAtPath<GameObject>(DroneModelPath);
            if (drone.droneModel == null) Debug.LogWarning($"[BossPatternData] 드론 모델이 없다(SVN 확인): {DroneModelPath}");
            EditorUtility.SetDirty(drone);
        }

        foreach (string path in BossData)
        {
            var so = AssetDatabase.LoadAssetAtPath<BossDataSO>(path);
            if (so == null) { Debug.LogWarning($"[BossPatternData] 없음: {path}"); continue; }
            bool dirty = false;
            if (so.electricFloor == null) { so.electricFloor = floor; dirty = true; }
            if (so.wellsDrone == null) { so.wellsDrone = drone; dirty = true; }
            if (dirty) EditorUtility.SetDirty(so);
            Debug.Log($"[BossPatternData] {path} — 장판 {(so.electricFloor ? so.electricFloor.name : "null")} · 드론 {(so.wellsDrone ? so.wellsDrone.name : "null")}{(dirty ? " (연결함)" : "")}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[BossPatternData] 완료 — 드론 모델 {(drone.droneModel ? drone.droneModel.name : "없음")}");
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        Debug.Log($"[BossPatternData] 생성: {path}");
        return a;
    }
}
