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
//  🔴 왜 맵마다 저작하지 않아도 되는가(팀장 판단): 존은 **항상 수면(-3.1)보다 위**에 생성된다.
//     그래서 바닥은 맵 배치와 무관하고, 미리 한 장 깔아 두면 어떤 셔플 맵에서도 같은 느낌이 난다.
//
//  프로파일: 가운데가 얕고(구조물이 모이는 곳 = 밝게) 바깥으로 갈수록 깊다(= 어둡게).
// ----------------------------------------------------------------------------
public static class WaterBedAuthoring
{
    private const string MeshPath = "Assets/3.Materials/Water/WaterBedMesh.asset";
    private const string MaterialPath = "Assets/3.Materials/Water/MA_WaterBed.mat";
    private const string ParentName = "Abyss";
    private const string ObjectName = "WaterBed";

    // 수면 높이. 바닥 깊이는 전부 이 값 기준의 상대값이다.
    private const float WaterY = -3.1f;

    // 한 변 길이(m). AbyssWater 쿼드(330x330)와 맞춘다.
    private const float Size = 330f;

    // 격자 분할 수. 경사가 부드러우려면 어느 정도 필요하지만, 많아도 얻는 게 없다.
    private const int Segments = 96;

    [MenuItem("Tools/Rendering/Look/Rebuild Water Bed (open scene)")]
    public static void Rebuild()
    {
        // 🔴 경사는 **플레이 구역 안에서** 끝나야 한다. 반경 165m 전체에 퍼뜨리면
        //    정작 사람이 보는 40~60m 안에서는 거의 평평해서 통째로 얕은색이 된다(1차 시도의 실패).
        Mesh mesh = BuildMesh(centerDepth: 2.5f, rimDepth: 14f, gradientRadius: 70f, curve: 1.6f);

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            mesh = existing;
            EditorUtility.SetDirty(mesh);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        AssetDatabase.SaveAssets();

        bool placed = Place(mesh);
        Debug.Log($"[WaterBed] 메시 재생성 — 정점 {mesh.vertexCount}개, " +
                  $"배치={(placed ? "OK" : "실패")}. " +
                  $"수면 {WaterY} 기준 가운데 -2.5m → 반경 70m 에서 -14m.");
    }

    /// <summary>
    /// 가운데가 얕고 바깥으로 깊어지는 격자 메시. 높이는 <b>수면 기준 상대 깊이</b>다.
    /// </summary>
    /// <param name="centerDepth">가운데 수심(m).</param>
    /// <param name="rimDepth">가장 바깥 수심(m).</param>
    /// <param name="gradientRadius">이 거리(m)에서 <paramref name="rimDepth"/> 에 도달한다. 그 밖은 평평.</param>
    /// <param name="curve">1 이면 직선 경사. 크면 가운데가 넓고 평평하다가 바깥에서 급해진다.</param>
    private static Mesh BuildMesh(float centerDepth, float rimDepth, float gradientRadius, float curve)
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
                float wx = (fx - 0.5f) * Size;
                float wz = (fz - 0.5f) * Size;

                // 🔴 사각형 거리(체비쇼프)가 아니라 원형 거리로 재면 모서리가 과하게 깊어진다.
                //    반대로 사각형 거리만 쓰면 등고선이 네모나서 인공적으로 보인다.
                //    둘을 섞어 "둥근 사각형"으로 만든다.
                float rRound = Mathf.Sqrt(wx * wx + wz * wz);
                float rSquare = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                float rMeters = Mathf.Lerp(rSquare, rRound, 0.55f);
                float r = Mathf.Clamp01(rMeters / Mathf.Max(gradientRadius, 1f));

                float depth = Mathf.Lerp(centerDepth, rimDepth, Mathf.Pow(r, curve));

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

    private static bool Place(Mesh mesh)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[WaterBed] 활성 씬이 없다. 맵 씬을 연 뒤 다시 실행할 것.");
            return false;
        }

        GameObject parent = GameObject.Find(ParentName);
        if (parent == null)
        {
            Debug.LogWarning($"[WaterBed] '{ParentName}' 부모를 찾지 못했다.");
            return false;
        }

        Transform t = parent.transform.Find(ObjectName);
        GameObject go;
        if (t != null)
        {
            go = t.gameObject;
        }
        else
        {
            go = new GameObject(ObjectName);
            go.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create WaterBed");
        }

        // 🔴 메시가 이미 월드 높이를 담고 있으므로 트랜스폼은 수면 높이만 옮기고 회전·스케일은 단위다.
        //    회전을 90도 주면(쿼드 시절 습관) 경사가 옆으로 서 버린다.
        go.transform.localPosition = new Vector3(0f, WaterY, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        // 물에 완전히 가려 보이지 않으므로 그림자는 낭비다.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // 🔴 콜라이더는 붙이지 않는다. 붙으면 낙사 판정이 막히고 NavMesh 베이크에도 잡힌다.
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }
}
