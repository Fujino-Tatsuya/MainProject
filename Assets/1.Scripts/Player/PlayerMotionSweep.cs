using UnityEngine;

/// <summary>
/// 대시와 일반 이동이 공유하는 경사/벽 충돌 해석(단일 소스). (PLAN §5, §8)
///
/// - 걸을 수 있는 경사(법선 각 &lt;= maxWalkableAngle)는 접선 이동에는 열고, 중력처럼 면 안쪽으로
///   향하는 이동만 막아 kinematic 바디가 지면을 통과하지 않게 한다.
/// - 급경사/벽/천장만 막고, 비스듬한 충돌은 접선으로 미끄러진다.
/// - Rigidbody.MovePosition이 지연 적용되므로 한 프레임의 모든 캐스트는 누적 오프셋으로 근사하고,
///   호출자는 반환된 delta로 MovePosition을 1회만 적용한다.
///
/// 캡슐 방향은 Y축(direction==1) 가정.
/// </summary>
public static class PlayerMotionSweep
{
    private const float MovementEpsilon = 0.00001f;
    private const float LandingOverlapTolerance = 0.0001f;

    /// <summary>desiredDelta를 충돌 해석해 실제 적용할 이동량으로 보정해 반환한다.</summary>
    public static Vector3 Resolve(
        CapsuleCollider capsule,
        Vector3 desiredDelta,
        Vector3 horizontalStepDelta,
        bool isGrounded,
        float stepOffset,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        int maxIterations,
        RaycastHit[] buffer)
    {
        return Resolve(
            capsule,
            Vector3.zero,
            desiredDelta,
            horizontalStepDelta,
            isGrounded,
            stepOffset,
            maxWalkableAngle,
            obstacleMask,
            skin,
            maxIterations,
            buffer);
    }

    /// <summary>
    /// Rigidbody/Transform을 옮기지 않고 과거 위치에서 재생할 수 있도록 현재 캡슐 위치 기준 오프셋을 받는다.
    /// </summary>
    public static Vector3 Resolve(
        CapsuleCollider capsule,
        Vector3 originOffset,
        Vector3 desiredDelta,
        Vector3 horizontalStepDelta,
        bool isGrounded,
        float stepOffset,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        int maxIterations,
        RaycastHit[] buffer)
    {
        if (capsule == null || buffer == null || desiredDelta.sqrMagnitude <= 1e-10f)
            return desiredDelta;

        Transform owner = capsule.transform;
        Vector3 resolved = ResolveCore(capsule, owner, originOffset, desiredDelta, horizontalStepDelta, isGrounded,
            stepOffset, maxWalkableAngle, obstacleMask, skin, maxIterations, buffer);
        LogIfBlockedByEnemy(capsule, owner, originOffset, desiredDelta, resolved, obstacleMask, skin, buffer);
        return resolved;
    }

    // ─── [임시 진단 · 2026-09-29 경석] 보스·중간보스 끼임 원인 확정용 — 동작 무변경, 원인 확정 후 삭제 ───
    // 수평 이동을 요청했는데 거의 못 움직였고, 그 원인이 Enemy 레이어(보스 몸)일 때만 0.5초에 한 번 찍는다.
    // 가설: CapsuleCastNonAlloc 은 시작부터 겹친 콜라이더를 distance 0 · normal = −진행방향으로 돌려줘 전 방향이 막힌다.
    private static float s_lastEnemyBlockLog = -10f;
    private static readonly Collider[] s_enemyOverlap = new Collider[8];

    private static void LogIfBlockedByEnemy(CapsuleCollider capsule, Transform owner, Vector3 originOffset,
        Vector3 desiredDelta, Vector3 resolved, LayerMask obstacleMask, float skin, RaycastHit[] buffer)
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0 || (obstacleMask.value & (1 << enemyLayer)) == 0) return;

        Vector3 want = new Vector3(desiredDelta.x, 0f, desiredDelta.z);
        Vector3 got = new Vector3(resolved.x, 0f, resolved.z);
        if (want.sqrMagnitude < 1e-6f || got.sqrMagnitude > want.sqrMagnitude * 0.01f) return;
        if (Time.realtimeSinceStartup - s_lastEnemyBlockLog < 0.5f) return;

        Vector3 lossy = owner.lossyScale;
        float r = capsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float half = Mathf.Max(0f, capsule.height * Mathf.Abs(lossy.y) * 0.5f - r);
        Vector3 c = owner.TransformPoint(capsule.center) + originOffset;
        Vector3 p1 = c + owner.up * half, p2 = c - owner.up * half;

        int overlaps = Physics.OverlapCapsuleNonAlloc(p1, p2, r, s_enemyOverlap, 1 << enemyLayer, QueryTriggerInteraction.Ignore);
        Vector3 dir = want.normalized;
        int hits = Physics.CapsuleCastNonAlloc(p1, p2, Mathf.Max(0.01f, r - skin), dir, buffer, want.magnitude + skin,
            obstacleMask, QueryTriggerInteraction.Ignore);

        bool enemyInvolved = overlaps > 0;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < hits; i++)
        {
            RaycastHit h = buffer[i];
            if (h.collider == null || h.collider.transform.IsChildOf(owner)) continue;
            bool enemy = h.collider.gameObject.layer == enemyLayer;
            enemyInvolved |= enemy;
            sb.Append($"\n  · 히트 {h.collider.name}[{LayerMask.LayerToName(h.collider.gameObject.layer)}] 거리 {h.distance:0.###} " +
                      $"법선 {h.normal} 시작겹침 {(h.distance <= 0f && h.point == Vector3.zero)}");
        }
        if (!enemyInvolved) return;

        s_lastEnemyBlockLog = Time.realtimeSinceStartup;
        for (int i = 0; i < overlaps; i++)
        {
            Collider o = s_enemyOverlap[i];
            if (o == null) continue;
            bool pen = Physics.ComputePenetration(capsule, owner.position + originOffset, owner.rotation,
                o, o.transform.position, o.transform.rotation, out Vector3 pd, out float pdist);
            sb.Append($"\n  · Enemy 겹침 {o.name} 관통 {(pen ? $"{pdist:0.###}m 방향 {pd}" : "없음")}");
        }
        Debug.Log($"[모터/끼임진단] {owner.name} 요청 {want.magnitude:0.###} → 실제 {got.magnitude:0.###} · 방향 {dir} · " +
                  $"Enemy 겹침 {overlaps}개{sb}", owner);
    }

    private static Vector3 ResolveCore(
        CapsuleCollider capsule,
        Transform owner,
        Vector3 originOffset,
        Vector3 desiredDelta,
        Vector3 horizontalStepDelta,
        bool isGrounded,
        float stepOffset,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        int maxIterations,
        RaycastHit[] buffer)
    {
        Vector3 regularDelta = ResolveMovement(
            capsule,
            owner,
            originOffset,
            desiredDelta,
            maxWalkableAngle,
            obstacleMask,
            skin,
            maxIterations,
            buffer);

        Vector3 planarStepDelta = new Vector3(horizontalStepDelta.x, 0f, horizontalStepDelta.z);
        if (!isGrounded || stepOffset <= 0f || planarStepDelta.sqrMagnitude <= MovementEpsilon * MovementEpsilon)
            return regularDelta;

        if (!TryResolveStep(
                capsule,
                owner,
                originOffset,
                planarStepDelta,
                desiredDelta - planarStepDelta,
                regularDelta,
                stepOffset,
                maxWalkableAngle,
                obstacleMask,
                skin,
                maxIterations,
                buffer,
                out Vector3 stepDelta))
        {
            return regularDelta;
        }

        return stepDelta;
    }

    private static Vector3 ResolveMovement(
        CapsuleCollider capsule,
        Transform owner,
        Vector3 originOffset,
        Vector3 desiredDelta,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        int maxIterations,
        RaycastHit[] buffer,
        bool constrainToHorizontal = false)
    {
        Vector3 accumulated = originOffset;
        Vector3 remaining = desiredDelta;
        int iterations = Mathf.Max(1, maxIterations);

        for (int i = 0; i < iterations; i++)
        {
            float dist = remaining.magnitude;
            if (dist <= 1e-5f)
                break;

            Vector3 dir = remaining / dist;

            if (TryCast(capsule, owner, accumulated, dir, dist + skin, maxWalkableAngle, obstacleMask, skin, buffer,
                    out RaycastHit hit, out float hitDistance))
            {
                bool walkableGround = IsWalkable(hit.normal, maxWalkableAngle);
                // 지면 스냅/중력은 표면까지 정확히 가야 한다. 벽에는 기존 skin을 유지한다.
                float allowed = Mathf.Max(0f, hitDistance - (walkableGround ? 0f : skin));
                accumulated += dir * allowed;
                Vector3 leftover = dir * (dist - allowed);
                if (constrainToHorizontal)
                {
                    Vector3 planarNormal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                    remaining = planarNormal.sqrMagnitude > MovementEpsilon * MovementEpsilon
                        ? Vector3.ProjectOnPlane(leftover, planarNormal.normalized)
                        : leftover;
                    remaining.y = 0f;
                }
                else
                {
                    remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
                }
            }
            else
            {
                accumulated += remaining;
                break;
            }
        }

        return accumulated - originOffset;
    }

    private static bool TryResolveStep(
        CapsuleCollider capsule,
        Transform owner,
        Vector3 originOffset,
        Vector3 horizontalDelta,
        Vector3 remainingDelta,
        Vector3 regularDelta,
        float stepOffset,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        int maxIterations,
        RaycastHit[] buffer,
        out Vector3 resolvedStepDelta)
    {
        resolvedStepDelta = default;

        float horizontalDistance = horizontalDelta.magnitude;
        Vector3 horizontalDirection = horizontalDelta / horizontalDistance;

        // 계단 후보는 제출된 수평 이동이 실제 비보행면에 막혔을 때만 만든다.
        if (!TryCast(
                capsule,
                owner,
                originOffset,
                horizontalDirection,
                horizontalDistance + skin,
                maxWalkableAngle,
                obstacleMask,
                skin,
                buffer,
                out RaycastHit blockingHit,
                out _))
        {
            return false;
        }

        if (IsWalkable(blockingHit.normal, maxWalkableAngle))
            return false;

        // 위 이동은 텔레포트하지 않는다. 필요한 높이까지 조금이라도 막히면 계단 후보를 버린다.
        if (TryCast(
                capsule,
                owner,
                originOffset,
                Vector3.up,
                stepOffset,
                maxWalkableAngle,
                obstacleMask,
                skin,
                buffer,
                out _,
                out _))
        {
            return false;
        }

        Vector3 raisedOffset = originOffset + Vector3.up * stepOffset;
        Vector3 raisedHorizontalDelta = ResolveMovement(
            capsule,
            owner,
            raisedOffset,
            horizontalDelta,
            maxWalkableAngle,
            obstacleMask,
            skin,
            maxIterations,
            buffer,
            constrainToHorizontal: true);

        Vector3 landingOrigin = raisedOffset + raisedHorizontalDelta;
        if (!TryCast(
                capsule,
                owner,
                landingOrigin,
                Vector3.down,
                stepOffset,
                maxWalkableAngle,
                obstacleMask,
                skin,
                buffer,
                out RaycastHit landingHit,
                out float landingDistance))
        {
            return false;
        }

        if (!IsWalkable(landingHit.normal, maxWalkableAngle))
            return false;

        // 축소 캐스트가 stepOffset보다 높은 턱에 얕게 파고든 상태를 착지로 오인하지 않게 한다.
        float castInset = GetCastInset(capsule, owner, skin);
        if (landingHit.distance + LandingOverlapTolerance < castInset)
            return false;

        Vector3 candidateDelta =
            raisedHorizontalDelta +
            Vector3.up * stepOffset +
            Vector3.down * Mathf.Clamp(landingDistance, 0f, stepOffset);
        if (remainingDelta.sqrMagnitude > MovementEpsilon * MovementEpsilon)
        {
            candidateDelta += ResolveMovement(
                capsule,
                owner,
                originOffset + candidateDelta,
                remainingDelta,
                maxWalkableAngle,
                obstacleMask,
                skin,
                maxIterations,
                buffer);
        }

        float regularProgress = Vector3.Dot(new Vector3(regularDelta.x, 0f, regularDelta.z), horizontalDirection);
        float candidateProgress = Vector3.Dot(new Vector3(candidateDelta.x, 0f, candidateDelta.z), horizontalDirection);
        if (candidateProgress <= regularProgress + MovementEpsilon)
            return false;

        resolvedStepDelta = candidateDelta;
        return true;
    }

    /// <param name="skin">
    /// 캐스트 반경을 줄이는 여유 두께.
    ///
    /// ⚠️ 캡슐 반경을 그대로 쏘면 <b>이미 닿아 있는 면</b>이 distance 0으로 히트한다. 특히 캡슐
    /// 하단이 지면에 1~2cm 파묻힌 상태(스폰 Y가 낮을 때)에서는 수평 스윕이 바닥 메시의 측면
    /// 삼각형(법선 수평 → 벽 판정)을 매 tick 때려 이동 전량이 클램프된다 — 대시가 제자리에서
    /// 끝나는 원인이었다. 반경을 skin만큼 줄여 접촉면을 떼고, 그만큼 늘어난 히트 거리는
    /// <paramref name="hitDistance"/>에서 되돌린다. (MoveRoot·PlayerGroundingSensor와 같은 패턴)
    /// </param>
    /// <param name="hitDistance">인셋 보정을 되돌린 히트 거리(원래 반경 기준). 호출부는 이 값을 쓴다.</param>
    private static bool TryCast(
        CapsuleCollider capsule,
        Transform owner,
        Vector3 originOffset,
        Vector3 dir,
        float maxDistance,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        RaycastHit[] buffer,
        out RaycastHit best,
        out float hitDistance)
    {
        best = default;
        hitDistance = 0f;

        Vector3 lossy = owner.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float heightScale = Mathf.Abs(lossy.y);
        float fullRadius = capsule.radius * radiusScale;

        // 캡슐 끝점(p1/p2)은 원래 지오메트리 그대로 두고 반경만 줄인다 → 스윕 볼륨이 전 방향으로
        // skin만큼 축소돼 접촉면에서 떨어진다(캡슐이 길어지지 않는다).
        float radius = Mathf.Max(0.01f, fullRadius - skin);
        float inset = Mathf.Max(0f, fullRadius - radius);

        float height = Mathf.Max(capsule.height * heightScale, fullRadius * 2f);
        float half = Mathf.Max(0f, height * 0.5f - fullRadius);
        Vector3 center = owner.TransformPoint(capsule.center) + originOffset;
        Vector3 up = owner.up;
        Vector3 p1 = center + up * half;
        Vector3 p2 = center - up * half;

        // 반경을 줄인 만큼 같은 벽을 inset 늦게 만난다 — 검사 거리도 그만큼 늘려야 사거리가 보존된다.
        int count = Physics.CapsuleCastNonAlloc(
            p1, p2, radius, dir, buffer, maxDistance + inset, obstacleMask, QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = buffer[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform == owner || hit.collider.transform.IsChildOf(owner))
                continue;
            if (IsEscapingEnemyOverlap(hit, capsule, owner, originOffset, dir))
                continue;
            // 걸을 수 있는 경사는 접선 이동에는 장애물이 아니지만, 중력/스냅처럼 표면 안쪽으로
            // 향하는 이동은 막아야 kinematic 바디가 지면을 통과하지 않는다.
            bool walkableGround = IsWalkable(hit.normal, maxWalkableAngle);
            if (walkableGround && Vector3.Dot(dir, hit.normal) >= -0.0001f)
                continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                best = hit;
                found = true;
            }
        }

        // 인셋 보정을 되돌려 원래 반경 기준 거리로 환산한다 → 호출부의 정지 지점(hit - skin)이 종전과 같다.
        hitDistance = found ? Mathf.Max(0f, best.distance - inset) : 0f;
        return found;
    }

    // 🔴 [2026-09-29 경석 · 끼임] CapsuleCastNonAlloc 은 **시작부터 겹친** 콜라이더를 distance 0 · point 0 ·
    //    normal = −진행방향으로 돌려준다 → 서버가 움직이는 보스 몸(Enemy)이 파고든 뒤엔 어느 방향이든 0 만큼만
    //    가고 법선에 투영된 잔여도 0 이라 **전 방향이 막힌다**(`[모터/끼임진단]` 실측: 거리 0 · 시작겹침 True).
    //    Enemy 레이어의 시작 겹침만, **빠져나가는 방향**(관통 해소 방향과 같은 쪽)으로 움직일 때 무시한다.
    //    강제 밀어내기가 아니라 플레이어 입력으로 걸어 나가는 것이라 벽 뚫기·입력 경합이 없다(Codex 09-29 지적 회피).
    //    더 파고드는 방향은 그대로 막는다. 정적 지형(Enemy 외)은 무관.
    private static int s_enemyLayer = int.MinValue;

    private static bool IsEscapingEnemyOverlap(RaycastHit hit, CapsuleCollider capsule, Transform owner,
        Vector3 originOffset, Vector3 dir)
    {
        if (hit.distance > 0f || hit.point != Vector3.zero)
            return false;   // 시작 겹침이 아닌 정상 접촉
        if (s_enemyLayer == int.MinValue)
            s_enemyLayer = LayerMask.NameToLayer("Enemy");
        if (s_enemyLayer < 0 || hit.collider.gameObject.layer != s_enemyLayer)
            return false;

        Transform other = hit.collider.transform;
        if (!Physics.ComputePenetration(capsule, owner.position + originOffset, owner.rotation,
                hit.collider, other.position, other.rotation, out Vector3 separate, out _))
            return true;    // 스윕용으로 줄인 반경에서만 겹친 경계 — 실제 몸은 이미 떨어져 있다

        separate.y = 0f;
        Vector3 planar = new Vector3(dir.x, 0f, dir.z);
        return Vector3.Dot(planar, separate) >= 0f;
    }

    private static bool IsWalkable(Vector3 normal, float maxWalkableAngle)
    {
        return Vector3.Angle(normal, Vector3.up) <= maxWalkableAngle;
    }

    private static float GetCastInset(CapsuleCollider capsule, Transform owner, float skin)
    {
        Vector3 lossy = owner.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float fullRadius = capsule.radius * radiusScale;
        float radius = Mathf.Max(0.01f, fullRadius - skin);
        return Mathf.Max(0f, fullRadius - radius);
    }
}
