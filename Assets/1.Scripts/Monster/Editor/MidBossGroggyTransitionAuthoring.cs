using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 저작 도구 — SpinnerBot 컨트롤러에 **그로기 진입 전이**를 넣는다 (2026-09-08).
//
// 배경: 중간보스 카운터가 성공하면 `MonsterBase` 가 Groggy 로 가고 `groggyBool`(SpinnerBot = `IsDizzy`)을
// true 로 만든다. 그런데 `Controller_SpinBot` 의 `Dizzy` 진입 전이는 **`Spin Attack Loop` 에만** 걸려 있다.
// 즉 예비동작에서 카운터가 성공하면 **논리만 그로기고 화면은 그대로**다. AnyState 경로 하나가 없어서다.
// (이탈은 이미 있다 — `Dizzy → Movement`, `IsDizzy` false.)
//
// 🔴 왜 손으로 .controller 를 고치지 않고 도구로 만들었나
//    `Assets/50.Art` 는 **SVN** 이다. 아티스트가 컨트롤러를 다시 올리면 이 전이가 통째로 날아간다.
//    그때 이 메뉴를 다시 누르면 복구된다(멱등 — 이미 있으면 아무것도 하지 않는다).
//    같은 이유로 만들어진 선례가 <see cref="No23ClipEventAuthoring"/> 다.
//
// ⚠️ Gauntlet(`Controller_TrainerBot_Boss`)은 이 도구의 대상이 아니다. 그쪽은 그로기 파라미터·상태·클립이
//    **아예 없어서** 전이를 만들 대상이 없다. Gauntlet 의 그로기는 "Idle 자세로 끊고 정지"로 표현한다
//    (PLAN.md §9.3 — 리그가 Generic 이고 스켈레톤이 달라 클립을 가져올 길이 없다).
public static class MidBossGroggyTransitionAuthoring
{
    const string SpinnerControllerPath =
        "Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/" +
        "Robot Sentries Pack 02/Animations/SpinnerBot/Controller_SpinBot.controller";

    const string DizzyState = "Dizzy";
    const string DizzyParam = "IsDizzy";
    const float TransitionDuration = 0.1f;   // SafeCrossFade 와 같은 값 — 끊기는 느낌을 통일한다

    [MenuItem("Tools/Boss/중간보스 — SpinnerBot 그로기 진입 전이 보장 (멱등)")]
    public static void EnsureSpinnerDizzyEntry()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(SpinnerControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[MidBossGroggy] 컨트롤러를 못 찾았다: {SpinnerControllerPath}");
            return;
        }

        if (!HasBoolParameter(controller, DizzyParam))
        {
            Debug.LogError($"[MidBossGroggy] `{DizzyParam}` bool 파라미터가 없다 — 데이터의 groggyBool 과 어긋난다.");
            return;
        }

        AnimatorStateMachine root = controller.layers[0].stateMachine;
        AnimatorState dizzy = FindState(root, DizzyState);
        if (dizzy == null)
        {
            Debug.LogError($"[MidBossGroggy] `{DizzyState}` 상태를 못 찾았다.");
            return;
        }

        // 멱등 가드 — 이미 같은 전이가 있으면 손대지 않는다.
        foreach (AnimatorStateTransition t in root.anyStateTransitions)
        {
            if (t.destinationState != dizzy) continue;
            foreach (AnimatorCondition c in t.conditions)
            {
                if (c.parameter == DizzyParam && c.mode == AnimatorConditionMode.If)
                {
                    Debug.Log($"[MidBossGroggy] 이미 있다 — AnyState → {DizzyState} ({DizzyParam}). 변경 없음.");
                    return;
                }
            }
        }

        AnimatorStateTransition added = root.AddAnyStateTransition(dizzy);
        added.AddCondition(AnimatorConditionMode.If, 0f, DizzyParam);
        added.hasExitTime = false;          // 그로기는 즉시 들어가야 한다
        added.duration = TransitionDuration;
        added.canTransitionToSelf = false;  // 그로기 중 재진입으로 클립이 처음부터 다시 돌지 않게

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MidBossGroggy] 추가: AnyState → {DizzyState} ({DizzyParam} == true, exitTime 없음). " +
                  "🔴 SVN 커밋 필요 — 이 컨트롤러는 git 이 아니라 SVN 관리다.");
    }

    [MenuItem("Tools/Boss/중간보스 — 그로기 애니 경로 점검 (읽기 전용)")]
    public static void ReportGroggyPaths()
    {
        Report(SpinnerControllerPath, DizzyParam, DizzyState);
        Report("Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/" +
               "Robot Sentries Pack 03/Animations/TrainerBot/Controller_TrainerBot_Boss.controller",
               "Groggy", "Groggy");
        Report("Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/" +
               "Robot Sentries Pack 03/Animations/WallBot/Controller_WallBot.controller",
               "Groggy", "Groggy");
    }

    static void Report(string path, string param, string state)
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (c == null) { Debug.LogWarning($"[MidBossGroggy] 없음: {path}"); return; }

        bool hasParam = HasBoolParameter(c, param);
        AnimatorState st = FindState(c.layers[0].stateMachine, state);
        int entries = 0;
        foreach (AnimatorStateTransition t in c.layers[0].stateMachine.anyStateTransitions)
            if (t.destinationState == st) entries++;

        Debug.Log($"[MidBossGroggy] {c.name}: 파라미터 `{param}` {(hasParam ? "있음" : "없음")} · " +
                  $"상태 `{state}` {(st != null ? "있음" : "없음")} · AnyState 진입 {entries}개");
    }

    static bool HasBoolParameter(AnimatorController c, string name)
    {
        foreach (AnimatorControllerParameter p in c.parameters)
            if (p.name == name && p.type == AnimatorControllerParameterType.Bool) return true;
        return false;
    }

    // 서브 상태머신까지 훑는다 — 이 팩의 컨트롤러들은 공격 상태를 서브머신에 묶어 둔 것이 있다.
    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        var queue = new Queue<AnimatorStateMachine>();
        queue.Enqueue(sm);
        while (queue.Count > 0)
        {
            AnimatorStateMachine cur = queue.Dequeue();
            foreach (ChildAnimatorState cs in cur.states)
                if (cs.state != null && cs.state.name == name) return cs.state;
            foreach (ChildAnimatorStateMachine cm in cur.stateMachines)
                if (cm.stateMachine != null) queue.Enqueue(cm.stateMachine);
        }
        return null;
    }
}
