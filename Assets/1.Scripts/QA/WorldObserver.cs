using UnityEngine;

/// <summary>
/// 게임 상태 읽기 전용 관측기. 게임 코드 무수정 — 이미 공개된 static/프로퍼티만 읽는다.
/// (Player.LocalPlayer, Unit 복제 HP, BossHudTarget.Active 레지스트리)
/// </summary>
public static class WorldObserver
{
    /// <summary>이 클라이언트가 조작하는 플레이어(오너/오프라인). 없으면 null.</summary>
    public static Player LocalPlayer => Player.LocalPlayer;

    /// <summary>플레이어 체력 비율 0~1. 플레이어 없거나 MaxHp 0이면 0.</summary>
    public static float PlayerHealthNormalized
    {
        get
        {
            Player p = Player.LocalPlayer;
            if (p == null || p.MaxHp <= 0)
                return 0f;
            return Mathf.Clamp01((float)p.CurrentHealth / p.MaxHp);
        }
    }

    /// <summary>스폰된 보스 중 from에서 가장 가까운 살아있는 보스를 찾는다.</summary>
    public static bool TryGetNearestBoss(Vector3 from, out Unit boss, out float distance)
    {
        boss = null;
        distance = float.MaxValue;

        var list = BossHudTarget.Active;
        for (int i = 0; i < list.Count; i++)
        {
            BossHudTarget marker = list[i];
            if (marker == null || marker.Unit == null)
                continue;
            if (marker.Unit.CurrentHealth <= 0)
                continue;

            float d = Vector3.Distance(from, marker.transform.position);
            if (d < distance)
            {
                distance = d;
                boss = marker.Unit;
            }
        }

        return boss != null;
    }

    /// <summary>보스 체력 비율 0~1.</summary>
    public static float HealthNormalized(Unit unit)
    {
        if (unit == null || unit.MaxHp <= 0)
            return 0f;
        return Mathf.Clamp01((float)unit.CurrentHealth / unit.MaxHp);
    }
}
