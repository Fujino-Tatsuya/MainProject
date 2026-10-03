using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 몬스터 8종의 애니 상태별 클립 길이 · 상태 speed · 애니 이벤트 시각을 콘솔에 표로 낸다(읽기 전용).
///
/// 용도 — PLAN-monster-anim-speed: 쿨다운을 종료 기준으로 바꿀 때 "공격 길이" 를 재는 근거,
/// S4 이동 클립 맞춤의 출발점. 재생 시간(초) = 이벤트 시각 ÷ 상태 speed.
/// </summary>
static class MonsterAttackClipReport
{
    static readonly string[] Prefabs =
    {
        "ChompBot", "GauntletBot", "HumanoidBot", "MortarBot",
        "PeekABot", "SpinnerBot", "TeslaBot", "WallBot",
    };

    [MenuItem("Tools/Monster/공격 클립 길이 보고")]
    static void Report()
    {
        var sb = new StringBuilder("[AttackClipReport]\n");
        foreach (string name in Prefabs)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/2.Prefabs/Monster/{name}.prefab");
            if (go == null) { sb.AppendLine($"{name}: 프리팹 없음"); continue; }

            var monster = go.GetComponentInChildren<MonsterBase>(true);
            MonsterDataSO data = monster != null
                ? new SerializedObject(monster).FindProperty("data").objectReferenceValue as MonsterDataSO
                : null;
            var animator = go.GetComponentInChildren<Animator>(true);
            RuntimeAnimatorController rac = data != null && data.animatorControllerOverride != null
                ? data.animatorControllerOverride
                : animator != null ? animator.runtimeAnimatorController : null;

            var ac = rac as AnimatorController;
            if (ac == null && rac is AnimatorOverrideController oc) ac = oc.runtimeAnimatorController as AnimatorController;
            if (ac == null) { sb.AppendLine($"{name}: 컨트롤러 없음"); continue; }

            sb.AppendLine($"── {name} ({ac.name}) attackDuration={data?.attackDuration} cooldown={data?.attackCooldown} " +
                          $"move={data?.moveSpeed} chase={data?.chaseSpeed} loco={data?.locomotionState}");
            var bones = new List<string>();
            if (animator != null)
                foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("foot") || n.Contains("toe") || n.Contains("ankle") || n.Contains("wheel") || n.Contains("leg") || n.Contains("track"))
                        bones.Add(t.name);
                }
            sb.AppendLine($"  bones: {string.Join(", ", bones)}");
            for (int li = 0; li < ac.layers.Length; li++)
            foreach (var (state, path) in States(ac.layers[li].stateMachine, li == 0 ? "" : $"[L{li} {ac.layers[li].name}] "))
            {
                if (state.motion is BlendTree bt)
                {
                    sb.AppendLine($"  {path}: BlendTree param={bt.blendParameter} type={bt.blendType} speed={state.speed:0.##}");
                    foreach (var ch in bt.children)
                    {
                        var c = ch.motion as AnimationClip;
                        sb.AppendLine(c == null
                            ? $"    · {ch.motion?.name ?? "(없음)"} th={ch.threshold:0.###}"
                            : $"    · {c.name} th={ch.threshold:0.###} timeScale={ch.timeScale:0.##} len={c.length:0.###}s loop={c.isLooping} " +
                              $"root avgSpeed={c.averageSpeed.magnitude:0.###} (xyz {c.averageSpeed.x:0.##},{c.averageSpeed.y:0.##},{c.averageSpeed.z:0.##}) hasGenericRoot={c.hasGenericRootTransform}");
                    }
                    continue;
                }
                if (!(state.motion is AnimationClip clip)) { sb.AppendLine($"  {path}: (없음)"); continue; }
                float spd = Mathf.Approximately(state.speed, 0f) ? 1f : state.speed;
                var ev = new List<string>();
                foreach (var e in AnimationUtility.GetAnimationEvents(clip))
                    ev.Add($"{e.functionName}@{e.time:0.###}s(={e.time / spd:0.###}s)");
                sb.AppendLine($"  {path}: clip={clip.name} len={clip.length:0.###}s speed={spd:0.##} → 재생 {clip.length / spd:0.###}s | {string.Join(", ", ev)}");
            }
        }
        Debug.Log(sb.ToString());
    }

    static IEnumerable<(AnimatorState, string)> States(AnimatorStateMachine sm, string prefix)
    {
        foreach (var cs in sm.states) yield return (cs.state, prefix + cs.state.name);
        foreach (var sub in sm.stateMachines)
            foreach (var x in States(sub.stateMachine, prefix + sub.stateMachine.name + "/")) yield return x;
    }
}
