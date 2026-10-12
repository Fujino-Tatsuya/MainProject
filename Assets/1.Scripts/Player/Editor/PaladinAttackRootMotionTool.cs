using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 팔라딘 평타 4타 루트모션 재저작 도구 (PLAN-paladin-da C2).
/// 분석 = 읽기 전용. 클립을 Paladin 아바타에 샘플해 양발 궤적(캐릭터 로컬)을 콘솔·CSV 로 낸다.
/// </summary>
public static class PaladinAttackRootMotionTool
{
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Paladin/Paladin_Armature.prefab";
    private const string ClipFolder = "Assets/4.Animations/Player/Garen/";
    private const float SampleRate = 60f;
    // 이 높이(최저점 기준)·수평 속도 이하면 디딘 발로 본다.
    private const float PlantHeightTolerance = 0.03f;
    private const float PlantSpeedTolerance = 0.6f;

    [MenuItem("Tools/Player/Paladin/평타 루트모션 분석 (읽기 전용)")]
    private static void Analyze()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError($"[PaladinRootMotion] {ArmaturePath} 에 Humanoid Animator 가 없다.");
                return;
            }

            for (int i = 1; i <= 4; i++)
            {
                string path = $"{ClipFolder}Garen_Default_Attack_{i}.anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[PaladinRootMotion] 클립 없음: {path}");
                    continue;
                }

                AnalyzeClip(animator, clip, i);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AnalyzeClip(Animator animator, AnimationClip clip, int hitNumber)
    {
        Transform character = animator.transform;
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        int frames = Mathf.CeilToInt(clip.length * SampleRate) + 1;
        var left = new Vector3[frames];
        var right = new Vector3[frames];
        var body = new Vector3[frames];

        for (int f = 0; f < frames; f++)
        {
            float t = Mathf.Min(f / SampleRate, clip.length);
            clip.SampleAnimation(animator.gameObject, t);
            left[f] = character.InverseTransformPoint(leftFoot.position);
            right[f] = character.InverseTransformPoint(rightFoot.position);
            body[f] = character.InverseTransformPoint(hips.position);
        }

        var csv = new StringBuilder("t,lx,ly,lz,rx,ry,rz,hx,hy,hz\n");
        for (int f = 0; f < frames; f++)
        {
            csv.AppendLine(string.Join(",",
                F(f / SampleRate),
                F(left[f].x), F(left[f].y), F(left[f].z),
                F(right[f].x), F(right[f].y), F(right[f].z),
                F(body[f].x), F(body[f].y), F(body[f].z)));
        }

        string csvPath = Path.GetFullPath($"Temp/PaladinRootMotion_{hitNumber}.csv");
        File.WriteAllText(csvPath, csv.ToString());

        var report = new StringBuilder();
        report.AppendLine($"[PaladinRootMotion] {hitNumber}타 {clip.name} 길이 {F(clip.length)}s · {frames}프레임 · CSV {csvPath}");
        AppendPlantSegments(report, "왼발", left);
        AppendPlantSegments(report, "오른발", right);
        report.AppendLine($"  골반 z: 시작 {F(body[0].z)} · 최대 {F(MaxZ(body))} · 끝 {F(body[frames - 1].z)}");
        Debug.Log(report.ToString());
    }

    private static void AppendPlantSegments(StringBuilder report, string label, Vector3[] foot)
    {
        float minY = float.MaxValue;
        foreach (Vector3 p in foot)
            minY = Mathf.Min(minY, p.y);

        report.AppendLine($"  {label}: 최저 y {F(minY)} · z {F(foot[0].z)} → {F(foot[foot.Length - 1].z)}");

        int start = -1;
        for (int f = 0; f <= foot.Length; f++)
        {
            bool planted = f < foot.Length && IsPlanted(foot, f, minY);
            if (planted && start < 0)
                start = f;

            if (!planted && start >= 0)
            {
                int end = f - 1;
                report.AppendLine($"    디딤 {F(start / SampleRate)}~{F(end / SampleRate)}s · z {F(foot[start].z)} → {F(foot[end].z)}");
                start = -1;
            }
        }
    }

    private static bool IsPlanted(Vector3[] foot, int f, float minY)
    {
        if (foot[f].y > minY + PlantHeightTolerance)
            return false;

        int prev = Mathf.Max(0, f - 1);
        int next = Mathf.Min(foot.Length - 1, f + 1);
        if (next == prev)
            return true;

        Vector3 delta = foot[next] - foot[prev];
        delta.y = 0f;
        float speed = delta.magnitude * SampleRate / (next - prev);
        return speed <= PlantSpeedTolerance;
    }

    private static float MaxZ(Vector3[] points)
    {
        float max = float.MinValue;
        foreach (Vector3 p in points)
            max = Mathf.Max(max, p.z);
        return max;
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // ─── 쓰기 ─────────────────────────────────────────────────────────────
    // 은희 결정(10-07): 실제 재생 구간(0 ~ End 이벤트)에서 떠 있는 발이 z 로 움직인 거리를
    // 방향 무관하게 전부 전진으로 본다(뒤로 빼는 발 = 발 바꾸기). End 이후 꼬리는 전진 없음.
    // 클립 공간 = RootT + RootQ·goal (Humanoid IK 목표는 루트 기준 상대 좌표).
    // 디딘 발은 IK 목표를 전진량만큼 반대로 밀어 월드에 고정하고, 다음에 뜰 때 원래 자리로 돌려놓는다.
    // 콤보는 1→2→3→4 로 이어지므로 앞 타의 End 시점 보정량을 다음 타가 넘겨받는다(1타는 대기 자세에서 시작).

    private static readonly string[] FootPrefixes = { "LeftFootT", "RightFootT" };

    [MenuItem("Tools/Player/Paladin/평타 루트모션 미리보기 (쓰지 않음)")]
    private static void Preview() => Run(false);

    [MenuItem("Tools/Player/Paladin/평타 루트모션 쓰기 (역산 보폭)")]
    private static void Write() => Run(true);

    private static void Run(bool apply)
    {
        var clips = new ClipCurves[4];
        for (int i = 0; i < 4; i++)
        {
            string path = $"{ClipFolder}Garen_Default_Attack_{i + 1}.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                Debug.LogError($"[PaladinRootMotion] 클립 없음: {path}");
                return;
            }

            if (!AnimationUtility.GetAnimationClipSettings(clip).loopBlendPositionXZ)
            {
                Debug.LogError($"[PaladinRootMotion] {clip.name} 은 이미 XZ 베이크가 꺼져 있다 — 두 번 쓰면 전진이 겹친다. git 으로 되돌린 뒤 다시 실행할 것.");
                return;
            }

            clips[i] = ClipCurves.Read(clip);
            if (clips[i] == null)
                return;
        }

        // 프리팹 편집용 미리보기 씬에서는 PlayableGraph 가 포즈를 안 갱신한다 — 저장 안 되는 숨긴 인스턴스로 돌린다.
        GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath));
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>();
            animator.Rebind();
            float humanScale = animator.humanScale > 0f ? animator.humanScale : 1f;

            float[] carry = new float[2];
            var report = new StringBuilder($"[PaladinRootMotion] 쓰기 (humanScale {F(humanScale)})\n");
            for (int i = 0; i < 4; i++)
            {
                ClipCurves c = clips[i];
                c.Solve(carry, humanScale);
                carry = c.OffsetsAt(c.EndIndex);

                // 기준선 = 지금 클립(전진 없음). 측정 자체의 오차가 여기 드러난다.
                AnimationClip baseline = Object.Instantiate(c.Clip);
                AnimationUtility.SetAnimationEvents(baseline, new AnimationEvent[0]);
                Simulate(animator, baseline, c, out _, out float baseWindow, out float baseTail, $"Temp/PaladinSim_base{i + 1}.csv");
                Object.DestroyImmediate(baseline);

                AnimationClip preview = Object.Instantiate(c.Clip);
                AnimationUtility.SetAnimationEvents(preview, new AnimationEvent[0]);
                c.WriteTo(preview);
                Simulate(animator, preview, c, out float travel, out float slideWindow, out float slideTail, $"Temp/PaladinSim_new{i + 1}.csv");
                Object.DestroyImmediate(preview);

                report.AppendLine(
                    $"  {i + 1}타: 역산 {F(c.Travel[c.EndIndex] * humanScale)}m (End {F(c.EndTime)}s) · 시뮬 전체 이동 {F(travel)}m · " +
                    $"디딘 발 미끄러짐 구간 {F(slideWindow * 100f)}cm(기준 {F(baseWindow * 100f)}) / 꼬리 {F(slideTail * 100f)}cm(기준 {F(baseTail * 100f)}) · " +
                    $"End 보정 넘김 L {F(carry[0] * humanScale)} / R {F(carry[1] * humanScale)}m");
            }

            if (!apply)
            {
                Debug.Log(report.ToString());
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                clips[i].WriteTo(clips[i].Clip);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clips[i].Clip);
                settings.loopBlendPositionXZ = false;
                AnimationUtility.SetAnimationClipSettings(clips[i].Clip, settings);
                EditorUtility.SetDirty(clips[i].Clip);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // 게임과 같은 조건(발 IK, XZ 루트모션을 캐릭터 전방에만 적용)으로 돌려 디딘 발이 월드에서 얼마나 움직이는지 잰다.
    private static void Simulate(Animator animator, AnimationClip clip, ClipCurves source, out float travel, out float slideWindow, out float slideTail, string tracePath = null)
    {
        Transform character = animator.transform;
        Vector3 startPosition = character.position;
        Transform[] feet =
        {
            animator.GetBoneTransform(HumanBodyBones.LeftFoot),
            animator.GetBoneTransform(HumanBodyBones.RightFoot)
        };

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopBlendPositionXZ = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        PlayableGraph graph = PlayableGraph.Create("PaladinRootMotionPreview");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Preview", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(true);
            output.SetSourcePlayable(playable);
            // 클립의 루트 옆 흔들림은 0 으로 만들었으므로 Animator 가 직접 옮기는 것 = 게임의 전방 투영과 같다.
            animator.applyRootMotion = true;
            // 화면에 안 보이는 숨긴 인스턴스라 컬링이 켜져 있으면 루트모션만 돌고 포즈는 안 바뀐다.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            slideWindow = 0f;
            slideTail = 0f;
            float dt = 1f / 240f;
            int steps = Mathf.CeilToInt(clip.length / dt);
            graph.Evaluate(0f);
            var plantedAt = new Vector3[2];
            for (int k = 0; k < 2; k++)
                plantedAt[k] = Vector3.Scale(feet[k].position, new Vector3(1f, 0f, 1f));

            var trace = tracePath != null ? new StringBuilder("t,lx,lz,lp,rx,rz,rp,cx,cz\n") : null;
            for (int s = 1; s <= steps; s++)
            {
                graph.Evaluate(dt);

                float t = Mathf.Min(s * dt, clip.length);
                trace?.AppendLine(string.Join(",", F(t), F(feet[0].position.x), F(feet[0].position.z), source.IsPlantedAt(0, t) ? "1" : "0", F(feet[1].position.x), F(feet[1].position.z), source.IsPlantedAt(1, t) ? "1" : "0", F(character.position.x), F(character.position.z)));
                for (int k = 0; k < 2; k++)
                {
                    bool planted = source.IsPlantedAt(k, t);
                    bool wasPlanted = source.IsPlantedAt(k, t - dt);
                    Vector3 p = feet[k].position;
                    p.y = 0f;
                    // 첫 Evaluate(0) 은 포즈를 안 입힌다(바인드 포즈) — 기준점은 첫 실제 평가에서 잡는다.
                    if (s == 1 || (planted && !wasPlanted))
                        plantedAt[k] = p;
                    else if (planted && t <= source.EndTime)
                        slideWindow = Mathf.Max(slideWindow, Vector3.Distance(plantedAt[k], p));
                    else if (planted)
                        slideTail = Mathf.Max(slideTail, Vector3.Distance(plantedAt[k], p));
                    else
                        plantedAt[k] = p;
                }
            }

            travel = Vector3.Distance(startPosition, character.position);
            if (trace != null)
                File.WriteAllText(Path.GetFullPath(tracePath), trace.ToString());
        }
        finally
        {
            graph.Destroy();
            character.position = startPosition;
        }
    }

    private sealed class ClipCurves
    {
        public AnimationClip Clip;
        public float EndTime;
        public int EndIndex;
        public float[] Times;
        public Vector3[] RootT;
        public Quaternion[] RootQ;
        public Vector3[][] Goal = new Vector3[2][];
        public Vector3[][] Foot = new Vector3[2][];
        public bool[][] Planted = new bool[2][];
        public float[] Travel;
        public float[][] Offset = new float[2][];

        private readonly System.Collections.Generic.Dictionary<string, EditorCurveBinding> bindings = new();

        public static ClipCurves Read(AnimationClip clip)
        {
            var c = new ClipCurves { Clip = clip };
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(Animator) && string.IsNullOrEmpty(b.path))
                    c.bindings[b.propertyName] = b;
            }

            AnimationCurve rootZ = c.Curve("RootT.z");
            if (rootZ == null)
            {
                Debug.LogError($"[PaladinRootMotion] {clip.name} 에 RootT.z 가 없다.");
                return null;
            }

            c.EndTime = clip.length;
            foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
            {
                if (e.functionName == "EndDefaultAttack")
                    c.EndTime = e.time;
            }

            int n = rootZ.length;
            c.Times = new float[n];
            c.RootT = new Vector3[n];
            c.RootQ = new Quaternion[n];
            for (int k = 0; k < 2; k++)
            {
                c.Goal[k] = new Vector3[n];
                c.Foot[k] = new Vector3[n];
            }

            for (int f = 0; f < n; f++)
            {
                float t = rootZ.keys[f].time;
                c.Times[f] = t;
                c.RootT[f] = c.Vector("RootT", t);
                c.RootQ[f] = new Quaternion(c.Value("RootQ.x", t), c.Value("RootQ.y", t), c.Value("RootQ.z", t), c.Value("RootQ.w", t)).normalized;
                for (int k = 0; k < 2; k++)
                {
                    c.Goal[k][f] = c.Vector(FootPrefixes[k], t);
                    c.Foot[k][f] = c.RootT[f] + c.RootQ[f] * c.Goal[k][f];
                }

                if (t <= c.EndTime + 0.0001f)
                    c.EndIndex = f;
            }

            return c;
        }

        public void Solve(float[] initialOffsets, float humanScale)
        {
            int n = Times.Length;
            for (int k = 0; k < 2; k++)
                Planted[k] = DetectPlanted(Foot[k], humanScale);

            Travel = new float[n];
            for (int f = 1; f < n; f++)
            {
                float d = 0f;
                if (f <= EndIndex)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        if (!(Planted[k][f] && Planted[k][f - 1]))
                            d += Mathf.Abs(Foot[k][f].z - Foot[k][f - 1].z);
                    }
                }

                Travel[f] = Travel[f - 1] + d;
            }

            for (int k = 0; k < 2; k++)
            {
                var o = new float[n];
                o[0] = initialOffsets[k];
                int swingStart = -1;
                for (int f = 1; f < n; f++)
                {
                    if (Planted[k][f])
                    {
                        swingStart = -1;
                        if (f <= EndIndex)
                            o[f] = o[f - 1] + (Travel[f] - Travel[f - 1]);
                        else
                        {
                            // 꼬리: 전진은 없고, 남은 보정은 클립 끝까지 천천히 풀어 대기 자세와 맞춘다.
                            float remaining = Times[n - 1] - Times[f - 1];
                            float step = Times[f] - Times[f - 1];
                            o[f] = remaining > 0f ? o[f - 1] * (1f - step / remaining) : 0f;
                        }

                        continue;
                    }

                    if (swingStart < 0)
                        swingStart = f - 1;

                    int land = f;
                    while (land < n - 1 && !Planted[k][land])
                        land++;

                    float u = Mathf.InverseLerp(Times[swingStart], Times[land], Times[f]);
                    o[f] = o[swingStart] * (1f - Mathf.SmoothStep(0f, 1f, u));
                }

                Offset[k] = o;
            }
        }

        public float[] OffsetsAt(int index) => new[] { Offset[0][index], Offset[1][index] };

        public bool IsPlantedAt(int k, float t)
        {
            if (t < 0f)
                return false;

            int f = System.Array.BinarySearch(Times, t);
            if (f < 0)
                f = Mathf.Clamp(~f - 1, 0, Times.Length - 1);
            return Planted[k][f];
        }

        public void WriteTo(AnimationClip target)
        {
            int n = Times.Length;
            var rootX = new float[n];
            var rootZ = new float[n];
            var goals = new Vector3[2][];
            for (int k = 0; k < 2; k++)
                goals[k] = new Vector3[n];

            for (int f = 0; f < n; f++)
            {
                // 옆 흔들림은 게임이 전방 성분만 쓰므로 루트에서 빼고(발은 아래 목표로 그대로 둔다).
                Vector3 newRoot = new Vector3(RootT[0].x, RootT[f].y, RootT[f].z + Travel[f]);
                rootX[f] = newRoot.x;
                rootZ[f] = newRoot.z;
                Quaternion inverse = Quaternion.Inverse(RootQ[f]);
                for (int k = 0; k < 2; k++)
                {
                    Vector3 world = Foot[k][f] + new Vector3(0f, 0f, Travel[f] - Offset[k][f]);
                    goals[k][f] = inverse * (world - newRoot);
                }
            }

            SetCurve(target, "RootT.x", rootX);
            SetCurve(target, "RootT.z", rootZ);
            for (int k = 0; k < 2; k++)
            {
                SetCurve(target, FootPrefixes[k] + ".x", Component(goals[k], 0));
                SetCurve(target, FootPrefixes[k] + ".y", Component(goals[k], 1));
                SetCurve(target, FootPrefixes[k] + ".z", Component(goals[k], 2));
            }
        }

        private bool[] DetectPlanted(Vector3[] foot, float humanScale)
        {
            int n = foot.Length;
            float minY = float.MaxValue;
            foreach (Vector3 p in foot)
                minY = Mathf.Min(minY, p.y);

            var planted = new bool[n];
            for (int f = 0; f < n; f++)
            {
                int prev = Mathf.Max(0, f - 1);
                int next = Mathf.Min(n - 1, f + 1);
                Vector3 delta = foot[next] - foot[prev];
                delta.y = 0f;
                float span = Times[next] - Times[prev];
                float speed = span > 0f ? delta.magnitude * humanScale / span : 0f;
                planted[f] = (foot[f].y - minY) * humanScale <= PlantHeightTolerance && speed <= PlantSpeedTolerance;
            }

            return planted;
        }

        private void SetCurve(AnimationClip target, string property, float[] values)
        {
            var keys = new Keyframe[values.Length];
            for (int f = 0; f < values.Length; f++)
            {
                int prev = Mathf.Max(0, f - 1);
                int next = Mathf.Min(values.Length - 1, f + 1);
                float span = Times[next] - Times[prev];
                float slope = span > 0f ? (values[next] - values[prev]) / span : 0f;
                keys[f] = new Keyframe(Times[f], values[f], slope, slope);
            }

            AnimationUtility.SetEditorCurve(target, bindings[property], new AnimationCurve(keys));
        }

        private static float[] Component(Vector3[] values, int axis)
        {
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = values[i][axis];
            return result;
        }

        private readonly System.Collections.Generic.Dictionary<string, AnimationCurve> curves = new();

        private AnimationCurve Curve(string property)
        {
            if (curves.TryGetValue(property, out AnimationCurve cached))
                return cached;

            AnimationCurve curve = bindings.TryGetValue(property, out EditorCurveBinding b)
                ? AnimationUtility.GetEditorCurve(Clip, b)
                : null;
            curves[property] = curve;
            return curve;
        }

        private float Value(string property, float t) => Curve(property)?.Evaluate(t) ?? 0f;

        private Vector3 Vector(string prefix, float t) =>
            new Vector3(Value(prefix + ".x", t), Value(prefix + ".y", t), Value(prefix + ".z", t));
    }
}
