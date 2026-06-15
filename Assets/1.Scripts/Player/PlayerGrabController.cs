using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerGrabController : NetworkBehaviour
{
    Player _player;
    PlayerMovement _movement;
    Rigidbody _rigidbody;
    //PlayerStateController _state;

    Transform _grabSocket;
    bool _isGrabbed;
    bool _wasMovementEnabled;
    bool _wasUseGravity;
    bool _wasKinematic;

    bool _beingAttacking = false;

    int _landingDamage;

    void Awake()
    {
        _player = GetComponent<Player>();
        _movement = GetComponent<PlayerMovement>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (!IsServer || !_isGrabbed || _grabSocket == null)
            return;

        if (_rigidbody != null)
        {
            _rigidbody.MovePosition(_grabSocket.position);
            _rigidbody.MoveRotation(_grabSocket.rotation);
        }
        else
        {
            transform.SetPositionAndRotation(_grabSocket.position, _grabSocket.rotation);
        }
    }

    /// <summary>
    /// 플레이어를 보스의 잡기 소켓에 고정하고, 잡기 시작 피해를 적용합니다.
    /// </summary>
    /// <param name="grabSocket">잡힌 동안 플레이어가 따라갈 보스의 소켓 Transform입니다.</param>
    /// <param name="startDamage">잡기 성공 시 즉시 적용할 피해량입니다.</param>
    public void BeginGrab(Transform grabSocket, int startDamage)
    {
        if (!IsServer) return;

        if (_isGrabbed)
            return;

        if (_player == null)
        {
            Debug.LogError("Unit component is not found.", this);
            return;
        }

        if (grabSocket == null)
        {
            Debug.LogError("Grab socket is null.", this);
            return;
        }

        _player.TakeDamage(startDamage);
        //_state.ChangeState(PlayerState.Grabbed);

        _grabSocket = grabSocket;
        _isGrabbed = true;
        _beingAttacking = true;

        if (_movement != null)
        {
            _wasMovementEnabled = _movement.enabled;
            _movement.enabled = false;
        }

        if (_rigidbody != null)
        {
            _wasUseGravity = _rigidbody.useGravity;
            _wasKinematic = _rigidbody.isKinematic;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
            _rigidbody.position = grabSocket.position;
            _rigidbody.rotation = grabSocket.rotation;
        }
        else
        {
            transform.SetPositionAndRotation(grabSocket.position, grabSocket.rotation);
        }
    }

    /// <summary>
    /// 잡기 유지 상태에서 플레이어에게 지속 피해를 적용합니다.
    /// </summary>
    /// <param name="damage">이번 피해 틱에 적용할 피해량입니다.</param>
    public void ApplyHoldDamage(int damage)
    {
        if (!IsServer) return;

        if (_player == null)
        {
            Debug.LogError("Unit component is not found.", this);
            return;
        }

        _player.TakeDamage(damage);
    }

    /// <summary>
    /// 잡기 고정을 해제하고 플레이어 Rigidbody에 던지기 속도를 적용합니다.
    /// </summary>
    /// <param name="force">ForceMode.VelocityChange로 적용할 던지기 방향과 속도 벡터입니다.</param>
    /// <param name="landingDamage">착지 시 적용할 피해량입니다.</param>
    public void Throw(Vector3 force, int landingDamage)
    {
        if (!IsServer) return;

        //_state.ChangeState(PlayerState.Thrown);
        _isGrabbed = false;
        _grabSocket = null;
        _landingDamage = landingDamage;

        if (_rigidbody == null)
        {
            Debug.LogError("Rigidbody component is not found.", this);
            RestoreDefaultControl();
            return;
        }

        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.AddForce(force, ForceMode.VelocityChange);
    }

    /// <summary>
    /// 던져진 플레이어가 착지했을 때 착지 피해를 적용하고 조작 상태를 복구합니다.
    /// </summary>
    void OnLandedAfterThrow()
    {
        if (!IsServer) return;

        if (_player != null)
        {
            _player.TakeDamage(_landingDamage);
        }
        else
        {
            Debug.LogError("Unit component is not found.", this);
        }

        //_state.ChangeState(PlayerState.Normal);
        RestoreDefaultControl();
    }

    void RestoreDefaultControl()
    {
        _isGrabbed = false;
        _beingAttacking = false;
        _grabSocket = null;

        if (_movement != null)
            _movement.enabled = _wasMovementEnabled;

        if (_rigidbody != null)
        {
            _rigidbody.useGravity = _wasUseGravity;
            _rigidbody.isKinematic = _wasKinematic;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;

        int groundLayer = LayerMask.NameToLayer("Surface");
        if (_beingAttacking && LayerMask.Equals(collision.gameObject.layer, groundLayer))
        {
            Debug.Log("바닥/벽면과 충돌!");

            OnLandedAfterThrow();
        }
    }
}
