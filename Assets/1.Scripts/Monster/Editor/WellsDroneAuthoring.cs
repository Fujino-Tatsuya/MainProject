#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 웰즈 자폭 드론 — 재질 + 래퍼 프리팹 만들기 (2026-10-16 팀장: 드론이 회색 박스).
//
// 🔴 원인: DRONE.fbx(SVN r352)에는 머티리얼이 **아예 없다**(FBX 안 Material 노드 0). 그래서 임포터가 메시에
//    URP 기본 Lit.mat(회색)을 직접 꽂고, 임포터 "머티리얼 연결(remap)" 에 걸 슬롯도 생기지 않는다.
//    머티리얼 에셋도 git·SVN 어디에도 커밋된 적이 없다 — 예전에 보였다면 로컬에서만 연결됐던 것이다.
//
// → 그래서 FBX 대신 **래퍼 프리팹**(빈 루트 + FBX 모델 자식, 팀 할 일 40 규약)에 재질을 입히고
//   WellsDroneData.droneModel 을 그 프리팹으로 바꾼다. SVN 아트(FBX·텍스처)는 임포트 설정만 고친다.
//
// 재질 = URP Lit · 베이스 컬러 + 노멀만(팀장 10-16: 드론은 찰나라 이머시브·오클루전 불필요).
// 몬스터·23호 재질이 전부 URP Lit 이라 톤도 맞다(Flat Kit 은 몬스터에 안 쓴다).
// 다시 눌러도 같은 결과(재질·프리팹은 있으면 갱신, guid 유지).
public static class WellsDroneAuthoring
{
    const string FbxPath = "Assets/50.Art/Char/Drone/Models/DRONE.fbx";
    const string BaseMapPath = "Assets/50.Art/Char/Drone/Textures/DRONE_BaseColor.png";
    const string NormalPath = "Assets/50.Art/Char/Drone/Textures/DRONE_Normal.png";
    const string MaterialPath = "Assets/50.Art/Char/Drone/Materials/DRONE_Mat.mat";   // SVN(아트 옆)
    const string PrefabPath = "Assets/2.Prefabs/Monster/Boss/WellsDrone.prefab";      // git
    const string DataPath = "Assets/2.Prefabs/Monster/Data/WellsDroneData.asset";

    [MenuItem("Tools/Boss/웰즈 드론 — 재질·래퍼 프리팹 만들기")]
    public static void Build()
    {
        // 1) 노멀 텍스처가 일반 텍스처(sRGB)로 들어와 있었다 — 노멀 맵으로.
        if (AssetImporter.GetAtPath(NormalPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
        {
            ni.textureType = TextureImporterType.NormalMap;
            ni.SaveAndReimport();
            Debug.Log($"[드론재질] 노멀 텍스처 임포트 = NormalMap: {NormalPath}");
        }

        // 2) 재질
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseMapPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (baseMap == null || normal == null) { Debug.LogError("[드론재질] 텍스처를 못 찾았다 — SVN 업데이트 확인"); return; }

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) { Debug.LogError("[드론재질] URP Lit 셰이더 없음"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            string dir = System.IO.Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/50.Art/Char/Drone", "Materials");
            mat = new Material(lit) { name = "DRONE_Mat" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.shader = lit;
        mat.SetTexture("_BaseMap", baseMap);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(mat);

        // 3) 래퍼 프리팹 — 빈 루트 + 모델 자식. 생성 코드는 GetComponentInChildren<Animator> 로 자식 애니메이터를 찾는다.
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError($"[드론재질] FBX 없음: {FbxPath}"); return; }

        var root = new GameObject("WellsDrone");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
            model.name = "Model";
            int n = 0;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                n++;
            }
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (!ok || saved == null) { Debug.LogError($"[드론재질] 프리팹 저장 실패: {PrefabPath}"); return; }
            Debug.Log($"[드론재질] 래퍼 프리팹 저장 — 렌더러 {n}개에 {MaterialPath}: {PrefabPath}");

            // 4) 데이터 연결
            var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DataPath);
            if (data == null) { Debug.LogError($"[드론재질] 드론 데이터 없음: {DataPath}"); return; }
            var so = new SerializedObject(data);
            SerializedProperty p = so.FindProperty("droneModel");
            if (p == null) { Debug.LogError("[드론재질] WellsDroneData.droneModel 칸 없음"); return; }
            p.objectReferenceValue = saved;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[드론재질] 완료 — 재질(SVN 커밋 필요)·프리팹·데이터(git) · 노멀 임포트 설정(SVN .meta)");
    }
}
#endif
