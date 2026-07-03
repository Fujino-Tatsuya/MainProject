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
    const string PrefabDir = "Assets/50.Art/MapGen/MapObj/Zoneprefab"; // 2026-07-03 아트가 prefab→Zoneprefab로 폴더명 변경(GUID 유지)

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

    // 파일명 → (Size, Role). ZoneL_*=Large, ZoneM_*=Medium, ZoneS_*=Small (신규 명칭).
    // 구 명칭(zone_L_* 등) 폴백 지원. typeBoss=보스맵 입구, typeStart=플레이어 스폰, 그 외=전투 풀.
    static (ZoneSize size, ZoneRole role) TagFromName(string name)
    {
        ZoneSize size = (name.StartsWith("ZoneL") || name.Contains("_L_")) ? ZoneSize.Large
                      : (name.StartsWith("ZoneM") || name.Contains("_M_")) ? ZoneSize.Medium
                      : ZoneSize.Small;
        ZoneRole role = name.Contains("typeBoss")  ? ZoneRole.BossRoom
                      : name.Contains("typeStart") ? ZoneRole.PlayerSpawn
                      : ZoneRole.Combat;
        return (size, role);
    }

    // 피벗 자동 보정: 블렌더 원점이 존 구석에 있어 슬롯 앵커 배치/회전이 어긋난다.
    // 렌더러 합산 바운즈의 XZ 중심을 새 루트 피벗으로(Y는 저작값 유지 — 다리 높이 정렬).
    // → 슬롯 좌표 = 존 "중앙", 90° 회전 슬롯도 중심 기준으로 안전하게 돈다.
    static GameObject CenterPivot(GameObject go, string name)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        var root = new GameObject(name);
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            go.transform.position -= new Vector3(b.center.x, 0f, b.center.z);
            Debug.Log($"[ZoneImporter] {name} 피벗 센터링: 풋프린트 {b.size.x:F1}x{b.size.z:F1}m, 오프셋 ({-b.center.x:F1}, {-b.center.z:F1})");
        }
        go.transform.SetParent(root.transform, true);
        return root;
    }

    // 출입구 자동 감지: 4변 가장자리 밴드에서 벽(높은 조각)의 변 방향 커버리지를 재고,
    // 4m 이상 트인 구간이 있으면 출입구로 판단. 오탐 시 프리팹 인스펙터에서 수동 보정.
    const float EdgeBand = 2.0f;      // 변에서 안쪽 스캔 폭(m)
    const float MinWallTopY = 1.2f;   // 이보다 낮은 조각은 바닥/트렌치로 무시
    const float MinGap = 8.0f;        // "완전 개방변" 최소 폭 — 4m 장식 문 구멍(딱 4.0)을 개방변으로 오인하지 않도록 8m
                                      //  (실측: 문 구멍=4.0m / 완전 개방변=17.8~37.8m. 문 변 통행은 WallCut이 담당)
    const float CornerMargin = 1.5f;  // 모서리 끝 빈틈 무시

    static void DetectOpenings(GameObject root, ZoneLayout layout)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds all = rends[0].bounds;
        foreach (var r in rends) all.Encapsulate(r.bounds);

        layout.OpenN = EdgeHasGap(rends, all, 0, out float gN);
        layout.OpenE = EdgeHasGap(rends, all, 1, out float gE);
        layout.OpenS = EdgeHasGap(rends, all, 2, out float gS);
        layout.OpenW = EdgeHasGap(rends, all, 3, out float gW);
        Debug.Log($"[ZoneImporter] {root.name} 변별 최대 빈틈(m): N={gN:F1} E={gE:F1} S={gS:F1} W={gW:F1} (기준 {MinGap})");
    }

    // 둘레 구조물(벽/코너/문 계열)만 커버리지에 포함 — Cube/Cylinder 등 내부 프롭·플랫폼 오인 방지.
    static bool IsPerimeterPiece(Transform t)
    {
        for (var c = t; c != null; c = c.parent)
        {
            string n = c.name.ToLowerInvariant();
            if (n.Contains("wall") || n.Contains("corner") || n.Contains("door")) return true;
        }
        return false;
    }

    // dir: 0=N(+Z) 1=E(+X) 2=S(-Z) 3=W(-X)
    static bool EdgeHasGap(Renderer[] rends, Bounds all, int dir, out float maxGap)
    {
        bool alongX = dir == 0 || dir == 2;                 // N/S 변은 X축 커버리지
        float lo = (alongX ? all.min.x : all.min.z) + CornerMargin;
        float hi = (alongX ? all.max.x : all.max.z) - CornerMargin;
        maxGap = 0f;
        if (hi - lo < MinGap) return false;

        var iv = new System.Collections.Generic.List<Vector2>();
        foreach (var r in rends)
        {
            Bounds b = r.bounds;
            if (b.max.y < MinWallTopY) continue;            // 바닥/낮은 조각 제외
            if (!IsPerimeterPiece(r.transform)) continue;   // 내부 프롭/플랫폼 제외
            bool inBand = dir switch
            {
                0 => b.max.z >= all.max.z - EdgeBand,
                1 => b.max.x >= all.max.x - EdgeBand,
                2 => b.min.z <= all.min.z + EdgeBand,
                _ => b.min.x <= all.min.x + EdgeBand,
            };
            if (!inBand) continue;
            iv.Add(alongX ? new Vector2(b.min.x, b.max.x) : new Vector2(b.min.z, b.max.z));
        }

        iv.Sort((a, b) => a.x.CompareTo(b.x));
        float cursor = lo;
        foreach (var s in iv)
        {
            if (s.x > cursor) maxGap = Mathf.Max(maxGap, Mathf.Min(s.x, hi) - cursor);
            cursor = Mathf.Max(cursor, s.y);
            if (cursor >= hi) break;
        }
        if (hi > cursor) maxGap = Mathf.Max(maxGap, hi - cursor);
        return maxGap >= MinGap;
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

        // 피벗 센터링(모델을 새 루트 자식으로) 후 루트에 ZoneLayout — 파일명 기반 자동 태깅
        go = CenterPivot(go, name);
        var layout = go.GetComponent<ZoneLayout>();
        if (layout == null) layout = go.AddComponent<ZoneLayout>();
        (layout.Size, layout.Role) = TagFromName(name);
        layout.Difficulty = 0;
        layout.ThemeName = "Factory";

        // 출입구(벽 트임) 자동 감지 → 배치 회전 매칭용
        DetectOpenings(go, layout);
        string open = $"{(layout.OpenN ? "N" : "")}{(layout.OpenE ? "E" : "")}{(layout.OpenS ? "S" : "")}{(layout.OpenW ? "W" : "")}";
        Debug.Log($"[ZoneImporter] {name} 출입구 감지: [{(open.Length > 0 ? open : "없음!")}]");

        if (!AssetDatabase.IsValidFolder(PrefabDir)) { Debug.LogError($"[ZoneImporter] 폴더 없음 {PrefabDir}"); Object.DestroyImmediate(go); return false; }
        string path = $"{PrefabDir}/{name}.prefab"; // 고정 경로(재실행 시 덮어쓰기)
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Debug.Log($"[ZoneImporter] {name} ✔ {layout.Size}/{layout.Role} / 렌더러 {rc} / 콜라이더 {cc} → {path}");
        return true;
    }

    // ---------------- 아트 수제 프리팹 정규화 (2026-07) ----------------
    // 신규 존 프리팹 9종은 아트가 씬에서 직접 조립해 FBX 임포트 파이프라인을 안 거침 —
    // 콜라이더/ZoneLayout(개방변)/센터 피벗이 없고 개방변 방향도 저작 규칙(N/W)과 다를 수 있다.
    // 이 툴이 프리팹을 규칙에 맞게 보정한다. 아트 재익스포트 후 재실행하면 복구(멱등).

    [MenuItem("Tools/MapGen/Normalize Zone Prefabs (아트 프리팹 정규화)")]
    public static void NormalizeAll()
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) { Debug.LogError($"[ZoneImporter] 폴더 없음 {PrefabDir}"); return; }
        int ok = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || !prefab.name.StartsWith("Zone")) continue;
            if (NormalizeOne(prefab, path)) ok++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZoneImporter] 정규화 완료: {ok}개 — 이후 Wire → Build Static Geometry V2 → Test Generate 재실행 필요");
    }

    static bool NormalizeOne(GameObject prefab, string path)
    {
        string name = prefab.name;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = name;

        // 0) 아트 씬에서 박힌 루트 오프셋 제거 — 런타임 Instantiate는 루트 위치를 슬롯으로
        //    덮어쓰므로, 측정(MeasureFloorTopY)과 런타임이 같은 기준을 보게 만든다.
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;

        // 1) MeshCollider (플레이어 물리/NavMesh)
        int cc = 0;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<MeshCollider>() == null)
            { mf.gameObject.AddComponent<MeshCollider>(); cc++; }

        // 2) 피벗 센터링 — 재실행(멱등) 대응: 이미 ZoneLayout 루트면 재랩하지 않고 자식만 재센터링
        GameObject root;
        var layout = inst.GetComponent<ZoneLayout>();
        if (layout == null)
        {
            root = CenterPivot(inst, name);
            layout = root.AddComponent<ZoneLayout>();
        }
        else
        {
            root = inst;
            RecenterChildren(root);
        }
        (layout.Size, layout.Role) = TagFromName(name);
        layout.Difficulty = 0;
        layout.ThemeName = "Factory";

        // 3) 개방변 감지 → 저작 규칙(N/W 개방)에 맞도록 내용물 90° 단위 회전 베이크
        int k = PickNormalizeYaw(root);
        if (k != 0)
        {
            foreach (Transform c in TopChildren(root))
                c.RotateAround(root.transform.position, Vector3.up, 90f * k);
            Debug.Log($"[ZoneImporter] {name} 개방변 정렬: 내용물 {90 * k}° 회전 베이크");
        }
        DetectOpenings(root, layout); // 최종 방향 기준 감지
        string open = $"{(layout.OpenN ? "N" : "")}{(layout.OpenE ? "E" : "")}{(layout.OpenS ? "S" : "")}{(layout.OpenW ? "W" : "")}";

        PrefabUtility.SaveAsPrefabAsset(root, path); // 같은 경로 덮어쓰기(GUID 유지 — 카탈로그 참조 안 깨짐)
        Object.DestroyImmediate(root);
        Debug.Log($"[ZoneImporter] {name} 정규화 ✔ {layout.Size}/{layout.Role} / 콜라이더 +{cc} / 개방변 [{(open.Length > 0 ? open : "없음!")}] → {path}");
        return true;
    }

    // 개방변이 N/W를 최대로 향하는 회전(90° 스텝) 선택 — 빈틈 "크기 합"으로 판정해
    // 4m 문 구멍(불리언로는 오인 가능)과 완전 개방변을 구분. 동점이면 최소 회전(결정적).
    // 내용물을 +90°*k 회전하면 변 d의 빈틈은 원래 (d - k) 변의 것이 된다.
    static int PickNormalizeYaw(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0;
        Bounds all = rends[0].bounds;
        foreach (var r in rends) all.Encapsulate(r.bounds);

        var gap = new float[4];
        for (int d = 0; d < 4; d++) EdgeHasGap(rends, all, d, out gap[d]);

        int best = 0; float bestScore = -1f;
        for (int k = 0; k < 4; k++)
        {
            float score = gap[(0 - k + 4) & 3] + gap[(3 - k + 4) & 3]; // 회전 후 N + W 빈틈 합
            if (score > bestScore + 0.01f) { bestScore = score; best = k; }
        }
        return best;
    }

    static void RecenterChildren(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Vector3 delta = root.transform.position - new Vector3(b.center.x, root.transform.position.y, b.center.z);
        foreach (Transform c in TopChildren(root)) c.position += delta;
    }

    static System.Collections.Generic.List<Transform> TopChildren(GameObject root)
    {
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in root.transform) list.Add(c);
        return list;
    }
}
#endif
