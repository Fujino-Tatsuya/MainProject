using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Unit, Rigidbody/NavMeshAgent와 같은 최상단 오브젝트에 위치해 있어야 함
//
// 🔴 할 일 136 — 물리 임펄스(Rigidbody Dynamic 전환 + AddForce) 대신 **정해진 거리·시간을 직접 민다**(23호 취약 넉백·MonsterBase 밀림과 같은 방식).
//    예전: useGravity 꺼진 질량 1 바디에 임펄스 → 마찰 없이 등속으로 벽까지 미끄러져 박히고(Discrete), 끝나면 2m 안 NavMesh 로 Warp → 벽 뒤 섬·갇힘.
//    지금: 초속 = 세기(임펄스 ÷ 질량 1 과 같은 출발 속도), 감속 = knockbackDeceleration(m/s², 플레이어 GameRule 넉백 감속 6 과 같은 체감).
//    에이전트가 있으면 NavMesh.Raycast 로 메시 끝에서, 없으면(허수아비·고정형) 몸 캡슐 스윕으로 Wall·Barrier 앞에서 멈춘다.
//    벽 충돌 판정(UnitWallImpacts.ServerProbe · WallImpactEpisode)은 그대로 — 막힌 틱의 요청/실제 이동을 넘긴다.
[DataTableSheet("Monster", Order = 2)]
public class LinearKnockback : NetworkBehaviour, IKnockbackable
{
    NavMeshAgent _navMeshAgent;
    bool _isKnockbacking;

    [SerializeField] float maxDistance = 2f;
    [Header("넉백 종료 조건")]
    [DataTableIgnore] [SerializeField] float minKnockbackTime = 0.15f;
    [DataTableIgnore] [SerializeField] float maxKnockbackTime = 1.5f;
    [DataTableIgnore] [SerializeField] float stopSpeed = 0.15f;
    [Tooltip("밀림 감속(m/s²). 거리 = 세기² ÷ (2 × 감속). 기본 6 = 플레이어 넉백 감속(PlayerGameRuleData.KnockbackDeceleration)과 같은 체감 — 세기 5 ≈ 2.1m·0.83초.")]
    [DataTableIgnore] [SerializeField, Min(0.1f)] float knockbackDeceleration = LinearKnockbackRules.DefaultDeceleration;

    float _knockbackStartTime;
    Vector3 _knockbackDir;
    float _knockbackInitialSpeed;
    float _lastSpeed;
    bool _blocked;

    Unit _unit;
    readonly WallImpactEpisode _wallImpactEpisode = new WallImpactEpisode();

    /// <summary>
    /// IKnockbackable의 ApplyKnockback 구현
    /// </summary>
    /// <param name="direction">넉백 방향</param>
    /// <param name="strength">넉백 세기(출발 속도 m/s)</param>
    public void ApplyKnockback(Vector3 direction, float strength)
    {
        if (!IsServer) return;

        // 넉백을 적용할 오브젝트의 소유자에게만 RPC를 보내서 넉백을 적용
        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { OwnerClientId }
            }
        };
        ApplyKnockbackClientRpc(direction, strength, rpcParams);
    }
    [ClientRpc]
    void ApplyKnockbackClientRpc(Vector3 direction, float strength, ClientRpcParams rpcParams = default)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || strength <= 0f) return;

        StartKnockback();

        _knockbackDir = direction.normalized;
        _knockbackInitialSpeed = strength;
        _lastSpeed = strength;
        _blocked = false;

        // 할 일 32 벽 충돌 — 넉백 한 번 = 에피소드 하나. 판정은 서버(몬스터 오너 = 서버)에서만.
        _wallImpactEpisode.Begin();
    }

    /// <summary>남은 밀림을 지운다(허수아비 자리 복귀 등 — 복귀 뒤 남은 시간 동안 다시 밀리지 않게).</summary>
    public void CancelKnockback()
    {
        if (_isKnockbacking) EndKnockback();
    }

    void StartKnockback()
    {
        _isKnockbacking = true;
        _knockbackStartTime = Time.time;

        // 서버틱 직접 이동과 FSM 추격이 싸우지 않게 에이전트를 내려놓는다(MonsterBase 밀림과 같다). 종료 시 재획득.
        if (_navMeshAgent && _navMeshAgent.enabled)
        {
            if (_navMeshAgent.isOnNavMesh) _navMeshAgent.ResetPath();
            _navMeshAgent.enabled = false;
        }
    }

    void EndKnockback()
    {
        _isKnockbacking = false;
        _wallImpactEpisode.End();

        if (_navMeshAgent)
        {
            _navMeshAgent.enabled = true;
            // 밀림이 메시 위에서만 움직였으므로 보통 제자리 — 높이만 메시에 맞춘다.
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
                _navMeshAgent.Warp(hit.position);
        }
    }

    void Awake()
    {
        _navMeshAgent = GetComponent<NavMeshAgent>();
        _unit = GetComponent<Unit>();
        _isKnockbacking = false;
    }


    void FixedUpdate()
    {
        if (!_isKnockbacking || !IsOwner) return;

        float elapsed = Time.time - _knockbackStartTime;
        float speed = _blocked ? 0f : LinearKnockbackRules.Speed(_knockbackInitialSpeed, knockbackDeceleration, elapsed);
        float requested = speed * Time.fixedDeltaTime;

        float moved = requested > 0f ? MoveClamped(requested) : 0f;
        if (requested > 0f && moved < requested - 0.0001f) _blocked = true;   // 벽(메시 끝)에 닿으면 멈춘다

        // 막힌 뒤(속도 0)에도 직전 속도로 판정한다 — 예전 물리 경로의 '직전 속도' 규칙과 같다.
        if (speed > 0f) _lastSpeed = speed;
        ProbeWallImpact(requested, moved, _lastSpeed);

        if (elapsed < minKnockbackTime)
            return;

        if (_blocked || speed <= stopSpeed || elapsed >= maxKnockbackTime)
            EndKnockback();
    }

    // 이번 틱 requested 만큼 밀되 막히면 그 앞까지. 실제 수평 이동(m)을 돌려준다.
    float MoveClamped(float requested)
    {
        Vector3 before = transform.position;

        if (_navMeshAgent)
        {
            // NavMesh 위에서만 — 메시 밖(벽 뒤·낭떠러지)으로는 절대 밀리지 않는다(종료 Warp 가 엉뚱한 섬으로 가지 않게).
            if (!NavMesh.SamplePosition(before, out NavMeshHit from, 1f, NavMesh.AllAreas)) return 0f;
            Vector3 to = from.position + _knockbackDir * requested;
            if (NavMesh.Raycast(from.position, to, out NavMeshHit edge, NavMesh.AllAreas)) to = edge.position;
            transform.position = new Vector3(to.x, before.y, to.z);
        }
        else
        {
            // 에이전트 없음(허수아비·고정형) — 몸 캡슐 스윕으로 Wall·Barrier 앞에서 자른다(할 일 51 과 같은 규칙).
            float allowed = requested;
            if (_unit != null)
            {
                UnitWallImpacts.GetBodyCapsule(_unit, out Vector3 p1, out Vector3 p2, out float radius);
                allowed = TrainingDummyKnockbackSweep.Sweep(p1, p2, radius, _knockbackDir, requested,
                    transform, TrainingDummyKnockbackSweep.BlockMask);
            }
            if (allowed > 0f) transform.position = before + _knockbackDir * allowed;
        }

        Vector3 delta = transform.position - before;
        delta.y = 0f;
        return delta.magnitude;
    }

    // 할 일 32 — 밀린 방향·이번 틱 요청/실제 이동·속도로 판정한다(막혔으면 판정 거리가 넓어진다). 민 쪽은 Unit.Knockback(방향, 세기) 에 없어서 null.
    // Docs/tech/handoff-wall-impact.md
    void ProbeWallImpact(float requested, float moved, float speed)
    {
        if (!IsServer || _unit == null) return;
        UnitWallImpacts.ServerProbe(_unit, _wallImpactEpisode, _knockbackDir, requested, moved, speed, null, out _);
    }
}

/// <summary>
/// <see cref="LinearKnockback"/> 의 세기 → 속도·거리 규칙(순수 계산, 할 일 136).
/// 초속 = 세기(m/s, 예전 임펄스 ÷ 질량 1 과 같은 출발 속도), 등감속 = deceleration(m/s²).
/// 거리 = 세기² ÷ (2 × 감속), 시간 = 세기 ÷ 감속 — 둘 다 maxTime 에서 자른다.
/// </summary>
public static class LinearKnockbackRules
{
    /// <summary>기본 감속 = 플레이어 넉백 감속(PlayerGameRuleData.KnockbackDeceleration 기본 6) — 같은 세기에 같은 거리.</summary>
    public const float DefaultDeceleration = 6f;

    public static float Speed(float strength, float deceleration, float elapsed)
    {
        if (!(strength > 0f)) return 0f;
        return Mathf.Max(0f, strength - Mathf.Max(0.01f, deceleration) * Mathf.Max(0f, elapsed));
    }

    public static float Duration(float strength, float deceleration, float maxTime)
    {
        if (!(strength > 0f)) return 0f;
        return Mathf.Min(strength / Mathf.Max(0.01f, deceleration), Mathf.Max(0f, maxTime));
    }

    public static float Distance(float strength, float deceleration, float maxTime)
    {
        float t = Duration(strength, deceleration, maxTime);
        float a = Mathf.Max(0.01f, deceleration);
        return Mathf.Max(0f, strength * t - 0.5f * a * t * t);
    }
}
