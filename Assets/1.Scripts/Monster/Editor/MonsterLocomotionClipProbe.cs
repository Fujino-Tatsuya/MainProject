using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 이동 클립 고유 속도 측정(PLAN-monster-anim-speed S4).
///
/// 루트모션이 없는 클립이라 <b>땅에 닿은 발이 뒤로 밀리는 속도</b>를 잰다 = 재생 속도 1 에서 몸이 앞으로 가는 속도.
///  - 다리: 매 샘플에서 더 낮은 발 = 디딘 발. 그 발의 전방(z) 변위 ÷ 시간의 중앙값.
///  - 바퀴: 회전 각속도 × 반경(바퀴 본 높이 ≈ 반경 — 바닥이 루트 높이라고 가정).
///  - 본이 없으면(SpinnerBot) 측정 불가 → 0(맞추지 않음).
/// 클립 시간 → 실제 시간: × (블렌드 자식 timeScale × 상태 speed).
///
/// 결과 = 블렌드 마지막 자식(가장 빠른 이동 클립)의 W 와 그 임계값 th. 런타임은 재생 속도 = max(v, th) / W.
/// 🔴 근사다 — 발 본 원점 기준·블렌드 선형 가정. 표를 보고 이상하면 SO 에서 직접 고친다.
/// </summary>
static class MonsterLocomotionClipProbe
{
    static readonly string[] Prefabs =
    {
        "ChompBot", "GauntletBot", "HumanoidBot", "MortarBot", "SpinnerBot", "WallBot",
    };

    const int Samples = 120;

    struct Result
    {
        public string prefab, clip, method;
        public float W, th, idleCycle, moveCycle;
        public bool ok;   // 측정이 끝까지 됐는가 — false 면 SO 에 쓰지 않는다(기존 값 보존)
        public MonsterDataSO data;
    }

    [MenuItem("Tools/Monster/이동 클립 고유 속도 측정 (보고만)")]
    static void ReportOnly() => Run(false);

    [MenuItem("Tools/Monster/이동 클립 고유 속도 측정 → SO 기록")]
    static void ReportAndWrite() => Run(true);

    static void Run(bool write)
    {
        // 🔴 Play 중엔 SO 가 데이터 테이블 값으로 메모리에서 덮여 있다 — 여기서 저장하면 그 값이 디스크에 박힌다(10-03 교차검증).
        if (write && EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[LocomotionProbe] Play 중에는 SO 에 기록하지 않는다 — Play 를 끄고 다시 실행할 것.");
            return;
        }
        var sb = new StringBuilder($"[LocomotionProbe] {(write ? "기록" : "보고만")}\n");
        foreach (string name in Prefabs)
        {
            Result r = Measure(name, sb);
            sb.AppendLine($"  ⇒ {name}: W={r.W:0.###} m/s · th={r.th:0.###} · 주기 대기 {r.idleCycle:0.###}s / 이동 {r.moveCycle:0.###}s ({r.method}, {r.clip})");
            if (!write || r.data == null) continue;
            if (!r.ok) { sb.AppendLine($"  ⚠️ {name}: 측정 실패 — SO 를 건드리지 않는다"); continue; }

            Undo.RecordObject(r.data, "Locomotion clip speed");
            r.data.locomotionClipSpeed = r.W;
            r.data.locomotionFullBlendSpeed = r.th;
            r.data.locomotionIdleCycleSeconds = r.idleCycle;
            r.data.locomotionMoveCycleSeconds = r.moveCycle;
            EditorUtility.SetDirty(r.data);
        }
        if (write) AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
    }

    static Result Measure(string name, StringBuilder sb)
    {
        var res = new Result { prefab = name, method = "실패" };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/2.Prefabs/Monster/{name}.prefab");
        if (prefab == null) { sb.AppendLine($"── {name}: 프리팹 없음"); return res; }

        var monster = prefab.GetComponentInChildren<MonsterBase>(true);
        res.data = monster != null
            ? new SerializedObject(monster).FindProperty("data").objectReferenceValue as MonsterDataSO
            : null;
        if (res.data == null) { sb.AppendLine($"── {name}: 데이터 없음"); return res; }

        var go = (GameObject)Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = go.GetComponentInChildren<Animator>(true);
            var rac = res.data.animatorControllerOverride != null ? res.data.animatorControllerOverride
                    : animator != null ? animator.runtimeAnimatorController : null;
            var ac = rac as AnimatorController;
            if (ac == null && rac is AnimatorOverrideController oc) ac = oc.runtimeAnimatorController as AnimatorController;
            if (ac == null) { sb.AppendLine($"── {name}: 컨트롤러 없음"); return res; }

            var state = ac.layers[0].stateMachine.states.Select(s => s.state)
                .FirstOrDefault(s => s.name == res.data.locomotionState);
            if (state == null || !(state.motion is BlendTree bt) || bt.children.Length < 2)
            { sb.AppendLine($"── {name}: 이동 블렌드 없음"); return res; }

            var ordered = bt.children.OrderBy(c => c.threshold).ToArray();
            var top = ordered.Last();
            var low = ordered.First();
            var clip = top.motion as AnimationClip;
            if (clip == null) { sb.AppendLine($"── {name}: 이동 클립 없음"); return res; }
            if (ordered.Length != 2 || low.threshold > 0.001f)
                sb.AppendLine($"  ⚠️ 자식 {ordered.Length}개 · 최저 임계값 {low.threshold:0.##} — 정책은 '0 에서 대기 ↔ th 에서 이동' 2자식 1D 를 가정한다. 값을 확인할 것");

            res.clip = clip.name;
            res.th = Mathf.Max(0f, top.threshold);
            float stateSpeed = Mathf.Approximately(state.speed, 0f) ? 1f : state.speed;
            float rate = Mathf.Max(0.01f, top.timeScale * stateSpeed);
            res.moveCycle = clip.length / rate;
            if (low.motion is AnimationClip idleClip)
                res.idleCycle = idleClip.length / Mathf.Max(0.01f, low.timeScale * stateSpeed);

            Transform root = animator.transform;
            var all = animator.GetComponentsInChildren<Transform>(true);
            var feet = all.Where(t => t.name.ToLowerInvariant().Contains("foot")).ToList();
            if (feet.Count < 2)
                feet = all.Where(t => { var n = t.name.ToLowerInvariant(); return n.StartsWith("leg") || n.Contains(".leg") || n.Contains("_leg"); })
                          .Where(t => !t.name.ToLowerInvariant().Contains("fore") && !t.name.ToLowerInvariant().Contains("lowe")).ToList();
            var wheels = all.Where(t => t.name.ToLowerInvariant().Contains("wheel")).ToList();

            sb.AppendLine($"── {name}: clip={clip.name} len={clip.length:0.###}s th={res.th:0.##} rate={rate:0.##} " +
                          $"feet=[{string.Join(",", feet.Select(f => f.name))}] wheels=[{string.Join(",", wheels.Select(w => w.name))}]");

            if (feet.Count >= 2)
            {
                float clipSpeed = MeasureFeet(animator.gameObject, root, clip, feet.Take(2).ToArray(), sb);
                res.W = clipSpeed * rate;
                res.method = "발";
                res.ok = res.W > 0f;
            }
            else if (wheels.Count >= 1)
            {
                float clipSpeed = MeasureWheel(animator.gameObject, root, clip, wheels[0], sb);
                res.W = clipSpeed * rate;
                res.method = "바퀴";
                res.ok = res.W > 0f;
            }
            else
            {
                sb.AppendLine("  측정 불가 — 발·바퀴 본 없음. 0(맞추지 않음)으로 둔다.");
                res.method = "없음";
                res.ok = true;   // 측정할 본이 없다는 것까지 확인됨 = 0(맞춤 끔)이 정답
            }
            return res;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 디딘 발(더 낮은 발)의 전방 변위 속도 중앙값(클립 시간 기준 m/s).
    static float MeasureFeet(GameObject animGo, Transform root, AnimationClip clip, Transform[] feet, StringBuilder sb)
    {
        var pos = new Vector3[feet.Length, Samples + 1];
        for (int i = 0; i <= Samples; i++)
        {
            clip.SampleAnimation(animGo, clip.length * i / Samples);
            for (int f = 0; f < feet.Length; f++) pos[f, i] = root.InverseTransformPoint(feet[f].position);
        }

        float dt = clip.length / Samples;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int f = 0; f < feet.Length; f++)
            for (int i = 0; i <= Samples; i++) { minY = Mathf.Min(minY, pos[f, i].y); maxY = Mathf.Max(maxY, pos[f, i].y); }
        float groundBand = minY + (maxY - minY) * 0.25f;   // 바닥 근처 25% = 디딘 것으로 본다

        var speeds = new List<float>();
        for (int i = 0; i < Samples; i++)
        {
            int planted = pos[0, i].y <= pos[1, i].y ? 0 : 1;
            if (pos[planted, i].y > groundBand || pos[planted, i + 1].y > groundBand) continue;
            float dz = pos[planted, i + 1].z - pos[planted, i].z;   // 디딘 발은 뒤로(−z) 간다
            speeds.Add(-dz / dt);
        }
        if (speeds.Count == 0) { sb.AppendLine("  디딘 구간 없음"); return 0f; }

        speeds.Sort();
        float median = speeds[speeds.Count / 2];
        sb.AppendLine($"  발 샘플 {speeds.Count}/{Samples} · 중앙값 {median:0.###} · 범위 {speeds[0]:0.##}~{speeds[speeds.Count - 1]:0.##} (클립 시간 m/s) · 발 높이 {minY:0.###}~{maxY:0.###}");
        return Mathf.Max(0f, median);
    }

    // 바퀴 각속도(rad/s, 클립 시간) × 반경(바퀴 본의 루트 기준 높이).
    static float MeasureWheel(GameObject animGo, Transform root, AnimationClip clip, Transform wheel, StringBuilder sb)
    {
        float dt = clip.length / Samples, total = 0f, radius = 0f;
        clip.SampleAnimation(animGo, 0f);
        Quaternion prev = wheel.localRotation;
        for (int i = 1; i <= Samples; i++)
        {
            clip.SampleAnimation(animGo, clip.length * i / Samples);
            Quaternion cur = wheel.localRotation;
            total += Quaternion.Angle(prev, cur) * Mathf.Deg2Rad;
            prev = cur;
            radius += root.InverseTransformPoint(wheel.position).y;
        }
        radius /= Samples;
        float omega = total / clip.length;
        sb.AppendLine($"  바퀴 {wheel.name}: 각속도 {omega:0.###} rad/s · 반경(본 높이) {radius:0.###} m · dt {dt:0.####}");
        return omega * Mathf.Max(0f, radius);
    }
}
