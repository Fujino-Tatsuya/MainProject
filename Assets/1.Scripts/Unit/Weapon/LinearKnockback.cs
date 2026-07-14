using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Unit, Rigidbody/NavMeshAgent와 같은 최상단 오브젝트에 위치해 있어야 함
public class LinearKnockback : NetworkBehaviour, IKnockbackable
{
    NavMeshAgent _navMeshAgent;
    Rigidbody _rigidbody;
    bool _isKnockbacking;

    [SerializeField] float maxDistance = 2f;
    [Header("넉백 종료 조건")]
    [SerializeField] float minKnockbackTime = 0.15f;
    [SerializeField] float maxKnockbackTime = 1.5f;
    [SerializeField] float stopSpeed = 0.15f;

    float _knockbackStartTime;

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
        _isKnockbacking = false;
    }


    void FixedUpdate()
    {
        if (!_isKnockbacking || !IsOwner) return;

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
}