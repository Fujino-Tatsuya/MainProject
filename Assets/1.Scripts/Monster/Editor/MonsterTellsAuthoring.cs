using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 일반 몹 공격 예고 준비(PLAN-monster-tells). <b>멱등</b> — 이미 된 단계는 건너뛴다.
/// ① 발광 재질 <c>Resources/Telegraph/MA_AttackGlow</c>(코드가 불러 쓴다 — 빌드에 셰이더 포함)
/// ② Chomp 컨트롤러: Movement →(Attack 트리거)→ <b>AttackAnticipation</b>(FBX 0~30 프레임, 재생 속도로 ≈0.55초) → Attack
///    🔴 컨트롤러·FBX .meta 는 50.Art = <b>SVN</b> — 결과를 SVN 에 커밋해야 다른 PC 에 간다.
/// ③ Chomp 턱 분리: 스킨드 메시를 본(UpperJaw·LowerJaw) 기준으로 몸통/턱 두 메시로 굽고 턱 렌더러를 형제로 만든다
///    (본·rootBone 공유 — 스키닝 총량 불변, FBX·아바타·본 경로 무변경 — SpinnerBotMeshSplit 선례). 메시 사본 = SVN.
///    + <see cref="MonsterAttackTell"/>(턱 발광).
/// ④ Humanoid: <see cref="MonsterAttackTell"/>(도끼 G_SlendertBot_Weapon 발광 — 중첩 아트 프리팹 안이라 이름으로 찾는다).
/// ⑤ Mortar 데이터: rangedStyle = SkyStrike.
/// </summary>
public static class MonsterTellsAuthoring
{
    const string GlowShaderName = "Monster/AttackGlow";
    const string GlowMaterialPath = "Assets/Resources/Telegraph/MA_AttackGlow.mat";

    const string ChompPrefabPath = "Assets/2.Prefabs/Monster/ChompBot.prefab";
    const string HumanoidPrefabPath = "Assets/2.Prefabs/Monster/HumanoidBot.prefab";
    const string MortarDataPath = "Assets/2.Prefabs/Monster/Data/MortarBotData.asset";

    const string ChompArtDir = "Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/Robot Sentries Pack 01/Animations/ChompBot";
    const string ChompControllerPath = ChompArtDir + "/Controller_ChompBot.controller";
    const string ChompAttackFbxPath = ChompArtDir + "/A_Chompbot_Attack.fbx";
    const string AnticipationClipName = "Anticipation Attack";
    const string AnticipationStateName = "AttackAnticipation";
    // FBX 0~30 프레임(30fps = 1초) → 재생 속도 1.8 ≈ 0.55초. 물기 몹 정체성 유지(Humanoid 0.85초보다 짧게) — Play 로 조정.
    const float ChompAnticipationSpeed = 1.8f;

    const string ChompBodyRendererName = "ChumpBot";
    const string ChompJawObjectName = "ChompBot_Jaw";
    static readonly string[] JawBoneNames = { "UpperJaw", "LowerJaw" };

    [MenuItem("Tools/Monster/몬스터 공격 예고 준비 (멱등)")]
    static void Run()
    {
        EnsureGlowMaterial();
        EnsureChompAnticipation();
        EnsureChompJaw();
        EnsureHumanoidTell();
        EnsureMortarSkyStrike();
        AssetDatabase.SaveAssets();
        Debug.Log("[MonsterTells] 준비 완료");
    }

    // ① ───────────────────────────────────────────────────────────
    static void EnsureGlowMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath) != null) return;
        Shader s = Shader.Find(GlowShaderName);
        if (s == null) { Debug.LogError($"[MonsterTells] 셰이더 '{GlowShaderName}' 없음"); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Telegraph")) AssetDatabase.CreateFolder("Assets/Resources", "Telegraph");
        AssetDatabase.CreateAsset(new Material(s) { name = "MA_AttackGlow" }, GlowMaterialPath);
        Debug.Log($"[MonsterTells] 생성 {GlowMaterialPath}");
    }

    // ② ───────────────────────────────────────────────────────────
    static void EnsureChompAnticipation()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ChompControllerPath);
        if (ctrl == null) { Debug.LogError($"[MonsterTells] {ChompControllerPath} 없음"); return; }
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        if (sm.states.Any(s => s.state.name == AnticipationStateName)) { Debug.Log("[MonsterTells] Chomp 예비동작 상태 이미 있음"); return; }

        AnimatorState attack = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Attack");
        if (attack == null) { Debug.LogError("[MonsterTells] Chomp 컨트롤러에 Attack 상태가 없다"); return; }

        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ChompAttackFbxPath).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == AnticipationClipName);
        if (clip == null)
        {
            Debug.LogError($"[MonsterTells] {ChompAttackFbxPath} 에 '{AnticipationClipName}' 클립이 없다 — FBX .meta 의 clipAnimations 를 확인(0~30 프레임).");
            return;
        }

        Vector3 attackPos = sm.states.First(s => s.state == attack).position;
        AnimatorState ant = sm.AddState(AnticipationStateName, attackPos + new Vector3(-220f, 0f, 0f));
        ant.motion = clip;
        ant.speed = ChompAnticipationSpeed;
        ant.writeDefaultValues = attack.writeDefaultValues;

        // Attack 트리거로 Attack 에 들어가던 전이를 예비동작으로 돌린다(조건·길이 그대로).
        int redirected = 0;
        foreach (ChildAnimatorState cs in sm.states)
            foreach (AnimatorStateTransition t in cs.state.transitions)
                if (t.destinationState == attack && t.conditions.Any(c => c.parameter == "Attack")) { t.destinationState = ant; redirected++; }
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
            if (t.destinationState == attack && t.conditions.Any(c => c.parameter == "Attack")) { t.destinationState = ant; redirected++; }

        AnimatorStateTransition next = ant.AddTransition(attack);
        next.hasExitTime = true;
        next.exitTime = 1f;
        next.duration = 0f;

        EditorUtility.SetDirty(ctrl);
        Debug.Log($"[MonsterTells] Chomp 예비동작 상태 추가 · 전이 {redirected}개 변경 · 속도 {ChompAnticipationSpeed} (SVN 커밋 필요)");
    }

    // ③ ───────────────────────────────────────────────────────────
    static void EnsureChompJaw()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ChompPrefabPath);
        try
        {
            SkinnedMeshRenderer body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(r => r.name == ChompBodyRendererName);
            if (body == null) { Debug.LogError($"[MonsterTells] Chomp 몸 렌더러 '{ChompBodyRendererName}' 없음"); return; }

            Transform jawT = body.transform.parent.Find(ChompJawObjectName);
            SkinnedMeshRenderer jaw = jawT != null ? jawT.GetComponent<SkinnedMeshRenderer>() : null;

            if (jaw == null)
            {
                Mesh src = body.sharedMesh;
                if (src.subMeshCount != 1) { Debug.LogError("[MonsterTells] Chomp 메시 서브메시가 1개가 아니다 — 분리 중단"); return; }
                if (src.blendShapeCount > 0) Debug.LogWarning("[MonsterTells] 블렌드셰이프는 복사하지 않는다");

                var jawBoneIdx = new HashSet<int>();
                for (int i = 0; i < body.bones.Length; i++)
                    if (body.bones[i] != null && JawBoneNames.Contains(body.bones[i].name)) jawBoneIdx.Add(i);
                if (jawBoneIdx.Count == 0) { Debug.LogError("[MonsterTells] 턱 본을 못 찾았다"); return; }

                string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(src)).Replace('\\', '/');
                Mesh bodyMesh = Split(src, jawBoneIdx, wantJaw: false, src.name + "_Body");
                Mesh jawMesh = Split(src, jawBoneIdx, wantJaw: true, src.name + "_Jaw");
                if (bodyMesh == null || jawMesh == null) return;
                bodyMesh = SaveMesh(bodyMesh, $"{dir}/{src.name}_Body.asset");
                jawMesh = SaveMesh(jawMesh, $"{dir}/{src.name}_Jaw.asset");

                var go = new GameObject(ChompJawObjectName) { layer = body.gameObject.layer };
                go.transform.SetParent(body.transform.parent, false);
                go.transform.localPosition = body.transform.localPosition;
                go.transform.localRotation = body.transform.localRotation;
                go.transform.localScale = body.transform.localScale;
                go.transform.SetSiblingIndex(body.transform.GetSiblingIndex() + 1);
                jaw = go.AddComponent<SkinnedMeshRenderer>();
                jaw.sharedMesh = jawMesh;
                jaw.bones = body.bones;
                jaw.rootBone = body.rootBone;
                jaw.sharedMaterials = body.sharedMaterials;
                jaw.localBounds = body.localBounds;
                jaw.updateWhenOffscreen = body.updateWhenOffscreen;
                jaw.quality = body.quality;
                jaw.shadowCastingMode = body.shadowCastingMode;
                jaw.receiveShadows = body.receiveShadows;
                jaw.renderingLayerMask = body.renderingLayerMask;
                body.sharedMesh = bodyMesh;
                Debug.Log($"[MonsterTells] Chomp 턱 분리 — 몸 {bodyMesh.vertexCount}정점 · 턱 {jawMesh.vertexCount}정점 (메시 사본 SVN 커밋 필요: {dir})");
            }

            var tell = root.GetComponent<MonsterAttackTell>();
            if (tell == null) tell = root.AddComponent<MonsterAttackTell>();   // 🔴 ?? 금지 — 없는 컴포넌트는 "가짜 null" 이라 통과한다
            var so = new SerializedObject(tell);
            so.FindProperty("target").objectReferenceValue = jaw;
            so.FindProperty("targetName").stringValue = ChompJawObjectName;
            so.FindProperty("anticipationState").stringValue = AnticipationStateName;
            so.FindProperty("strikeState").stringValue = "Attack";
            so.FindProperty("hitNormalizedTime").floatValue = 0.357f;   // A_Chompbot_Attack OnAttackHit
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, ChompPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 본 하나에 강체로 묶인 정점(정점당 본 1개 — ChompBot_FormReview_v5)을 기준으로 삼각형을 나눈다.
    // 삼각형의 세 정점이 모두 턱 본이면 턱, 아니면 몸통. 쓰지 않는 정점은 버린다(스키닝 총량 불변).
    static Mesh Split(Mesh src, HashSet<int> jawBones, bool wantJaw, string name)
    {
        BoneWeight[] bw = src.boneWeights;
        if (bw == null || bw.Length != src.vertexCount) { Debug.LogError("[MonsterTells] 본 가중치 없음"); return null; }
        bool IsJaw(int v) => jawBones.Contains(bw[v].boneIndex0) && bw[v].weight0 >= 0.5f;

        int[] tris = src.GetTriangles(0);
        var keep = new List<int>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            bool jaw = IsJaw(tris[i]) && IsJaw(tris[i + 1]) && IsJaw(tris[i + 2]);
            if (jaw == wantJaw) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
        }
        if (keep.Count == 0) { Debug.LogError($"[MonsterTells] {name}: 삼각형 0개 — 본 기준이 맞지 않는다"); return null; }

        var map = new Dictionary<int, int>();
        var order = new List<int>();
        foreach (int v in keep) if (!map.ContainsKey(v)) { map[v] = order.Count; order.Add(v); }

        T[] Pick<T>(T[] a) => a == null || a.Length != src.vertexCount ? null : order.Select(v => a[v]).ToArray();
        var m = new Mesh { name = name, indexFormat = order.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m.vertices = Pick(src.vertices);
        var n = Pick(src.normals); if (n != null) m.normals = n;
        var tg = Pick(src.tangents); if (tg != null) m.tangents = tg;
        var col = Pick(src.colors); if (col != null) m.colors = col;
        for (int ch = 0; ch < 4; ch++)
        {
            var uv = new List<Vector2>(); src.GetUVs(ch, uv);
            if (uv.Count == src.vertexCount) m.SetUVs(ch, order.Select(v => uv[v]).ToList());
        }
        m.boneWeights = Pick(bw);
        m.bindposes = src.bindposes;
        m.SetTriangles(keep.Select(v => map[v]).ToArray(), 0);
        m.RecalculateBounds();
        return m;
    }

    static Mesh SaveMesh(Mesh m, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); return existing; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ④ ───────────────────────────────────────────────────────────
    static void EnsureHumanoidTell()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HumanoidPrefabPath);
        try
        {
            var tell = root.GetComponent<MonsterAttackTell>();
            if (tell == null) tell = root.AddComponent<MonsterAttackTell>();   // 🔴 ?? 금지 — 없는 컴포넌트는 "가짜 null" 이라 통과한다
            var so = new SerializedObject(tell);
            Renderer weapon = root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "G_SlendertBot_Weapon");
            if (weapon == null) Debug.LogWarning("[MonsterTells] Humanoid 도끼 렌더러 G_SlendertBot_Weapon 을 못 찾았다 — 이름으로만 둔다(런타임 탐색)");
            // 중첩 아트 프리팹 안 렌더러 참조는 외부 프리팹에서 해석이 안 된 전례가 있다 → 참조 대신 이름.
            so.FindProperty("target").objectReferenceValue = null;
            so.FindProperty("targetName").stringValue = "G_SlendertBot_Weapon";
            so.FindProperty("anticipationState").stringValue = "AttackAnticipation";
            so.FindProperty("strikeState").stringValue = "AttackStrike";
            so.FindProperty("hitNormalizedTime").floatValue = 0.102f;   // A_Attack Strike OnAttackHit 0.1019
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HumanoidPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ⑤ ───────────────────────────────────────────────────────────
    static void EnsureMortarSkyStrike()
    {
        var data = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(MortarDataPath);
        if (data == null) { Debug.LogError($"[MonsterTells] {MortarDataPath} 없음"); return; }
        if (data.rangedStyle == MonsterRangedStyle.SkyStrike) return;
        data.rangedStyle = MonsterRangedStyle.SkyStrike;
        EditorUtility.SetDirty(data);
        Debug.Log("[MonsterTells] Mortar = SkyStrike");
    }
}
