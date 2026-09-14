using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TeslaBot 의 <c>Shoot</c> 클립에 <c>OnAttackHit</c>·<c>OnAttackEnd</c> 애니메이션 이벤트를 넣는다.
///
/// 🔴 <b>왜 필요한가</b>(2026-09-14 실측): TeslaBot 은 조준선까지 나오는데 <b>발사를 하지 않았다</b>.
/// 원인은 이벤트 위치였다 —
/// <code>
///   PeekABot A_Shoot : 클립 Shoot  → OnAttackHit(0.4) · OnAttackEnd(0.95) · PlayEffect(0.409)
///   TeslaBot A_Shoot : 클립 Charge → OnAttackHit          ← Shoot 클립에는 이벤트가 0개
/// </code>
/// 우리 컨트롤러는 <c>Shoot</c> 클립을 재생하므로 TeslaBot 은 히트 이벤트를 영영 못 받는다.
/// 그리고 <c>MonsterBase.HandleAttack</c> 은 <b>타이머 폴백을 의도적으로 제거</b>했다 —
/// "이벤트가 없으면 데미지가 나가지 않는다". 즉 구조적으로 발사가 불가능한 상태였다.
/// (원래 아트 컨트롤러가 <c>Charge → Shoot</c> 였던 이유가 이것이다. 그 경로는 우리가 쓰지 않는
///  <c>Attack</c> 트리거를 요구해서 갇히는 문제가 있었고, 그래서 컨트롤러를 갈아 끼웠다.)
///
/// 시각은 PeekABot 과 같은 값을 쓴다(두 클립 모두 frames 0~9, loopTime 0 으로 구조가 같다).
/// <c>PlayEffect</c> 는 넣지 않는다 — TeslaBot 에는 <c>EffectAnimEvents</c> 수신자가 없어서
/// 수신자 없는 이벤트 경고만 매 발사마다 쌓인다.
/// 수신자(<c>MonsterAnimationEventRelay</c>)는 <c>MonsterBase.cs:141</c> 이 런타임에 붙여 준다.
///
/// ⚠️ <b>이 .meta 는 SVN 이다</b>(<c>Assets/50.Art/</c> 아래). 적용하면 git 이 아니라 SVN 커밋
///    대상이고, <b>아트가 팩을 다시 올리면 덮인다</b> — 그때 이 도구를 다시 돌려야 한다.
/// </summary>
public static class TeslaShootClipEventAuthoring
{
    const string FbxPath =
        "Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack/" +
        "Robot Sentries Pack 02/Animations/TeslaBot/A_Shoot.fbx";

    const string ClipName = "Shoot";
    const string HitEvent = "OnAttackHit";
    const string EndEvent = "OnAttackEnd";
    const float HitTime = 0.4f;    // PeekABot 과 동일
    const float EndTime = 0.95f;   // PeekABot 과 동일

    [MenuItem("Tools/Boss/TeslaBot — Shoot 클립 이벤트 저작 (검증)")]
    public static void Validate() => Run(dryRun: true);

    [MenuItem("Tools/Boss/TeslaBot — Shoot 클립 이벤트 저작 (적용)")]
    public static void Apply() => Run(dryRun: false);

    static void Run(bool dryRun)
    {
        var log = new StringBuilder($"[TeslaShoot/{(dryRun ? "검증" : "적용")}] {ClipName}\n");

        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            log.Append($"  ✗ ModelImporter 로 열 수 없다 — {FbxPath}");
            Debug.LogError(log.ToString());
            return;
        }

        // 🔴 clipAnimations 는 복사본을 돌려준다. 고친 뒤 반드시 다시 대입해야 한다.
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            log.Append("  ✗ clipAnimations 가 비어 있다");
            Debug.LogError(log.ToString());
            return;
        }

        int idx = -1;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i].name == ClipName) { idx = i; break; }

        if (idx < 0)
        {
            log.Append($"  ✗ '{ClipName}' 클립이 없다 (있는 것: {string.Join(", ", Names(clips))})");
            Debug.LogError(log.ToString());
            return;
        }

        AnimationEvent[] have = clips[idx].events ?? new AnimationEvent[0];
        bool hasHit = Has(have, HitEvent, HitTime);
        bool hasEnd = Has(have, EndEvent, EndTime);

        if (hasHit && hasEnd)
        {
            log.Append($"  = 이미맞음 — {HitEvent}@{HitTime} · {EndEvent}@{EndTime} (이벤트 {have.Length}개)");
            Debug.Log(log.ToString());
            return;
        }

        log.AppendLine($"  ▶ 현재 이벤트 {have.Length}개 → {HitEvent}@{HitTime}{(hasHit ? "(있음)" : " 추가")} · " +
                       $"{EndEvent}@{EndTime}{(hasEnd ? "(있음)" : " 추가")}");

        if (dryRun)
        {
            log.Append("  (검증만 — 적용하지 않았다. 이 .meta 는 SVN 이다)");
            Debug.Log(log.ToString());
            return;
        }

        clips[idx].events = new[]
        {
            new AnimationEvent { time = HitTime, functionName = HitEvent },
            new AnimationEvent { time = EndTime, functionName = EndEvent },
        };
        importer.clipAnimations = clips;   // 🔴 복사본이므로 되돌려 놓는다
        importer.SaveAndReimport();

        // 되읽어 확인한다.
        var reread = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        AnimationEvent[] after = new AnimationEvent[0];
        foreach (ModelImporterClipAnimation c in reread.clipAnimations)
            if (c.name == ClipName) { after = c.events ?? after; break; }

        bool ok = Has(after, HitEvent, HitTime) && Has(after, EndEvent, EndTime);
        log.Append(ok
            ? $"  ✓ 적용 (되읽기: 이벤트 {after.Length}개 ✓)\n" +
              "  ⚠️ 이 .meta 는 SVN 이다 — SVN 으로 커밋해야 팀에 반영된다. 아트가 팩을 갱신하면 덮인다."
            : "  🔴 적용했는데 되읽으니 없다");

        if (ok) Debug.Log(log.ToString());
        else Debug.LogError(log.ToString());
    }

    static bool Has(AnimationEvent[] events, string fn, float time)
    {
        foreach (AnimationEvent e in events)
            if (e.functionName == fn && Mathf.Abs(e.time - time) < 0.001f) return true;
        return false;
    }

    static string[] Names(ModelImporterClipAnimation[] clips)
    {
        var n = new string[clips.Length];
        for (int i = 0; i < clips.Length; i++) n[i] = clips[i].name;
        return n;
    }
}
