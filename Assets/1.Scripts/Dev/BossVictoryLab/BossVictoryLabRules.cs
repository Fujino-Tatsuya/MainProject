using UnityEngine;

/// <summary>
/// 보스 처치 연출 반복 디버그 랩(할 일 244 — DebugBossVictory 씬)의 순수 규칙. 상태가 없어 EditMode 로 고정한다.
/// 막타/팀원 배치. (옛 단계 이름·점프·시계 밀기는 ZZZ 글리치 교체로 뺐다 — 새 연출은 각 피어 로컬 시간으로 흐른다.)
/// </summary>
public static class BossVictoryLabRules
{
    /// <summary>
    /// 막타 위치 — 보스에서 <paramref name="screenSouth"/>(화면 아래, 수평) 쪽으로 <paramref name="distance"/> m 를
    /// 위에서 볼 때 시계 방향 <paramref name="angleDegrees"/>° 돌린 곳. 높이는 <paramref name="groundY"/>.
    /// </summary>
    public static Vector3 KillerPosition(Vector3 boss, Vector3 screenSouth, float angleDegrees, float distance, float groundY)
    {
        Vector3 dir = Flat(screenSouth);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
        dir = Quaternion.Euler(0f, angleDegrees, 0f) * dir.normalized;
        Vector3 p = boss + dir * Mathf.Max(0f, distance);
        p.y = groundY;
        return p;
    }

    /// <summary>막타가 보스를 보는 방향에 <paramref name="yawOffsetDegrees"/>° 를 더한 회전(수평).</summary>
    public static Quaternion KillerFacing(Vector3 killer, Vector3 boss, float yawOffsetDegrees)
    {
        Vector3 to = Flat(boss - killer);
        if (to.sqrMagnitude < 0.0001f) to = Vector3.forward;
        return Quaternion.Euler(0f, yawOffsetDegrees, 0f) * Quaternion.LookRotation(to.normalized, Vector3.up);
    }

    /// <summary>팀원 위치 — 막타 기준 로컬 오프셋(x = 오른쪽, z = 앞 = 보스 쪽). 높이는 막타와 같다.</summary>
    public static Vector3 TeammatePosition(Vector3 killer, Quaternion killerFacing, Vector3 localOffset)
    {
        Vector3 p = killer + killerFacing * new Vector3(localOffset.x, 0f, localOffset.z);
        p.y = killer.y;
        return p;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
