using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버] 전기 장판 · 자폭 드론이 공유하는 플레이어 판정.
/// 기획: boss-electric-floor.md §6.4 · wells-suicide-drone.md §5.1 · §9.2.
///
/// - <b>유효(살아 있고 조작 가능)</b> = 생명 상태 Alive + 23호가 <b>실제로 쥐고 있지 않음</b>.
///   끌려오는 중(아직 안 붙잡힘)은 유효하다 — 기획서가 명시한 구분이고 코드에도 그대로 있다
///   (<c>_pulledPlayers</c> = 끌어오는 중 · <c>_grabbed</c> = 쥔 대상).
/// - 쓰러짐·완전 사망 = Alive 가 아님(코드에 별도 '쓰러짐' 상태가 없다 — DeadPresentation/Soul 이 그 자리).
/// - 무적은 여기서 거르지 않는다 — 피해는 <c>Player.CanApplyHealthDamage</c> 가 막고, 게이지 인원에서만 뺀다.
/// </summary>
public static class BossPatternTargets
{
    static readonly List<Player> _buffer = new List<Player>(8);

    /// <summary>접속한 플레이어 전원(PlayerObject 기준). 반환 리스트는 재사용 버퍼 — 보관하지 말 것.</summary>
    public static List<Player> AllPlayers()
    {
        _buffer.Clear();
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return _buffer;

        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            NetworkObject po = client.PlayerObject;
            if (po == null) continue;
            Player p = po.GetComponent<Player>();
            if (p == null) p = po.GetComponentInChildren<Player>();
            if (p != null) _buffer.Add(p);
        }
        return _buffer;
    }

    public static bool IsAlive(Player p)
    {
        if (p == null || !p.gameObject.activeInHierarchy) return false;
        PlayerLifeCycleController life = p.GetComponent<PlayerLifeCycleController>();
        if (life == null) life = p.GetComponentInParent<PlayerLifeCycleController>();
        return life == null || life.State == PlayerLifeState.Alive;
    }

    /// <summary>살아 있고 23호가 쥐고 있지 않다 — 표식 대상 · 장판/드론 피해 대상.</summary>
    public static bool IsValid(Player p, TwentyThreeBoss boss) =>
        IsAlive(p) && (boss == null || !boss.IsHolding(p));

    public static bool IsInvulnerable(Player p)
    {
        PlayerInvulnerability inv = p != null ? p.GetComponent<PlayerInvulnerability>() : null;
        return inv != null && inv.IsServerInvulnerable;
    }

    /// <summary>발 위치 — 장판 판정 기준(§6.2). 루트 피벗이 발이다.</summary>
    public static Vector3 FootPosition(Player p) => p.transform.position;

    /// <summary>플레이어 몸 중심 — 크로스헤어가 따라가는 점(드론 §6.1).</summary>
    public static Vector3 BodyCenter(Transform t)
    {
        Collider c = t.GetComponent<CapsuleCollider>();
        if (c == null) c = t.GetComponentInChildren<Collider>();
        return c != null ? c.bounds.center : t.position + Vector3.up;
    }

    /// <summary>[서버] 일반 피해 1회. 방어·감소·보호막은 수신측이 처리한다.</summary>
    public static void Damage(Unit target, int damage, Vector3 source, Transform sourceTransform)
    {
        if (target == null || damage <= 0) return;
        var info = new AttackInfo(damage, AttackType.Default);
        var ctx = new AttackHitContext(source, sourceTransform);
        target.ReceiveAttack(info, ctx);
    }
}
