using UnityEngine;

/// <summary>
/// 플레이어↔유닛(몬스터·다른 플레이어) 충돌 규칙의 순수 계산. 물리 쿼리·씬 상태에 의존하지 않는다. (할 일 11)
///
/// - 장애물 마스크: 생존 플레이어는 Enemy·Player 에 막히고, Soul 은 Player 를 통과한다.
///   유닛 통과 이동(암살자 Q·거너 우클릭 후폭풍)은 Enemy·Player 를 모두 빼고 벽·지형에만 막힌다.
/// - 겹침 밀어내기: 관통 벡터를 수평으로 모아 프레임당 상한(속도 × dt)까지만 옮긴다 — 순간이동 대신 부드럽게 빠진다.
/// - 일반 대시: 이번 틱 이동이 깎였고 남은 경로를 먼저 막은 것이 유닛이면 대시를 끝낸다(벽은 종전대로 상태 유지).
/// </summary>
public static class PlayerUnitCollisionRules
{
    /// <summary>이보다 짧은 밀어내기는 버린다(접촉 경계의 수치 떨림 방지).</summary>
    public const float MinSeparationDistance = 0.001f;

    /// <summary>대시 이동이 이만큼(m) 이상 깎였을 때만 "막혔다" 로 본다.</summary>
    public const float DashBlockedTolerance = 0.0005f;

    /// <summary>
    /// 일반 대시가 뚫고 지나가는 일반 몬스터 수(할 일 209, 전 캐릭터 공통). 이 수만큼 통과하고 그다음 일반 몬스터에서 멈춘다.
    /// 보스·중간보스는 0번째부터 막는다(<see cref="IsDashPassableUnit"/>). xlsx 칸 없음 — 코드 상수가 원본.
    /// </summary>
    public const int DashSmallMonsterPassLimit = 2;

    /// <summary>
    /// 일반 대시가 통과할 수 있는 유닛인가(할 일 209) — 몬스터(<c>MonsterBase</c>)이고 등급이 일반일 때만.
    /// 중간보스·보스·몬스터가 아닌 Enemy 레이어 몸(파일런·허수아비 등)·다른 플레이어는 지금처럼 막는다.
    /// </summary>
    public static bool IsDashPassableUnit(bool isMonster, MonsterRank rank) => isMonster && rank == MonsterRank.Normal;

    public static int ResolveObstacleMask(
        int baseMask,
        int playerBit,
        int soulBit,
        int enemyBit,
        bool isSoul,
        bool blockOtherPlayers,
        bool passThroughUnits)
    {
        int mask = baseMask & ~playerBit & ~soulBit;
        if (!isSoul && blockOtherPlayers)
            mask |= playerBit;
        if (passThroughUnits)
            mask &= ~(enemyBit | playerBit);
        return mask;
    }

    /// <summary>ComputePenetration 결과 하나(방향·거리)를 수평 성분만 누적한다.</summary>
    public static Vector3 AccumulatePenetration(Vector3 sum, Vector3 direction, float distance)
    {
        if (distance <= 0f)
            return sum;

        Vector3 planar = new Vector3(direction.x, 0f, direction.z);
        return sum + planar * distance;
    }

    /// <summary>
    /// 정적 지형 ComputePenetration 결과 하나를 수평 성분만 누적한다. (할 일 136)
    /// 해소 방향이 걸을 수 있는 면 법선(위쪽)이면 접지 겹침이라 버린다 — 경사에서 옆으로 미끄러지지 않게.
    /// </summary>
    public static Vector3 AccumulateStaticPenetration(Vector3 sum, Vector3 direction, float distance, float maxWalkableAngle)
    {
        if (distance <= 0f || direction.sqrMagnitude <= 1e-8f)
            return sum;
        if (Vector3.Angle(direction, Vector3.up) <= maxWalkableAngle)
            return sum;

        return AccumulatePenetration(sum, direction, distance);
    }

    /// <summary>
    /// 정적 겹침 해소 재료(할 일 136). 비보행면 관통만 모은다 — 수평 합, 전체(수직 포함) 합, 가장 깊은 관통 하나.
    /// 수평 합이 0(벽 박스 한가운데·양쪽 벽이 상쇄)이면 전체 합, 그것도 0 이면 가장 깊은 관통으로 푼다.
    /// </summary>
    public struct StaticPenetration
    {
        public Vector3 Planar;
        public Vector3 Full;
        public Vector3 DeepestDirection;
        public float DeepestDistance;
    }

    /// <summary>
    /// ComputePenetration 결과 하나를 <see cref="StaticPenetration"/> 에 누적한다. 걸을 수 있는 면 법선(위쪽)은 접지 겹침이라 버린다.
    /// 가장 깊은 관통은 깊이가 같으면(1e-4 m) 방향 성분 x→z→y 큰 쪽을 골라 겹침 버퍼 순서와 무관하게 서버·오너가 같은 답을 낸다.
    /// </summary>
    public static StaticPenetration AccumulateStaticEscape(
        StaticPenetration acc, Vector3 direction, float distance, float maxWalkableAngle)
    {
        if (distance <= 0f || direction.sqrMagnitude <= 1e-8f)
            return acc;
        if (Vector3.Angle(direction, Vector3.up) <= maxWalkableAngle)
            return acc;

        acc.Planar = AccumulatePenetration(acc.Planar, direction, distance);
        acc.Full += direction * distance;

        const float DepthTie = 1e-4f;
        if (distance > acc.DeepestDistance + DepthTie ||
            (Mathf.Abs(distance - acc.DeepestDistance) <= DepthTie && PrecedesDirection(direction, acc.DeepestDirection)))
        {
            acc.DeepestDirection = direction;
            acc.DeepestDistance = distance;
        }

        return acc;
    }

    /// <summary>
    /// 정적 겹침 해소 한 틱 변위 — 수평 합 → 전체 합 → 가장 깊은 관통 순으로 처음 0 이 아닌 것을 maxSpeed × deltaTime 으로 자른다.
    /// 수평으로 풀리면 종전과 같다(수직 0). 벽 박스 안 공중처럼 수평이 상쇄돼 0 이면 수직 성분까지 쓴다.
    /// </summary>
    public static Vector3 ComputeStaticEscapeStep(StaticPenetration penetration, float maxSpeed, float deltaTime)
    {
        Vector3 planar = ComputeSeparationStep(penetration.Planar, maxSpeed, deltaTime);
        if (planar != Vector3.zero)
            return planar;

        Vector3 fallback = penetration.Full.magnitude >= MinSeparationDistance
            ? penetration.Full
            : penetration.DeepestDirection * penetration.DeepestDistance;
        return CapStep(fallback, maxSpeed, deltaTime);
    }

    private static Vector3 CapStep(Vector3 vector, float maxSpeed, float deltaTime)
    {
        float distance = vector.magnitude;
        float maxStep = Mathf.Max(0f, maxSpeed) * Mathf.Max(0f, deltaTime);
        if (distance < MinSeparationDistance || maxStep <= 0f)
            return Vector3.zero;

        return distance <= maxStep ? vector : vector * (maxStep / distance);
    }

    private static bool PrecedesDirection(Vector3 a, Vector3 b)
    {
        const float Eps = 1e-4f;
        if (Mathf.Abs(a.x - b.x) > Eps) return a.x > b.x;
        if (Mathf.Abs(a.z - b.z) > Eps) return a.z > b.z;
        return a.y > b.y + Eps;
    }

    /// <summary>
    /// 유닛 밀어내기가 벽에 정면으로 막혔을 때 벽을 따라 미끄러질 수평 변위. (할 일 136)
    /// 원래 변위의 접선 성분 부호를 따르고, 정면(접선 0)이면 법선 기준 고정 방향(up × n)을 골라 서버·오너가 같은 답을 낸다.
    /// 길이는 원래 변위 길이 그대로. 법선이 수평 성분이 없거나 변위가 0 이면 0.
    /// </summary>
    public static Vector3 ComputeWallSlideStep(Vector3 step, Vector3 wallNormal)
    {
        Vector3 planarStep = new Vector3(step.x, 0f, step.z);
        Vector3 planarNormal = new Vector3(wallNormal.x, 0f, wallNormal.z);
        float length = planarStep.magnitude;
        if (length < MinSeparationDistance || planarNormal.sqrMagnitude <= 1e-8f)
            return Vector3.zero;

        Vector3 tangent = Vector3.Cross(Vector3.up, planarNormal.normalized);
        float side = Vector3.Dot(planarStep, tangent);
        if (side < -1e-4f)
            tangent = -tangent;
        return tangent * length;
    }

    /// <summary>누적 관통 벡터를 이번 틱에 옮길 수평 변위로 바꾼다 — 길이는 maxSpeed × deltaTime 을 넘지 않는다.</summary>
    public static Vector3 ComputeSeparationStep(Vector3 penetrationSum, float maxSpeed, float deltaTime)
    {
        Vector3 planar = new Vector3(penetrationSum.x, 0f, penetrationSum.z);
        float distance = planar.magnitude;
        float maxStep = Mathf.Max(0f, maxSpeed) * Mathf.Max(0f, deltaTime);
        if (distance < MinSeparationDistance || maxStep <= 0f)
            return Vector3.zero;

        return distance <= maxStep ? planar : planar * (maxStep / distance);
    }

    /// <param name="requestedPlanar">이번 틱 요청 수평 이동량(m).</param>
    /// <param name="appliedPlanar">스윕 후 실제 적용된 수평 이동량(m).</param>
    /// <param name="unitBlocksRemaining">남은 경로를 가장 먼저 막는 것이 유닛(Enemy·Player 몸)인가.</param>
    public static bool ShouldEndDashOnUnitBlock(float requestedPlanar, float appliedPlanar, bool unitBlocksRemaining)
    {
        if (!unitBlocksRemaining || requestedPlanar <= DashBlockedTolerance)
            return false;

        return appliedPlanar < requestedPlanar - DashBlockedTolerance;
    }
}

/// <summary>
/// 대시 한 번의 유닛 관통 장부(할 일 209) — 이번 대시에서 통과한 유닛 id 와 남은 통과 수. 물리·씬에 의존하지 않는다.
/// <see cref="PlayerMotor"/> 가 대시 상태 동안 켜 두고 이동 스윕(<see cref="PlayerMotionSweep"/>)이 읽는다.
/// 시뮬레이션하는 피어가 각자 로컬로 들고 복제하지 않는다(<see cref="PlayerMotor.PassThroughUnitsOverride"/> 와 같은 방식).
/// </summary>
public sealed class PlayerDashUnitPass
{
    private readonly System.Collections.Generic.HashSet<int> passed = new System.Collections.Generic.HashSet<int>();

    /// <summary>대시 중이라 관통 규칙이 켜져 있다.</summary>
    public bool IsActive { get; private set; }
    /// <summary>이번 대시에서 통과할 수 있는 최대 유닛 수.</summary>
    public int Limit { get; private set; }
    /// <summary>이번 대시에서 이미 통과한 유닛 수.</summary>
    public int PassedCount => passed.Count;

    /// <summary>대시 시작 — 장부를 비우고 켠다.</summary>
    public void Begin(int limit)
    {
        passed.Clear();
        Limit = Mathf.Max(0, limit);
        IsActive = true;
    }

    /// <summary>대시 끝 — 장부를 비우고 끈다.</summary>
    public void End()
    {
        passed.Clear();
        Limit = 0;
        IsActive = false;
    }

    /// <summary>이번 대시에서 이미 통과한 유닛인가(그 유닛의 모든 콜라이더를 무시한다).</summary>
    public bool IsPassed(int unitId) => IsActive && passed.Contains(unitId);

    /// <summary>
    /// 이동 경로에서 가장 먼저 만난 유닛을 통과할지 정한다. 이미 통과한 유닛이면 참, 통과 불가 유닛·남은 수 0 이면 거짓(막힘).
    /// 통과하기로 하면 장부에 올린다 — 같은 유닛은 대시 내내 다시 세지 않는다.
    /// </summary>
    public bool TryPass(int unitId, bool passable)
    {
        if (!IsActive)
            return false;
        if (passed.Contains(unitId))
            return true;
        if (!passable || passed.Count >= Limit)
            return false;

        passed.Add(unitId);
        return true;
    }

    /// <summary>다른 장부를 그대로 베낀다(대시 경로 예측이 진행 중 대시 장부를 건드리지 않고 이어 굴릴 때).</summary>
    public void CopyFrom(PlayerDashUnitPass other)
    {
        passed.Clear();
        if (other == null)
        {
            IsActive = false;
            Limit = 0;
            return;
        }

        passed.UnionWith(other.passed);
        Limit = other.Limit;
        IsActive = other.IsActive;
    }
}
