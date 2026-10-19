using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 거너 스킬 클립 애니메이션 이벤트 저작 (PLAN-anim-event-timing P3 ①).
///
/// gunner.fbx(SVN 아트) 하위 클립의 임포트 설정(ModelImporter.clipAnimations)에 <c>HandleSkillEvent(int)</c> 이벤트를 넣는다
/// (Hit 0 / End 1 / Custom0 2 / Custom1 3 → PlayerAnimationEventRelay → PlayerSkillController → 스킬 OnAnimationEvent, 서버 전용).
/// 처음 위치 = 지금까지 타이머가 쓰던 실제 초(데이터 에셋 = xlsx 값)를 애니메이터 상태 속도로 클립 시각에 환산한 자리 → 체감 변화 0.
///
/// 🔴 재실행해도 <b>이미 있는 이벤트는 옮기지 않는다</b> — 사용자가 FBX 임포트 창에서 끌어 옮긴 위치를 지킨다.
///    빠진 이벤트만 채운다. 처음 위치로 되돌리려면 "기본 위치로 재설정" 메뉴.
/// 🔴 gunner.fbx.meta 는 SVN 이다 — git 커밋 대상이 아니고, 바꾼 뒤 SVN 커밋은 사용자 확인 후.
/// </summary>
public static class GunnerAnimEventAuthoring
{
    const string Tag = "[Gunner/AnimEvent]";
    const string ModelPath = "Assets/50.Art/Char/gunner/gunner.fbx";
    const string ControllerPath = "Assets/4.Animations/Player/Gunner/GunnerAnimatorController.controller";
    const string DataFolder = "Assets/9.ScriptableObject/Player/Gunner";

    internal const string FunctionName = "HandleSkillEvent";

    /// <summary>클립 하나에 넣을 이벤트 하나. seconds = 상태 진입 뒤 실제 초(애니 속도 반영 전).</summary>
    internal readonly struct EventSpec
    {
        public readonly SkillAnimationEventType Type;
        public readonly float Seconds;
        public readonly string Source;

        public EventSpec(SkillAnimationEventType type, float seconds, string source)
        {
            Type = type;
            Seconds = seconds;
            Source = source;
        }
    }

    internal sealed class ClipSpec
    {
        public string ClipName;
        public string StateName;
        public EventSpec[] Events;
    }

    [MenuItem("Tools/Player/Gunner/스킬 클립 애니 이벤트 저작 (빠진 것만)")]
    public static void AuthorMissing() => Author(resetExisting: false);

    [MenuItem("Tools/Player/Gunner/스킬 클립 애니 이벤트 기본 위치로 재설정")]
    public static void AuthorReset()
    {
        if (!EditorUtility.DisplayDialog("거너 애니 이벤트 재설정",
                "옮겨 둔 HandleSkillEvent 이벤트를 데이터 초 기준 처음 위치로 되돌린다. 계속할까?", "재설정", "취소"))
            return;
        Author(resetExisting: true);
    }

    [MenuItem("Tools/Player/Gunner/스킬 클립 애니 이벤트 검사")]
    public static void ValidateMenu()
    {
        int missing = Validate(out string report);
        if (missing > 0)
            Debug.LogWarning($"{Tag} 필요한 이벤트 {missing}개 없음 — '스킬 클립 애니 이벤트 저작' 실행\n{report}");
        else
            Debug.Log($"{Tag} 검사 통과\n{report}");
    }

    /// <summary>스펙 = 클립 · 상태 · 이벤트별 실제 초. 초는 데이터 에셋에서 읽는다(xlsx 원본).</summary>
    internal static List<ClipSpec> BuildSpecs()
    {
        var specs = new List<ClipSpec>();

        // 우클릭은 Hit·End 가 판정·종료·후폭풍 출발이라(P3-2 ①) hitDelay·skillDuration 은 미사용 칸이다 — 처음 위치는 옛 값으로 고정.
        var interrupt = Load<GunnerInterruptData>("GunnerInterruptData");
        if (interrupt != null)
            specs.Add(new ClipSpec
            {
                ClipName = "gunner_skill_RMB_interrupt",
                StateName = interrupt.AnimatorStateName,
                Events = new[]
                {
                    new EventSpec(SkillAnimationEventType.Hit, 0.2f, "처음 위치 고정(옛 hitDelay 0.2)"),
                    new EventSpec(SkillAnimationEventType.End, 0.7f, "처음 위치 고정(옛 skillDuration 0.7)"),
                },
            });

        var backstep = Load<GunnerCoolBackstepData>("GunnerCoolBackstepData");
        if (backstep != null)
            specs.Add(new ClipSpec
            {
                ClipName = "gunner_skill_E_cool_backstep",
                StateName = backstep.AnimatorStateName,
                Events = new[]
                {
                    new EventSpec(SkillAnimationEventType.End, backstep.MoveDuration, "GunnerCoolBackstepData.moveDuration"),
                },
            });

        var tracking = Load<GunnerTrackingLaserData>("GunnerTrackingLaserData");
        if (tracking != null)
            specs.Add(new ClipSpec
            {
                ClipName = "gunner_skill_ULT_cast",
                StateName = tracking.AnimatorStateName,
                Events = new[]
                {
                    new EventSpec(SkillAnimationEventType.Custom0, 0f, "레이저 생성(기존 OnServerStart 즉시)"),
                    new EventSpec(SkillAnimationEventType.End, tracking.CastDuration, "GunnerTrackingLaserData.castDuration"),
                },
            });

        // Q 는 시작 상태(Gunner_Q_Start)가 아니라 발사 때 GunnerBeamView 가 CrossFade 하는 Gunner_Q_Fire 기준이다.
        // 발사 후 회복 0.4s 는 Q_Fire(0.8s) 안에서 끝나므로 Q_Recover 가 아니라 Q_Fire 에 넣는다.
        var charge = Load<GunnerChargeLaserData>("GunnerChargeLaserData");
        if (charge != null)
            specs.Add(new ClipSpec
            {
                ClipName = "gunner_skill_Q_fire",
                StateName = "Gunner_Q_Fire",
                Events = new[]
                {
                    new EventSpec(SkillAnimationEventType.End, charge.FireRecovery, "GunnerChargeLaserData.fireRecovery"),
                },
            });

        return specs;
    }

    static void Author(bool resetExisting)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"{Tag} {ModelPath} 임포터를 못 찾았다.");
            return;
        }

        Dictionary<string, AnimationClip> clips = LoadClips();
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        ModelImporterClipAnimation[] clipAnimations = importer.clipAnimations;
        var log = new StringBuilder();
        bool changed = false;

        foreach (ClipSpec spec in BuildSpecs())
        {
            int index = System.Array.FindIndex(clipAnimations, c => c.name == spec.ClipName);
            if (index < 0 || !clips.TryGetValue(spec.ClipName, out AnimationClip clip))
            {
                Debug.LogError($"{Tag} 클립 '{spec.ClipName}' 이 {ModelPath} 에 없다.");
                continue;
            }

            float speed = StateSpeed(controller, spec.StateName);
            var wanted = new List<(int type, float normalizedTime)>();
            foreach (EventSpec e in spec.Events)
            {
                float clipTime = AnimEventTiming.ToClipTime(e.Seconds, speed, clip.length);
                wanted.Add(((int)e.Type, AnimEventTiming.ToNormalized(clipTime, clip.length)));
                log.AppendLine($"  {spec.ClipName} [{spec.StateName} 속도 {speed:0.###}] {e.Type} = 실제 {e.Seconds:0.###}s → 클립 {clipTime:0.###}s / {clip.length:0.###}s ({e.Source})");
            }

            ModelImporterClipAnimation clipAnimation = clipAnimations[index];
            AnimationEvent[] merged = AnimEventTiming.Merge(
                clipAnimation.events, FunctionName, wanted, resetExisting, out int added, out int moved);
            if (added == 0 && moved == 0)
                continue;

            clipAnimation.events = merged;
            clipAnimations[index] = clipAnimation;
            changed = true;
            log.AppendLine($"    → 추가 {added}, 재설정 {moved}");
        }

        if (!changed)
        {
            Debug.Log($"{Tag} 바꿀 것 없음(이미 있는 이벤트는 그대로 둔다)\n{log}");
            return;
        }

        importer.clipAnimations = clipAnimations;
        importer.SaveAndReimport();
        Debug.Log($"{Tag} 저작 완료 — {ModelPath}(SVN) 재임포트\n{log}");
        ValidateMenu();
    }

    /// <summary>임포트된 클립에 스펙의 이벤트 종류가 다 있는지. 반환 = 빠진 개수.</summary>
    internal static int Validate(out string report)
    {
        Dictionary<string, AnimationClip> clips = LoadClips();
        var sb = new StringBuilder();
        int missing = 0;

        foreach (ClipSpec spec in BuildSpecs())
        {
            if (!clips.TryGetValue(spec.ClipName, out AnimationClip clip))
            {
                sb.AppendLine($"  {spec.ClipName}: 클립 없음");
                missing += spec.Events.Length;
                continue;
            }

            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            foreach (EventSpec e in spec.Events)
            {
                AnimationEvent found = System.Array.Find(events,
                    x => x.functionName == FunctionName && x.intParameter == (int)e.Type);
                if (found == null)
                {
                    missing++;
                    sb.AppendLine($"  {spec.ClipName} {e.Type}: 없음");
                }
                else
                {
                    sb.AppendLine($"  {spec.ClipName} {e.Type}: 클립 {found.time:0.###}s / {clip.length:0.###}s");
                }
            }
        }

        report = sb.ToString();
        return missing;
    }

    static Dictionary<string, AnimationClip> LoadClips()
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(ModelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                clips[clip.name] = clip;
        }
        return clips;
    }

    // 상태 speed × (speed 파라미터를 쓰면 그 기본값). 상태를 못 찾으면 1.
    static float StateSpeed(AnimatorController controller, string stateName)
    {
        if (controller == null || string.IsNullOrEmpty(stateName))
            return 1f;

        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                AnimatorState state = child.state;
                if (state.name != stateName)
                    continue;

                float multiplier = 1f;
                if (state.speedParameterActive)
                {
                    AnimatorControllerParameter parameter = System.Array.Find(controller.parameters,
                        p => p.name == state.speedParameter);
                    if (parameter != null)
                        multiplier = parameter.defaultFloat;
                }
                return state.speed * multiplier;
            }
        }

        Debug.LogWarning($"{Tag} 애니메이터 상태 '{stateName}' 를 못 찾았다 — 속도 1 로 환산");
        return 1f;
    }

    static T Load<T>(string assetName) where T : Object
    {
        string path = $"{DataFolder}/{assetName}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            Debug.LogError($"{Tag} 데이터 에셋이 없다: {path}");
        return asset;
    }
}

/// <summary>애니 이벤트 저작의 순수 계산(시간 환산·병합). EditMode 테스트 대상.</summary>
internal static class AnimEventTiming
{
    /// <summary>상태 진입 뒤 실제 초 → 클립 초. 상태 속도가 2 면 클립은 두 배로 진행한다. 클립 길이 안으로 자른다.</summary>
    public static float ToClipTime(float realSeconds, float stateSpeed, float clipLength)
    {
        float time = Mathf.Max(0f, realSeconds) * Mathf.Max(0f, stateSpeed);
        return Mathf.Clamp(time, 0f, Mathf.Max(0f, clipLength));
    }

    /// <summary>ModelImporterClipAnimation.events 의 time 은 클립 길이 대비 정규화 값이다.</summary>
    public static float ToNormalized(float clipTime, float clipLength) =>
        clipLength > 0f ? Mathf.Clamp01(clipTime / clipLength) : 0f;

    /// <summary>
    /// <paramref name="functionName"/>(intParameter = 종류) 이벤트를 원하는 목록에 맞춘다.
    /// 같은 종류가 이미 있으면 <paramref name="resetExisting"/> 일 때만 위치를 바꾼다. 다른 함수·종류의 이벤트는 그대로 둔다.
    /// 결과는 시간순.
    /// </summary>
    public static AnimationEvent[] Merge(AnimationEvent[] existing, string functionName,
        IReadOnlyList<(int type, float time)> wanted, bool resetExisting, out int added, out int moved)
    {
        added = 0;
        moved = 0;
        var result = new List<AnimationEvent>(existing ?? System.Array.Empty<AnimationEvent>());

        foreach ((int type, float time) in wanted)
        {
            AnimationEvent current = result.Find(e => e.functionName == functionName && e.intParameter == type);
            if (current == null)
            {
                result.Add(new AnimationEvent { functionName = functionName, intParameter = type, time = time });
                added++;
            }
            else if (resetExisting && !Mathf.Approximately(current.time, time))
            {
                current.time = time;
                moved++;
            }
        }

        result.Sort((a, b) => a.time.CompareTo(b.time));
        return result.ToArray();
    }
}
