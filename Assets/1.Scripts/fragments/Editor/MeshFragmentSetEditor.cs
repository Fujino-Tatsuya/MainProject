using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="MeshFragmentSet"/>에 Bake 버튼을 붙이고, 실제로 굽는 일을 한다.
///
/// 굽기는 삼각형 하나를 <b>삼각기둥</b> 하나로 바꾸는 작업이다. 면마다 정점을 따로 두어
/// (뚜껑 2×3 + 옆면 3×2×3 = 삼각형 8개 · <b>정점 24개</b>)
/// RecalculateNormals가 면별 평평한 음영을 내도록 하고, 옆면은 감는 방향을 계산해 맞춘다 —
/// 뒤집히면 컬링돼서 파편에 구멍이 뚫린 것처럼 보인다.
/// </summary>
[CustomEditor(typeof(MeshFragmentSet))]
public class MeshFragmentSetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MeshFragmentSet set = (MeshFragmentSet)target;

        EditorGUILayout.Space(10);

        if (set.sourceMesh == null)
        {
            EditorGUILayout.HelpBox("Source Mesh를 지정하세요.", MessageType.Info);
            return;
        }

        if (set.sourceMesh.isReadable == false)
        {
            EditorGUILayout.HelpBox(
                $"'{set.sourceMesh.name}'의 Read/Write Enabled가 꺼져 있어 정점을 읽을 수 없습니다.\n" +
                "모델 임포트 설정에서 켜고 Bake 하세요. 구운 뒤에는 다시 꺼도 됩니다 " +
                "— 런타임에는 구워진 파편만 쓰므로 원본 정점이 필요 없습니다.",
                MessageType.Error);
            return;
        }

        int triangleCount = set.sourceMesh.triangles.Length / 3;
        int willBake = set.maxFragments > 0 ? Mathf.Min(set.maxFragments, triangleCount) : triangleCount;

        EditorGUILayout.HelpBox(
            $"원본 삼각형 {triangleCount}개 → 파편 {willBake}개를 굽습니다.\n" +
            "파편 하나당 Rigidbody 하나가 되므로, 100개를 넘기면 터질 때 물리 부담이 큽니다.",
            willBake > 100 ? MessageType.Warning : MessageType.Info);

        if (GUILayout.Button(set.IsBaked ? "다시 Bake (기존 파편 교체)" : "Bake", GUILayout.Height(30)))
        {
            Bake(set);
        }

        if (set.IsBaked == true)
        {
            EditorGUILayout.LabelField($"구워진 파편: {set.fragments.Length}개");

            EditorGUILayout.Space(6);

            if (set.fragmentMaterial == null)
            {
                EditorGUILayout.HelpBox(
                    "Fragment Material이 비어 있어 버스트 프리팹을 구울 수 없습니다.", MessageType.Warning);
            }
            else if (GUILayout.Button(
                set.burstPrefab != null ? "버스트 프리팹 다시 굽기" : "버스트 프리팹 굽기",
                GUILayout.Height(24)))
            {
                BakeBurstPrefab(set);
            }

            if (set.burstPrefab != null)
                EditorGUILayout.ObjectField("버스트 프리팹", set.burstPrefab, typeof(GameObject), false);
        }
    }

    /// <summary>
    /// 파편 N개를 자식으로 가진 <b>버스트 프리팹</b>을 굽는다. 이게 EffectEntry의 파트가 되어
    /// EffectManager 풀에 올라간다 — 파편 풀링이 "배치된 상자 수"가 아니라
    /// "동시 폭발 수"에 비례하게 되는 지점이다.
    ///
    /// ⚠️ <b>콜라이더를 붙이지 않는다.</b> 파편은 피어마다 다른 난수로 흩어지므로, 충돌시키면
    /// 플레이어가 밀리는 결과가 클라마다 갈려 디싱크가 된다. 파편은 연출이지 게임플레이가 아니다.
    /// </summary>
    static void BakeBurstPrefab(MeshFragmentSet set)
    {
        string setPath = AssetDatabase.GetAssetPath(set);
        if (string.IsNullOrEmpty(setPath))
        {
            Debug.LogError("[MeshFragmentSet] 프로젝트에 저장된 에셋에만 Bake 할 수 있습니다.");
            return;
        }

        var root = new GameObject($"FX_{set.name}_Burst");
        var burst = root.AddComponent<FragmentBurstEffect>();

        var pieces = new Transform[set.fragments.Length];

        for (int i = 0; i < set.fragments.Length; i++)
        {
            MeshFragmentSet.Fragment fragment = set.fragments[i];
            if (fragment == null || fragment.mesh == null) continue;

            var go = new GameObject($"fragment_{i:D3}");
            go.transform.SetParent(root.transform, false);

            // 원본 메시에서의 제자리. 여기가 파편의 "rest pose"이고,
            // FragmentBurstEffect가 반납할 때마다 이 좌표로 되돌린다.
            go.transform.localPosition = fragment.center;

            go.AddComponent<MeshFilter>().sharedMesh = fragment.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = set.fragmentMaterial;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = set.fragmentMass;
            rb.linearDamping = set.linearDamping;
            rb.angularDamping = set.angularDamping;
            rb.isKinematic = true;   // 대출 전까지 잠들어 있는다

            pieces[i] = go.transform;
        }

        // 자동 수집에 맡기지 않고 명시적으로 넣어둔다 — 순서가 곧 rest pose 배열의 순서다.
        var so = new SerializedObject(burst);
        SerializedProperty list = so.FindProperty("fragments");
        list.arraySize = pieces.Length;
        for (int i = 0; i < pieces.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = pieces[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        string dir = System.IO.Path.GetDirectoryName(setPath).Replace('\\', '/');
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/FX_{set.name}_Burst.prefab");

        // 다시 구울 때는 같은 경로를 덮어써야 EffectEntry의 참조가 안 끊긴다.
        if (set.burstPrefab != null)
        {
            string existing = AssetDatabase.GetAssetPath(set.burstPrefab);
            if (!string.IsNullOrEmpty(existing)) prefabPath = existing;
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        DestroyImmediate(root);

        set.burstPrefab = saved;
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MeshFragmentSet] 버스트 프리팹을 구웠습니다 — {prefabPath} (파편 {pieces.Length}개)", saved);
    }

    static void Bake(MeshFragmentSet set)
    {
        string path = AssetDatabase.GetAssetPath(set);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[MeshFragmentSet] 프로젝트에 저장된 에셋에만 Bake 할 수 있습니다.");
            return;
        }

        // 이전에 구운 메시는 이 에셋의 서브에셋으로 남아 있다. 지우지 않으면 다시 구울 때마다 쌓인다.
        Object[] existing = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] is Mesh) DestroyImmediate(existing[i], true);
        }

        Mesh source = set.sourceMesh;
        Vector3[] vertices = source.vertices;
        Vector2[] uvs = source.uv;
        int[] triangles = source.triangles;

        bool hasUv = uvs != null && uvs.Length == vertices.Length;
        if (hasUv == false)
        {
            Debug.LogWarning($"[MeshFragmentSet] '{source.name}'에 UV가 없어 파편 텍스처가 한 점으로 뭉칩니다. " +
                             "단색으로 보여도 괜찮다면 무시해도 됩니다.", set);
        }

        int triangleCount = triangles.Length / 3;
        int limit = set.maxFragments > 0 ? Mathf.Min(set.maxFragments, triangleCount) : triangleCount;

        // 상한이 걸리면 앞에서부터 자르지 않는다 — 메시의 삼각형 순서는 대개 한쪽 면에 몰려 있어서
        // 앞부분만 쓰면 파편이 한쪽에서만 나온다. 고르게 건너뛰며 뽑는다.
        float stride = (float)triangleCount / limit;

        List<MeshFragmentSet.Fragment> baked = new List<MeshFragmentSet.Fragment>(limit);

        for (int f = 0; f < limit; f++)
        {
            int t = Mathf.Min(triangleCount - 1, Mathf.FloorToInt(f * stride));
            int i0 = triangles[t * 3 + 0];
            int i1 = triangles[t * 3 + 1];
            int i2 = triangles[t * 3 + 2];

            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];

            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
            if (faceNormal.sqrMagnitude < 1e-12f) continue;   // 면적 0인 삼각형은 기둥이 안 나온다
            faceNormal.Normalize();

            Vector3 center = (v0 + v1 + v2) / 3f;

            Mesh prism = BuildPrism(
                v0 - center, v1 - center, v2 - center,
                hasUv ? uvs[i0] : Vector2.zero,
                hasUv ? uvs[i1] : Vector2.zero,
                hasUv ? uvs[i2] : Vector2.zero,
                faceNormal, set.thickness);

            prism.name = $"fragment_{baked.Count:D3}";
            AssetDatabase.AddObjectToAsset(prism, set);

            baked.Add(new MeshFragmentSet.Fragment
            {
                mesh = prism,
                center = center,
                normal = faceNormal,
            });
        }

        set.fragments = baked.ToArray();

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        Debug.Log($"[MeshFragmentSet] '{set.name}': '{source.name}'에서 파편 {baked.Count}개를 구웠습니다.", set);
    }

    /// <summary>
    /// 삼각형 하나를 두께 있는 삼각기둥 메시로 만든다. 정점은 전달받은 좌표계(무게중심 기준) 그대로다.
    /// 면마다 정점을 복제하므로 RecalculateNormals가 면별로 평평한 음영을 낸다.
    /// </summary>
    static Mesh BuildPrism(Vector3 a, Vector3 b, Vector3 c,
                           Vector2 ua, Vector2 ub, Vector2 uc,
                           Vector3 faceNormal, float thickness)
    {
        Vector3 half = faceNormal * (thickness * 0.5f);

        Vector3 fa = a + half, fb = b + half, fc = c + half;
        Vector3 ba = a - half, bb = b - half, bc = c - half;

        List<Vector3> verts = new List<Vector3>(24);
        List<Vector2> uv = new List<Vector2>(24);
        List<int> tris = new List<int>(24);

        // 앞뚜껑: 원본 감는 방향 그대로가 바깥이다.
        AddTriangle(verts, uv, tris, fa, fb, fc, ua, ub, uc, faceNormal);
        // 뒤뚜껑: 법선이 반대이므로 감는 방향을 뒤집는다.
        AddTriangle(verts, uv, tris, bc, bb, ba, uc, ub, ua, -faceNormal);

        // 옆면 세 개. 각 모서리의 바깥 방향은 (모서리 방향 × 면 법선)이다.
        AddSide(verts, uv, tris, fa, fb, bb, ba, ua, ub, faceNormal);
        AddSide(verts, uv, tris, fb, fc, bc, bb, ub, uc, faceNormal);
        AddSide(verts, uv, tris, fc, fa, ba, bc, uc, ua, faceNormal);

        Mesh m = new Mesh();
        m.SetVertices(verts);
        m.SetUVs(0, uv);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>삼각형 하나를 추가한다. 계산된 법선이 <paramref name="outward"/>와 반대면 감는 방향을 뒤집는다.</summary>
    static void AddTriangle(List<Vector3> verts, List<Vector2> uv, List<int> tris,
                            Vector3 p0, Vector3 p1, Vector3 p2,
                            Vector2 u0, Vector2 u1, Vector2 u2, Vector3 outward)
    {
        int baseIndex = verts.Count;

        verts.Add(p0); verts.Add(p1); verts.Add(p2);
        uv.Add(u0); uv.Add(u1); uv.Add(u2);

        // 감는 방향을 손으로 맞추려 들면 실수하기 쉽다. 계산해서 확인하는 쪽이 확실하다 —
        // 뒤집힌 면은 컬링돼서 파편에 구멍이 뚫린 것처럼 보인다.
        bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) < 0f;

        tris.Add(baseIndex + 0);
        tris.Add(baseIndex + (flip ? 2 : 1));
        tris.Add(baseIndex + (flip ? 1 : 2));
    }

    /// <summary>옆면 사각형 하나(삼각형 둘)를 추가한다.</summary>
    static void AddSide(List<Vector3> verts, List<Vector2> uv, List<int> tris,
                        Vector3 f0, Vector3 f1, Vector3 b1, Vector3 b0,
                        Vector2 u0, Vector2 u1, Vector3 faceNormal)
    {
        Vector3 outward = Vector3.Cross(f1 - f0, faceNormal).normalized;

        AddTriangle(verts, uv, tris, f0, f1, b1, u0, u1, u1, outward);
        AddTriangle(verts, uv, tris, f0, b1, b0, u0, u1, u0, outward);
    }
}
