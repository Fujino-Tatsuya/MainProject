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
/// 2단계(정수 인자 2 — <see cref="PodAnimEventRelay.KindSpeedUp2"/>) 처음 위치 553프레임부터 launchFastSpeed2(할 일 121).
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
                "옮겨 둔 PodClipEvent 이벤트를 처음 위치(로비 해치 103프레임, 로비 발사 가속 1단계 213프레임·2단계 553프레임, 나머지 끝 프레임)로 되돌린다. 계속할까?", "재설정", "취소"))
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
        Add(specs, lobby.launchState, -1, stage, $"로비 발사 끝 → 대기 {lobby.launchEndWaitSeconds:0.##}s · 페이드 {lobby.launchEndFadeSeconds:0.##}s → 로딩");
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
            float time = ToNormalized(spec.Frame, clipAnimation.firstFrame, clipAnimation.lastFrame);
            AnimationEvent[] merged = AnimEventTiming.Merge(clipAnimation.events, PodAnimEventRelay.FunctionName,
                new List<(int type, float time)> { (spec.Kind, time) }, resetExisting, out int added, out int moved);
            string frameText = spec.Frame < 0 ? $"끝 {clipAnimation.lastFrame:0}" : spec.Frame.ToString();
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
