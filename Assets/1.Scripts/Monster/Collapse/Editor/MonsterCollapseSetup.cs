using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 잡몹 사망 연출을 <b>디졸브 → 부위 붕괴</b>로 갈아끼우는 설정 도구.
///
/// 한 몹에 대해 세 가지를 한다:
/// <list type="number">
/// <item><c>MonsterPartSet</c> 에셋을 만들고(없으면) 스킨 메시를 본 단위로 Bake</item>
/// <item>프리팹의 <see cref="DissolveDeath"/> 를 떼고 <see cref="CollapseDeath"/> 를 붙인다</item>
/// <item>디졸브 템플릿·파티클을 옛 컴포넌트에서 그대로 옮겨 적는다</item>
/// </list>
///
/// 🔴 <b><c>MonsterBase</c> 는 <c>GetComponent&lt;IDeathEffect&gt;()</c> 로 하나만 집는다.</b>
/// 그래서 둘을 같이 두면 어느 쪽이 걸릴지 알 수 없다 — 반드시 교체이지 추가가 아니다.
///
/// 🔴 <b>NetworkBehaviour 가 하나 빠지고 하나 들어간다.</b> 개수는 같지만 목록 순서가 바뀐다.
/// 모든 피어가 같은 프리팹을 쓰므로 동작에는 문제가 없으나, 몬스터 프리팹은 경석 님 영역이라
/// 공유가 필요하다.
/// </summary>
static class MonsterCollapseSetup
{
    const string PartsDir = "Assets/2.Prefabs/Monster/Collapse";
    const string StylizedDissolvePath = "Assets/50.Art/VFX/Shaders/StylizedSurfaceDissolve.shader";
    const string DissolveTemplatePath = "Assets/50.Art/VFX/Common/Dissolve/M_Dissolve_Template.mat";

    /// <summary>잡몹 8종. 🔴 보스·중간보스는 뺀다 — 23호는 쓰러진 자세를 2초 보여 주는 연출이
    /// 팀장 지시(2026-09-30)로 들어가 있어 별도 합의 없이 바꿀 것이 아니다.</summary>
    static readonly string[] Mobs =
    {
        "ChompBot", "GauntletBot", "HumanoidBot", "MortarBot",
        "PeekABot", "SpinnerBot", "TeslaBot", "WallBot",
    };

    [MenuItem("Tools/Monster/부위 붕괴 — 잡몹 전부 설정")]
    static void WireAllMobs()
    {
        if (!EditorUtility.DisplayDialog("부위 붕괴",
                $"잡몹 {Mobs.Length}종에 적용합니다." + System.Environment.NewLine + System.Environment.NewLine +
                "각 프리팹의 DissolveDeath 가 CollapseDeath 로 바뀝니다." + System.Environment.NewLine +
                "보스·중간보스는 건드리지 않습니다.", "진행", "취소"))
            return;

        for (int i = 0; i < Mobs.Length; i++)
        {
            EditorUtility.DisplayProgressBar("부위 붕괴", Mobs[i], (float)i / Mobs.Length);
            Wire($"Assets/2.Prefabs/Monster/{Mobs[i]}.prefab");
        }
        EditorUtility.ClearProgressBar();
        Debug.Log($"[부위 붕괴] 잡몹 {Mobs.Length}종 완료. 위 로그에서 조각 수와 경계 교차 경고를 확인할 것.");
    }

    [MenuItem("Tools/Monster/부위 붕괴 — ChompBot 설정")]
    static void WireChompBot() => Wire("Assets/2.Prefabs/Monster/ChompBot.prefab");

    /// <summary>프로젝트 창에서 고른 몬스터 프리팹들에 적용한다. 나머지 7종은 이걸로 돌린다.</summary>
    [MenuItem("Tools/Monster/부위 붕괴 — 선택한 프리팹에 적용")]
    static void WireSelection()
    {
        Object[] sel = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);
        if (sel.Length == 0)
        {
            EditorUtility.DisplayDialog("부위 붕괴", "프로젝트 창에서 몬스터 프리팹을 골라 주세요.", "확인");
            return;
        }

        foreach (Object o in sel)
            Wire(AssetDatabase.GetAssetPath(o));
    }

    static void Wire(string prefabPath)
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError($"[부위 붕괴] 프리팹을 못 찾았다: {prefabPath}");
            return;
        }

        string name = Path.GetFileNameWithoutExtension(prefabPath);

        // 스킨이 하나도 없어도 고정 메시만으로 조각이 나올 수 있다(무기형 몹). 렌더러 유무만 본다.
        if (prefabAsset.GetComponentInChildren<SkinnedMeshRenderer>(true) == null &&
            prefabAsset.GetComponentInChildren<MeshRenderer>(true) == null)
        {
            Debug.LogError($"[부위 붕괴] '{name}' 에 렌더러가 없다 — 가를 메시가 없다.");
            return;
        }

        // ── ① 조각 에셋 ─────────────────────────────────────────────
        if (!Directory.Exists(PartsDir))
        {
            Directory.CreateDirectory(PartsDir);
            AssetDatabase.Refresh();
        }

        string setPath = $"{PartsDir}/{name}_Parts.asset";
        var set = AssetDatabase.LoadAssetAtPath<MonsterPartSet>(setPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<MonsterPartSet>();
            AssetDatabase.CreateAsset(set, setPath);
            Debug.Log($"[부위 붕괴] 조각 에셋을 만들었다: {setPath}");
        }

        set.sourcePrefab = prefabAsset;
        EditorUtility.SetDirty(set);
        MonsterPartSetEditor.Bake(set);

        if (!set.IsBaked)
        {
            Debug.LogError($"[부위 붕괴] '{name}' Bake 에 실패했다 — 컴포넌트 교체를 건너뛴다.");
            return;
        }

        // ── ② 컴포넌트 교체 ──────────────────────────────────────────
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            // 옛 연출에서 가져올 것들. 둘 다 캐릭터별이 아니라 공용이라 그대로 옮기면 된다.
            Material template = null;
            ParticleSystem particle = null;

            // 🔴 루트에 있다고 단정하지 않는다. NetworkBehaviour 라 NetworkObject 아래 어디든 될 수 있고,
            //    못 찾고 지나가면 옛 디졸브가 남아 둘 중 어느 쪽이 걸릴지 모르게 된다.
            var old = root.GetComponentInChildren<DissolveDeath>(true);
            GameObject host = old != null ? old.gameObject : root;

            if (old != null)
            {
                var so = new SerializedObject(old);
                template = so.FindProperty("dissolveTemplate").objectReferenceValue as Material;
                particle = so.FindProperty("particlePrefab").objectReferenceValue as ParticleSystem;
                Object.DestroyImmediate(old, true);
                Debug.Log($"[부위 붕괴] '{name}' 의 DissolveDeath 를 떼어 냈다 " +
                          "(디졸브 템플릿·파티클은 붕괴 쪽으로 옮긴다).");
            }

            // 옛 컴포넌트가 있던 자리를 그대로 쓴다 — 거기가 NetworkObject 가 인정하는 자리다.
            var collapse = host.GetComponent<CollapseDeath>();
            if (collapse == null) collapse = host.AddComponent<CollapseDeath>();

            // 폴백 템플릿 — 옛 컴포넌트가 없어 못 가져왔으면 경로로 찾는다.
            if (template == null)
                template = AssetDatabase.LoadAssetAtPath<Material>(DissolveTemplatePath);

            // 🔴 FlatKit 룩을 유지한 채 녹이는 셰이더. 없으면 녹는 순간 질감이 바뀐다.
            var stylized = AssetDatabase.LoadAssetAtPath<Shader>(StylizedDissolvePath);
            if (stylized == null)
                Debug.LogWarning("[부위 붕괴] " + StylizedDissolvePath + " 를 못 찾았다 — " +
                                 "녹을 때 셀 음영이 빠지는 옛 경로로 떨어진다.");

            var co = new SerializedObject(collapse);
            co.FindProperty("partSet").objectReferenceValue = set;
            co.FindProperty("stylizedDissolveShader").objectReferenceValue = stylized;
            // 노이즈는 템플릿이 쓰던 것을 그대로 빌려 쓴다 — 같은 그림이어야 하니까.
            if (template != null && template.HasProperty("_NoiseTexture"))
                Assign(co, "dissolveNoise", template.GetTexture("_NoiseTexture"));
            // 이미 값이 있으면 덮지 않는다 — 다시 돌렸을 때 손으로 맞춘 값을 지우면 안 된다.
            Assign(co, "dissolveTemplate", template);
            Assign(co, "particlePrefab", particle);
            co.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.SaveAssets();

            int tris = 0;
            for (int i = 0; i < set.parts.Length; i++) tris += set.parts[i].triangleCount;

            Debug.Log($"[부위 붕괴] '{name}' 배선 완료" + System.Environment.NewLine +
                      $"  조각 {set.parts.Length}개 · 삼각형 {tris}" + System.Environment.NewLine +
                      $"  조각 에셋 = {setPath}" + System.Environment.NewLine +
                      $"  디졸브 템플릿 = {(template != null ? template.name : "없음(조각이 그냥 사라진다)")}" +
                      System.Environment.NewLine +
                      "  🔴 NetworkBehaviour 목록이 바뀌었다 — 경석 님께 공유할 것.", prefabAsset);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Assign(SerializedObject so, string field, Object value)
    {
        if (value == null) return;
        SerializedProperty p = so.FindProperty(field);
        if (p != null && p.objectReferenceValue == null) p.objectReferenceValue = value;
    }
}
