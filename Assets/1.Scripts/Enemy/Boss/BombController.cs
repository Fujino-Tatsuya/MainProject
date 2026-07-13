using Unity.Netcode;
using UnityEngine;

#region Enums

enum BombState
{
    None,
    BombTimer,     // 폭발 대기 타이머
    InitFlight,    // 초기 포물선 발사
    Flight,
    Floor          // 장판 유지
}

#endregion

public class BombController : NetworkBehaviour
{
    #region Inspector Variables

    [Header("폭탄 표시 오브젝트")]
    [SerializeField] GameObject bomb;
    [SerializeField] GameObject floor;
    SpriteRenderer _floorRenderer;
    Collider _floorCollider;

    [Header("\n타이머")]
    [SerializeField] float bombTime;
    [SerializeField] float floorTime;

    [Header("\n데미지와 시간")]
    [SerializeField] int bombDamage;
    [SerializeField] float bombFlightDuration = 0.5f;

    [Header("\n레이어와 태그")]
    [SerializeField] LayerMask player;
    [SerializeField] LayerMask enemy;
    [SerializeField] LayerMask ground;
    [SerializeField] LayerMask wall;
    [SerializeField] LayerMask hazardArea;

    [Header("\n범위 설정")]
    [SerializeField] float bombRadius;
    [SerializeField] float floorIncreasAmount;

    #endregion

    #region Component Variables

    KnockbackAttack _knockbackAttack;
    Rigidbody _rigidbody;
    Bomb _bombComponent;

    #endregion

    #region State Variables

    Quaternion _baseRot;
    BombState _bombState = BombState.None;
    float _bombTimer = 0f;
    float _floorTimer = 0f;

    #endregion

    #region Flight Variables

    Vector3 _prevPos;
    Vector3 _startPos;
    Vector3 _targetPos;
    float _duration;
    float _arcHeight;
    float _elapsed;

    #endregion

    #region Follow Variables

    Transform _followTarget;

    #endregion

    #region LifeCycle

    /// <summary>
    /// 네트워크 스폰 시 폭탄 표시 상태와 서버 전용 컴포넌트 참조를 초기화합니다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        _floorRenderer = floor.GetComponent<SpriteRenderer>();
        _floorCollider = floor.GetComponent<Collider>();

        bomb.SetActive(true);
        SetFloorEnable(false);

        if (!IsServer) return;

        _bombComponent = bomb.GetComponent<Bomb>();
        if (_bombComponent == null)
        {
            Debug.LogAssertion("Bomb 컴포넌트가 연결되어 있지 않습니다.");
        }
        else
        {
            _bombComponent.OnTriggered += BombHit;
        }

        _knockbackAttack = GetComponent<KnockbackAttack>();
        if (_knockbackAttack == null)
        {
            Debug.LogAssertion("KnockbackAttack 컴포넌트가 연결되어 있지 않습니다.");
        }

        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            Debug.LogAssertion("Rigidbody 컴포넌트가 연결되어 있지 않습니다.");
        }

        _baseRot = transform.rotation;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (_bombComponent != null)
        {
            _bombComponent.OnTriggered -= BombHit;
        }
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        FollowSocket();
        UpdateFlight();
    }

    void Update()
    {
        if (!IsServer) return;

        CheckBombTimer();
        CheckFloor();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, bombRadius);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 폭탄을 지정한 소켓의 월드 위치와 회전에 따라가도록 고정합니다.
    /// </summary>
    /// <param name="socket">폭탄이 따라갈 소켓 Transform입니다.</param>
    public void Hold(Transform socket)
    {
        if (!IsServer) return;

        _followTarget = socket;
        _bombState = BombState.None;

        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;

        transform.SetPositionAndRotation(socket.position, socket.rotation);
    }

    /// <summary>
    /// 폭탄을 현재 위치에서 목표 위치까지 포물선 궤적으로 발사합니다.
    /// </summary>
    /// <param name="target">폭탄이 도착할 월드 위치입니다.</param>
    /// <param name="duration">목표 지점까지 날아가는 시간입니다.</param>
    /// <param name="arcHeight">포물선의 최대 높이입니다.</param>
    public void Launch(Vector3 target, float duration, float arcHeight)
    {
        if (!IsServer) return;

        _followTarget = null;
        _bombState = BombState.InitFlight;

        _startPos = transform.position;
        _prevPos = _startPos;
        _targetPos = target;
        _duration = duration;
        _arcHeight = arcHeight;
        _elapsed = 0f;

        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;
    }
    /// <summary>
    /// 폭탄을 현재 위치에서 일정 거리만큼 직선으로 발사합니다.
    /// </summary>
    /// <param name="referencePoint">발사 방향 계산을 위한 위치</param>
    /// <param name="distance">발사할 거리</param>
    void LinearLaunch(Vector3 referencePoint, float distance)
    {
        if (!IsServer) return;

        _followTarget = null;
        _bombState = BombState.Flight;

        _startPos = transform.position;
        _prevPos = _startPos;

        Vector3 dir = referencePoint - _startPos;
        dir.y = 0f;
        dir.Normalize();

        _targetPos = _startPos - dir * distance;
        _duration = bombFlightDuration;
        _arcHeight = 0;
        _elapsed = 0f;

        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;
    }

    /// <summary>
    /// 장판 유지 시간을 처음부터 다시 계산하도록 타이머를 초기화합니다.
    /// </summary>
    void InitFloorTimer()
    {
        _floorTimer = 0f;
    }

    #endregion

    #region State Control Methods

    void Init()
    {
        transform.rotation = _baseRot;
        _bombState = BombState.None;
        _bombTimer = 0f;
        _floorTimer = 0f;
    }

    void DespawnBomb()
    {
        Init();

        // 임시로 폭탄 네트워크 오브젝트를 제거합니다.
        GetComponent<NetworkObject>().Despawn(true);
    }

    #endregion

    #region Timer Methods

    void CheckBombTimer()
    {
        if (_bombState != BombState.BombTimer) return;

        _bombTimer += Time.deltaTime;
        if (bombTime <= _bombTimer)
        {
            Explode();
            MakeFloor();
        }
    }

    void CheckFloor()
    {
        if (_bombState != BombState.Floor) return;

        _floorTimer += Time.deltaTime;
        if (floorTime <= _floorTimer)
        {
            SetFloorEnableClientRpc(false);
            DespawnBomb();
        }
    }

    #endregion

    #region Follow Methods

    void FollowSocket()
    {
        if (_followTarget == null) return;
        if (_bombState != BombState.None) return;

        _rigidbody.MovePosition(_followTarget.position);
        _rigidbody.MoveRotation(_followTarget.rotation);
    }

    #endregion

    #region Flight Methods

    void UpdateFlight()
    {
        if (_bombState != BombState.InitFlight && _bombState != BombState.Flight) return;

        _elapsed += Time.fixedDeltaTime;

        float t = Mathf.Clamp01(_elapsed / _duration);
        Vector3 nextPos = EvaluatePosition(t);

        if (CheckHitBetween(_prevPos, nextPos))
            return;

        _rigidbody.MovePosition(nextPos);
        _prevPos = nextPos;

        if (t >= 1f)
        {
            _rigidbody.MovePosition(_targetPos);
            _bombState = BombState.BombTimer;
            _elapsed = 0f;
        }
    }

    Vector3 EvaluatePosition(float t)
    {
        Vector3 linear = Vector3.Lerp(_startPos, _targetPos, t);
        float arc = 4f * _arcHeight * t * (1 - t);

        return linear + Vector3.up * arc;
    }

    void BombHit(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (_bombState != BombState.BombTimer) return;

        LinearLaunch(hitContext.sourcePosition, attackInfo.damage);
    }

    #endregion

    #region Collision Methods

    bool CheckHitBetween(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float distance = dir.magnitude;

        if (distance <= 0f)
            return false;

        int layerMask = player | enemy | wall;
        if (_bombState == BombState.InitFlight)
            layerMask = layerMask | ground;

        if (Physics.SphereCast(
            from,
            bombRadius,
            dir.normalized,
            out RaycastHit hit,
            distance,
            layerMask,
            QueryTriggerInteraction.Collide))
        {
            HandleHit(hit.collider);
            return true;
        }

        return false;
    }

    void HandleHit(Collider collider)
    {
        int layer = collider.gameObject.layer;

        if ((player.value & (1 << layer)) != 0)
        {
            Unit unit = collider.GetComponentInParent<Unit>();

            Explode();
            unit?.TakeDamage(new AttackInfo(bombDamage));
            _knockbackAttack.ApplyKnockbackAttack(collider.gameObject);
            MakeFloor();
            Edit.Log("플레이어와 충돌!");
        }
        else if ((enemy.value & (1 << layer)) != 0 && _bombState != BombState.InitFlight)
        {
            Unit unit = collider.GetComponentInParent<Unit>();

            Explode();
            unit?.TakeDamage(new AttackInfo(bombDamage));
            MakeFloor();
            Edit.Log("적과 충돌!");
        }
        else if ((wall.value & (1 << layer)) != 0)
        {
            Explode();
            MakeFloor();
            Edit.Log("벽과 충돌!");
        }
        else if ((ground.value & (1 << layer)) != 0 && _bombState != BombState.Flight)
        {
            _bombState = BombState.BombTimer;
        }
    }

    #endregion

    #region Explosion Methods

    void Explode()
    {
        SetBombEnableClientRpc(false);
    }

    void MakeFloor()
    {
        RaycastHit hit;

        if (Physics.Raycast(transform.position, Vector3.down, out hit, Mathf.Infinity, ground))
        {
            Quaternion slopeRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
            transform.rotation = _baseRot * slopeRot;

            Vector3 pos = hit.point;
            pos.y += 0.01f;
            transform.position = pos;
        }

        BombAreaEffect bombAreaEffect = CheckDoubleExplosion();
        if (bombAreaEffect != null)
        {
            bombAreaEffect.Grow();

            BombController bombController = bombAreaEffect.GetComponentInParent<BombController>();
            if (bombController == null)
            {
                Debug.LogError("BombController 컴포넌트가 연결되어 있지 않아 장판 타이머를 초기화하지 못했습니다.");
                DespawnBomb();
                return;
            }

            bombController.InitFloorTimer();
            DespawnBomb();
            return;
        }

        SetFloorEnableClientRpc(true);
        _bombState = BombState.Floor;
    }

    BombAreaEffect CheckDoubleExplosion()
    {
        BombAreaEffect bombAreaEffect = null;
        Collider[] hits = Physics.OverlapSphere(transform.position, bombRadius, hazardArea);

        foreach (Collider collider in hits)
        {
            BombAreaEffect currentBombAreaEffect = collider.GetComponent<BombAreaEffect>();

            if (currentBombAreaEffect == null)
                continue;

            if (currentBombAreaEffect.CanGrowOnOverlap == false)
                continue;

            if (currentBombAreaEffect.MergeGroup != AreaMergeType.GrowOnOverlap)
                continue;

            bombAreaEffect = currentBombAreaEffect;
            break;
        }

        return bombAreaEffect;
    }

    #endregion

    #region ClientRpc Methods

    [ClientRpc]
    void SetBombEnableClientRpc(bool enable)
    {
        bomb.SetActive(enable);
    }

    void SetFloorEnable(bool enable)
    {
        _floorRenderer.enabled = enable;
        _floorCollider.enabled = enable;
    }
    [ClientRpc]
    void SetFloorEnableClientRpc(bool enable)
    {
        SetFloorEnable(enable);
    }

    [ClientRpc]
    void SetEnableClientRpc(bool enable)
    {
        gameObject.SetActive(enable);
    }

    #endregion
}
