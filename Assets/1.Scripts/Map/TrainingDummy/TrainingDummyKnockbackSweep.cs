using UnityEngine;

/// <summary>스윕 히트 하나를 거리 규칙이 보는 만큼만 옮긴 값.</summary>
public struct KnockbackSweepHit
{
    /// <summary>스윕 시작부터 닿기까지 거리(m).</summary>
    public float Distance;
    /// <summary>시작부터 겹쳐 있던 콜라이더(CapsuleCast 의 거리 0·점 0).</summary>
    public bool StartedOverlapping;
    /// <summary>허수아비 자신(루트 자식)의 콜라이더 — 막힘이 아니다.</summary>
    public bool IsSelf;
}

/// <summary>
/// 허수아비 지속 넉백(할 일 51)의 벽 앞 정지 — 이번 프레임 밀릴 거리를 몸 캡슐 스윕으로 잘라낸다.
/// <list type="bullet">
/// <item>막는 레이어 = Wall · Barrier 만(<see cref="BlockMask"/>). 바닥·Default·Env·유닛은 보지 않는다.</item>
/// <item>벽 충돌 이벤트(<see cref="UnitWallImpacts.ServerProbe"/>)는 내지 않는다 — 멈추기만 한다.</item>
/// <item>거리 규칙은 순수 <see cref="ResolveDistance"/>, 물리 쿼리는 <see cref="Sweep"/>.</item>
/// </list>
/// 상세: Docs/tech/wall-concept.md §4.
/// </summary>
public static class TrainingDummyKnockbackSweep
{
    /// <summary>벽 면과 몸 사이에 남길 틈(m). 다음 프레임 스윕이 '시작부터 겹침'으로 잡히지 않게.</summary>
    public const float DefaultSkin = 0.02f;

    const int MaxHits = 16;
    static readonly RaycastHit[] RaycastBuffer = new RaycastHit[MaxHits];
    static readonly KnockbackSweepHit[] HitBuffer = new KnockbackSweepHit[MaxHits];

    /// <summary>넉백 이동을 막는 레이어 = 벽 + 배리어.</summary>
    public static int BlockMask => GameLayers.WallMask | GameLayers.BarrierMask;

    /// <summary>
    /// 허용 이동 거리. 막힘 없으면 <paramref name="requested"/>, 막히면 (가장 가까운 유효 히트 − <paramref name="skin"/>) 을 0 ~ 요청으로 클램프.
    /// 자기 콜라이더는 건너뛰고, 시작부터 겹친 히트는 막힘(0)으로 본다 — 관통보다 안전하고 자리 복귀가 끼임을 푼다.
    /// </summary>
    public static float ResolveDistance(float requested, KnockbackSweepHit[] hits, int count, float skin = DefaultSkin)
    {
        if (!(requested > 0f)) return 0f;   // 0·음수·NaN

        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            KnockbackSweepHit h = hits[i];
            if (h.IsSelf) continue;
            if (h.StartedOverlapping) return 0f;
            if (h.Distance < nearest) nearest = h.Distance;
        }

        if (float.IsPositiveInfinity(nearest)) return requested;
        return Mathf.Clamp(nearest - Mathf.Max(0f, skin), 0f, requested);
    }

    /// <summary>
    /// 캡슐(<paramref name="p1"/>·<paramref name="p2"/>·<paramref name="radius"/>)을 <paramref name="direction"/>(수평 단위 벡터)으로
    /// 요청 거리 + 스킨만큼 스윕해 허용 이동 거리를 돌려준다. 트리거 무시, <paramref name="self"/> 아래 콜라이더 무시.
    /// </summary>
    public static float Sweep(Vector3 p1, Vector3 p2, float radius, Vector3 direction, float requested,
                              Transform self, int mask, float skin = DefaultSkin)
    {
        if (!(requested > 0f) || direction.sqrMagnitude < 1e-8f) return 0f;
        if (mask == 0) return requested;

        int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, direction.normalized, RaycastBuffer,
                                                requested + Mathf.Max(0f, skin), mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = RaycastBuffer[i];
            HitBuffer[i] = new KnockbackSweepHit
            {
                Distance = h.distance,
                StartedOverlapping = h.distance <= 0f && h.point == Vector3.zero,
                IsSelf = self != null && h.collider.transform.IsChildOf(self),
            };
        }

        return ResolveDistance(requested, HitBuffer, count, skin);
    }
}
