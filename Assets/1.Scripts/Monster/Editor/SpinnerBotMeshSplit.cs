using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

// 저작 도구 — SpinnerBot 의 스킨드 메시를 **몸통 / 날개 두 개로 쪼갠다** (2026-09-14). 멱등이다.
//
// ── 왜 쪼개야 하는가 ────────────────────────────────────────────────────────────
// "인터럽트 가능" 오버레이는 렌더러의 머티리얼 슬롯을 **서브메시 개수보다 하나 더** 늘려서
// 같은 메시를 한 번 더 그리는 방식이다. 그런데 슬롯은 서브메시와 1:1 로 대응하고,
// 개수를 넘는 슬롯은 **언제나 마지막 서브메시**에 붙는다.
//
//  - GauntletBot · WallBot : 서브메시 1개 → 초과 슬롯이 몸 전체를 덮는다 ✅
//  - SpinnerBot            : 서브메시 2개(몸통 · 날개) → 초과 슬롯이 **날개**에만 붙는다 ❌
//
// 몸통을 서브메시 1개짜리 렌더러로 떼어내면 중간보스 3종이 같은 구조가 된다.
//
// ── 왜 Blender 왕복이 아니라 여기인가 (2026-09-14 실측) ──────────────────────────
// 🔴 Blender 로 FBX 를 다시 내보내면 **`Armature` 노드가 하나 끼어든다.** 원본 FBX(3ds Max 산)에는
//    그 문자열이 0회, Blender 재export 본에는 4회 나온다 — 실제로 내보내서 확인했다.
//    이 리그는 Generic(`animationType: 2`)이고 클립 7종이 전부
//    `R_Spinnerbot_01` 의 아바타를 **Copy From Other Avatar** 로 물고 있다. Generic 은 본 **경로**로
//    바인딩하므로 노드가 하나 끼면 경로가 통째로 밀려 클립이 조용히 안 붙는다.
//    → 메시만 프로젝트 안에서 쪼개고 FBX · 아바타 · 본 GameObject 는 **건드리지 않는다.**
//
// ── 하는 일 ────────────────────────────────────────────────────────────────────
//  ① 원본 SkinnedMeshRenderer 의 sharedMesh 를 서브메시별로 갈라 Mesh 에셋 2개를 굽는다
//     (쓰지 않는 정점은 버린다 — 통째로 복사하면 날개 렌더러가 몸통 2291 정점을 매 프레임 스키닝한다)
//  ② 원본 렌더러 = 몸통. 머티리얼 [몸통, Interrupt] — 초과 슬롯이 이제 몸통을 덮는다
//  ③ 형제로 날개 렌더러를 새로 만들고 **같은 bones · rootBone** 을 물린다(본은 공유, 스키닝 총량 불변)
//
// DissolveOverlay(구 InterruptOverlay) 는 원본 렌더러에 그대로 남는다 — 그 렌더러가 곧 몸통이 되므로 옮길 필요가 없다.
public static class SpinnerBotMeshSplit
{
    const string PrefabPath = "Assets/2.Prefabs/Monster/SpinnerBot.prefab";

    // 사본은 원본 FBX 와 같은 폴더에 둔다(팀장 지시). 50.Art 이므로 **SVN** 관할이다.
    const string OutputDir =
        "Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/Robot Sentries Pack 02/Models";
    const string BodyMeshPath = OutputDir + "/R_Spinnerbot_01_Body.asset";
    const string BladesMeshPath = OutputDir + "/R_Spinnerbot_01_Blades.asset";

    const string BladesMaterialPath =
        "Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/Robot Sentries Pack 02/Materials/M_SpinBotBlades.mat";
    const string InterruptMaterialPath = "Assets/50.Art/VFX/_Materials/Interrupt.mat";

    const string BladesObjectName = "SpinnerBot_Blades";

    // 원본 메시의 서브메시 순서. Blender 로 열어 실측했다(2026-09-14):
    // 0 = 몸통 2299면 / 1 = 날개 **1면**(회전 잔상용 사각판 한 장).
    const int BodySubMesh = 0;
    const int BladesSubMesh = 1;

    [MenuItem("Tools/Boss/SpinnerBot — 메시 분리 (몸통/날개) (멱등)")]
    public static void Split()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[SpinnerBot 분리] 프리팹을 못 열었다: {PrefabPath}");
            return;
        }

        try
        {
            SkinnedMeshRenderer body = FindBodyRenderer(root);
            if (body == null) return;

            Mesh source = body.sharedMesh;
            if (source.blendShapeCount > 0)
            {
                Debug.LogError(
                    $"[SpinnerBot 분리] 블렌드셰이프가 {source.blendShapeCount}개 있다 — 이 도구는 옮기지 않는다. " +
                    "중단한다(모르는 채로 잃는 것보다 낫다).");
                return;
            }

            Mesh bodyMesh = ExtractSubMesh(source, BodySubMesh, "R_Spinnerbot_01_Body");
            Mesh bladesMesh = ExtractSubMesh(source, BladesSubMesh, "R_Spinnerbot_01_Blades");

            SaveMesh(bodyMesh, BodyMeshPath);
            SaveMesh(bladesMesh, BladesMeshPath);

            Mesh savedBody = AssetDatabase.LoadAssetAtPath<Mesh>(BodyMeshPath);
            Mesh savedBlades = AssetDatabase.LoadAssetAtPath<Mesh>(BladesMeshPath);

            Material bodyMat = body.sharedMaterials.Length > 0 ? body.sharedMaterials[0] : null;
            Material bladesMat = AssetDatabase.LoadAssetAtPath<Material>(BladesMaterialPath);
            Material interruptMat = AssetDatabase.LoadAssetAtPath<Material>(InterruptMaterialPath);

            // 🔴 몸통 슬롯은 정확히 2개다: [0] 몸통(서브메시 0) · [1] Interrupt(초과 슬롯 → 몸통을 한 번 더).
            //    3개로 두면 안 된다 — 초과 슬롯이 둘이 되어 오버레이가 두 번 그려진다.
            body.sharedMesh = savedBody;
            body.sharedMaterials = new[] { bodyMat, interruptMat };
            body.localBounds = savedBody.bounds;

            SkinnedMeshRenderer blades = EnsureBladesRenderer(body);
            blades.sharedMesh = savedBlades;
            blades.sharedMaterials = new[] { bladesMat };
            blades.bones = body.bones;            // 본을 공유한다 — 복제하면 애니가 따로 논다
            blades.rootBone = body.rootBone;
            blades.localBounds = savedBlades.bounds;
            blades.quality = body.quality;
            blades.updateWhenOffscreen = body.updateWhenOffscreen;
            blades.shadowCastingMode = body.shadowCastingMode;
            blades.receiveShadows = body.receiveShadows;
            blades.renderingLayerMask = body.renderingLayerMask;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[SpinnerBot 분리] 완료 — 몸통 {savedBody.vertexCount}정점/{savedBody.triangles.Length / 3}면 · " +
                $"날개 {savedBlades.vertexCount}정점/{savedBlades.triangles.Length / 3}면. " +
                $"몸통 슬롯 [{Name(bodyMat)}, {Name(interruptMat)}] · 날개 슬롯 [{Name(bladesMat)}]");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/Boss/SpinnerBot — 메시 분리 검증 (읽기 전용)")]
    public static void Verify()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) return;

        try
        {
            SkinnedMeshRenderer[] all = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer r in all)
            {
                int subs = r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0;
                int slots = r.sharedMaterials.Length;
                string verdict = subs == 1 && slots == 2
                    ? "✅ 초과 슬롯 1개 → 이 렌더러 전체를 덮는다"
                    : subs == 1 && slots == 1 ? "— 오버레이 없음(정상)"
                    : $"⚠️ 서브메시 {subs} · 슬롯 {slots} — 초과 슬롯은 마지막 서브메시에만 붙는다";

                Debug.Log($"[SpinnerBot 분리 검증] {r.name}: 서브메시 {subs} · 슬롯 {slots} · " +
                          $"본 {r.bones.Length} · 루트본 {Name(r.rootBone)} → {verdict}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 쪼갤 대상 렌더러를 찾는다. 이미 쪼갠 뒤면 서브메시가 1개라 아무것도 하지 않는다(멱등).
    /// </summary>
    static SkinnedMeshRenderer FindBodyRenderer(GameObject root)
    {
        SkinnedMeshRenderer[] all = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer r in all)
        {
            if (r.name == BladesObjectName) continue;
            if (r.sharedMesh == null) continue;
            if (r.sharedMesh.subMeshCount >= 2) return r;
        }

        Debug.Log("[SpinnerBot 분리] 서브메시가 2개 이상인 렌더러가 없다 — 이미 분리됐다. 아무것도 하지 않는다.");
        return null;
    }

    static SkinnedMeshRenderer EnsureBladesRenderer(SkinnedMeshRenderer body)
    {
        Transform parent = body.transform.parent != null ? body.transform.parent : body.transform;
        Transform existing = parent.Find(BladesObjectName);
        if (existing != null)
        {
            SkinnedMeshRenderer found = existing.GetComponent<SkinnedMeshRenderer>();
            if (found != null) return found;
            return existing.gameObject.AddComponent<SkinnedMeshRenderer>();
        }

        // 🔴 몸통의 **자식**이 아니라 형제로 둔다. 자식으로 두면 몸통 트랜스폼이 한 번 더 곱해지는데,
        //    스킨드 메시는 본으로 움직이므로 그게 눈에 띄는 어긋남이 된다.
        GameObject go = new GameObject(BladesObjectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = body.transform.localPosition;
        go.transform.localRotation = body.transform.localRotation;
        go.transform.localScale = body.transform.localScale;
        go.layer = body.gameObject.layer;
        return go.AddComponent<SkinnedMeshRenderer>();
    }

    /// <summary>
    /// 서브메시 하나만 뽑아 **쓰는 정점만 남긴** 새 메시를 만든다.
    ///
    /// 정점 버퍼를 통째로 복사하면 편하지만, 스키닝은 인덱스가 아니라 **정점 수**에 비례한다 —
    /// 면 한 장짜리 날개가 몸통 2291 정점을 매 프레임 변형하게 된다. 그래서 압축한다.
    ///
    /// 본 인덱스와 bindposes 는 <b>건드리지 않는다.</b> 두 메시가 같은 bones 배열을 쓰므로
    /// 인덱스 의미가 그대로여야 한다.
    /// </summary>
    static Mesh ExtractSubMesh(Mesh source, int subMesh, string name)
    {
        int[] tris = source.GetTriangles(subMesh);

        var remap = new Dictionary<int, int>();
        var keep = new List<int>();
        var newTris = new int[tris.Length];
        for (int i = 0; i < tris.Length; i++)
        {
            int old = tris[i];
            if (!remap.TryGetValue(old, out int mapped))
            {
                mapped = keep.Count;
                remap[old] = mapped;
                keep.Add(old);
            }
            newTris[i] = mapped;
        }

        Vector3[] srcVerts = source.vertices;
        Vector3[] srcNormals = source.normals;
        Vector4[] srcTangents = source.tangents;
        Color[] srcColors = source.colors;
        var srcUv0 = new List<Vector2>(); source.GetUVs(0, srcUv0);
        var srcUv1 = new List<Vector2>(); source.GetUVs(1, srcUv1);

        var verts = new Vector3[keep.Count];
        var normals = srcNormals.Length > 0 ? new Vector3[keep.Count] : null;
        var tangents = srcTangents.Length > 0 ? new Vector4[keep.Count] : null;
        var colors = srcColors.Length > 0 ? new Color[keep.Count] : null;
        var uv0 = srcUv0.Count > 0 ? new Vector2[keep.Count] : null;
        var uv1 = srcUv1.Count > 0 ? new Vector2[keep.Count] : null;

        for (int i = 0; i < keep.Count; i++)
        {
            int old = keep[i];
            verts[i] = srcVerts[old];
            if (normals != null) normals[i] = srcNormals[old];
            if (tangents != null) tangents[i] = srcTangents[old];
            if (colors != null) colors[i] = srcColors[old];
            if (uv0 != null) uv0[i] = srcUv0[old];
            if (uv1 != null) uv1[i] = srcUv1[old];
        }

        var mesh = new Mesh { name = name };
        mesh.indexFormat = keep.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.vertices = verts;
        if (normals != null) mesh.normals = normals;
        if (tangents != null) mesh.tangents = tangents;
        if (colors != null) mesh.colors = colors;
        if (uv0 != null) mesh.SetUVs(0, uv0);
        if (uv1 != null) mesh.SetUVs(1, uv1);

        mesh.subMeshCount = 1;
        mesh.SetTriangles(newTris, 0);

        CopyBoneWeights(source, mesh, keep);
        mesh.bindposes = source.bindposes;   // 본 배열을 공유하므로 그대로 간다

        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 가중치를 남긴 정점만 옮긴다.
    ///
    /// 🔴 <c>Mesh.boneWeights</c>(구 API)가 아니라 <c>GetAllBoneWeights</c> 를 쓴다 —
    ///    구 API 는 정점당 영향 4개로 **잘라낸다.** 이 메시는 최대 3개라 지금은 같은 결과지만,
    ///    아트가 리깅을 다시 하면 조용히 달라질 자리다.
    /// </summary>
    static void CopyBoneWeights(Mesh source, Mesh target, List<int> keep)
    {
        NativeArray<byte> srcPerVertex = source.GetBonesPerVertex();
        NativeArray<BoneWeight1> srcWeights = source.GetAllBoneWeights();
        if (srcPerVertex.Length == 0) return;

        // 정점 i 의 가중치가 srcWeights 의 어디서 시작하는지 — 누적합을 미리 만든다.
        var offset = new int[srcPerVertex.Length];
        int running = 0;
        for (int i = 0; i < srcPerVertex.Length; i++)
        {
            offset[i] = running;
            running += srcPerVertex[i];
        }

        var perVertex = new byte[keep.Count];
        var weights = new List<BoneWeight1>(keep.Count * 4);
        for (int i = 0; i < keep.Count; i++)
        {
            int old = keep[i];
            byte n = srcPerVertex[old];
            perVertex[i] = n;
            for (int k = 0; k < n; k++) weights.Add(srcWeights[offset[old] + k]);
        }

        var outPerVertex = new NativeArray<byte>(perVertex, Allocator.Temp);
        var outWeights = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp);
        target.SetBoneWeights(outPerVertex, outWeights);
        outPerVertex.Dispose();
        outWeights.Dispose();
    }

    static void SaveMesh(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            // 🔴 지우고 다시 만들지 않는다 — 프리팹이 물고 있는 참조(GUID+fileID)가 끊긴다.
            //    같은 에셋의 **내용만** 갈아끼운다.
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            return;
        }

        AssetDatabase.CreateAsset(mesh, path);
    }

    static string Name(Object o) => o != null ? o.name : "(없음)";
}
