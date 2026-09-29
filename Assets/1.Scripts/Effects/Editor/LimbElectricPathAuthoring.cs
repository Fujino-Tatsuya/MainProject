using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 23호의 <b>팔다리 전기</b> 경로를 본 이름으로 채우는 저작 도구.
///
/// <b>왜 도구인가.</b> 경로는 4갈래 × 4점 = <b>16개</b>고, 전부 중첩 FBX 인스턴스 안의 본이다.
/// 인스펙터로 끌어다 넣으면 하나 잘못 넣어도 티가 안 난다 — 펄스가 엉뚱한 관절을 지날 뿐
/// 에러도 경고도 없다. 이름으로 찾아 넣으면 그 실수가 원천적으로 없고,
/// <b>리깅이 바뀌어도 다시 돌리면 그만</b>이다(잡기 소켓 저작 도구와 같은 이유).
///
/// <b>왜 프리팹 텍스트를 직접 못 쓰나.</b> 중첩 인스턴스의 트랜스폼은 <c>--- !u!4 &amp;id stripped</c>
/// 스텁으로 참조되는데, 그 fileID 는 유니티가 <b>본 이름을 해시해</b> 만든다. 손으로 지어낼 수 없다.
///
/// 🔴 <b>보스 루트 전체를 뒤지면 안 된다.</b> 23호 프리팹은 <b>Wells 를 자식으로 품고 있고</b>,
/// Wells 도 같은 Auto-Rig Pro 리그라 <c>hand.l</c>·<c>foot.r</c>·<c>c_spine_02.x</c> …
/// <b>쓰는 본 이름 14개가 전부 겹친다</b>. 이름만 보고 먼저 만난 것을 집으면 절반 이상이
/// Wells 의 본으로 간다(2026-09-28 실제로 그랬다 — 16개 중 12개). 그래서 탐색 범위를
/// <b>보스가 실제로 돌리는 Animator 의 하위</b>로 좁힌다. 그 Animator 는 정의상 23호 리그 위에 있다.
///
/// <b>쓰는 법</b>: 보스 프리팹을 <b>Prefab Mode 로 열고</b> (또는 씬 인스턴스를 선택하고)
/// <c>Tools/Effects/23호 팔다리 전기 경로 채우기</c>. 멱등이라 여러 번 돌려도 된다.
/// </summary>
public static class LimbElectricPathAuthoring
{
    const string MenuPath = "Tools/Effects/23호 팔다리 전기 경로 채우기";

    /// <summary>
    /// 갈래 하나의 정의. <b>순서가 곧 펄스가 지나갈 순서</b>다.
    ///
    /// 팔은 가슴(<c>c_spine_02.x</c>), 다리는 골반(<c>c_root_master.x</c>)에서 출발한다 —
    /// 가슴에서 다리로 내려가면 몸통을 가로지르는 긴 구간이 생겨 관절에서 속도가 튄다.
    /// </summary>
    struct Limb
    {
        public string host;          // EffectPathPlayer 가 붙어 있는 오브젝트 이름
        public string[] bones;       // 경로 (시작 → 끝)

        public Limb(string host, params string[] bones) { this.host = host; this.bones = bones; }
    }

    static readonly Limb[] Limbs =
    {
        new Limb("LimbElectricVFX_ArmL", "c_spine_02.x",    "c_shoulder.l", "forearm.l",  "hand.l"),
        new Limb("LimbElectricVFX_ArmR", "c_spine_02.x",    "c_shoulder.r", "forearm.r",  "hand.r"),
        new Limb("LimbElectricVFX_LegL", "c_root_master.x", "c_thigh_b.l",  "c_leg_fk.l", "foot.l"),
        new Limb("LimbElectricVFX_LegR", "c_root_master.x", "c_thigh_b.r",  "c_leg_fk.r", "foot.r"),
    };

    [MenuItem(MenuPath)]
    public static void Run()
    {
        if (!ResolveScopes(out GameObject root, out Transform boneScope, out string error))
        {
            EditorUtility.DisplayDialog("팔다리 전기 경로", error, "확인");
            return;
        }

        // 🔴 본 색인은 **boneScope 하위만** 훑는다. root 를 훑으면 Wells 본이 섞인다(클래스 주석 참조).
        var byName = new Dictionary<string, Transform>();
        var duplicated = new List<string>();
        foreach (Transform t in boneScope.GetComponentsInChildren<Transform>(true))
        {
            if (byName.ContainsKey(t.name)) duplicated.Add(t.name);
            else byName.Add(t.name, t);
        }

        int filled = 0;
        var problems = new List<string>();

        foreach (Limb limb in Limbs)
        {
            // 호스트(LimbElectricVFX_*)는 모델 밖(Effects/)에 있으므로 root 에서 찾는다.
            EffectPathPlayer player = FindHost(root.transform, limb.host);
            if (player == null)
            {
                problems.Add($"'{limb.host}' 를 찾지 못했거나 EffectPathPlayer 가 없다");
                continue;
            }

            var bones = new Transform[limb.bones.Length];
            bool ok = true;
            for (int i = 0; i < limb.bones.Length; i++)
            {
                if (byName.TryGetValue(limb.bones[i], out Transform bone)) bones[i] = bone;
                else { problems.Add($"본 '{limb.bones[i]}' 없음 ({limb.host})"); ok = false; }
            }
            if (!ok) continue;

            // path 는 private 직렬화 필드라 SerializedObject 로 쓴다.
            // 🔴 리플렉션으로 직접 쓰면 Undo·더티 플래그가 안 붙어 저장되지 않는다.
            var so = new SerializedObject(player);
            SerializedProperty prop = so.FindProperty("path");
            Undo.RecordObject(player, "팔다리 전기 경로 채우기");
            prop.arraySize = bones.Length;
            for (int i = 0; i < bones.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = bones[i];
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(player);
            filled++;
        }

        // Prefab Mode 에서는 스테이지를, 씬 인스턴스면 씬을 더티로 만들어야 저장된다.
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) EditorSceneManager.MarkSceneDirty(stage.scene);
        else EditorSceneManager.MarkSceneDirty(root.scene);

        if (duplicated.Count > 0)
        {
            // 범위 안에서 이름이 겹치면 어느 쪽을 집었는지 보장할 수 없다 — 조용히 넘기지 않는다.
            Debug.LogWarning($"[팔다리 전기] 탐색 범위 '{boneScope.name}' 안에 이름이 중복된 오브젝트가 있다: " +
                             string.Join(", ", duplicated) + " — 경로가 엉뚱한 곳을 가리킬 수 있다.", boneScope);
        }

        if (problems.Count == 0)
            Debug.Log($"[팔다리 전기] {filled}갈래 경로를 채웠다 (탐색 범위 '{boneScope.name}'). 저장할 것(Ctrl+S).", root);
        else
            Debug.LogWarning($"[팔다리 전기] {filled}갈래 성공 / 실패 {problems.Count}건:\n  " +
                             string.Join("\n  ", problems), root);
    }

    /// <summary>
    /// 작업 대상(root)과 <b>본 탐색 범위</b>(boneScope)를 가른다.
    ///
    /// boneScope 는 <b>보스가 실제로 돌리는 Animator 의 트랜스폼</b>이다. 이름으로 모델 루트를 찾거나
    /// <c>GetComponentInChildren&lt;Animator&gt;</c> 로 아무거나 집으면 Wells 를 잡을 수 있다 —
    /// Wells 도 자기 Animator 를 갖고 있고 계층 순서는 보장되지 않는다.
    /// 반면 <c>MonsterBase.animator</c> 는 이 보스가 <c>No23Controller</c> 를 물려 둔 그 컴포넌트라
    /// 정의상 23호 리그 위에 있다.
    /// </summary>
    static bool ResolveScopes(out GameObject root, out Transform boneScope, out string error)
    {
        boneScope = null;
        error = null;

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        root = stage != null
            ? stage.prefabContentsRoot
            : (Selection.activeGameObject != null ? Selection.activeGameObject.transform.root.gameObject : null);

        if (root == null)
        {
            error = "보스 프리팹을 Prefab Mode 로 열거나, 씬의 보스 인스턴스를 선택한 뒤 다시 실행할 것.";
            return false;
        }

        var boss = root.GetComponent<MonsterBase>();
        if (boss == null)
        {
            error = $"'{root.name}' 루트에 MonsterBase 가 없다. 보스 프리팹의 최상위에서 실행할 것.";
            return false;
        }

        // protected [SerializeField] 라 코드로는 못 읽는다 — 직렬화 경로로 읽는다.
        SerializedProperty prop = new SerializedObject(boss).FindProperty("animator");
        var animator = prop != null ? prop.objectReferenceValue as Animator : null;
        if (animator == null)
        {
            error = $"'{root.name}' 의 MonsterBase.animator 가 비어 있어 23호 리그를 특정할 수 없다. " +
                    "인스펙터에서 먼저 연결할 것.";
            return false;
        }

        boneScope = animator.transform;
        return true;
    }

    /// <summary>이름으로 <see cref="EffectPathPlayer"/> 호스트를 찾는다. 이쪽은 모델 밖이라 root 전체를 본다.</summary>
    static EffectPathPlayer FindHost(Transform root, string name)
    {
        foreach (EffectPathPlayer p in root.GetComponentsInChildren<EffectPathPlayer>(true))
        {
            if (p.name == name) return p;
        }
        return null;
    }
}
