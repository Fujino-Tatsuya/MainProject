using UnityEngine;

/// <summary>
/// 벽 충돌 한 건(서버 판정 결과). <see cref="UnitWallImpacts.ServerImpacted"/>·<see cref="Unit.ServerWallImpacted"/> 로 나간다.
/// 효과(피해·기절 등)는 없다 — 이벤트만(할 일 32 사용자 결정 10-13).
/// </summary>
public readonly struct UnitWallImpact
{
    public UnitWallImpact(Unit target, Unit pusher, Vector3 point, Vector3 normal, float speed, Collider wall)
    {
        Target = target;
        Pusher = pusher;
        Point = point;
        Normal = normal;
        Speed = speed;
        Wall = wall;
    }

    /// <summary>벽에 부딪힌 유닛.</summary>
    public Unit Target { get; }
    /// <summary>민 쪽(알 수 없으면 null — 예: <c>Unit.Knockback(방향, 세기)</c> 경로).</summary>
    public Unit Pusher { get; }
    /// <summary>벽 접촉점(월드).</summary>
    public Vector3 Point { get; }
    /// <summary>벽 면 법선(수평 단위 — 벽에서 유닛 쪽).</summary>
    public Vector3 Normal { get; }
    /// <summary>충격 속도(m/s) — 이동 속도의 벽 법선 반대 성분(정면이면 그대로, 비스듬하면 줄어든다).</summary>
    public float Speed { get; }
    /// <summary>부딪힌 Wall 레이어 콜라이더(서버 로컬 참조 — 복제하지 않는다).</summary>
    public Collider Wall { get; }
}

/// <summary>스윕 결과 한 건(순수 판정 입력). 런타임은 <see cref="RaycastHit"/> 에서 채운다.</summary>
public struct WallSweepHit
{
    public int Layer;
    public float Distance;
    public Vector3 Point;
    public Vector3 Normal;
    /// <summary>스윕 시작부터 겹친 콜라이더(PhysX 가 distance 0 · point 0 · normal = −방향 으로 준다).</summary>
    public bool StartedOverlapping;
    /// <summary>움직이는 유닛 자신의 콜라이더.</summary>
    public bool IsSelf;
}

public enum WallJudgeResult : byte
{
    /// <summary>앞에 아무것도 없다.</summary>
    None,
    /// <summary>벽 충돌.</summary>
    Wall,
    /// <summary>가장 가까운 것이 벽이 아니다(배리어·플레이어·몬스터·상자 등) — 벽 충돌 아님.</summary>
    BlockedByOther,
    /// <summary>벽이지만 비스듬히 스쳤다(진행 방향 · 벽 법선 &gt; −minFacing).</summary>
    Grazing,
    /// <summary>벽이 판정 거리(reach) 밖.</summary>
    OutOfReach,
}

/// <summary>
/// 벽 충돌 순수 판정(EditMode — <c>UnitWallImpactJudgeTests</c>). 몸 크기 스윕 결과 → 벽 충돌인가.
/// <list type="number">
/// <item>자기 콜라이더는 버린다. 시작부터 겹친 <b>벽 아닌</b> 콜라이더(바닥·옆 유닛)도 버린다 — 앞을 막는 게 아니다.</item>
/// <item>남은 것 중 가장 가까운 것이 <see cref="WallJudgeResult.Wall"/> 레이어여야 한다. 배리어·유닛·상자가 먼저면 <see cref="WallJudgeResult.BlockedByOther"/>.</item>
/// <item>그 거리 ≤ reach, 진행 방향이 벽 면을 향해야 한다(수평 법선과 내적 ≤ −minFacing).</item>
/// </list>
/// 한 번의 외부 힘(넉백·밀치기)에 1회만 내는 것은 <see cref="WallImpactEpisode"/> 몫.
/// </summary>
public static class UnitWallImpactJudge
{
    /// <summary>정면 판정 하한 — 벽 법선과 진행 방향 사이 약 73° 까지(스피너 연계의 벽 쪽 판정과 같은 값).</summary>
    public const float DefaultMinFacing = 0.3f;

    public static WallJudgeResult Judge(WallSweepHit[] hits, int count, int wallMask, Vector3 moveDirection, float reach,
                                        out int wallIndex, float minFacing = DefaultMinFacing)
    {
        wallIndex = -1;
        int nearest = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            WallSweepHit h = hits[i];
            if (h.IsSelf) continue;
            bool isWall = IsInMask(h.Layer, wallMask);
            if (h.StartedOverlapping && !isWall) continue;
            float d = h.StartedOverlapping ? 0f : h.Distance;
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        if (nearest < 0) return WallJudgeResult.None;
        if (best > reach) return WallJudgeResult.OutOfReach;
        if (!IsInMask(hits[nearest].Layer, wallMask)) return WallJudgeResult.BlockedByOther;

        Vector3 dir = Horizontal(moveDirection);
        Vector3 normal = hits[nearest].StartedOverlapping ? -dir : Horizontal(hits[nearest].Normal);
        if (dir == Vector3.zero || normal == Vector3.zero) return WallJudgeResult.Grazing;
        if (Vector3.Dot(dir, normal) > -Mathf.Abs(minFacing)) return WallJudgeResult.Grazing;

        wallIndex = nearest;
        return WallJudgeResult.Wall;
    }

    /// <summary>판정 거리 — 밀린 이동이 막혔으면(<paramref name="moved"/> &lt; 요청의 절반) 더 넉넉한 <paramref name="blockedSlack"/>.
    /// NavMesh 로 움직이는 몬스터는 벽에서 에이전트 반경만큼 안쪽에서 멈추기 때문이다.</summary>
    public static float Reach(float requested, float moved, float contactSlack, float blockedSlack) =>
        IsBlocked(requested, moved) ? Mathf.Max(contactSlack, blockedSlack) : Mathf.Max(0f, contactSlack);

    public static bool IsBlocked(float requested, float moved) => requested > 1e-4f && moved < requested * 0.5f;

    /// <summary>충격 속도 — 이동 속도의 벽 법선 반대 성분.</summary>
    public static float ImpactSpeed(float speed, Vector3 moveDirection, Vector3 wallNormal)
    {
        Vector3 dir = Horizontal(moveDirection);
        Vector3 n = Horizontal(wallNormal);
        return Mathf.Max(0f, speed) * Mathf.Max(0f, -Vector3.Dot(dir, n));
    }

    public static bool IsInMask(int layer, int mask) => layer >= 0 && layer < 32 && (mask & (1 << layer)) != 0;

    public static Vector3 Horizontal(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero;
    }
}

/// <summary>
/// 외부 힘 한 번(넉백·밀치기) = 에피소드 하나. 에피소드마다 벽 충돌을 <b>1회</b>만 낸다(순수).
/// 같은 넉백이 매 틱 갱신되는 경우(Q 홀드 견인)는 호출자가 <see cref="Begin"/> 을 다시 부르지 않는다.
/// </summary>
public sealed class WallImpactEpisode
{
    public bool IsActive { get; private set; }
    public bool HasImpacted { get; private set; }

    /// <summary>새 외부 힘 — 다시 1회 낼 수 있다.</summary>
    public void Begin()
    {
        IsActive = true;
        HasImpacted = false;
    }

    public void End() => IsActive = false;

    /// <summary>이번 충돌을 이 에피소드의 1회로 쓴다. 이미 냈거나 진행 중이 아니면 false.</summary>
    public bool TryConsume()
    {
        if (!IsActive || HasImpacted) return false;
        HasImpacted = true;
        return true;
    }
}
