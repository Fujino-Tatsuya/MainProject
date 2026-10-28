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

        return ResolveCore(capsule, capsule.transform, originOffset, desiredDelta, horizontalStepDelta, isGrounded,
            stepOffset, maxWalkableAngle, obstacleMask, skin, maxIterations, buffer);
    }

    /// <summary>
    /// dir 로 distance 만큼 갈 때 가장 먼저 막는 것이 유닛(Enemy 또는 다른 플레이어 몸)인가. 벽·지형이 먼저면 false.
    /// 같은 캡슐·필터(<see cref="TryCast"/>)를 쓰므로 실제 이동 스윕과 판정이 어긋나지 않는다.
    /// </summary>
    public static bool IsBlockedByUnitFirst(
        CapsuleCollider capsule,
        Vector3 originOffset,
        Vector3 dir,
        float distance,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        RaycastHit[] buffer)
    {
        if (capsule == null || buffer == null || distance <= 0f || dir.sqrMagnitude <= 1e-8f)
            return false;

        if (!TryCast(capsule, capsule.transform, originOffset, dir.normalized, distance, maxWalkableAngle,
                obstacleMask, skin, buffer, out RaycastHit hit, out _))
        {
            return false;
        }

        return IsUnitLayer(hit.collider.gameObject.layer);
    }

    /// <summary>
    /// 이미 겹친 유닛(Enemy·Player 몸)에서 빠져나갈 수평 관통 벡터의 합. 겹침이 없으면 0.
    /// 길이 제한·벽 스윕은 호출자가 한다(<see cref="PlayerUnitCollisionRules.ComputeSeparationStep"/>).
    /// </summary>
    public static Vector3 ComputeUnitPenetration(
        CapsuleCollider capsule,
        Vector3 originOffset,
        LayerMask unitMask,
        Collider[] overlapBuffer)
    {
        if (capsule == null || overlapBuffer == null || unitMask.value == 0)
            return Vector3.zero;

        Transform owner = capsule.transform;
        GetCapsuleWorld(capsule, owner, originOffset, out Vector3 p1, out Vector3 p2, out float radius);
        int count = Physics.OverlapCapsuleNonAlloc(
            p1, p2, radius, overlapBuffer, unitMask, QueryTriggerInteraction.Ignore);

        Vector3 sum = Vector3.zero;
        Vector3 position = owner.position + originOffset;
        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            if (other == null || other.transform == owner || other.transform.IsChildOf(owner))
                continue;
            if (IgnoresUnitCollider(other))
                continue;

            Transform otherTransform = other.transform;
            if (Physics.ComputePenetration(capsule, position, owner.rotation,
                    other, otherTransform.position, otherTransform.rotation,
                    out Vector3 direction, out float distance))
            {
                sum = PlayerUnitCollisionRules.AccumulatePenetration(sum, direction, distance);
            }
        }

        return sum;
    }

    /// <summary>
    /// 이미 파고든 정적 지형(벽·턱 등 비보행면)에서 빠져나갈 수평 관통 벡터의 합. 겹침이 없으면 0. (할 일 136)
    /// 시작부터 겹친 정적 콜라이더는 <see cref="TryCast"/> 가 전 방향 0 을 내므로 스윕 전에 이 값으로 먼저 푼다.
    /// 걸을 수 있는 면(바닥·완경사)의 접지 겹침은 제외한다 — 경사에서 미끄러져 내려가지 않게.
    /// </summary>
    public static Vector3 ComputeStaticPenetration(
        CapsuleCollider capsule,
        Vector3 originOffset,
        LayerMask staticMask,
        float maxWalkableAngle,
        Collider[] overlapBuffer)
    {
        if (capsule == null || overlapBuffer == null || staticMask.value == 0)
            return Vector3.zero;

        Transform owner = capsule.transform;
        GetCapsuleWorld(capsule, owner, originOffset, out Vector3 p1, out Vector3 p2, out float radius);
        int count = Physics.OverlapCapsuleNonAlloc(
            p1, p2, radius, overlapBuffer, staticMask, QueryTriggerInteraction.Ignore);

        Vector3 sum = Vector3.zero;
        Vector3 position = owner.position + originOffset;
        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            if (other == null || other.transform == owner || other.transform.IsChildOf(owner))
                continue;
            if (IsUnitLayer(other.gameObject.layer))
                continue;

            Transform otherTransform = other.transform;
            if (Physics.ComputePenetration(capsule, position, owner.rotation,
                    other, otherTransform.position, otherTransform.rotation,
                    out Vector3 direction, out float distance))
            {
                sum = PlayerUnitCollisionRules.AccumulateStaticPenetration(sum, direction, distance, maxWalkableAngle);
            }
        }

        return sum;
    }

    /// <summary>
    /// 정적 겹침 해소 한 틱 분량 — <see cref="ComputeStaticPenetration"/> 을 maxSpeed × deltaTime 으로 자른다(순간이동 아님).
    /// 탈출 방향은 관통 해소 방향 그 자체라 다시 스윕하지 않는다.
    /// </summary>
    public static Vector3 ComputeStaticSeparationStep(
        CapsuleCollider capsule,
        Vector3 originOffset,
        LayerMask staticMask,
        float maxWalkableAngle,
        float maxSpeed,
        float deltaTime,
        Collider[] overlapBuffer)
    {
        Vector3 penetration = ComputeStaticPenetration(capsule, originOffset, staticMask, maxWalkableAngle, overlapBuffer);
        return PlayerUnitCollisionRules.ComputeSeparationStep(penetration, maxSpeed, deltaTime);
    }

    /// <summary>
    /// 몸을 origin(루트 위치)에 둔 채 target 까지 정적 지형으로 스윕해, 벽 앞에서 자른 루트 위치를 돌려준다. (할 일 136)
    /// 구속 포즈(23호 잡기 Push 정면점·Carry 손 소켓)가 벽 너머로 나가 몸이 벽에 박히지 않게 한다.
    /// 걸을 수 있는 면은 접선 이동이라 막지 않는다(<see cref="TryCast"/> 규칙 그대로).
    /// </summary>
    public static Vector3 ClampPlacement(
        CapsuleCollider capsule,
        Vector3 origin,
        Vector3 target,
        LayerMask staticMask,
        float maxWalkableAngle,
        float skin,
        RaycastHit[] buffer)
    {
        if (capsule == null || buffer == null || staticMask.value == 0)
            return target;

        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance <= 1e-5f)
            return target;

        Transform owner = capsule.transform;
        Vector3 dir = delta / distance;
        bool blocked = TryCast(capsule, owner, origin - owner.position, dir, distance + skin, maxWalkableAngle,
            staticMask, skin, buffer, out _, out float hitDistance);
        return origin + dir * PlayerUnitCollisionRules.ClampPlacementDistance(distance, blocked, hitDistance, skin);
    }

    /// <summary>dir 로 distance 만큼 갈 때 가장 먼저 막는 면의 법선. 막는 것이 없으면 false. (할 일 136 벽 따라 밀어내기)</summary>
    public static bool TryGetBlockingNormal(
        CapsuleCollider capsule,
        Vector3 originOffset,
        Vector3 dir,
        float distance,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        RaycastHit[] buffer,
        out Vector3 normal)
    {
        normal = Vector3.zero;
        if (capsule == null || buffer == null || distance <= 0f || dir.sqrMagnitude <= 1e-8f)
            return false;

        if (!TryCast(capsule, capsule.transform, originOffset, dir.normalized, distance, maxWalkableAngle,
                obstacleMask, skin, buffer, out RaycastHit hit, out _))
        {
            return false;
        }

        normal = hit.normal;
        return true;
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
            if (IgnoresUnitCollider(hit.collider))
                continue;
            if (IsEscapingUnitOverlap(hit, capsule, owner, originOffset, dir))
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
    //    가고 법선에 투영된 잔여도 0 이라 **전 방향이 막힌다**(진단 실측: 거리 0 · 시작겹침 True).
    //    유닛(Enemy·Player) 레이어의 시작 겹침만, **빠져나가는 방향**(관통 해소 방향과 같은 쪽)으로 움직일 때 무시한다.
    //    더 파고드는 방향은 그대로 막는다. 정적 지형은 무관. 겹침 자체는 PlayerMotor 의 밀어내기가 푼다(할 일 11).
    private static int s_enemyLayer = int.MinValue;
    private static int s_playerLayer = int.MinValue;

    private static void CacheUnitLayers()
    {
        if (s_enemyLayer != int.MinValue)
            return;
        s_enemyLayer = LayerMask.NameToLayer("Enemy");
        s_playerLayer = LayerMask.NameToLayer("Player");
    }

    private static bool IsUnitLayer(int layer)
    {
        CacheUnitLayers();
        return (s_enemyLayer >= 0 && layer == s_enemyLayer) || (s_playerLayer >= 0 && layer == s_playerLayer);
    }

    /// <summary>
    /// Player 레이어 콜라이더 중 다른 플레이어의 **몸 캡슐**(PlayerMotor 가 붙은 루트)만 장애물로 인정한다.
    /// 가붕이 평타·스킬 히트박스(AAC1~4 등, Player 레이어 비트리거)가 켜진 순간 막히지 않게 하고,
    /// 사망 연출·완전 사망 플레이어도 통과시킨다(<see cref="PlayerMotor.BlocksOtherUnits"/>).
    /// </summary>
    private static bool IgnoresUnitCollider(Collider collider)
    {
        CacheUnitLayers();
        if (s_playerLayer < 0 || collider.gameObject.layer != s_playerLayer)
            return false;
        return !collider.TryGetComponent(out PlayerMotor otherMotor) || !otherMotor.BlocksOtherUnits;
    }

    private static bool IsEscapingUnitOverlap(RaycastHit hit, CapsuleCollider capsule, Transform owner,
        Vector3 originOffset, Vector3 dir)
    {
        if (hit.distance > 0f || hit.point != Vector3.zero)
            return false;   // 시작 겹침이 아닌 정상 접촉
        if (!IsUnitLayer(hit.collider.gameObject.layer))
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

    private static void GetCapsuleWorld(CapsuleCollider capsule, Transform owner, Vector3 originOffset,
        out Vector3 p1, out Vector3 p2, out float radius)
    {
        Vector3 lossy = owner.lossyScale;
        radius = capsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float height = Mathf.Max(capsule.height * Mathf.Abs(lossy.y), radius * 2f);
        float half = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 center = owner.TransformPoint(capsule.center) + originOffset;
        p1 = center + owner.up * half;
        p2 = center - owner.up * half;
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
