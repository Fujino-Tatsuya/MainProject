#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// 블렌더 통짜 존 FBX → 존 프리팹 헬퍼 (v2 — Tex_zone 개별 텍스처).
//  - Mesh_zone 폴더 일괄 임포트 메뉴 + 선택 FBX 단건 메뉴.
//  - 머티리얼: 블렌더 머티리얼 "이름" 기반 슬롯 교체(피스 오브젝트명은 Cube/Dupli 등 비신뢰).
//    9종 FBX 공통 세트: floor/floor02, wall/wall01, wall_window, prop_01, convayotbelt(컨베이어), TEMP_QT.002(임시→벽 폴백)
//  - MeshCollider 부여 + ZoneLayout 컴포넌트(파일명으로 Size/Role 자동 태깅) → 프리팹 저장.
//  - 원점: 존 피벗은 블렌더 저작 기준(코너) 그대로 사용 — 슬롯 위치에서 육안 보정.
public static class MapZoneImporter
{
    const string FbxDir = "Assets/50.Art/mesh/Mesh_zone";
    const string TexDir = "Assets/50.Art/texture/Tex_zone";
    const string PrefabDir = "Assets/50.Art/MapGen/MapObj/ZoneLayout/Prefabs";

    // (머티리얼 에셋명, 베이스컬러, 노멀[없으면 null])
    static readonly (string mat, string baseTex, string normalTex)[] MatDefs =
    {
        ("zone_floor_basic",        "floor_basic_basecolor.png",        "floor_basic_normal01.png"),
        ("zone_floor_conveyorbelt", "floor_conveyorbelt_basecolor.png", "floor_convayorbelt_normal.png"),
        ("zone_wall_basic",         "wall_basic_basecolor.png",         "wall_basic_normal.png"),
        ("zone_wall_window",        "wall_window_basecolor.png",        "wall_window_normal.png"),
        ("zone_prop01",             "prop01_basecolor.png",             null),
        ("zone_prop02",             "prop02_basecolor.png",             null),
    };

    // 블렌더 머티리얼 이름 → 머티리얼 에셋명. 오타 변형(convayot/conveyor/conveyer) 포괄.
    static string MapMatName(string blenderMat)
    {
        string n = blenderMat.ToLowerInvariant();
        if (n.Contains("conv")) return "zone_floor_conveyorbelt";
        if (n.Contains("floor")) return "zone_floor_basic";
        if (n.Contains("window")) return "zone_wall_window";
        if (n.Contains("prop_02") || n.Contains("prop02")) return "zone_prop02";
        if (n.Contains("prop")) return "zone_prop01";
        return "zone_wall_basic"; // wall/wall01/TEMP_* 폴백
    }

    // 노멀맵 텍스처 타입 보정 + URP Lit 머티리얼 생성/갱신
    static Material EnsureMat(string matName, string baseTex, string normalTex)
    {
        string path = $"{TexDir}/{matName}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = matName };
            AssetDatabase.CreateAsset(m, path);
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{baseTex}");
        if (tex == null) Debug.LogWarning($"[ZoneImporter] 베이스컬러 없음: {TexDir}/{baseTex}");
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);

        if (!string.IsNullOrEmpty(normalTex))
        {
            string np = $"{TexDir}/{normalTex}";
            // 노멀맵 임포트 타입 보정(안 하면 보라색/경고)
            if (AssetImporter.GetAtPath(np) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
            var ntex = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
            if (ntex == null) Debug.LogWarning($"[ZoneImporter] 노멀맵 없음: {np}");
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", ntex); m.EnableKeyword("_NORMALMAP"); }
        }

        EditorUtility.SetDirty(m);
        return m;
    }

    // 파일명 → (Size, Role). zone_L_*=Large, zone_M_*=Medium, zone_S_*=Small,
    // typeBoss=보스방(역할 고정), typeStart=플레이어 스폰(역할 고정), 그 외=전투 풀.
    static (ZoneSize size, ZoneRole role) TagFromName(string name)
    {
        ZoneSize size = name.Contains("_L_") ? ZoneSize.Large
                      : name.Contains("_M_") ? ZoneSize.Medium
                      : ZoneSize.Small;
        ZoneRole role = name.Contains("typeBoss") ? ZoneRole.BossRoom
                      : name.Contains("typeStart") ? ZoneRole.PlayerSpawn
                      : ZoneRole.Combat;
        return (size, role);
    }

    [MenuItem("Tools/MapGen/Import All Zone FBX (Mesh_zone)")]
    static void ImportAll()
    {
        if (!AssetDatabase.IsValidFolder(FbxDir)) { Debug.LogError($"[ZoneImporter] 폴더 없음 {FbxDir}"); return; }
        int ok = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { FbxDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (ImportOne(path)) ok++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZoneImporter] 일괄 임포트 완료: {ok}개 → {PrefabDir}");
    }

    [MenuItem("Tools/MapGen/Zone Prefab From Selected FBX")]
    static void MakeFromSelected()
    {
        var sel = Selection.activeObject as GameObject;
        string srcPath = sel != null ? AssetDatabase.GetAssetPath(sel) : null;
        if (string.IsNullOrEmpty(srcPath)) { Debug.LogError("[ZoneImporter] 프로젝트뷰에서 FBX 선택 필요"); return; }
        if (ImportOne(srcPath)) AssetDatabase.SaveAssets();
    }

    static bool ImportOne(string srcPath)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
        if (src == null) { Debug.LogError($"[ZoneImporter] 모델 못 찾음: {srcPath}"); return false; }

        string name = Path.GetFileNameWithoutExtension(srcPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = name;
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // 머티리얼 캐시 준비
        var mats = new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var d in MatDefs) mats[d.mat] = EnsureMat(d.mat, d.baseTex, d.normalTex);

        // 슬롯별 원본(블렌더) 머티리얼 이름 → 프로젝트 머티리얼 교체
        int rc = 0;
        foreach (var rend in go.GetComponentsInChildren<Renderer>())
        {
            var shared = rend.sharedMaterials;
            var arr = new Material[shared.Length];
            for (int i = 0; i < shared.Length; i++)
            {
                string srcName = shared[i] != null ? shared[i].name : "";
                arr[i] = mats[MapMatName(srcName)];
            }
            rend.sharedMaterials = arr;
            rc++;
        }

        // MeshCollider 부여(플레이어 물리/NavMesh용)
        int cc = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<MeshCollider>() == null)
            { mf.gameObject.AddComponent<MeshCollider>(); cc++; }

        // ZoneLayout — 파일명 기반 자동 태깅
        var layout = go.GetComponent<ZoneLayout>();
        if (layout == null) layout = go.AddComponent<ZoneLayout>();
        (layout.Size, layout.Role) = TagFromName(name);
        layout.Difficulty = 0;
        layout.ThemeName = "Factory";

        if (!AssetDatabase.IsValidFolder(PrefabDir)) { Debug.LogError($"[ZoneImporter] 폴더 없음 {PrefabDir}"); Object.DestroyImmediate(go); return false; }
        string path = $"{PrefabDir}/{name}.prefab"; // 고정 경로(재실행 시 덮어쓰기)
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Debug.Log($"[ZoneImporter] {name} ✔ {layout.Size}/{layout.Role} / 렌더러 {rc} / 콜라이더 {cc} → {path}");
        return true;
    }
}
#endif
