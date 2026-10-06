using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="MonsterPartSet"/> 의 Bake — 몬스터의 모든 렌더러를 <b>부위 덩어리</b>로 가른다.
///
/// <list type="number">
/// <item><b>SkinnedMeshRenderer 전부</b>를 훑는다. SpinnerBot 은 몸체와 날이 별개 렌더러다 —
///       첫 번째만 가르면 날은 안 무너진다.</item>
/// <item><b>고정 MeshRenderer</b>(HumanoidBot 의 총, GauntletBot 의 건틀릿)는 통째로 한 조각.
///       빼먹으면 몸만 무너지고 무기는 공중에서 증발한다.</item>
/// <item>조각이 <see cref="MonsterPartSet.maxParts"/> 를 넘으면 <b>작은 것부터 부모 본에 합친다.</b></item>
/// </list>
///
/// 🔴 <b>본 경계를 가로지르는 삼각형 수를 찍는다.</b> 그런 삼각형은 어느 쪽에 넣어도 반대쪽에
/// 구멍이 남는다. 합치기를 거치면 대개 사라지지만, 남으면 숫자로 먼저 알려 준다 —
/// 조용히 뚫린 채로 지나가는 것이 제일 나쁘다.
///
/// 🔴 <b>Read/Write 가 꺼진 FBX 는 잠깐 켰다가 <c>.meta</c> 를 통째로 되돌린다.</b>
/// 켠 채로 두면 런타임 메모리가 두 배가 되고, 이 모델들은 SVN 이라 변경이 남으면 안 된다.
/// </summary>
[CustomEditor(typeof(MonsterPartSet))]
public class MonsterPartSetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var set = (MonsterPartSet)target;
        EditorGUILayout.Space(8);

        if (set.sourcePrefab == null)
        {
            EditorGUILayout.HelpBox("Source Prefab 이 비어 있습니다. 몬스터 프리팹을 넣으세요.", MessageType.Info);
            return;
        }

        if (GUILayout.Button(set.IsBaked ? "부위 다시 가르기" : "부위 가르기", GUILayout.Height(26)))
            Bake(set);

        if (!set.IsBaked) return;

        int tris = 0, statics = 0;
        for (int i = 0; i < set.parts.Length; i++)
        {
            tris += set.parts[i].triangleCount;
            if (set.parts[i].isStaticMesh) statics++;
        }
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox($"조각 {set.parts.Length}개 (고정 메시 {statics}개) · 삼각형 합계 {tris}",
                                MessageType.None);
    }

    // ── 굽기 ────────────────────────────────────────────────────────

    /// <summary>외부(설정 도구)에서도 부를 수 있게 열어 둔다.</summary>
    public static void Bake(MonsterPartSet set)
    {
        string setPath = AssetDatabase.GetAssetPath(set);
        if (string.IsNullOrEmpty(setPath))
        {
            Debug.LogError("[MonsterPartSet] 프로젝트에 저장된 에셋에만 Bake 할 수 있습니다.");
            return;
        }

        GameObject root = set.sourcePrefab;

        // 🔴 **꺼진 렌더러를 빼는 것이 핵심이다.** 몬스터들은 리디자인(SurfaceV1) 전의 옛 메시를
        //    끈 채로 프리팹에 그대로 달고 있다. includeInactive 로 긁으면 그 옛 메시까지 조각이 되어
        //    옛 머티리얼(FK_*_01)을 달고 같이 쏟아진다 — "죽을 때만 머티리얼이 달라진다"의 정체다.
        //    꺼진 렌더러는 살아 있을 때 안 보이므로 죽을 때도 보이면 안 된다.
        var skinned = new List<SkinnedMeshRenderer>();
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (IsLive(smr)) skinned.Add(smr);

        var statics = new List<MeshRenderer>();
        foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!IsLive(mr)) continue;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) statics.Add(mr);
        }

        int hidden = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length
                     + root.GetComponentsInChildren<MeshRenderer>(true).Length
                     - skinned.Count - statics.Count;
        if (hidden > 0)
            Debug.Log($"[MonsterPartSet] 꺼져 있는 렌더러 {hidden}개를 건너뛰었습니다 " +
                      "(리디자인 전 옛 메시 등 — 살아 있을 때 안 보이는 것은 죽을 때도 안 나옵니다).");

        if (skinned.Count == 0 && statics.Count == 0)
        {
            Debug.LogError($"[MonsterPartSet] '{root.name}' 에 가를 렌더러가 없습니다.");
            return;
        }

        // Read/Write 가 꺼진 모델을 잠시 켠다. .meta 는 바이트째 떠 뒀다가 되돌린다 —
        // isReadable 만 되돌리면 SaveAndReimport 가 무관한 줄까지 다시 써 SVN 에 남는다.
        var metaBackup = new Dictionary<string, string>();
        try
        {
            foreach (SkinnedMeshRenderer smr in skinned) MakeReadable(smr.sharedMesh, metaBackup);
            foreach (MeshRenderer mr in statics) MakeReadable(mr.GetComponent<MeshFilter>().sharedMesh, metaBackup);

            BakeInner(set, root, skinned, statics, setPath);
        }
        finally
        {
            foreach (KeyValuePair<string, string> kv in metaBackup)
            {
                File.WriteAllText(kv.Key + ".meta", kv.Value);
                AssetDatabase.ImportAsset(kv.Key, ImportAssetOptions.ForceUpdate);
            }
            if (metaBackup.Count > 0)
                Debug.Log($"[MonsterPartSet] 모델 {metaBackup.Count}개의 .meta 를 건드리기 전 내용으로 되돌렸습니다.");
        }
    }

    /// <summary>
    /// 지금 화면에 그려지는 렌더러인가. <c>enabled</c> 와 조상들의 <c>activeSelf</c> 를 모두 본다.
    ///
    /// 🔴 프리팹 에셋에서는 <c>activeInHierarchy</c> 를 믿을 수 없다(씬에 들어 있지 않다).
    /// 그래서 부모를 직접 타고 올라간다.
    /// </summary>
    static bool IsLive(Renderer r)
    {
        if (r == null || !r.enabled) return false;
        for (Transform t = r.transform; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) return false;
        return true;
    }

    static void MakeReadable(Mesh mesh, Dictionary<string, string> backup)
    {
        if (mesh == null || mesh.isReadable) return;

        string path = AssetDatabase.GetAssetPath(mesh);
        if (backup.ContainsKey(path)) return;
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
        {
            Debug.LogWarning($"[MonsterPartSet] '{mesh.name}' 는 Read/Write 가 꺼져 있는데 ModelImporter 가 " +
                             "아니라 자동으로 켤 수가 없습니다. 이 메시는 건너뜁니다.");
            return;
        }

        backup[path] = File.ReadAllText(path + ".meta");
        importer.isReadable = true;
        importer.SaveAndReimport();
        Debug.Log($"[MonsterPartSet] '{Path.GetFileName(path)}' 의 Read/Write 를 잠시 켰습니다.");
    }

    /// <summary>굽는 중에만 쓰는 조각 하나. 메시가 되기 전 단계다.</summary>
    class Group
    {
        public Transform follow;     // 따라갈 트랜스폼(본 또는 고정 렌더러 자신)
        public Matrix4x4 toLocal;    // 메시 공간 -> follow 로컬. 고정 메시면 항등
        public Material[] materials;
        public Renderer renderer;    // 머티리얼을 런타임에 다시 읽어 올 출처
        public bool isStatic;
        public List<int>[] tris;     // 서브메시별 인덱스
        public Mesh source;
    }

    static void BakeInner(MonsterPartSet set, GameObject root,
                          List<SkinnedMeshRenderer> skinned, List<MeshRenderer> statics, string setPath)
    {
        var groups = new List<Group>();
        int crossing = 0, crossTotal = 0;

        foreach (SkinnedMeshRenderer smr in skinned)
            crossing += SplitSkinned(set, smr, groups, ref crossTotal);

        foreach (MeshRenderer mr in statics)
            AddStatic(mr, groups);

        if (groups.Count == 0)
        {
            Debug.LogError($"[MonsterPartSet] '{root.name}' 에서 조각이 하나도 안 나왔습니다.");
            return;
        }

        // 지난 Bake 가 남긴 서브에셋 메시를 치운다. 안 지우면 에셋이 계속 불어난다.
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(setPath))
            if (o is Mesh old) Object.DestroyImmediate(old, true);

        var parts = new List<MonsterPartSet.Part>();
        foreach (Group g in groups)
        {
            MonsterPartSet.Part part = Build(g, root.transform);
            if (part != null)
            {
                AssetDatabase.AddObjectToAsset(part.mesh, set);
                parts.Add(part);
            }
        }

        set.parts = parts.ToArray();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(setPath);

        int total = 0, statCount = 0;
        var report = new System.Text.StringBuilder();
        for (int i = 0; i < set.parts.Length; i++)
        {
            total += set.parts[i].triangleCount;
            if (set.parts[i].isStaticMesh) statCount++;
            report.Append($"\n  {set.parts[i].followName,-18} 삼각 {set.parts[i].triangleCount,5}" +
                          (set.parts[i].isStaticMesh ? "  (고정 메시)" : ""));
        }

        Debug.Log($"[MonsterPartSet] '{set.name}' 조각 {set.parts.Length}개 (고정 {statCount}) · " +
                  $"삼각형 {total} · 렌더러 {skinned.Count + statics.Count}개에서{report}", set);

        if (crossing > 0)
        {
            float pct = crossTotal > 0 ? 100f * crossing / crossTotal : 0f;
            string level = pct < 1f ? "거의 안 보일 수준입니다"
                         : pct < 3f ? "이음매에 작은 틈이 보일 수 있습니다"
                         : "🔴 눈에 띄는 구멍이 생길 수 있습니다";
            Debug.LogWarning($"[MonsterPartSet] '{set.name}' 합치기 뒤에도 본 경계를 가로지르는 삼각형 " +
                             $"{crossing}개 ({pct:0.0}%) — 다수결로 한쪽에 넣었습니다. {level}", set);
        }
    }

    /// <summary>스킨 메시 하나를 본별로 가른다. 반환값 = 경계를 가로지른 삼각형 수.</summary>
    static int SplitSkinned(MonsterPartSet set, SkinnedMeshRenderer smr, List<Group> groups, ref int triTotal)
    {
        Mesh mesh = smr.sharedMesh;
        if (mesh == null || !mesh.isReadable) return 0;

        Transform[] bones = smr.bones;
        Matrix4x4[] bindposes = mesh.bindposes;
        BoneWeight[] weights = mesh.boneWeights;
        if (bones == null || bones.Length == 0 || weights == null || weights.Length == 0)
        {
            Debug.LogWarning($"[MonsterPartSet] '{smr.name}' 에 본 가중치가 없어 건너뜁니다.");
            return 0;
        }

        // 정점 -> 지배 본. BoneWeight 는 가중치 내림차순이라 boneIndex0 이 곧 지배 본이다.
        var dominant = new int[weights.Length];
        for (int v = 0; v < weights.Length; v++) dominant[v] = weights[v].boneIndex0;

        // ① 본별 삼각형 수를 먼저 세어 '살릴 본'을 고른다.
        var count = new Dictionary<int, int>();
        int sub = mesh.subMeshCount;
        var subTris = new int[sub][];
        for (int s = 0; s < sub; s++)
        {
            subTris[s] = mesh.GetTriangles(s);
            for (int t = 0; t < subTris[s].Length; t += 3)
            {
                int b = Majority(dominant[subTris[s][t]], dominant[subTris[s][t + 1]], dominant[subTris[s][t + 2]]);
                count.TryGetValue(b, out int c);
                count[b] = c + 1;
            }
        }

        Dictionary<int, int> remap = BuildMerge(set, bones, count);

        // ② 삼각형을 (합쳐진) 본별로 모은다.
        var byBone = new Dictionary<int, Group>();
        int cross = 0;
        for (int s = 0; s < sub; s++)
        {
            int[] tri = subTris[s];
            for (int t = 0; t < tri.Length; t += 3)
            {
                int a = dominant[tri[t]], b2 = dominant[tri[t + 1]], c2 = dominant[tri[t + 2]];
                int bone = Majority(a, b2, c2);
                triTotal++;

                int target = remap.TryGetValue(bone, out int r) ? r : bone;
                // 합친 뒤에도 세 정점이 서로 다른 조각으로 갈리면 그게 진짜 '구멍 위험'이다.
                if (Target(remap, a) != target || Target(remap, b2) != target || Target(remap, c2) != target)
                    cross++;

                if (!byBone.TryGetValue(target, out Group g))
                {
                    Transform follow = target >= 0 && target < bones.Length ? bones[target] : smr.transform;
                    g = new Group
                    {
                        follow = follow != null ? follow : smr.transform,
                        toLocal = target >= 0 && target < bindposes.Length ? bindposes[target] : Matrix4x4.identity,
                        materials = smr.sharedMaterials,
                        renderer = smr,
                        isStatic = false,
                        tris = new List<int>[sub],
                        source = mesh,
                    };
                    byBone[target] = g;
                    groups.Add(g);
                }
                (g.tris[s] ??= new List<int>()).Add(tri[t]);
                g.tris[s].Add(tri[t + 1]);
                g.tris[s].Add(tri[t + 2]);
            }
        }

        return cross;
    }

    static int Target(Dictionary<int, int> remap, int bone)
        => remap.TryGetValue(bone, out int r) ? r : bone;

    static int Majority(int a, int b, int c)
    {
        if (a == b || a == c) return a;
        if (b == c) return b;
        return a;
    }

    /// <summary>
    /// 작은 조각을 <b>부모 본</b>으로 접는 표를 만든다.
    /// 볼트가 그 팔뚝에 붙는 식이라, 쪼개진 자리가 아니라 관절에서 떨어진다.
    /// </summary>
    static Dictionary<int, int> BuildMerge(MonsterPartSet set, Transform[] bones, Dictionary<int, int> count)
    {
        var remap = new Dictionary<int, int>();
        if (count.Count == 0) return remap;

        var keep = new HashSet<int>(count.Keys);

        // 작은 순서대로, 상한/최소 삼각형 조건을 만족할 때까지 접는다.
        var order = new List<int>(count.Keys);
        order.Sort((x, y) => count[x].CompareTo(count[y]));

        foreach (int bone in order)
        {
            bool tooSmall = set.minTriangles > 0 && count[bone] < set.minTriangles;
            bool tooMany = set.maxParts > 0 && keep.Count > set.maxParts;
            if (!tooSmall && !tooMany) break;
            if (keep.Count <= 1) break;

            // 살아 있는 조상을 찾는다. 없으면 가장 큰 조각으로 보낸다.
            int target = -1;
            Transform t = bone >= 0 && bone < bones.Length && bones[bone] != null ? bones[bone].parent : null;
            while (t != null)
            {
                int idx = System.Array.IndexOf(bones, t);
                if (idx >= 0 && keep.Contains(idx) && idx != bone) { target = idx; break; }
                t = t.parent;
            }
            if (target < 0)
            {
                target = order[order.Count - 1];
                if (target == bone) continue;
                while (remap.TryGetValue(target, out int up)) target = up;
                if (target == bone) continue;
            }

            remap[bone] = target;
            keep.Remove(bone);
            count[target] += count[bone];
        }

        // 체인을 평탄화한다(A→B→C 를 A→C 로). 안 하면 조각이 중간 본을 따라가 엉뚱한 데 붙는다.
        foreach (int k in new List<int>(remap.Keys))
        {
            int v = remap[k];
            while (remap.TryGetValue(v, out int up)) v = up;
            remap[k] = v;
        }
        return remap;
    }

    /// <summary>고정 메시는 통째로 한 조각. 본이 아니라 제 트랜스폼을 따라간다.</summary>
    static void AddStatic(MeshRenderer mr, List<Group> groups)
    {
        Mesh mesh = mr.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null || !mesh.isReadable) return;

        int sub = mesh.subMeshCount;
        var g = new Group
        {
            follow = mr.transform,
            toLocal = Matrix4x4.identity,   // 이미 제 로컬 공간이다
            materials = mr.sharedMaterials,
            renderer = mr,
            isStatic = true,
            tris = new List<int>[sub],
            source = mesh,
        };
        for (int s = 0; s < sub; s++) g.tris[s] = new List<int>(mesh.GetTriangles(s));
        groups.Add(g);
    }

    /// <summary>모아 둔 삼각형으로 실제 조각 메시를 만든다.</summary>
    static MonsterPartSet.Part Build(Group g, Transform root)
    {
        Mesh src = g.source;
        Vector3[] sPos = src.vertices;
        Vector3[] sNrm = src.normals;
        Vector4[] sTan = src.tangents;
        Vector2[] sUv = src.uv;
        Vector2[] sUv2 = src.uv2;
        Color[] sCol = src.colors;

        // 법선·탄젠트는 평행이동을 먹으면 안 된다. 회전만 꺼내 쓴다 —
        // bindpose 는 강체 변환이라 역전치까지 갈 필요가 없다.
        Quaternion rot = g.toLocal.rotation;

        var remap = new Dictionary<int, int>();
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var tan = new List<Vector4>();
        var uv = new List<Vector2>();
        var uv2 = new List<Vector2>();
        var col = new List<Color>();
        var outTris = new List<int[]>();
        var outMats = new List<Material>();
        var outSlots = new List<int>();

        int triCount = 0;
        for (int s = 0; s < g.tris.Length; s++)
        {
            if (g.tris[s] == null || g.tris[s].Count == 0) continue;
            List<int> list = g.tris[s];
            var dst = new int[list.Count];

            for (int i = 0; i < list.Count; i++)
            {
                int v = list[i];
                if (!remap.TryGetValue(v, out int n))
                {
                    n = pos.Count;
                    remap[v] = n;
                    pos.Add(g.toLocal.MultiplyPoint3x4(sPos[v]));
                    if (sNrm.Length > 0) nrm.Add(rot * sNrm[v]);
                    if (sTan.Length > 0)
                    {
                        Vector3 t3 = rot * (Vector3)sTan[v];
                        tan.Add(new Vector4(t3.x, t3.y, t3.z, sTan[v].w));
                    }
                    if (sUv.Length > 0) uv.Add(sUv[v]);
                    if (sUv2.Length > 0) uv2.Add(sUv2[v]);
                    if (sCol.Length > 0) col.Add(sCol[v]);
                }
                dst[i] = n;
            }

            triCount += list.Count / 3;
            outTris.Add(dst);
            // 🔴 슬롯 '번호'를 같이 적는다. 런타임이 살아 있는 렌더러에서 이 번호로 다시 읽는다 —
            //    아래 머티리얼 배열은 그때 못 찾았을 때를 위한 폴백일 뿐이다.
            outSlots.Add(s);
            outMats.Add(s < g.materials.Length ? g.materials[s] : null);
        }

        if (pos.Count == 0) return null;

        // 무게중심 = 바운드 중심. 리지드바디의 회전 중심이 조각 한가운데여야 자연스럽게 구른다.
        Vector3 min = pos[0], max = pos[0];
        for (int i = 1; i < pos.Count; i++) { min = Vector3.Min(min, pos[i]); max = Vector3.Max(max, pos[i]); }
        Vector3 center = (min + max) * 0.5f;
        for (int i = 0; i < pos.Count; i++) pos[i] -= center;

        var m = new Mesh { name = "Part_" + g.follow.name };
        m.indexFormat = pos.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(pos);
        if (nrm.Count == pos.Count) m.SetNormals(nrm);
        if (tan.Count == pos.Count) m.SetTangents(tan);
        if (uv.Count == pos.Count) m.SetUVs(0, uv);
        if (uv2.Count == pos.Count) m.SetUVs(1, uv2);
        if (col.Count == pos.Count) m.SetColors(col);
        m.subMeshCount = outTris.Count;
        for (int i = 0; i < outTris.Count; i++) m.SetTriangles(outTris[i], i, false);
        m.RecalculateBounds();

        return new MonsterPartSet.Part
        {
            mesh = m,
            followPath = PathOf(root, g.follow),
            followName = g.follow.name,
            boneOffset = center,
            materials = outMats.ToArray(),
            rendererPath = PathOf(root, g.renderer != null ? g.renderer.transform : null),
            submeshIndices = outSlots.ToArray(),
            isStaticMesh = g.isStatic,
            triangleCount = triCount,
        };
    }

    /// <summary>루트 기준 경로. 런타임이 <c>Transform.Find</c> 로 되짚는다.</summary>
    static string PathOf(Transform root, Transform t)
    {
        if (t == null || t == root) return string.Empty;
        string s = t.name;
        t = t.parent;
        while (t != null && t != root) { s = t.name + "/" + s; t = t.parent; }
        return s;
    }
}
