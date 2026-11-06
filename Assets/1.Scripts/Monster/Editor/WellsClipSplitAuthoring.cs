#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 웰즈 애니 클립 분리(할 일 239) — SK_welz.fbx 서브클립 6개를 .anim 복사본으로 추출하고 WellsBossController 모션을 바꾼다.
/// 🔴 원본 FBX·.meta(SVN)는 읽기만 한다 — 임포트 설정·서브클립을 고치지 않는다.
///
/// · 출력 = Assets/4.Animations/Wells&No.23/Wells/Clips/{Idle|Move|Attack|Reaction|Death}/ (git). 클립 이름 = 파일 이름.
/// · 복사 = 플로트 커브(GetCurveBindings/GetEditorCurve) + ObjectReference 커브 + AnimationClipSettings(loopTime 등) + 이벤트.
///   구간을 자르는 클립은 끝 프레임 뒤 키를 지우고 끝 프레임/frameRate 지점에 원본 값으로 키를 남긴다. 프레임레이트 = FBX 클립 값.
/// · 다시 실행해도 같은 경로의 .anim 에 덮어써 GUID 가 유지된다.
/// · 컨트롤러: 기존 4상태(Idle·Throw·Groggy·Die) 모션 교체 + 상태 2개(yaho!·getownedV1) 추가 + 할 일 240 순간 클립용 Fear 상태·한 번짜리 → Idle exit-time 전이(ApplyCueStates). 그 밖 전이는 사용자가 다듬는다.
/// · 저장은 SaveAssetIfDirty 로 만든 .anim·컨트롤러만 — 씬·다른 에셋을 저장하지 않는다.
/// </summary>
public static class WellsClipSplitAuthoring
{
    const string FbxPath = "Assets/50.Art/Char/Boss/SK/SK_welz.fbx";
    const string ClipsRoot = "Assets/4.Animations/Wells&No.23/Wells/Clips";
    const string ControllerPath = "Assets/4.Animations/Wells&No.23/Wells/Controller/WellsBossController.controller";
    const int Whole = -1;

    struct Spec
    {
        public string Source, Output, Folder, State;
        public int EndFrame;   // Whole = 통째
        public bool NewState;  // true = 상태 추가, false = 기존 상태(State) 모션 교체
        public Spec(string source, string output, string folder, int endFrame, string state, bool newState)
        {
            Source = source; Output = output; Folder = folder; EndFrame = endFrame; State = state; NewState = newState;
        }
    }

    static readonly Spec[] Specs =
    {
        new Spec("Boss_welz_idle", "Boss_welz_idle", "Idle", Whole, "Idle", false),
        new Spec("Boss_welz_jump", "yaho!", "Move", 53, "yaho!", true),
        new Spec("Boss_welz_throwing", "Charge!", "Attack", Whole, "Throw", false),
        new Spec("Boss_welz_groggy", "Boss_welz_groggy", "Reaction", Whole, "Groggy", false),
        new Spec("Boss_welz_getowned", "getownedV1", "Reaction", 93, "getownedV1", true),
        new Spec("Boss_welz_die", "Fear", "Death", 122, "Die", false),
    };

    [MenuItem("Tools/Boss/웰즈 애니 클립 분리 (239)")]
    public static void Run()
    {
        Dictionary<string, AnimationClip> sources = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name, c => c);

        var report = new StringBuilder("[WellsClipSplit] 239 웰즈 클립 분리\n");
        var made = new Dictionary<string, AnimationClip>();
        foreach (Spec s in Specs)
        {
            if (!sources.TryGetValue(s.Source, out AnimationClip src))
            {
                Debug.LogError($"[WellsClipSplit] FBX 에 서브클립 {s.Source} 가 없다 — 중단.");
                return;
            }

            string folder = EnsureFolder(ClipsRoot + "/" + s.Folder);
            string path = folder + "/" + s.Output + ".anim";
            AnimationClip clip = Extract(src, s.Output, s.EndFrame);
            clip = SaveClip(clip, path);
            made[s.Output] = clip;

            int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
            report.AppendLine($"  {s.Source} → {path} : {clip.length:F3}s · {frames}f @ {clip.frameRate}fps · loop {AnimationUtility.GetAnimationClipSettings(clip).loopTime}");
        }

        ApplyController(made, report);
        Debug.Log(report.ToString());
    }

    // ─── 추출 ─────────────────────────────────────────────────────────

    static AnimationClip Extract(AnimationClip src, string name, int endFrame)
    {
        float fps = src.frameRate;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(src);
        bool trim = endFrame != Whole;
        float start = settings.startTime;
        float end = trim ? start + endFrame / fps : settings.stopTime;
        const float eps = 1e-4f;

        var dst = new AnimationClip { name = name, frameRate = fps, legacy = false };

        EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(src);
        var curves = new AnimationCurve[floatBindings.Length];
        for (int i = 0; i < floatBindings.Length; i++)
        {
            AnimationCurve c = AnimationUtility.GetEditorCurve(src, floatBindings[i]);
            curves[i] = trim ? TrimCurve(c, end, fps, eps) : c;
        }
        AnimationUtility.SetEditorCurves(dst, floatBindings, curves);

        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(src))
        {
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(src, b);
            AnimationUtility.SetObjectReferenceCurve(dst, b, trim ? TrimObjectCurve(keys, end, eps) : keys);
        }

        if (trim) settings.stopTime = end;
        AnimationUtility.SetAnimationClipSettings(dst, settings);

        AnimationEvent[] events = AnimationUtility.GetAnimationEvents(src);
        if (trim) events = events.Where(e => e.time <= end + eps).ToArray();
        AnimationUtility.SetAnimationEvents(dst, events);
        return dst;
    }

    // 끝 뒤 키 삭제 + 끝 지점에 원본 값 키(이미 있으면 그대로).
    static AnimationCurve TrimCurve(AnimationCurve c, float end, float fps, float eps)
    {
        if (c == null) return null;
        Keyframe[] keys = c.keys;
        var kept = new List<Keyframe>(keys.Length);
        bool hasEnd = false;
        foreach (Keyframe k in keys)
        {
            if (k.time > end + eps) continue;
            if (Mathf.Abs(k.time - end) <= eps) hasEnd = true;
            kept.Add(k);
        }
        if (!hasEnd)
        {
            float dt = 1f / fps;
            float value = c.Evaluate(end);
            float slope = (value - c.Evaluate(end - dt)) / dt;
            kept.Add(new Keyframe(end, value, slope, 0f));
        }
        var result = new AnimationCurve(kept.ToArray())
        {
            preWrapMode = c.preWrapMode,
            postWrapMode = c.postWrapMode,
        };
        return result;
    }

    static ObjectReferenceKeyframe[] TrimObjectCurve(ObjectReferenceKeyframe[] keys, float end, float eps)
    {
        var kept = new List<ObjectReferenceKeyframe>();
        Object last = null;
        bool hasLast = false, hasEnd = false;
        foreach (ObjectReferenceKeyframe k in keys)
        {
            if (k.time > end + eps) continue;
            if (Mathf.Abs(k.time - end) <= eps) hasEnd = true;
            kept.Add(k);
            last = k.value;
            hasLast = true;
        }
        if (hasLast && !hasEnd) kept.Add(new ObjectReferenceKeyframe { time = end, value = last });
        return kept.ToArray();
    }

    // ─── 저장 ─────────────────────────────────────────────────────────

    static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        // 다시 실행 — GUID 유지하며 내용만 덮어쓴다.
        EditorUtility.CopySerialized(clip, existing);
        existing.name = Path.GetFileNameWithoutExtension(path);
        Object.DestroyImmediate(clip);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    static string EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return path;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        return path;
    }

    // ─── 컨트롤러 ──────────────────────────────────────────────────────

    static void ApplyController(Dictionary<string, AnimationClip> made, StringBuilder report)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0)
        {
            Debug.LogError($"[WellsClipSplit] 컨트롤러 {ControllerPath} 를 못 읽었다 — 클립만 만들었다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        int added = 0;
        foreach (Spec s in Specs)
        {
            AnimationClip clip = made[s.Output];
            AnimatorState state = sm.states.Select(c => c.state).FirstOrDefault(st => st.name == s.State);
            if (state == null)
            {
                if (!s.NewState)
                {
                    Debug.LogWarning($"[WellsClipSplit] 기존 상태 {s.State} 가 없다 — 건너뜀(새로 만들지 않음).");
                    continue;
                }
                // 전이 없이 상태만 — 기존 상태 열(x 300) 오른쪽에 둔다.
                state = sm.AddState(s.State, new Vector3(560f, 60f * added, 0f));
                added++;
                report.AppendLine($"  컨트롤러: 상태 추가 {s.State} (전이 없음)");
            }
            else
            {
                report.AppendLine($"  컨트롤러: {s.State} 모션 → {clip.name}");
            }
            state.motion = clip;
        }

        ApplyCueStates(sm, made, report);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
    }

    // ─── 할 일 240 — 코드(BossWells 순간 클립)가 쓰는 상태·전이 ─────────────
    // 코드는 상태 이름으로 바로 CrossFade 한다(파라미터 없음). 여기서 만드는 것:
    //  · Fear 상태(Fear 클립, 나가는 전이 없음 — 제압 동안 마지막 프레임 유지). Die 는 그대로 Fear 클립.
    //  · yaho!·getownedV1 → Idle exit-time 전이(끝까지 재생 후 Idle). Throw(Charge!) → Idle 은 원래 있다.
    // 이미 있으면 건드리지 않는다(사용자가 다듬은 값 보존).
    const string FearState = "Fear";
    const string IdleState = "Idle";
    static readonly string[] OneShotStates = { "yaho!", "getownedV1", "Throw" };

    static void ApplyCueStates(AnimatorStateMachine sm, Dictionary<string, AnimationClip> made, StringBuilder report)
    {
        AnimatorState Find(string n) => sm.states.Select(c => c.state).FirstOrDefault(st => st.name == n);

        AnimatorState idle = Find(IdleState);
        if (idle == null)
        {
            Debug.LogWarning("[WellsClipSplit] Idle 상태가 없어 순간 클립 전이를 만들지 않았다.");
            return;
        }

        AnimatorState fear = Find(FearState);
        if (fear == null)
        {
            fear = sm.AddState(FearState, new Vector3(560f, 180f, 0f));
            report.AppendLine("  컨트롤러: 상태 추가 Fear (나가는 전이 없음 — 제압 동안 유지)");
        }
        fear.motion = made["Fear"];

        foreach (string name in OneShotStates)
        {
            AnimatorState st = Find(name);
            if (st == null)
            {
                Debug.LogWarning($"[WellsClipSplit] 상태 {name} 가 없어 Idle 복귀 전이를 못 만들었다.");
                continue;
            }
            if (st.transitions.Any(t => t.destinationState == idle && t.hasExitTime)) continue;

            AnimatorStateTransition tr = st.AddTransition(idle);
            tr.hasExitTime = true;
            tr.exitTime = 0.95f;
            tr.hasFixedDuration = true;
            tr.duration = 0.1f;
            report.AppendLine($"  컨트롤러: {name} → Idle exit-time 전이 추가(끝까지 재생 후 Idle)");
        }
    }
}
#endif
