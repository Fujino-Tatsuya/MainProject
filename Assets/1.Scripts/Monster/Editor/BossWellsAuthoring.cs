#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// 검증 도구 — `Wells.prefab` 이 "안 보인다 / 폭탄을 안 던진다" 를 **애셋 쪽에서** 가른다 (2026-08-13).
//
// ─── 이 도구가 밝힌 것 (그리고 내가 두 번 틀린 것) ───────────────────────────
//
// 🔴 "본 계층(`rig`)이 빠졌다" → **틀렸다.** rig 는 있고 스킨 본 147개가 하나도 안 끊겼다.
//    경로 조회(`Wells/BombSocket`)가 실패한 것을 부재로 읽은 것이 오진의 원인이었다 —
//    소켓은 루트가 아니라 **`Bone` 이라는 본 밑**에 있었다.
//
// 🔴 "Animator 의 Avatar 가 null 이라 클립이 안 돈다" → **틀렸다.** 대조군인
//    `TwentyThree.prefab` 도 `m_Avatar: {fileID: 0}` 인데 보스 애니메이션은 정상 동작한다
//    (Generic 리그는 Avatar 없이도 **경로 이름으로** 커브를 바인딩한다).
//
// → 그래서 이 도구는 **고치지 않는다.** 애셋 쪽 사실만 찍어 준다. 원인이 런타임에 있으면
//   `BossWells` 에 심은 투척 체인 로그(`[Wells/진단]`)가 어느 단계에서 끊기는지 알려 준다.
//
// ⚠️ 프리팹을 다시 만드는 메뉴는 **일부러 두지 않았다.** 재생성은 내부 fileID 를 바꿔
//    `TwentyThree.prefab` 의 중첩 오버라이드를 깨뜨린다. 멀쩡한 구조를 상대로 치를 대가가 아니다.
public static class BossWellsAuthoring
{
    const string WellsPrefabPath = "Assets/2.Prefabs/Monster/Boss/Wells.prefab";
    const string BossPrefabPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";

    // 🔴 실측 경로다. 프리팹 루트 바로 밑이 아니라 **모델 루트(`TwentyThree`) 아래**에 리그가 있다
    //    — 이 접두사를 빼먹어 "23호 중첩 없음" 이라는 거짓 보고를 한 번 냈다.
    const string MountBonePath = "TwentyThree/rig/c_pos/c_traj/c_root_master.x";

    [MenuItem("Tools/Boss/Wells — 검증 (읽기 전용)")]
    public static void Verify()
    {
        var wells = AssetDatabase.LoadAssetAtPath<GameObject>(WellsPrefabPath);
        if (wells == null) { Debug.LogError($"[Wells검증] 프리팹 없음: {WellsPrefabPath}"); return; }

        var sb = new System.Text.StringBuilder("[Wells검증]\n");

        sb.AppendLine($"  rig(본 계층)      : {(wells.transform.Find("rig") != null ? "✓ 있음" : "🔴 없음 — 메시가 붕괴한다")}");

        var anim = wells.GetComponent<Animator>();
        sb.AppendLine($"  Animator          : {(anim != null ? "✓" : "🔴 없음")}");
        if (anim != null)
        {
            // Avatar 는 null 이 정상이다(23호 본체도 같다) — 참고용으로만 찍는다.
            sb.AppendLine($"    Avatar          : {(anim.avatar != null ? anim.avatar.name : "없음(23호 본체와 동일 — 정상)")}");
            sb.AppendLine($"    Controller      : {(anim.runtimeAnimatorController != null ? "✓ " + anim.runtimeAnimatorController.name : "🔴 없음 — 클립을 재생할 수 없다")}");
            sb.AppendLine($"    CullingMode     : {anim.cullingMode}{(anim.cullingMode == AnimatorCullingMode.AlwaysAnimate ? " ✓" : " ⚠️ 화면 밖에서 멈추면 투척 이벤트가 끊긴다")}");
        }

        var bw = wells.GetComponent<BossWells>();
        sb.AppendLine($"  BossWells         : {(bw != null ? "✓" : "🔴 없음")}");
        if (bw != null && bw.BombSocket != null)
            sb.AppendLine($"    BombSocket 부모 : {(bw.BombSocket.parent != null ? bw.BombSocket.parent.name : "(루트)")}");

        var smr = wells.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null)
        {
            int bad = 0;
            for (int i = 0; i < smr.bones.Length; i++) if (smr.bones[i] == null) bad++;
            sb.AppendLine($"  스킨 본           : {smr.bones.Length}개 중 끊긴 것 {bad}개 {(bad == 0 ? "✓" : "🔴 메시가 붕괴한다")}");
            sb.AppendLine($"    RootBone        : {(smr.rootBone != null ? smr.rootBone.name : "🔴 없음")}");
            sb.AppendLine($"    렌더러 활성     : {(smr.enabled ? "✓" : "🔴 꺼져 있다")} · 머티리얼 {smr.sharedMaterials.Length}개" +
                          $"{(System.Array.Exists(smr.sharedMaterials, m => m == null) ? " 🔴 빈 슬롯 있음" : "")}");
        }
        else sb.AppendLine("  SkinnedMeshRenderer: 🔴 없음");

        // 23호에 어떻게 붙어 있나 — 월드 스케일까지 본다(23호 모델 루트와 같아야 한다).
        var boss = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        Transform mount = boss != null ? boss.transform.Find(MountBonePath) : null;
        Transform nested = mount != null ? mount.Find("Wells") : null;
        if (nested != null)
        {
            sb.AppendLine($"  23호 중첩         : ✓ localScale {nested.localScale.x} · 매단 본 {mount.name}");
            sb.AppendLine($"    월드 스케일     : {nested.lossyScale.x:0.###} (모델 루트 {boss.transform.Find("TwentyThree")?.lossyScale.x ?? -1:0.###} 와 같아야 한다)");
        }
        else sb.AppendLine($"  23호 중첩         : 🔴 없음 ({MountBonePath} 밑에 Wells 가 없다)");

        Debug.Log(sb.ToString());
    }
}
#endif
