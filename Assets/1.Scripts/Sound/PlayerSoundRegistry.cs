using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소리 주인 판정용 플레이어 목록(PLAN-sound-mix Q6 — 다른 플레이어 소리 30%). 모든 피어에서 동작한다.
/// <para>
/// 서버 전용 목록(<c>BossPatternTargets.AllPlayers</c>)은 클라에서 비어 있어 쓸 수 없다. 모든 플레이어에 자동으로 붙는
/// <see cref="PlayerFootstepSound"/> 가 켜질 때 등록·꺼질 때 해제한다 — <c>Player.cs</c> 를 건드리지 않으려는 선택.
/// </para>
/// </summary>
public static class PlayerSoundRegistry
{
    static readonly List<Player> Players = new List<Player>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnLoad() => Players.Clear();

    public static void Register(Player player)
    {
        if (player != null && !Players.Contains(player)) Players.Add(player);
    }

    public static void Unregister(Player player) => Players.Remove(player);

    /// <summary>
    /// <paramref name="worldPos"/> 에서 수평 <paramref name="radius"/> 안 가장 가까운 플레이어. 없으면 null.
    /// <paramref name="localIsNear"/> = 내 캐릭터(오너 또는 스폰 전)가 반경 안에 있는가 — 있으면 호출자는 내 소리로 본다.
    /// </summary>
    public static Player Nearest(Vector3 worldPos, float radius, out bool localIsNear)
    {
        localIsNear = false;
        Player best = null;
        float bestSqr = radius * radius;
        for (int i = Players.Count - 1; i >= 0; i--)
        {
            Player p = Players[i];
            if (p == null) { Players.RemoveAt(i); continue; }

            Vector3 d = p.transform.position - worldPos;
            d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr > radius * radius) continue;

            if (!p.IsSpawned || p.IsOwner) localIsNear = true;
            if (sqr <= bestSqr) { bestSqr = sqr; best = p; }
        }
        return best;
    }
}
