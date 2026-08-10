using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// 저작 도구 — 23호 fbx 클립의 **애니메이션 이벤트 + Loop Time** 을 새 FSM 규약에 맞춘다 (2026-08-10).
//
// 배경: 보스 코드는 재작성이 끝났는데 클립 이벤트는 레거시 BT 시대 이름이 그대로 남아 있었다.
// 실측 결과 `SK_23.fbx` 의 이벤트는 5개이고 **전부 구 이름**이다:
//   SetTargetEvent(jump) · FallEvent/OnLandedEvent(landingattack) · TryGrabEvent(grab) · ThrowEvent(grabdump)
// 새 릴레이(`MonsterAnimationEventRelay`)가 받는 이름은 `OnAttackHit`/`OnAttackEnd`/`OnAttackCommit` 뿐이라
// **교집합이 0** 이었다. 그리고 히트는 **타이머 폴백이 없다**(`MonsterBase.HandleAttack`) —
// 즉 이 도구를 돌리기 전까지 보스의 훅·어퍼·잡기·돌진·착지는 **데미지를 한 발도 내지 못한다.**
//
// 🔴 왜 손으로 .meta 를 고치지 않고 도구로 만들었나
//    `Assets/50.Art` 는 **SVN** 이다. 아티스트가 fbx 를 다시 올리면 임포터 설정이 초기화되면서
//    이벤트가 통째로 날아간다. 그때 이 메뉴를 다시 누르면 복구된다(멱등).
//
// 🔴 시간 단위 = **정규화 0~1** (클립 길이 대비 비율). 초가 아니다.
//    근거: 39프레임 클립의 ThrowEvent 가 0.932 인데, 초라면 클립 길이를 넘어선다.
//
// ⚠️ 히트 타이밍은 **추정값**이다. 훅·어퍼·돌진에는 원래 이벤트가 없어서 기준으로 삼을 것이 없었다.
//    아래 표의 `hit` 은 눈으로 보고 맞춰야 한다 — Animation 창에서 실제 타격 프레임을 확인할 것.
//    (grab·landingattack 은 기존 이벤트의 시간을 **그대로 승계**했다. 그건 아티스트가 잡아 둔 값이다.)
public static class No23ClipEventAuthoring
{
    const string FbxPath = "Assets/50.Art/Char/Boss/SK/SK_23.fbx";

    const string HitEvent = "OnAttackHit";
    const string EndEvent = "OnAttackEnd";

    // 클립별 목표 상태. hit/end 가 음수면 "그 이벤트를 두지 않는다"는 뜻이다.
    struct ClipSpec
    {
        public float Hit;
        public float End;
        public bool ForceLoop;   // 오래 유지되는 상태 — loopTime 을 켠다
        public string Why;
    }

    static readonly Dictionary<string, ClipSpec> Spec = new Dictionary<string, ClipSpec>
    {
        // ── 근접 단타 3종. 원래 이벤트가 없어 hit 은 추정값이다(눈으로 맞출 것). ──
        { "Boss_23_hookL",         new ClipSpec { Hit = 0.40f, End = 0.85f, Why = "좌훅 — 추정값" } },
        { "Boss_23_hookR",         new ClipSpec { Hit = 0.40f, End = 0.85f, Why = "우훅 — 추정값" } },
        { "Boss_23_uppercut",      new ClipSpec { Hit = 0.40f, End = 0.85f, Why = "어퍼 — 추정값" } },

        // ── 기존 이벤트의 시간을 승계한다(아티스트가 잡아 둔 값). 이름만 바꾼다. ──
        { "Boss_23_grab",          new ClipSpec { Hit = 0.3544601f, End = -1f, Why = "TryGrabEvent 시간 승계. End 는 두지 않는다 — 잡기 체인이 자기 종료를 소유한다" } },
        { "Boss_23_landingattack", new ClipSpec { Hit = 0.20552148f, End = 0.90f, Why = "OnLandedEvent 시간 승계" } },

        // ── 돌진. hit = 돌진 시작 시점이라 앞쪽이어야 한다(추정값). ──
        // ⚠️ 이 클립은 DashAttack 과 Rage 상태가 **공유**한다. Rage 로 재생될 때도 이벤트가 뜬다.
        { "Boss_23_dash",          new ClipSpec { Hit = 0.15f, End = -1f, Why = "돌진 시작 — 추정값. End 는 두지 않는다(체인이 소유)" } },

        // ── 이벤트를 두지 않는 클립. 구 이벤트를 지우기 위해 명시적으로 올린다. ──
        { "Boss_23_jump",          new ClipSpec { Hit = -1f, End = -1f, Why = "체공은 타이머(ArriveJump)가 몬다 — SetTargetEvent 는 수신자가 없어 제거" } },
        { "Boss_23_grabdump",      new ClipSpec { Hit = -1f, End = -1f, Why = "던지기는 타이머(ReleaseGrabThrow)가 몬다 — ThrowEvent 는 수신자가 없어 제거" } },

        // ── Loop Time 만 손보는 클립(이벤트 없음). ──
        { "Boss_23_idle",          new ClipSpec { Hit = -1f, End = -1f, ForceLoop = true, Why = "로코모션 정지 — 안 켜면 한 바퀴 뒤 마지막 프레임에서 굳는다" } },
        { "Boss_23_charging",      new ClipSpec { Hit = -1f, End = -1f, ForceLoop = true, Why = "차징 최대 20초 — 안 켜면 굳는다" } },
    };

    [MenuItem("Tools/Boss/23호 — 클립 이벤트 저작 (검증만)")]
    public static void Validate() => Run(apply: false);

    // ⚠️ 모달 확인창을 두지 않는다 — 이 메뉴는 MCP(에이전트)로도 실행되는데, 모달이 뜨면
    //    누를 사람이 없어 에디터가 통째로 멈춘다. 안전장치는 위의 **「검증만」 메뉴**다:
    //    무엇이 바뀌는지 먼저 읽고 누를 것. 되돌리기는 `svn revert` 로 된다.
    [MenuItem("Tools/Boss/23호 — 클립 이벤트 저작 (적용)")]
    public static void Apply() => Run(apply: true);

    static void Run(bool apply)
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[23호 클립] {FbxPath} 를 ModelImporter 로 열 수 없다 — 경로를 확인할 것.");
            return;
        }

        // 🔴 clipAnimations 는 **복사본**을 돌려준다. 배열 요소를 고친 뒤 반드시 다시 대입해야 한다.
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            Debug.LogError("[23호 클립] clipAnimations 가 비어 있다 — fbx 임포터에 클립이 잘려 있지 않다.");
            return;
        }

        var log = new StringBuilder();
        log.AppendLine($"[23호 클립] {(apply ? "적용" : "검증만")} — 클립 {clips.Length}개, 대상 {Spec.Count}개");

        int changed = 0;
        var seen = new HashSet<string>();

        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation c = clips[i];
            if (!Spec.TryGetValue(c.name, out ClipSpec spec)) continue;
            seen.Add(c.name);

            int frames = Mathf.Max(1, Mathf.RoundToInt(c.lastFrame - c.firstFrame));

            // ── 이벤트 ──────────────────────────────────────────────
            var want = new List<AnimationEvent>();
            if (spec.Hit >= 0f) want.Add(new AnimationEvent { time = spec.Hit, functionName = HitEvent });
            if (spec.End >= 0f) want.Add(new AnimationEvent { time = spec.End, functionName = EndEvent });

            AnimationEvent[] have = c.events ?? new AnimationEvent[0];
            bool evtSame = have.Length == want.Count;
            if (evtSame)
            {
                for (int k = 0; k < want.Count; k++)
                {
                    if (have[k].functionName == want[k].functionName &&
                        Mathf.Abs(have[k].time - want[k].time) < 0.0001f) continue;
                    evtSame = false;
                    break;
                }
            }

            // ── Loop Time ──────────────────────────────────────────
            bool loopSame = !spec.ForceLoop || c.loopTime;

            if (evtSame && loopSame)
            {
                log.AppendLine($"  = {c.name}  이미 저작됨");
                continue;
            }

            changed++;
            string oldNames = have.Length == 0 ? "(없음)" : string.Join(", ", have.Select(e => $"{e.functionName}@{e.time:0.###}"));
            string newNames = want.Count == 0 ? "(없음)" : string.Join(", ", want.Select(e => $"{e.functionName}@{e.time:0.###}(≈{c.firstFrame + e.time * frames:0}f)"));

            log.AppendLine($"  ▶ {c.name}");
            log.AppendLine($"      이벤트 : {oldNames}  →  {newNames}");
            if (spec.ForceLoop && !c.loopTime)
                log.AppendLine($"      loopTime: off → **on**");
            log.AppendLine($"      이유   : {spec.Why}");

            if (!apply) continue;

            c.events = want.ToArray();
            if (spec.ForceLoop) c.loopTime = true;
            clips[i] = c;
        }

        // 표에 있는데 fbx 에 없는 클립 = 이름이 바뀐 것이다. 조용히 지나가면 안 된다.
        foreach (string name in Spec.Keys.Where(n => !seen.Contains(n)))
            Debug.LogError($"[23호 클립] 표의 \"{name}\" 이 fbx 에 없다 — 클립 이름이 바뀌었다. 표를 갱신할 것.");

        if (apply && changed > 0)
        {
            importer.clipAnimations = clips;   // 🔴 복사본이므로 반드시 되돌려 놓는다
            importer.SaveAndReimport();
            log.AppendLine($"  → 재임포트 완료. 🔴 SVN 커밋은 팀장이 직접 할 것({FbxPath}.meta).");
        }
        else if (changed == 0)
        {
            log.AppendLine("  → 바꿀 것이 없다(멱등).");
        }
        else
        {
            log.AppendLine("  → 검증만이라 아무것도 쓰지 않았다.");
        }

        Debug.Log(log.ToString());
    }
}
