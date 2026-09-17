using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// [G5] 23호 컨트롤러에 **잡기 사이클용 상태 2종**을 만든다 — <c>MagneticGrab</c>(끌어당김) ·
/// <c>GrabEnd</c>(놓아주기).
///
/// 🔴 왜 도구인가 — 클립은 이미 fbx 에 있는데(`Boss_23_magneticgrab` · `Boss_23_grabend`)
///    컨트롤러에 **상태가 없어서** 코드가 이름으로 CrossFade 해도 조용히 무시된다.
///    이 프로젝트에서 "문자열 상태명이 조용히 무시되는" 사고가 반복돼 왔다.
///
/// 🔴 왜 손으로 안 하나 — 컨트롤러는 git, fbx/`.meta` 는 **SVN** 이다. 아트가 fbx 를 다시 올리면
///    클립 참조가 흔들릴 수 있는데, 그때 이 메뉴를 다시 누르면 같은 상태가 복구된다(멱등).
///    `No23ClipEventAuthoring` 이 임포터 설정에 대해 하는 것과 같은 역할이다.
///
/// 멱등이다 — 이미 있으면 모션만 맞추고 상태를 새로 만들지 않는다.
public static class No23GrabStatesAuthoring
{
    const string ControllerPath =
        "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";

    // 상태명 → 클립명. 🔴 상태명은 `No23.asset` 의 문자열 필드와 **반드시 같아야 한다**.
    static readonly (string state, string clip)[] Wanted =
    {
        ("MagneticGrab", "Boss_23_magneticgrab"),
        ("GrabEnd",      "Boss_23_grabend"),

        // [G6] 인터럽트 성공 리액션 — 기존 `getowned` 는 **L 클립 하나뿐**이었다.
        // 잡기는 항상 R, 돌진은 L·R 난수라 오른쪽 상태가 따로 있어야 한다(팀장 확정 R1).
        ("getowned_R",   "Boss_23_getowned_R"),
    };

    [MenuItem("Tools/Boss/No23/Add Grab Cycle States")]
    public static void AddGrabCycleStates()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[No23] 컨트롤러를 찾지 못했다: {ControllerPath}");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        int added = 0, fixedMotion = 0, missingClip = 0;
        foreach ((string stateName, string clipName) in Wanted)
        {
            AnimationClip clip = FindClip(controller, clipName);
            if (clip == null)
            {
                // 🔴 클립을 못 찾으면 **상태를 만들지 않는다.** 모션이 빈 상태를 만들어 두면
                //    CrossFade 는 성공하는데 아무 동작도 안 나와 원인을 찾기가 더 어렵다.
                Debug.LogError($"[No23] 클립 '{clipName}' 을 컨트롤러가 참조하는 fbx 에서 찾지 못했다 — " +
                               $"'{stateName}' 상태를 만들지 않는다. SVN 에서 fbx 를 받았는지 확인할 것.");
                missingClip++;
                continue;
            }

            AnimatorState existing = FindState(sm, stateName);
            if (existing != null)
            {
                if (existing.motion != clip)
                {
                    existing.motion = clip;
                    fixedMotion++;
                    Debug.Log($"[No23] '{stateName}' 의 모션을 '{clipName}' 으로 맞췄다.");
                }
                continue;
            }

            AnimatorState state = sm.AddState(stateName);
            state.motion = clip;
            state.speed = 1f;
            // 🔴 나가는 전이를 만들지 않는다 — 잡기 사이클은 **코드가 단계마다 CrossFade** 로 몬다
            //    (23호의 관용구 2). 전이를 두면 코드 타이머와 그래프가 서로 다른 시각에 상태를 바꾼다.
            added++;
            Debug.Log($"[No23] 상태 '{stateName}' 추가 (모션 '{clipName}').");
        }

        if (added > 0 || fixedMotion > 0)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[No23] 잡기 사이클 상태 정리 완료 — 추가 {added} · 모션 수정 {fixedMotion} · " +
                  $"클립 없음 {missingClip}. 컨트롤러: {ControllerPath}");
    }

    /// <summary>컨트롤러가 이미 쓰고 있는 클립들 중에서 이름으로 찾는다(같은 fbx 를 쓰게 보장).</summary>
    static AnimationClip FindClip(AnimatorController controller, string clipName)
    {
        foreach (AnimationClip c in controller.animationClips)
            if (c != null && c.name == clipName) return c;

        // 컨트롤러가 아직 안 쓰는 클립이면 23호 fbx 에서 직접 찾는다.
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/50.Art/Char/Boss" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && c.name == clipName) return c;
        }
        return null;
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state != null && cs.state.name == name) return cs.state;
        return null;
    }
}
