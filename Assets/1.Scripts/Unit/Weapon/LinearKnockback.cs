using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Unit, Rigidbody/NavMeshAgent와 같은 최상단 오브젝트에 위치해 있어야 함
[DataTableSheet("Monster", Order = 2)]
public class LinearKnockback : NetworkBehaviour, IKnockbackable
{
    NavMeshAgent _navMeshAgent;
    Rigidbody _rigidbody;
    bool _isKnockbacking;

    [SerializeField] float maxDistance = 2f;
    [Header("넉백 종료 조건")]
    [DataTableIgnore] [SerializeField] float minKnockbackTime = 0.15f;
    [DataTableIgnore] [SerializeField] float maxKnockbackTime = 1.5f;
    [DataTableIgnore] [SerializeField] float stopSpeed = 0.15f;

    float _knockbackStartTime;

    Unit _unit;
    readonly WallImpactEpisode _wallImpactEpisode = new WallImpactEpisode();
    Vector3 _wallImpactDir;
    float _wallImpactLastSpeed;

    /// <summary>
    /// IKnockbackable의 ApplyKnockback 구현
    /// </summary>
    /// <param name="direction">넉백 방향</param>
    /// <param name="strength">넉백 세기</param>
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
        StartKnockback();

        _rigidbody.AddForce(direction * strength, ForceMode.Impulse);

        // 할 일 32 벽 충돌 — 임펄스 한 번 = 에피소드 하나. 판정은 서버(몬스터 오너 = 서버)에서만.
        _wallImpactDir = direction;
        _wallImpactLastSpeed = 0f;
        _wallImpactEpisode.Begin();
    }

    void StartKnockback()
    {
        _isKnockbacking = true;
        _knockbackStartTime = Time.time;

        if (_navMeshAgent)
        {
            _navMeshAgent.ResetPath();
            _navMeshAgent.enabled = false;
        }

        _rigidbody.isKinematic = false;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    void EndKnockback()
    {
        _isKnockbacking = false;
        _wallImpactEpisode.End();

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;

        if (_navMeshAgent)
        {
            _rigidbody.isKinematic = true;
            _navMeshAgent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
                _navMeshAgent.Warp(hit.position);
        }
    }

    void Awake()
    {
        _navMeshAgent = GetComponent<NavMeshAgent>();
        _rigidbody = GetComponent<Rigidbody>();
        _unit = GetComponent<Unit>();
        _isKnockbacking = false;
    }


    void FixedUpdate()
    {
        if (!_isKnockbacking || !IsOwner) return;

        ProbeWallImpact();

        float elapsed = Time.time - _knockbackStartTime;

        if (elapsed < minKnockbackTime)
            return;

        bool slowEnough =
            _rigidbody.linearVelocity.sqrMagnitude <= stopSpeed * stopSpeed;

        bool timeout =
            elapsed >= maxKnockbackTime;

        if (slowEnough || timeout)
        {
            EndKnockback();
        }
    }

    // 할 일 32 — 물리 넉백은 PhysX 가 벽에서 멈춘다. 벽에 막혀 속도가 0 이 돼도 처음 민 방향·직전 속도로 판정한다.
    // 민 쪽은 Unit.Knockback(방향, 세기) 에 없어서 null. Docs/tech/handoff-wall-impact.md
    void ProbeWallImpact()
    {
        if (!IsServer || _unit == null) return;
        Vector3 v = _rigidbody.linearVelocity;
        v.y = 0f;
        float speed = v.magnitude;
        Vector3 dir = speed > 0.05f ? v : _wallImpactDir;
        UnitWallImpacts.ServerProbe(_unit, _wallImpactEpisode, dir, 0f, 0f, Mathf.Max(speed, _wallImpactLastSpeed), null, out _);
        _wallImpactLastSpeed = speed;
    }
}