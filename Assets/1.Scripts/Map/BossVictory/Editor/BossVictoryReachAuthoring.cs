using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 '손 뻗기·걷기'(11-17) 저작 — 23호 컨트롤러에 상체 마스크 레이어(<see cref="BossVictoryReachRig.UpperLayer"/>, 기본 가중치 0)·
/// 손 뻗기 상태(<see cref="BossVictoryReachRig.UpperState"/> = Boss_23_magneticgrab, 속도 파라미터)·0층 걷기 상태(<see cref="BossVictoryReachRig.LowerState"/> = Boss_23_walk)를 넣는다.
/// 여러 번 눌러도 같은 결과(있으면 고침). 게임 중엔 레이어 가중치 0·전이 없음이라 영향 없음.
/// 23호 리그 = Generic(아바타 없음) → 마스크는 Transform 경로 방식. 상체 = 몸통 루트(c_root_master·c_root·root)·척추·가슴·어깨·팔·손·손가락, 하체(다리·발가락·꼬리)·rig·c_pos·c_traj = 0. 다른 Animator(웰즈) 아래는 넣지 않는다.
/// </summary>
public static class BossVictoryReachAuthoring
{
    public const string ControllerPath = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";
    public const string MaskPath = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23VictoryUpperBody.mask";
    const string BossPrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";
    const string GrabClipName = "Boss_23_magneticgrab";
    const string WalkClipName = "Boss_23_walk";

    static readonly Regex UpperBone = new Regex("spine|breast|neck|head|shoulder|arm|hand|index|middle|ring|pinky|thumb", RegexOptions.IgnoreCase);
    static readonly Regex LowerBone = new Regex("toes|foot|leg|thigh", RegexOptions.IgnoreCase);

    /// <summary>상체 마스크에 1 로 들어가는 뼈인가(경로 마지막 이름 기준 — 발가락 index/ring 같은 이름은 하체).</summary>
    // 몸통 루트(c_root_master.x·c_root.x·root.x)도 상체 — 이 리그(Auto-Rig Pro 납작 내보내기)는 팔이 척추가 아닌 c_traj 직속이라, 몸통 루트를 걷기에 두면 팔(손 뻗기 클립 c_traj 기준)과 몸통이 어긋나 어깨가 찢어진다. 다리는 c_traj 직속 형제라 안 끌려간다.
    static readonly System.Collections.Generic.HashSet<string> TorsoRoots = new System.Collections.Generic.HashSet<string> { "c_root_master.x", "c_root.x", "root.x" };

    public static bool IsUpperBone(string boneName) => TorsoRoots.Contains(boneName) || (UpperBone.IsMatch(boneName) && !LowerBone.IsMatch(boneName));

    [MenuItem("Tools/Boss Victory/보스 손 뻗기·걷기 레이어 만들기")]
    public static void Author()
    {
        string report = Run();
        Debug.Log(report);
    }

    public static string Run()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return $"[BossVictoryReach] {ControllerPath} 없음";
        AnimationClip grab = FindClip(controller, GrabClipName);
        AnimationClip walk = FindClip(controller, WalkClipName);
        if (grab == null || walk == null) return $"[BossVictoryReach] 클립 없음 grab={grab} walk={walk}";

        AvatarMask mask = AuthorMask(out int upper, out int total);
        if (mask == null) return $"[BossVictoryReach] {BossPrefabPath} 의 23호 Animator·rig 를 못 찾음";

        if (!controller.parameters.Any(p => p.name == BossVictoryReachRig.UpperSpeedParam))
            controller.AddParameter(new AnimatorControllerParameter { name = BossVictoryReachRig.UpperSpeedParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        if (!controller.parameters.Any(p => p.name == BossVictoryReachRig.WalkSpeedParam))
            controller.AddParameter(new AnimatorControllerParameter { name = BossVictoryReachRig.WalkSpeedParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        // 0층 걷기 — 전이 없음(처치 연출만 직접 튼다), 반복 클립이라 계속 걷는다.
        AnimatorStateMachine baseSm = controller.layers[0].stateMachine;
        AnimatorState walkState = FindState(baseSm, BossVictoryReachRig.LowerState) ?? baseSm.AddState(BossVictoryReachRig.LowerState, new Vector3(1000f, 600f, 0f));
        walkState.motion = walk;
        walkState.writeDefaultValues = false;
        walkState.speed = 1f;
        walkState.speedParameterActive = true;
        walkState.speedParameter = BossVictoryReachRig.WalkSpeedParam;
        EditorUtility.SetDirty(walkState);

        // 상체 레이어 — Override·마스크·기본 가중치 0(연출만 올림).
        int li = System.Array.FindIndex(controller.layers, l => l.name == BossVictoryReachRig.UpperLayer);
        if (li < 0)
        {
            controller.AddLayer(BossVictoryReachRig.UpperLayer);
            li = controller.layers.Length - 1;
        }
        AnimatorControllerLayer[] layers = controller.layers;
        layers[li].avatarMask = mask;
        layers[li].defaultWeight = 0f;
        layers[li].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[li].iKPass = false;
        controller.layers = layers;
        AnimatorStateMachine upperSm = controller.layers[li].stateMachine;
        AnimatorState empty = FindState(upperSm, "Empty") ?? upperSm.AddState("Empty", new Vector3(300f, 100f, 0f));
        empty.motion = null;
        empty.writeDefaultValues = false;
        upperSm.defaultState = empty;
        AnimatorState reach = FindState(upperSm, BossVictoryReachRig.UpperState) ?? upperSm.AddState(BossVictoryReachRig.UpperState, new Vector3(300f, 200f, 0f));
        reach.motion = grab;
        reach.writeDefaultValues = false;
        reach.speed = 1f;
        reach.speedParameterActive = true;
        reach.speedParameter = BossVictoryReachRig.UpperSpeedParam;
        EditorUtility.SetDirty(empty);
        EditorUtility.SetDirty(reach);
        EditorUtility.SetDirty(upperSm);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"[BossVictoryReach] 완료 — 레이어 '{BossVictoryReachRig.UpperLayer}'(#{li}) 상태 '{BossVictoryReachRig.UpperState}'={grab.name} · 0층 '{BossVictoryReachRig.LowerState}'={walk.name} · 마스크 상체 {upper}/{total} 뼈 ({MaskPath})";
    }

    static AvatarMask AuthorMask(out int upper, out int total)
    {
        upper = total = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        if (prefab == null) return null;
        Animator animator = prefab.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null && AssetDatabase.GetAssetPath(a.runtimeAnimatorController) == ControllerPath);
        if (animator == null) return null;
        Transform rig = animator.transform.Find("rig");
        if (rig == null) return null;

        var paths = new List<string>();
        var weights = new List<bool>();
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
        {
            if (UnderOtherAnimator(t, animator)) continue;   // 웰즈(탑승자) 등 자기 Animator 가 있는 하위 트리
            string path = AnimationUtility.CalculateTransformPath(t, animator.transform);
            bool on = IsUpperBone(t.name);
            paths.Add(path);
            weights.Add(on);
            if (on) upper++;
        }
        total = paths.Count;

        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        mask.transformCount = paths.Count;
        for (int i = 0; i < paths.Count; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, weights[i]);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static bool UnderOtherAnimator(Transform t, Animator self)
    {
        for (Transform p = t; p != null && p != self.transform; p = p.parent)
            if (p.TryGetComponent(out Animator a) && a != self) return true;
        return false;
    }

    [MenuItem("Tools/Boss Victory/보스 걷기 발 속도 재기")]
    public static void ReportWalkGroundSpeed()
    {
        float v = MeasureWalkGroundSpeed(out string detail);
        Debug.Log($"[BossVictoryReach] 걷기 발 속도 {v:0.000} m/s (애니 배율 1) — {detail}. 코드 기본값 BossVictoryReachRig.MeasuredWalkGroundSpeed = {BossVictoryReachRig.MeasuredWalkGroundSpeed}. 다르면 설정 SO bossReachWalkGroundSpeed 에 넣는다.");
    }

    /// <summary>
    /// Boss_23_walk 를 23호 임시 인스턴스에 샘플해 디딘 발(두 디폼 발 중 낮은 쪽)이 모델 기준 뒤로 가는 속도의 중앙값(m/s, 애니 배율 1) — 이만큼 앞으로 가면 발이 안 미끄러진다.
    /// 발 = rig/c_pos/c_traj/leg_stretch.*/foot.*(같은 이름의 소켓·FK 컨트롤 제외). 못 재면 0.
    /// </summary>
    public static float MeasureWalkGroundSpeed(out string detail)
    {
        detail = "";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        AnimationClip walk = controller != null ? FindClip(controller, WalkClipName) : null;
        if (prefab == null || walk == null) { detail = "프리팹·클립 없음"; return 0f; }
        var go = (GameObject)Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Animator animator = go.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController == controller);
            if (animator == null) { detail = "23호 Animator 없음"; return 0f; }
            Transform root = animator.transform;
            Transform footL = root.Find("rig/c_pos/c_traj/leg_stretch.l/foot.l");
            Transform footR = root.Find("rig/c_pos/c_traj/leg_stretch.r/foot.r");
            if (footL == null || footR == null) { detail = "발 뼈 경로 없음"; return 0f; }
            const int samples = 240;
            float dt = walk.length / samples;
            var l = new Vector3[samples + 1];
            var r = new Vector3[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                walk.SampleAnimation(animator.gameObject, walk.length * i / samples);
                l[i] = root.InverseTransformPoint(footL.position);
                r[i] = root.InverseTransformPoint(footR.position);
            }
            var v = new List<float>();
            for (int i = 1; i <= samples; i++)
            {
                bool leftDown = l[i].y + l[i - 1].y <= r[i].y + r[i - 1].y;
                Vector3[] p = leftDown ? l : r;
                v.Add(-(p[i].z - p[i - 1].z) / dt);
            }
            v.Sort();
            float median = v[v.Count / 2];
            detail = $"{walk.name} {walk.length:0.00}초 · {samples}샘플 · 몸 기준 디딘 발 뒤로 속도 중앙값 · 크기 {root.lossyScale.z:0.##}";
            return median;
        }
        finally { Object.DestroyImmediate(go); }
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    // 컨트롤러가 이미 쓰는 클립에서 이름으로(walk = Locomotion 블렌드 트리 자식, grab = MagneticGrab 상태).
    static AnimationClip FindClip(AnimatorController controller, string name) => controller.animationClips.FirstOrDefault(c => c != null && c.name == name);
}
