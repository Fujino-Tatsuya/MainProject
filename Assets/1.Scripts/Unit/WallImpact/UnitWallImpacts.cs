using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 공용 벽 충돌 판정(서버) — 할 일 32. 넉백·밀치기 등 <b>외부 힘</b>으로 유닛이 움직이는 경로가 매 틱 <see cref="ServerProbe"/> 를 부른다.
/// 몸 크기(캡슐)로 진행 방향을 스윕해 가장 가까운 것이 Wall 레이어 콜라이더면 <see cref="UnitWallImpact"/> 를 에피소드당 1회 낸다.
/// 판정 규칙은 순수 <see cref="UnitWallImpactJudge"/>, 1회 보장은 <see cref="WallImpactEpisode"/>.
/// <list type="bullet">
/// <item>벽 아님(레이어로 제외): Barrier · Player · Enemy · EnemyHurtBox(상자) · Default · Env — 이것들이 먼저 닿으면 '막힘'일 뿐 벽 충돌이 아니다.</item>
/// <item>효과 없음 — 서버 이벤트(<see cref="ServerImpacted"/>·<see cref="Unit.ServerWallImpacted"/>) + 연출 훅(<see cref="Unit.WallImpactPresented"/>, 모든 피어)만.</item>
/// <item>서버 권한(Docs/tech/networking.md) — 클라에서 부르면 아무 일도 하지 않는다.</item>
/// </list>
/// 상세·호출 경로: Docs/tech/wall-concept.md · 몬스터 쪽 훅: Docs/tech/handoff-wall-impact.md.
/// </summary>
public static class UnitWallImpacts
{
    /// <summary>막힘 없이 밀리는 중의 판정 거리(m) — 몸 가장자리 ↔ 벽 면.</summary>
    public const float DefaultContactSlack = 0.4f;
    /// <summary>밀린 이동이 막혔을 때(NavMesh 끝 등)의 판정 거리(m) — NavMesh 는 벽에서 에이전트 반경만큼 안쪽에서 끝난다.</summary>
    public const float DefaultBlockedSlack = 1.6f;

    // 바닥 턱·경사에 스윕 아래쪽이 걸리지 않게 몸 캡슐 아래를 이만큼 띄운다(m, 몸 높이의 30% 이하).
    const float FootClearance = 0.35f;
    // 이미 벽에 맞닿은 몸이 '시작부터 겹침'으로 잡히지 않게 반경을 살짝 줄인다.
    const float RadiusScale = 0.95f;

    const int MaxHits = 16;
    static readonly RaycastHit[] RaycastBuffer = new RaycastHit[MaxHits];
    static readonly WallSweepHit[] HitBuffer = new WallSweepHit[MaxHits];

    /// <summary>[서버] 벽 충돌(효과 없음). 전역 구독 — 튜토리얼 존3·연출·통계가 여기서 고른다.</summary>
    public static event Action<UnitWallImpact> ServerImpacted;

    /// <summary>스윕이 보는 레이어 = 벽 + 벽 아닌 막힘(먼저 닿으면 벽 충돌을 가린다).</summary>
    public static int SweepMask =>
        GameLayers.WallMask | GameLayers.BarrierMask |
        LayerMask.GetMask("Default", "Env", "Player", "Enemy", "EnemyHurtBox");

    /// <summary>
    /// [서버] 외부 힘으로 이번 틱 움직인 뒤 부른다. 지금 위치에서 <paramref name="direction"/> 으로 몸 캡슐을 스윕한다.
    /// <paramref name="requested"/>·<paramref name="moved"/> = 이번 틱 밀려야 했던/실제 수평 이동(m) — 막힘 판정(판정 거리 선택)용.
    /// <paramref name="speed"/> = 밀리는 속도(m/s, 충격 속도의 원본). 벽 충돌이면 이벤트를 내고 true.
    /// </summary>
    public static bool ServerProbe(Unit target, WallImpactEpisode episode, Vector3 direction, float requested, float moved,
                                   float speed, Unit pusher, out UnitWallImpact impact,
                                   float contactSlack = DefaultContactSlack, float blockedSlack = DefaultBlockedSlack)
    {
        impact = default;
        if (target == null || episode == null || !episode.IsActive || episode.HasImpacted) return false;
        if (!target.IsServer) return false;

        Vector3 dir = UnitWallImpactJudge.Horizontal(direction);
        if (dir == Vector3.zero) return false;

        int wallMask = GameLayers.WallMask;
        if (wallMask == 0) return false;

        float reach = UnitWallImpactJudge.Reach(requested, moved, contactSlack, blockedSlack);
        GetBodyCapsule(target, out Vector3 p1, out Vector3 p2, out float radius);

        int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, dir, RaycastBuffer, reach, SweepMask, QueryTriggerInteraction.Ignore);
        Transform self = target.transform;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = RaycastBuffer[i];
            HitBuffer[i] = new WallSweepHit
            {
                Layer = h.collider.gameObject.layer,
                Distance = h.distance,
                Point = h.point,
                Normal = h.normal,
                StartedOverlapping = h.distance <= 0f && h.point == Vector3.zero,
                IsSelf = h.collider.transform.IsChildOf(self),
            };
        }

        if (UnitWallImpactJudge.Judge(HitBuffer, count, wallMask, dir, reach, out int index) != WallJudgeResult.Wall) return false;
        if (!episode.TryConsume()) return false;

        RaycastHit wall = RaycastBuffer[index];
        Vector3 center = (p1 + p2) * 0.5f;
        Vector3 normal = HitBuffer[index].StartedOverlapping ? -dir : UnitWallImpactJudge.Horizontal(wall.normal);
        Vector3 point = HitBuffer[index].StartedOverlapping ? ClosestPoint(wall.collider, center) : wall.point;
        impact = new UnitWallImpact(target, pusher, point, normal, UnitWallImpactJudge.ImpactSpeed(speed, dir, normal), wall.collider);

        Edit.Log($"[WallImpact] {target.name} → 벽 '{wall.collider.name}' · 민 쪽 {(pusher != null ? pusher.name : "없음")} · " +
                 $"충격 {impact.Speed:0.##}m/s · 법선 ({normal.x:0.##}, {normal.z:0.##})", target);
        target.NotifyWallImpactServer(impact);
        ServerImpacted?.Invoke(impact);
        return true;
    }

    /// <summary>
    /// 유닛 몸 캡슐(월드) — 루트의 비트리거 CapsuleCollider, 없으면 NavMeshAgent 반경·높이, 그것도 없으면 0.5 / 1.8.
    /// 아래쪽은 <see cref="FootClearance"/> 만큼 띄운다(바닥 스침 방지).
    /// </summary>
    public static void GetBodyCapsule(Unit unit, out Vector3 p1, out Vector3 p2, out float radius)
    {
        Transform t = unit.transform;
        Vector3 up = Vector3.up;
        float horizontalScale = Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.z));
        float verticalScale = Mathf.Abs(t.lossyScale.y);

        float bottom, height;
        if (unit.TryGetComponent(out CapsuleCollider capsule) && !capsule.isTrigger)
        {
            radius = capsule.radius * horizontalScale;
            height = Mathf.Max(capsule.height * verticalScale, radius * 2f);
            bottom = t.TransformPoint(capsule.center).y - height * 0.5f;
        }
        else if (unit.TryGetComponent(out NavMeshAgent agent))
        {
            radius = agent.radius * horizontalScale;
            height = Mathf.Max(agent.height * verticalScale, radius * 2f);
            bottom = t.position.y;   // 에이전트 피벗 = 발
        }
        else
        {
            radius = 0.5f;
            height = 1.8f;
            bottom = t.position.y;
        }

        radius = Mathf.Max(0.05f, radius * RadiusScale);
        float lift = Mathf.Min(FootClearance, height * 0.3f);
        float lowCenter = bottom + lift + radius;
        float highCenter = Mathf.Max(lowCenter, bottom + height - radius);
        Vector3 basePos = new Vector3(t.position.x, 0f, t.position.z);
        p1 = basePos + up * lowCenter;
        p2 = basePos + up * highCenter;
    }

    // 비볼록 MeshCollider 는 ClosestPoint 를 못 쓴다 — 경계 상자로 대신한다.
    static Vector3 ClosestPoint(Collider c, Vector3 p) =>
        c is MeshCollider mesh && !mesh.convex ? c.ClosestPointOnBounds(p) : c.ClosestPoint(p);
}
