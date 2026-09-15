using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ----------------------------------------------------------------------------
//  WaterBedAuthoring.cs — 물 바닥(WaterBed) 메시 생성 · 배치
//
//  🔴 왜 경사 바닥이 필요한가(2026-09-15 팀장 지적으로 확정):
//     레퍼런스(FlatKit 수영장)의 부드러운 수심 그라데이션은 **바닥이 완만하게 경사**져서 나온다.
//     평평한 바닥 + 수직 벽이면 수심이 「벽 안쪽 0 → 바깥 8m」로 한 칸 만에 튀는 **계단 함수**라,
//     셰이더의 Gradient Size 를 아무리 키워도 폭 0 인 구간에 펴는 셈이라 뚝 끊긴 채로 남는다.
//     색·노이즈로는 절대 못 고친다 — 지오메트리를 고쳐야 한다.
//
//  🔴 왜 맵마다 저작하지 않아도 되는가(팀장 판단): 존은 **항상 수면보다 위**에 생성된다.
//     그래서 바닥은 맵 배치와 무관하고, 미리 한 장 깔아 두면 어떤 셔플 맵에서도 같은 느낌이 난다.
//
//  🔴 2026-09-15 일반화: 처음엔 맵 물 한 장(수면 -3.1 · 330m)만 상수로 박아 뒀는데,
//     보스룸 물(500,-19,11 · 200m)이 같은 비주얼을 요구했다. 상수를 복제하는 대신
//     **바닥이 물을 따라간다**로 바꿨다 — 물 쿼드의 위치 · 스케일 · 높이에서 전부 끌어온다.
//     그래서 물을 옮기거나 키워도 이 메뉴만 다시 돌리면 된다.
//
//  🔴 깊이 프로파일은 **수면 기준 상대값**이라 세 물이 전부 같은 색으로 나온다.
//     절대 높이(-3.1 / -19)가 서로 달라도 되는 이유가 이것이다 — 셰이더는 수심만 본다.
//
//  프로파일: 가운데가 얕고(구조물이 모이는 곳 = 밝게) 바깥으로 갈수록 깊다(= 어둡게).
// ----------------------------------------------------------------------------
public static class WaterBedAuthoring
{
    private const string WaterMaterialPath = "Assets/3.Materials/Water/WaterDark.mat";
    private const string BedMaterialPath = "Assets/3.Materials/Water/MA_WaterBed.mat";

    // 330m 물의 메시. 이미 커밋된 애셋이라 이름을 유지한다(참조가 깨지면 씬 diff 가 커진다).
    private const string LegacyMeshPath = "Assets/3.Materials/Water/WaterBedMesh.asset";
    private const float LegacyMeshSize = 330f;
    private const string MeshFolder = "Assets/3.Materials/Water";

    // 격자 분할 수. 경사가 부드러우려면 어느 정도 필요하지만, 많아도 얻는 게 없다.
    private const int Segments = 96;

    // 🔴 경사는 **플레이 구역 안에서** 끝나야 한다. 반경 165m 전체에 퍼뜨리면
    //    정작 사람이 보는 40~60m 안에서는 거의 평평해서 통째로 얕은색이 된다(1차 시도의 실패).
    //    아래 네 숫자가 룩 튜닝의 손잡이다.
    private const float CenterDepth = 2.5f;
    private const float RimDepth = 14f;
    private const float GradientRadius = 70f;
    private const float Curve = 1.6f;

    [MenuItem("Tools/Rendering/Look/Rebuild Water Beds (open scene)")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[WaterBed] 활성 씬이 없다. 맵 씬을 연 뒤 다시 실행할 것.");
            return;
        }

        List<Renderer> waters = FindWaters();
        if (waters.Count == 0)
        {
            // 🔴 "0장 처리 성공"으로 조용히 끝나면 안 된다 — 머티리얼 경로가 바뀐 경우가 여기로 온다.
            Debug.LogWarning("[WaterBed] 이 씬에서 " + WaterMaterialPath + " 를 쓰는 렌더러를 못 찾았다. " +
                             "머티리얼 경로가 바뀌었는지 확인할 것.");
            return;
        }

        var bedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BedMaterialPath);
        if (bedMaterial == null)
        {
            Debug.LogWarning("[WaterBed] 바닥 머티리얼이 없다: " + BedMaterialPath);
            return;
        }

        var report = new List<string>();
        foreach (Renderer water in waters)
        {
            // 쿼드는 X 축으로 눕혀 놨으므로(rotation 90) 한 변 길이는 lossyScale.x 다.
            float size = Mathf.Abs(water.transform.lossyScale.x);
            if (size < 1f)
            {
                Debug.LogWarning("[WaterBed] " + water.name + " 의 스케일이 " + size + " 라 건너뛴다.");
                continue;
            }

            Mesh mesh = GetOrBuildMesh(size);
            GameObject bed = Place(water, mesh, bedMaterial);
            if (bed == null) continue;

            report.Add(water.name + " → " + bed.name +
                       " (한 변 " + size.ToString("0") + "m, 수면 " +
                       water.transform.position.y.ToString("0.##") + ")");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[WaterBed] " + report.Count + "장 재생성 — 수면 기준 가운데 -" + CenterDepth +
                  "m → 반경 " + GradientRadius + "m 에서 -" + RimDepth + "m.\n  " +
                  string.Join("\n  ", report));
    }

    /// <summary>씬 안에서 물 머티리얼을 쓰는 렌더러 전부. 이름이 아니라 <b>머티리얼</b>로 찾는다.</summary>
    private static List<Renderer> FindWaters()
    {
        var waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        var found = new List<Renderer>();
        if (waterMaterial == null) return found;

        var renderers = Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var r in renderers)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == waterMaterial) { found.Add(r); break; }
            }
        }
        return found;
    }

    /// <summary>한 변 길이별로 메시를 하나씩 둔다. 같은 크기의 물끼리는 메시를 공유한다.</summary>
    private static Mesh GetOrBuildMesh(float size)
    {
        string path = Mathf.Approximately(size, LegacyMeshSize)
            ? LegacyMeshPath
            : MeshFolder + "/WaterBedMesh_" + size.ToString("0", CultureInfo.InvariantCulture) + ".asset";

        Mesh built = BuildMesh(size);
        // 메인 오브젝트 이름이 파일명과 다르면 임포트마다 경고가 뜬다.
        built.name = System.IO.Path.GetFileNameWithoutExtension(path);

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(built, existing);
            Object.DestroyImmediate(built);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(built, path);
        return built;
    }

    /// <summary>
    /// 가운데가 얕고 바깥으로 깊어지는 격자 메시. 높이는 <b>수면 기준 상대 깊이</b>다.
    /// </summary>
    private static Mesh BuildMesh(float size)
    {
        int n = Segments + 1;
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float fx = (float)x / Segments;
                float fz = (float)z / Segments;
                float wx = (fx - 0.5f) * size;
                float wz = (fz - 0.5f) * size;

                // 🔴 사각형 거리(체비쇼프)가 아니라 원형 거리로 재면 모서리가 과하게 깊어진다.
                //    반대로 사각형 거리만 쓰면 등고선이 네모나서 인공적으로 보인다.
                //    둘을 섞어 "둥근 사각형"으로 만든다.
                float rRound = Mathf.Sqrt(wx * wx + wz * wz);
                float rSquare = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                float rMeters = Mathf.Lerp(rSquare, rRound, 0.55f);
                float r = Mathf.Clamp01(rMeters / Mathf.Max(GradientRadius, 1f));

                float depth = Mathf.Lerp(CenterDepth, RimDepth, Mathf.Pow(r, Curve));

                verts[z * n + x] = new Vector3(wx, -depth, wz);
                uvs[z * n + x] = new Vector2(fx, fz);
            }
        }

        var tris = new int[Segments * Segments * 6];
        int t = 0;
        for (int z = 0; z < Segments; z++)
        {
            for (int x = 0; x < Segments; x++)
            {
                int i = z * n + x;
                tris[t++] = i;
                tris[t++] = i + n;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n;
                tris[t++] = i + n + 1;
            }
        }

        var mesh = new Mesh { name = "WaterBedMesh" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;   // 9409 정점 — 16bit 로도 되지만 여유를 둔다
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 물과 같은 부모 밑에, 물과 같은 XZ · 높이에 바닥을 놓는다.
    /// 🔴 물 쿼드의 <b>자식으로 넣으면 안 된다</b> — 쿼드는 회전 90도 · 스케일 330 이라 그걸 전부 물려받는다.
    /// </summary>
    private static GameObject Place(Renderer water, Mesh mesh, Material bedMaterial)
    {
        Transform parent = water.transform.parent;
        // 부모가 있으면 기존 이름(WaterBed)을 유지한다. 루트 물은 이름이 겹치므로 물 이름을 붙인다.
        string bedName = parent != null ? "WaterBed" : water.name + "_Bed";

        Transform t = parent != null
            ? parent.Find(bedName)
            : FindRootByName(water.gameObject.scene, bedName);

        GameObject go;
        if (t != null)
        {
            go = t.gameObject;
        }
        else
        {
            go = new GameObject(bedName);
            if (parent != null) go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create WaterBed");
        }

        // 🔴 메시가 이미 수면 기준 상대 깊이를 담고 있으므로 트랜스폼은 수면으로 옮기기만 한다.
        //    회전을 90도 주면(쿼드 시절 습관) 경사가 옆으로 서 버린다.
        go.transform.position = water.transform.position;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // 🔴 여기에 `GetComponent<T>() ?? AddComponent<T>()` 를 쓰면 안 된다(2026-09-15에 밟음).
        //    `??` 는 참조 null 검사라 UnityEngine.Object 의 오버로드된 `==` 를 우회한다.
        //    Unity 6 에서는 없는 컴포넌트가 marshalled null 로 와서 `??` 가 "있음"으로 통과하고,
        //    AddComponent 가 건너뛰어진 채 **다음 줄**에서 MissingComponentException 이 난다.
        //    UnityEngine.Object 에는 `== null` 만 쓸 것. `?.` 도 같은 이유로 금지.
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = bedMaterial;
        // 물에 완전히 가려 보이지 않으므로 그림자는 낭비다.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // 🔴 콜라이더는 붙이지 않는다. 붙으면 낙사 판정이 막히고 NavMesh 베이크에도 잡힌다.
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        EditorUtility.SetDirty(go);
        return go;
    }

    private static Transform FindRootByName(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root.transform;
        }
        return null;
    }
}
