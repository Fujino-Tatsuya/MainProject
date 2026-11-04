using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 포드 클립 애니 이벤트 저작(할 일 106) — SM_DropPod_LaunchStation_Combined_v001.fbx(SVN 아트) 클립 임포트 설정(ModelImporter.clipAnimations)에
/// <c>PodClipEvent</c>(<see cref="PodAnimEventRelay"/>) 이벤트를 넣는다. 로비 발사대·맵 포드가 같은 FBX 클립을 쓴다(컨트롤러 AC_DropPod_LaunchStation_v001).
/// 처음 위치: 로비 해치(DP_HatchLobbyOpenClose) = 103프레임(다 열린 자세에서 멈추고 탑승), 나머지 = 끝 프레임(다음 단계).
/// 단계 이벤트는 정수 인자 0(<see cref="PodAnimEventRelay.KindStage"/>). 로비 발사(DP_LaunchCycle)에는 가속 이벤트가 둘 더 있다 —
/// 1단계(정수 인자 1 — <see cref="PodAnimEventRelay.KindSpeedUp"/>) 처음 위치 213프레임부터 PodDropSettings launchFastSpeed(할 일 106 추가 2, 할 일 121 = 294 → 213),
/// 2단계(정수 인자 2 — <see cref="PodAnimEventRelay.KindSpeedUp2"/>) 처음 위치 553프레임부터 launchFastSpeed2(할 일 121),
/// 포드 하강 시작(정수 인자 3 — <see cref="PodAnimEventRelay.KindDescent"/>, 할 일 198) 처음 위치 = 포드 높이 커브로 찾은 프레임(<see cref="FindDescentFrame"/>) — 여기 + launchEndWaitSeconds 뒤 끝 페이드.
/// 이벤트를 FBX 에 두는 이유: 사용자가 FBX 임포트 창 Animation 탭에서 끌어 옮겨 튜닝하고, 코드·SO 에 클립 시각을 흉내 내는 숫자를 두지 않으려고.
///
/// 🔴 재실행해도 <b>이미 있는 이벤트는 옮기지 않는다</b> — 사용자가 옮긴 위치를 지킨다. 빠진 이벤트만 채운다. 처음 위치로 되돌리려면 "기본 위치로 재설정".
/// 🔴 fbx.meta 는 SVN 이다 — git 커밋 대상이 아니고, 바꾼 뒤 SVN 커밋은 사용자 확인 후.
/// </summary>
public static class PodAnimEventAuthoring
{
    const string Tag = "[PodAnim/Event]";
    internal const string ModelPath = "Assets/50.Art/Environment/Models/Machinery/Platforms/DropPodLauncher/Consolidated/v001/SM_DropPod_LaunchStation_Combined_v001.fbx";
    const string SettingsPath = "Assets/9.ScriptableObject/Lobby/PodDropSettings.asset";

    /// <summary>로비 해치 이벤트 처음 위치(클립 프레임) — 다 열린 자세(할 일 106 사양).</summary>
    internal const int LobbyHatchOpenFrame = 103;

    /// <summary>로비 발사 가속 1단계 이벤트 처음 위치(클립 프레임) — 여기부터 1단계 배율(할 일 106 추가 2 사양, 할 일 121 = 213).</summary>
    internal const int LobbyLaunchFastFrame = 213;

    /// <summary>로비 발사 가속 2단계 이벤트 처음 위치(클립 프레임) — 여기부터 2단계 배율(할 일 121).</summary>
    internal const int LobbyLaunchFast2Frame = 553;

    /// <summary>
    /// 로비 포드 하강 시작 이벤트(종류 3, 할 일 198) 처음 위치 표시 — 고정 프레임이 아니라 저작 때 발사 클립을 샘플링해
    /// 포드가 아래로 내려가기 시작하는 프레임을 찾는다(<see cref="FindDescentFrame"/>).
    /// </summary>
    internal const int AutoDescentFrame = -2;

    /// <summary>하강 시작 판정 — 꼭대기 높이에서 이만큼(m) 내려간 첫 프레임의 직전 프레임.</summary>
    internal const float DescentEpsilon = 0.005f;

    /// <summary>클립 하나에 넣을 이벤트 하나 — <c>Frame</c> &lt; 0 = 끝 프레임. <c>Kind</c> = 이벤트 정수 인자(종류).</summary>
    internal readonly struct Spec
    {
        public readonly string Clip;
        public readonly int Frame;
        public readonly int Kind;
        public readonly string Meaning;

        public Spec(string clip, int frame, int kind, string meaning)
        {
            Clip = clip;
            Frame = frame;
            Kind = kind;
            Meaning = meaning;
        }
    }

    [MenuItem("Tools/Lobby/포드 애니 이벤트 저작 (빠진 것만)")]
    public static void AuthorMissing() => Author(resetExisting: false);

    [MenuItem("Tools/Lobby/포드 애니 이벤트 기본 위치로 재설정")]
    public static void AuthorReset()
    {
        if (!EditorUtility.DisplayDialog("포드 애니 이벤트 재설정",
                "옮겨 둔 PodClipEvent 이벤트를 처음 위치(로비 해치 103프레임, 로비 발사 가속 1단계 213프레임·2단계 553프레임·하강 시작 = 포드 커브로 찾은 프레임, 나머지 끝 프레임)로 되돌린다. 계속할까?", "재설정", "취소"))
            return;
        Author(resetExisting: true);
    }

    [MenuItem("Tools/Lobby/포드 애니 이벤트 검사")]
    public static void ValidateMenu()
    {
        int missing = Validate(out string report);
        if (missing > 0)
            Debug.LogWarning($"{Tag} 필요한 이벤트 {missing}개 없음 — 'Tools/Lobby/포드 애니 이벤트 저작' 실행(없으면 클립 끝에서 넘어간다)\n{report}");
        else
            Debug.Log($"{Tag} 검사 통과\n{report}");
    }

    /// <summary>스펙 = 설정 에셋의 상태 이름(= 클립 이름). 에셋이 없으면 코드 기본값.</summary>
    internal static List<Spec> BuildSpecs()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PodDropSettings>(SettingsPath);
        PodDropLobbyTimings lobby = settings != null ? settings.Lobby : PodDropLobbyTimings.Default;
        PodDropMapTimings map = settings != null ? settings.Map : PodDropMapTimings.Default;

        const int stage = PodAnimEventRelay.KindStage;
        var specs = new List<Spec>();
        Add(specs, lobby.hatchState, LobbyHatchOpenFrame, stage, "로비 해치 열림 → 멈춤·탑승");
        Add(specs, lobby.launchState, LobbyLaunchFastFrame, PodAnimEventRelay.KindSpeedUp, $"로비 발사 가속 1단계 → ×{lobby.launchFastSpeed:0.##}");
        Add(specs, lobby.launchState, LobbyLaunchFast2Frame, PodAnimEventRelay.KindSpeedUp2, $"로비 발사 가속 2단계 → ×{lobby.launchFastSpeed2:0.##}");
        Add(specs, lobby.launchState, AutoDescentFrame, PodAnimEventRelay.KindDescent, $"로비 포드 하강 시작 → 대기 {lobby.launchEndWaitSeconds:0.##}s · 페이드 {lobby.launchEndFadeSeconds:0.##}s → 로딩");
        Add(specs, lobby.launchState, -1, stage, "로비 발사 끝 (하강 이벤트가 없을 때의 예비 기준)");
        // 맵 날개 펴기(wingDeployState)는 할 일 150 부터 안 쓴다 — 클립에 남은 이벤트는 그대로 둔다(스펙에서 빼면 저작이 지우지 않는다).
        Add(specs, map.wingFoldState, -1, stage, "맵 날개 접기 끝");
        Add(specs, map.gearDeployState, -1, stage, "맵 다리 펴기 끝 (둘 다 → 자유낙하)");
        Add(specs, map.hatchOpenState, -1, stage, "맵 해치 열기 끝 → 디졸브 대기");
        return specs;
    }

    static void Add(List<Spec> specs, string clip, int frame, int kind, string meaning)
    {
        if (!string.IsNullOrEmpty(clip) && specs.TrueForAll(s => s.Clip != clip || s.Kind != kind))
            specs.Add(new Spec(clip, frame, kind, meaning));
    }

    /// <summary>클립 프레임 → 임포트 이벤트 정규화 시각(0 = 첫 프레임, 1 = 끝 프레임). 음수 = 끝.</summary>
    internal static float ToNormalized(int frame, float firstFrame, float lastFrame)
    {
        float span = lastFrame - firstFrame;
        if (frame < 0 || span <= 0f) return 1f;
        return Mathf.Clamp01((frame - firstFrame) / span);
    }

    static void Author(bool resetExisting)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"{Tag} {ModelPath} 를 못 찾았다(SVN 아트 — svn update 확인).");
            return;
        }

        ModelImporterClipAnimation[] clipAnimations = importer.clipAnimations;
        var log = new StringBuilder();
        bool changed = false;

        foreach (Spec spec in BuildSpecs())
        {
            int index = System.Array.FindIndex(clipAnimations, c => c.name == spec.Clip);
            if (index < 0)
            {
                Debug.LogError($"{Tag} 클립 '{spec.Clip}' 이 {ModelPath} 임포트 설정에 없다.");
                continue;
            }

            ModelImporterClipAnimation clipAnimation = clipAnimations[index];
            int frame = spec.Frame;
            string found = "";
            if (frame == AutoDescentFrame)
            {
                // 이미 있고 재설정이 아니면 찾을 필요 없다(Merge 가 그대로 둔다).
                bool exists = System.Array.Exists(clipAnimation.events ?? System.Array.Empty<AnimationEvent>(),
                    e => e.functionName == PodAnimEventRelay.FunctionName && e.intParameter == spec.Kind);
                if (exists && !resetExisting)
                {
                    log.AppendLine($"  {spec.Clip} [종류 {spec.Kind}] — {spec.Meaning}: 이미 있음 → 그대로");
                    continue;
                }

                frame = FindDescentFrame(spec.Clip, out found);
                if (frame < 0)
                {
                    Debug.LogError($"{Tag} '{spec.Clip}' 에서 포드가 내려가기 시작하는 프레임을 못 찾았다 — {found}. 이벤트(종류 {spec.Kind})를 임포트 창에서 직접 넣을 것.");
                    continue;
                }
            }

            float time = ToNormalized(frame, clipAnimation.firstFrame, clipAnimation.lastFrame);
            AnimationEvent[] merged = AnimEventTiming.Merge(clipAnimation.events, PodAnimEventRelay.FunctionName,
                new List<(int type, float time)> { (spec.Kind, time) }, resetExisting, out int added, out int moved);
            string frameText = frame < 0 ? $"끝 {clipAnimation.lastFrame:0}" : string.IsNullOrEmpty(found) ? frame.ToString() : $"{frame} ({found})";
            log.AppendLine($"  {spec.Clip} [종류 {spec.Kind}] — {spec.Meaning}: 프레임 {frameText} (정규화 {time:0.####}) → 추가 {added}, 재설정 {moved}");
            if (added == 0 && moved == 0)
                continue;

            // 문자열 인자 = 클립 이름 — 수신 쪽이 클립 정보를 못 받을 때만 쓰는 예비.
            foreach (AnimationEvent e in merged)
            {
                if (e.functionName == PodAnimEventRelay.FunctionName && string.IsNullOrEmpty(e.stringParameter))
                    e.stringParameter = spec.Clip;
            }

            clipAnimation.events = merged;
            clipAnimations[index] = clipAnimation;
            changed = true;
        }

        if (!changed)
        {
            Debug.Log($"{Tag} 바꿀 것 없음 — 이벤트가 다 있다.\n{log}");
            return;
        }

        importer.clipAnimations = clipAnimations;
        importer.SaveAndReimport();
        s_assets = null;
        Debug.Log($"{Tag} 저작 완료 — {ModelPath}(SVN, .meta) 재임포트\n{log}");
        ValidateMenu();
    }

    /// <summary>
    /// 발사 클립에서 포드가 아래로 내려가기 시작하는 프레임(할 일 198) — FBX 모델을 잠깐 만들어 클립을 프레임마다 샘플링하고(월드 높이),
    /// 가장 크게 떨어지는 Transform 들(최대 낙차의 90% 이상 — 포드 선체와 그 안 부품)의 하강 시작 프레임 중 가장 많은 것(같으면 늦은 것)을 고른다.
    /// 해치처럼 포드에 붙어 먼저 아래로 닫히는 부품 하나가 앞당기지 않게 다수결이다. 못 찾으면 −1, <paramref name="detail"/> = 근거·이유.
    /// </summary>
    internal static int FindDescentFrame(string clipName, out string detail)
    {
        AnimationClip clip = LoadClip(clipName);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (clip == null || model == null)
        {
            detail = clip == null ? $"클립 '{clipName}' 없음" : "모델 없음";
            return -1;
        }

        GameObject go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
            float rate = clip.frameRate > 0f ? clip.frameRate : 30f;
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * rate)) + 1;
            var heights = new float[transforms.Length][];
            for (int i = 0; i < transforms.Length; i++) heights[i] = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                clip.SampleAnimation(go, Mathf.Min(f / rate, clip.length));
                for (int i = 0; i < transforms.Length; i++) heights[i][f] = transforms[i].position.y;
            }

            int frame = PickDescentFrame(heights, DescentEpsilon, out int best, out float drop);
            return frame >= 0 ? Found(frame, transforms[best], heights[best], drop, out detail) : NotFound(drop, out detail);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static int Found(int frame, Transform t, float[] h, float drop, out string detail)
    {
        detail = $"'{t.name}' 월드 높이 {h[frame]:0.###}m → 최저 {h[frame] - drop:0.###}m(낙차 {drop:0.##}m) — 프레임 {frame} 다음부터 내려감";
        return frame;
    }

    static int NotFound(float drop, out string detail)
    {
        detail = $"내려가는 Transform 없음(최대 낙차 {drop:0.###}m)";
        return -1;
    }

    /// <summary>
    /// Transform 별 높이 열(프레임 순) → 하강 시작 프레임(순수). 최대 낙차의 90% 이상인 열들의 <see cref="DescentStartFrame"/> 다수결(같으면 늦은 프레임).
    /// <paramref name="best"/> = 고른 프레임을 낸 첫 열, <paramref name="drop"/> = 최대 낙차. 없으면 −1.
    /// </summary>
    internal static int PickDescentFrame(IReadOnlyList<float[]> heights, float epsilon, out int best, out float drop)
    {
        best = -1;
        drop = 0f;
        if (heights == null) return -1;
        var drops = new float[heights.Count];
        for (int i = 0; i < heights.Count; i++)
        {
            drops[i] = Drop(heights[i]);
            drop = Mathf.Max(drop, drops[i]);
        }

        if (drop < epsilon) return -1;
        var votes = new Dictionary<int, int>();
        var firstColumn = new Dictionary<int, int>();
        for (int i = 0; i < heights.Count; i++)
        {
            if (drops[i] < drop * 0.9f) continue;
            int frame = DescentStartFrame(heights[i], epsilon);
            if (frame < 0) continue;
            votes[frame] = votes.TryGetValue(frame, out int n) ? n + 1 : 1;
            if (!firstColumn.ContainsKey(frame)) firstColumn[frame] = i;
        }

        int result = -1, most = 0;
        foreach (KeyValuePair<int, int> v in votes)
        {
            if (v.Value > most || (v.Value == most && v.Key > result))
            {
                result = v.Key;
                most = v.Value;
            }
        }

        if (result >= 0) best = firstColumn[result];
        return result;
    }

    // 최저점 전 최고 높이 − 최저 높이(m).
    static float Drop(IReadOnlyList<float> h)
    {
        if (h == null || h.Count == 0) return 0f;
        int iMin = ArgMin(h);
        float top = float.NegativeInfinity;
        for (int i = 0; i <= iMin; i++) top = Mathf.Max(top, h[i]);
        return top - h[iMin];
    }

    static int ArgMin(IReadOnlyList<float> h)
    {
        int iMin = 0;
        for (int i = 1; i < h.Count; i++)
            if (h[i] < h[iMin]) iMin = i;
        return iMin;
    }

    /// <summary>
    /// 높이 열(프레임 순) 하나의 하강 시작 프레임(순수) — 최저점 전 꼭대기(0.1mm 안 마지막 프레임 — 멈춰 있다 떨어지는 순간)에서
    /// <paramref name="epsilon"/> 보다 더 내려간 첫 프레임의 직전 프레임(= 마지막으로 꼭대기에 있던 프레임). 낙차가 <paramref name="epsilon"/> 미만이면 −1.
    /// </summary>
    internal static int DescentStartFrame(IReadOnlyList<float> heights, float epsilon)
    {
        if (heights == null || heights.Count < 2) return -1;
        int iMin = ArgMin(heights);
        float top = float.NegativeInfinity;
        for (int i = 0; i <= iMin; i++) top = Mathf.Max(top, heights[i]);
        if (top - heights[iMin] < epsilon) return -1;

        int iTop = 0;
        for (int i = 0; i <= iMin; i++)
            if (heights[i] >= top - 1e-4f) iTop = i;
        for (int i = iTop; i <= iMin; i++)
            if (heights[i] < top - epsilon) return Mathf.Max(iTop, i - 1);
        return -1;
    }

    /// <summary>스펙 클립마다 PodClipEvent 가 있는지 — 없는 수. 보고에 클립 시각(초)을 적는다.</summary>
    internal static int Validate(out string report)
    {
        var sb = new StringBuilder();
        int missing = 0;
        foreach (Spec spec in BuildSpecs())
        {
            AnimationClip clip = LoadClip(spec.Clip);
            if (clip == null)
            {
                sb.AppendLine($"  ✗ {spec.Clip} — 클립 없음");
                missing++;
                continue;
            }

            float seconds = EventSeconds(clip, spec.Kind, out bool found);
            string missingText = spec.Kind == PodAnimEventRelay.KindSpeedUp ? "1단계 가속 없음 — 2단계 이벤트까지 1배속"
                : spec.Kind == PodAnimEventRelay.KindSpeedUp2 ? "2단계 없음 — 1단계 배율로 끝까지 돈다"
                : spec.Kind == PodAnimEventRelay.KindDescent ? $"하강 시작 없음 — 발사 끝 {clip.length:0.###}s 부터 끝 대기·페이드"
                : $"클립 끝 {clip.length:0.###}s 에서 넘어간다";
            sb.AppendLine(found
                ? $"  ✓ {spec.Clip} — {spec.Meaning}: {seconds:0.###}s / {clip.length:0.###}s"
                : $"  ✗ {spec.Clip} — {spec.Meaning}: 이벤트 없음({missingText})");
            if (!found) missing++;
        }

        report = sb.ToString();
        return missing;
    }

    // 인스펙터 시각표가 다시 그릴 때마다 FBX 하위 에셋을 다 읽지 않게 잠깐(1초) 들고 있는다 — 이벤트를 옮기면 곧 반영된다.
    static Object[] s_assets;
    static double s_assetsAt = double.NegativeInfinity;

    /// <summary>FBX 안 클립(이름 = 상태 이름). 없으면 null.</summary>
    internal static AnimationClip LoadClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        if (s_assets == null || EditorApplication.timeSinceStartup - s_assetsAt > 1d)
        {
            s_assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            s_assetsAt = EditorApplication.timeSinceStartup;
        }

        foreach (Object asset in s_assets)
        {
            if (asset is AnimationClip clip && clip != null && clip.name == clipName && !clip.name.StartsWith("__preview__"))
                return clip;
        }

        return null;
    }

    /// <summary>클립의 첫 PodClipEvent(종류 <paramref name="kind"/>) 시각(초). 없으면 클립 길이(끝까지 돈 것으로 넘어가는 예비 동작과 같다).</summary>
    internal static float EventSeconds(AnimationClip clip, int kind, out bool found)
    {
        found = false;
        if (clip == null) return 0f;
        float best = float.MaxValue;
        foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
        {
            if (e.functionName != PodAnimEventRelay.FunctionName || e.intParameter != kind) continue;
            found = true;
            best = Mathf.Min(best, e.time);
        }

        return found ? best : clip.length;
    }

    /// <summary>시각표 표시용 — 상태(클립)의 단계 이벤트(또는 <paramref name="kind"/>) 시각(초). 비운 이름·없는 클립 = 0(애니 없이 곧바로 넘어간다).</summary>
    internal static float EventSecondsOf(string clipName, out bool found, int kind = PodAnimEventRelay.KindStage)
    {
        found = false;
        AnimationClip clip = LoadClip(clipName);
        return clip != null ? EventSeconds(clip, kind, out found) : 0f;
    }
}
