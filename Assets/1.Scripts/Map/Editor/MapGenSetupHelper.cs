using UnityEngine;
using UnityEditor;
using System.IO;

[InitializeOnLoad]
public static class MapGenSetupHelper
{
    static MapGenSetupHelper()
    {
        EditorApplication.delayCall += CheckAndCreateAssets;
    }

    private static void CheckAndCreateAssets()
    {
        string folderPath = "Assets/Resources/MapGen";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "MapGen");
        }

        // 1. MapGenConfigSO 생성
        string configPath = folderPath + "/MapGenConfig.asset";
        if (AssetDatabase.LoadAssetAtPath<MapGenConfigSO>(configPath) == null)
        {
            MapGenConfigSO config = ScriptableObject.CreateInstance<MapGenConfigSO>();
            AssetDatabase.CreateAsset(config, configPath);
            Debug.Log("[MapGenSetupHelper] MapGenConfig.asset 생성 완료.");
        }

        // 2. 기본 ZoneDefinitionSO 생성 (예시로 3개)
        for (int i = 1; i <= 3; i++)
        {
            string zonePath = folderPath + $"/ZoneDef_{i}.asset";
            if (AssetDatabase.LoadAssetAtPath<ZoneDefinitionSO>(zonePath) == null)
            {
                ZoneDefinitionSO zone = ScriptableObject.CreateInstance<ZoneDefinitionSO>();
                zone.ZoneID = i;
                zone.ZoneName = "Zone " + i;
                if (i == 1) zone.DefaultGrade = ZoneGrade.A_UpToTier1;
                else if (i == 2) zone.DefaultGrade = ZoneGrade.B_UpToTier2;
                else zone.DefaultGrade = ZoneGrade.Quest;

                AssetDatabase.CreateAsset(zone, zonePath);
                Debug.Log($"[MapGenSetupHelper] {zonePath} 생성 완료.");
            }
        }

        AssetDatabase.SaveAssets();
    }
}
