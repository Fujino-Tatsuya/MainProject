#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 개발용 보스 체력 깎기 키(2026-10-22 민경 요청). 페이즈 전환(송전기 차징 등) 연출을 바로 보려고 만든 것이다.
///
/// <list type="bullet">
/// <item><b>F4</b> — 살아 있는 보스(23호·웰즈) 체력을 최대 체력의 10% 만큼 깎는다</item>
/// </list>
///
/// 🔴 <b>DEV_HOTKEYS 를 지운 릴리스 빌드에는 안 들어간다.</b> 파일 전체가 <c>UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS</c> 안에 있다.
///
/// 🔴 <b>배선이 없다.</b> <see cref="DevMonsterKill"/> 처럼 <see cref="RuntimeInitializeOnLoadMethod"/> 로 스스로 올라온다 —
/// 씬·프리팹을 건드리지 않고, 지우고 싶으면 이 파일만 지우면 된다.
///
/// 🔴 <b>호스트에서만 듣는다.</b> 피해는 서버 권한이다. 클라에서 누르면 그렇다고 찍어 준다.
///
/// 🔴 <b>정상 경로로 때린다.</b> <c>TakeDamage(AttackInfo)</c> 를 그대로 타서 23호의 페이즈 판정(<c>EvaluatePhase</c>)·0줄 자폭 보호·사망이
/// 실제와 똑같이 흐른다. 방어력 경감(피해 × 100 / (100 + 방어력))만 미리 되돌려 실제로 10% 가 빠지게 한다 —
/// 쉴드·무적·자폭 보호 중이면 덜 빠지거나 안 빠질 수 있다(그것도 실제 규칙이다).
/// </summary>
public class DevBossHpCut : MonoBehaviour
{
    const KeyCode Key = KeyCode.F4;
    const float Ratio = 0.1f;

    static readonly List<MonsterBase> Buffer = new List<MonsterBase>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        // 도메인 리로드를 꺼 두면 두 번째 Play 에서 또 불린다. 이미 있으면 그대로 둔다.
        if (FindAnyObjectByType<DevBossHpCut>() != null) return;

        var go = new GameObject("[Dev] BossHpCut");
        go.AddComponent<DevBossHpCut>();
        DontDestroyOnLoad(go);
        Debug.Log("[DevBossHp] F4 = 보스 체력 10% 깎기 (호스트에서만, 디버그 단축키 빌드 전용)");
    }

    void Update()
    {
        if (!Input.GetKeyDown(Key)) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer)
        {
            Debug.LogWarning("[DevBossHp] 클라에서는 못 깎습니다 — 피해는 서버 권한입니다. 호스트에서 누르세요.");
            return;
        }

        Collect();
        if (Buffer.Count == 0)
        {
            Debug.Log("[DevBossHp] 살아 있는 보스가 없습니다.");
            return;
        }

        for (int i = 0; i < Buffer.Count; i++) Cut(Buffer[i]);
    }

    static void Cut(MonsterBase boss)
    {
        int max = boss.FinalMaxHp;
        int want = Mathf.Max(1, Mathf.CeilToInt(max * Ratio));
        int raw = Mathf.CeilToInt(want * (100f + boss.FinalDefense) / 100f);   // 방어력 경감을 되돌린다
        int before = boss.CurrentHealth;

        boss.TakeDamage(new AttackInfo(raw, AttackType.Default));

        int after = boss.CurrentHealth;
        Debug.Log($"[DevBossHp] '{boss.name}' {before} → {after} / {max} ({(max > 0 ? after * 100f / max : 0f):0.#}%)", boss);
    }

    /// <summary>살아 있는 보스만 — 23호(<see cref="TwentyThreeBoss"/>)와 웰즈(<see cref="BossWells"/> 가 붙은 몬스터).</summary>
    static void Collect()
    {
        Buffer.Clear();
        MonsterBase[] found = FindObjectsByType<MonsterBase>(FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            MonsterBase m = found[i];
            if (m == null || !m.isActiveAndEnabled || m.CurrentHealth <= 0) continue;
            if (!(m is TwentyThreeBoss) && !IsWells(m)) continue;
            Buffer.Add(m);
        }
    }

    // BossWells 는 MonsterBase 파생이 아니라 같은 보스 계층에 따로 붙는다 — 위·아래 어느 쪽이든 본다.
    static bool IsWells(MonsterBase m) =>
        m.GetComponentInParent<BossWells>() != null || m.GetComponentInChildren<BossWells>(true) != null;
}
#endif
