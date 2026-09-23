#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// [G2-P] 23호 애니메이터에 <b>준비동작(prep) 상태 4종</b>을 만든다. <b>멱등</b> — 없는 것만 만든다.
///
/// 왜 스크립트인가 — 이 레포의 관용구다(CombatHudSlotAuthoring · TitleRigAuthoring ·
/// PlayerInterruptSkillAuthoring). 손으로 만들면 ① 상태명 오타 ② 클립 연결 누락
/// ③ 팀원이 같은 작업을 반복해야 함 이 세 가지가 매번 생긴다.
///
/// 🔴 <b>나가는 전이를 만들지 않는다.</b> prep 클립은 <c>loopTime: 0</c> 이라
///    전이가 없어야 마지막 프레임에서 멈춘다 — 그 "멈춤"이 예고 길이를 채우는 방식이다.
///    전이를 그리면 예고 중에 보스가 제멋대로 다음 상태로 넘어간다.
///
/// 🔴 <b>클립은 SVN(FBX) · 컨트롤러는 git 이다.</b> 한쪽만 받은 사람에게는 클립을 못 찾는데,
///    조용히 넘어가면 예고가 통째로 안 보인다 → 못 찾으면 LogError 로 크게 울린다.
/// </summary>
public static class Boss23PrepStateAuthoring
{
    const string ControllerPath = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";
    const string FbxPath        = "Assets/50.Art/Char/Boss/23_action_01_RiderSlot_x1_7.fbx";

    // 상태명 → 클립명. 상태명은 No23.asset 의 prepStateName 과 **반드시 일치**해야 한다.
    static readonly (string state, string clip)[] Prep =
    {
        ("LeftHookPrep",  "Boss_23_hookL_prep"),
        ("RightHookPrep", "Boss_23_hookR_prep"),
        ("UppercutPrep",  "Boss_23_uppercut_prep"),
        ("DashPrep",      "Boss_23_dash_prep"),
    };

    [MenuItem("Tools/Monster/Authoring/23호 prep 상태 생성 (멱등)")]
    public static void Run()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[Boss23Prep] 컨트롤러를 못 찾았다: {ControllerPath}");
            return;
        }

        Dictionary<string, AnimationClip> clips = LoadClips();
        if (clips == null) return;

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        int created = 0, rewired = 0, skipped = 0;

        foreach ((string state, string clip) in Prep)
        {
            if (!clips.TryGetValue(clip, out AnimationClip motion))
            {
                // 🔴 조용히 넘어가지 않는다 — SVN 을 안 받은 상태일 수 있고, 그러면 예고가 안 보인다.
                Debug.LogError(
                    $"[Boss23Prep] 클립 '{clip}' 을 FBX 에서 못 찾았다. " +
                    $"SVN 최신화(r322 이상)가 됐는지 확인할 것: {FbxPath}");
                continue;
            }

            AnimatorState existing = FindState(sm, state);
            if (existing == null)
            {
                AnimatorState s = sm.AddState(state);
                s.motion = motion;
                s.speed = 1f;
                s.writeDefaultValues = false;   // 컨트롤러의 다른 상태와 맞춘다
                created++;
                continue;
            }

            // 이미 있으면 **모션만** 다시 배선한다(클립이 교체됐을 수 있다). 위치·전이는 건드리지 않는다.
            if (existing.motion != motion) { existing.motion = motion; rewired++; }
            else skipped++;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Boss23Prep] 완료 — 생성 {created} / 재배선 {rewired} / 변경없음 {skipped}. " +
                  "⚠️ prep 상태에는 나가는 전이를 만들지 말 것(마지막 프레임 홀드가 깨진다).");
    }

    /// <summary>FBX 안의 클립을 이름으로 모은다. 서브에셋이라 LoadAllAssets 로 훑는다.</summary>
    static Dictionary<string, AnimationClip> LoadClips()
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        if (all == null || all.Length == 0)
        {
            Debug.LogError($"[Boss23Prep] FBX 를 못 찾았다(SVN 최신화 필요?): {FbxPath}");
            return null;
        }

        var map = new Dictionary<string, AnimationClip>();
        foreach (Object o in all)
        {
            // 🔴 프리뷰용 __preview__ 클립이 섞여 들어온다 — 이름으로 거른다.
            if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                map[c.name] = c;
        }
        return map;
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state != null && cs.state.name == name)
                return cs.state;
        return null;
    }
}
#endif
