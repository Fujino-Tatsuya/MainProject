using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// 저작·진단 도구 — `BossDataSO`(보스 데이터 애셋)의 프리팹 참조 배선과 현재 값 확인 (2026-08-10).
//
// ── 왜 필요했나 ──────────────────────────────────────────────────────────────
// P9 첫 Play 에서 `bombPrefab 이 비어 있어 Wells 가 빈손으로 던진다` 경고가 떴다.
// P2 에서 `Bomb.prefab` 을 만들었는데 **어디에도 배선하지 않았기 때문**이다.
// 🔴 그리고 이 필드는 **프리팹이 아니라 SO 에 있다** — `BossDataSO.bombPrefab`.
//    보스 프리팹 인스펙터를 뒤져도 안 나온다. 폭탄·장판·예고 프리팹 전부 SO 쪽이다.
//
// ── 진단을 도구로 만든 이유 ──────────────────────────────────────────────────
// 같은 Play 에서 `maxHp` 가 디스크(2000)와 런타임(100)이 달랐다. 애셋 파일을 grep 하는 것으로는
// **Unity 가 메모리에 들고 있는 값**을 알 수 없다(교훈 #22·#69). 그래서 읽기도 엔진 안에서 한다.
public static class BossDataWiring
{
    const string BossDataPath = "Assets/2.Prefabs/Monster/Data/No23.asset";

    [MenuItem("Tools/Boss/보스 데이터 — 배선 검증 (읽기 전용)")]
    public static void Verify()
    {
        var data = AssetDatabase.LoadAssetAtPath<BossDataSO>(BossDataPath);
        if (data == null) { Debug.LogError($"[BossData] {BossDataPath} 를 못 찾았다."); return; }

        var sb = new StringBuilder($"[BossData] {data.name} — Unity 가 들고 있는 값\n");
        sb.AppendLine($"  archetype={data.archetype} · maxHp={data.maxHp} · defense={data.defense} · attackDamage={data.attackDamage}");
        sb.AppendLine($"  {Mark(data.bombPrefab != null)} bombPrefab        = {Name(data.bombPrefab)}");
        sb.AppendLine($"  {Mark(data.jumpTelegraphPrefab != null)} jumpTelegraphPrefab = {Name(data.jumpTelegraphPrefab)}");
        sb.AppendLine($"  {Mark(data.chargeZonePrefab != null)} chargeZonePrefab  = {Name(data.chargeZonePrefab)}");

        sb.AppendLine($"  attacks {data.attacks?.Length ?? 0}개:");
        if (data.attacks != null)
            foreach (BossAttackEntry a in data.attacks)
                sb.AppendLine($"      {a.attackId,-14} state='{a.animatorStateName}' anchor='{a.hitboxAnchorName}'" +
                              $"{(a.opensCounterWindow ? " [카운터]" : "")}{(a.superArmor ? " [슈퍼아머]" : "")}");

        sb.AppendLine($"  phases {data.phases?.Length ?? 0}개:");
        if (data.phases != null)
            foreach (BossPhaseEntry p in data.phases)
                sb.AppendLine($"      threshold={p.hpThreshold} sequence={p.sequence} 데미지×{p.damageMultiplier}");

        Debug.Log(sb.ToString());
    }

    // 차징 오라 범위 표시 — 점프 예고와 **같은 프리팹**(`TelegraphPrefabPath`)을 재사용한다.
    // 둘 다 바닥에 눕는 원이라 새로 만들 이유가 없다.
    //
    // 🔴 오라 범위가 **보여야** 플레이어가 피한다. 비워 두면 판정만 있고 표시가 없어
    //    "갑자기 밀린다"가 된다 — 예고 없는 판정은 이 프로젝트에서 금지에 가깝다.
    const string TelegraphPrefabPath = "Assets/2.Prefabs/Monster/Boss/JumpTelegraph.prefab";
    static string Mark(bool ok) => ok ? "✓" : "✗";
    static string Name(Object o) => o != null ? o.name : "(비어 있음)";
}
