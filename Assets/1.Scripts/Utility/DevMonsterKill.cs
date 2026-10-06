#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 개발용 즉사 키. 사망 연출(디졸브·부위 붕괴)을 반복해서 보려고 만든 것이다 —
/// ChompBot 은 100 HP 이고 거너 평타는 5 라, 과열 대기까지 넣으면 한 마리 잡는 데 9초쯤 걸린다.
/// 연출 하나 고칠 때마다 그걸 반복할 수는 없다.
///
/// <list type="bullet">
/// <item><b>F9</b> — 가장 가까운 몬스터 하나</item>
/// <item><b>Shift + F9</b> — 씬의 몬스터 전부</item>
/// </list>
///
/// 🔴 <b>빌드에 안 들어간다.</b> 파일 전체가 <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c> 안에 있다.
///
/// 🔴 <b>배선이 없다.</b> <see cref="RuntimeInitializeOnLoadMethod"/> 로 스스로 올라온다 —
/// 씬이나 프리팹에 손을 대지 않으므로 팀원 파일과 충돌할 일이 없고, 지우고 싶으면 이 파일만 지우면 된다.
///
/// 🔴 <b>호스트에서만 듣는다.</b> 피해·사망은 서버 권한이라(<c>MonsterBase.TakeDamage</c> 가
/// <c>IsServer</c> 로 막는다) 클라에서 눌러 봐야 조용히 아무 일도 안 일어난다. 그래서
/// 클라에서 누르면 그렇다고 찍어 준다. MPPM 2인 검증은 <b>호스트에서 눌러</b> 양쪽 화면을 보면 된다.
///
/// 🔴 <b>정상 경로로 죽인다.</b> <c>ApplyDirectHealthDamage</c> 는 HP 만 깎고 끝이라
/// <c>EnterDead</c> 가 안 불린다 — 사망 판정은 <c>MonsterBase.TakeDamage</c> 안에 있다.
/// 그래서 평타와 같은 <see cref="AttackInfo"/> 를 넣어 사망 애니 → <c>IDeathEffect</c> →
/// 디스폰까지 실제와 똑같이 흐르게 한다. 그러지 않으면 검증의 의미가 없다.
/// </summary>
public class DevMonsterKill : MonoBehaviour
{
    const KeyCode Key = KeyCode.F9;

    // 방어력·쉴드를 뚫고도 확실히 죽는 값. int 연산 중간에 넘치지 않게 넉넉히 작게 잡는다.
    const int LethalDamage = 1_000_000;

    static readonly List<MonsterBase> Buffer = new List<MonsterBase>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        // 도메인 리로드를 꺼 두면 두 번째 Play 에서 또 불린다. 이미 있으면 그대로 둔다.
        if (FindAnyObjectByType<DevMonsterKill>() != null) return;

        // 계층에 그대로 보이게 둔다 — 숨기면 "이 키가 어디서 오는지" 아무도 못 찾는다.
        var go = new GameObject("[Dev] MonsterKill");
        go.AddComponent<DevMonsterKill>();
        DontDestroyOnLoad(go);
        Debug.Log("[DevKill] F9 = 가장 가까운 몬스터 즉사 · Shift+F9 = 전부 (호스트에서만, 개발 빌드 전용)");
    }

    void Update()
    {
        if (!Input.GetKeyDown(Key)) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer)
        {
            Debug.LogWarning("[DevKill] 클라에서는 못 죽입니다 — 피해·사망은 서버 권한입니다. 호스트에서 누르세요.");
            return;
        }

        bool all = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (all) KillAll();
        else KillNearest();
    }

    static void KillNearest()
    {
        Collect();
        if (Buffer.Count == 0)
        {
            Debug.Log("[DevKill] 살아 있는 몬스터가 없습니다.");
            return;
        }

        Vector3 from = ViewerPosition();
        MonsterBase best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < Buffer.Count; i++)
        {
            float d = (Buffer[i].transform.position - from).sqrMagnitude;
            if (d >= bestSqr) continue;
            bestSqr = d;
            best = Buffer[i];
        }

        if (best == null) return;

        Debug.Log($"[DevKill] '{best.name}' 즉사 ({Mathf.Sqrt(bestSqr):0.0}m)", best);
        Kill(best);
    }

    static void KillAll()
    {
        Collect();
        Debug.Log($"[DevKill] 몬스터 {Buffer.Count}마리 즉사");
        for (int i = 0; i < Buffer.Count; i++) Kill(Buffer[i]);
    }

    /// <summary>평타와 같은 경로로 때린다 — 사망 애니·연출·디스폰이 실제와 똑같이 흐른다.</summary>
    static void Kill(MonsterBase monster)
    {
        monster.BreakShield();   // 쉴드가 남아 있으면 한 방에 안 죽는 몹이 있다
        monster.TakeDamage(new AttackInfo(LethalDamage, AttackType.Default));
    }

    /// <summary>살아 있는 몬스터만 모은다. 이미 죽은 것(디스폰 대기 중)을 또 때리면 로그만 지저분해진다.</summary>
    static void Collect()
    {
        Buffer.Clear();
        MonsterBase[] found = FindObjectsByType<MonsterBase>(FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null || !found[i].isActiveAndEnabled) continue;
            if (found[i].CurrentHealth <= 0) continue;
            Buffer.Add(found[i]);
        }
    }

    /// <summary>
    /// 거리 기준점. 내 플레이어가 있으면 그쪽, 없으면 카메라.
    /// (에디터에서 플레이어 없이 몬스터만 띄워 놓고 보는 경우가 있다.)
    /// </summary>
    static Vector3 ViewerPosition()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null && players[i].IsOwner)
                return players[i].transform.position;

        return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
    }
}
#endif
