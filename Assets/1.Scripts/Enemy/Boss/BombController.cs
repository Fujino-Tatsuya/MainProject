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

    Collider _floorCollider;

    [Tooltip("Floor 상태 동안 계속 재생되는 장판 이펙트. floor 하위 자식으로 두면 " +
             "FloorAreaEffect가 장판을 키울 때 스케일이 공짜로 따라온다.\n" +
             "비워두면 floor 하위에서 자동으로 찾는다")]
    [SerializeField] ParticleSystem floorEffect;

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

    [Header("\n스케일 (손 → 착지)")]
    [SerializeField] float heldScale = 0.5f;
    [SerializeField] float landedScale = 1f;

    [Header("\nVFX")]
    [Tooltip("InitFlight / Flight 구간에만 재생되는 궤적. 폭탄 본체를 따라간다.\n" +
             "비워두면 아무 일도 하지 않는다")]
    [SerializeField] EffectSocketPlayer trailPlayer;

    [Tooltip("폭발 순간 1회 재생. 비워두면 아무 일도 하지 않는다")]
    [SerializeField] EffectEntry explodeEffect;

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
    /// <summary>
    /// 자기 자신에 붙은 컴포넌트는 여기서 잡는다.
    /// <see cref="OnNetworkSpawn"/>에서 잡으면 그 앞줄이 하나라도 던질 때 통째로 날아간다.
    /// </summary>
    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            Edit.LogAssertion("[No.23] Rigidbody 컴포넌트가 연결되어 있지 않습니다.");
        }
    }

    public override void OnNetworkSpawn()
    {
        _floorCollider = floor != null ? floor.GetComponent<Collider>() : null;

        // 인스펙터에서 지정하지 않았으면 장판 하위에서 찾는다 — 이펙트를 다시 끼워 넣어도 따라온다.
        if (floorEffect == null && floor != null)
            floorEffect = floor.GetComponentInChildren<ParticleSystem>(true);

        bomb.SetActive(true);
        SetFloorEnable(false);

        if (!IsServer) return;

        _bombComponent = bomb.GetComponent<Bomb>();
        if (_bombComponent == null)
        {
            Edit.LogAssertion("[No.23] Bomb 컴포넌트가 연결되어 있지 않습니다.");
        }
        else
        {
            _bombComponent.OnTriggered += BombHit;
        }

        _knockbackAttack = GetComponent<KnockbackAttack>();
        if (_knockbackAttack == null)
        {
            Edit.LogAssertion("[No.23] KnockbackAttack 컴포넌트가 연결되어 있지 않습니다.");
        }

        _baseRot = transform.rotation;

        // [임시 진단] ground 마스크 실제 런타임 값 확인 (Ground만이면 8, Ground+Default면 9)
        Edit.Log($"[진단][No.23] ground.value = {ground.value} (Ground만=8, +Default=9)");
    }

    public override void OnNetworkDespawn()
    {
        // 가드보다 앞이다. 궤적 핸들은 서버·클라 모두 회수해야 풀 인스턴스가 돌아온다
        // (폭탄은 파괴되지 않고 재사용되므로 놓치면 조금씩 샌다).
        trailPlayer?.Stop();

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
        SetBombState(BombState.None);

        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;

        transform.SetPositionAndRotation(socket.position, socket.rotation);
        ApplyUniformScale(heldScale);
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
        SetBombState(BombState.InitFlight);

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
        SetBombState(BombState.Flight);

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
        ApplyUniformScale(landedScale);
        SetBombState(BombState.None);
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

        // 손에서 작게 들고 있다가 날아가는 동안 원래 크기로 돌아온다(착지 순간 = landedScale).
        // 되쳐낸 Flight는 이미 원래 크기이므로 보간하지 않는다.
        if (_bombState == BombState.InitFlight)
            ApplyUniformScale(Mathf.Lerp(heldScale, landedScale, t));

        Vector3 nextPos = EvaluatePosition(t);

        if (CheckHitBetween(_prevPos, nextPos))
            return;

        _rigidbody.MovePosition(nextPos);
        _prevPos = nextPos;

        if (t >= 1f)
        {
            _rigidbody.MovePosition(_targetPos);
            ApplyUniformScale(landedScale);
            SetBombState(BombState.BombTimer);
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

    static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

    /// <summary>
    /// 비행 구간(from→to)에 막히는 것이 있으면 처리하고 true. true면 <see cref="UpdateFlight"/>가
    /// 위치를 갱신하지 않으므로, <b>무시할 히트는 애초에 잡지 않아야 한다.</b>
    ///
    /// ⚠️ 여기서 폭탄이 보스 손 높이에 영구히 멈추는 버그가 났다. 두 가지가 겹쳤다:
    /// <list type="number">
    /// <item><see cref="HandleHit"/>의 enemy 분기는 <c>InitFlight</c>(=보스가 던진 직후)를 제외해
    ///   던진 보스 몸을 무시하는데, 예전 코드는 그 히트에도 true를 돌려줬다. 폭탄은 보스
    ///   <c>HurtBox</c>(EnemyHurtBox) 안에서 출발하므로 매 프레임 "막혔다"가 되어 한 발도 못 나갔고,
    ///   상태도 안 바뀌어 폭발 타이머조차 돌지 않았다. → InitFlight에는 enemy를 마스크에서 뺀다
    ///   (반대로 플레이어가 되쳐낸 <c>Flight</c>에서는 몹을 때려야 하므로 넣는다).</item>
    /// <item>유닛의 <b>공격 히트박스</b>는 몸도 바닥도 벽도 아니다. No.23은 Rage·DashAttack 등
    ///   공격 콜라이더가 Default(=ground 마스크)에 있어, 그대로 두면 폭탄이 던진 보스 몸통을
    ///   "바닥"으로 인식해 손 높이에서 폭발했다. → 몸 레이어가 아닌 유닛 콜라이더는 통과시킨다.</item>
    /// </list>
    /// </summary>
    bool CheckHitBetween(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float distance = dir.magnitude;

        if (distance <= 0f)
            return false;

        int layerMask = player | wall;
        layerMask |= _bombState == BombState.InitFlight ? ground.value : enemy.value;

        int count = Physics.SphereCastNonAlloc(
            from, bombRadius, dir.normalized, HitBuffer, distance,
            layerMask, QueryTriggerInteraction.Collide);

        Collider nearest = null;
        float nearestDistance = float.PositiveInfinity;
        int bodyLayers = player.value | enemy.value;

        for (int i = 0; i < count; i++)
        {
            Collider candidate = HitBuffer[i].collider;
            if (candidate == null)
                continue;

            bool isBodyLayer = (bodyLayers & (1 << candidate.gameObject.layer)) != 0;
            if (!isBodyLayer && candidate.GetComponentInParent<Unit>() != null)
                continue;   // 유닛의 공격 히트박스 — 폭탄은 그냥 통과한다

            if (HitBuffer[i].distance < nearestDistance)
            {
                nearestDistance = HitBuffer[i].distance;
                nearest = candidate;
            }
        }

        if (nearest == null)
            return false;

        HandleHit(nearest);
        return true;
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
            Edit.Log($"[No.23] 플레이어와 충돌! — {collider.name}(layer {layer})");
        }
        else if ((enemy.value & (1 << layer)) != 0 && _bombState != BombState.InitFlight)
        {
            Unit unit = collider.GetComponentInParent<Unit>();

            Explode();
            unit?.TakeDamage(new AttackInfo(bombDamage));
            MakeFloor();
            Edit.Log($"[No.23] 적과 충돌! — {collider.name}(layer {layer})");
        }
        else if ((wall.value & (1 << layer)) != 0)
        {
            Explode();
            MakeFloor();
            Edit.Log($"[No.23] 벽과 충돌! — {collider.name}(layer {layer})");
        }
        else if ((ground.value & (1 << layer)) != 0 && _bombState != BombState.Flight)
        {
            // ⚠️ 이 분기에 들어오면 비행이 그 자리에서 끝난다. 무엇을 "바닥"으로 봤는지 남기지 않으면
            // "폭탄이 공중에 멈춘다"는 증상만 보이고 원인을 못 짚는다(같은 증상으로 두 번 헤맸다).
            // ground 마스크는 Default를 포함하고 Default는 "미분류 전부"라, 보스 밑이 아닌 환경
            // 오브젝트(예: 송전기)까지 후보가 된다 — 이름과 레이어를 반드시 함께 찍는다.
            Edit.Log(
                $"[No.23] 폭탄이 바닥 판정에 걸려 정지 — {collider.name}(layer {layer}), " +
                $"현재 위치 {transform.position}, 목표 {_targetPos}", this);

            // 목표 지점보다 먼저 바닥에 닿은 경우 — 보간이 끝나기 전이므로 여기서 원래 크기로 맞춘다.
            ApplyUniformScale(landedScale);
            SetBombState(BombState.BombTimer);
        }
    }

    #endregion

    #region Scale Methods

    /// <summary>
    /// 폭탄 루트 스케일을 균일하게 맞춘다. 손에 있을 때는 <see cref="heldScale"/>, 바닥에 놓이면
    /// <see cref="landedScale"/>이고 비행 중에는 진행도에 따라 보간하므로 착지 순간 원래 크기가 된다.
    ///
    /// ⚠️ 루트를 스케일하면 장판(<c>floor</c>)과 히트박스도 같이 커진다. 착지·폭발 경로는 모두
    /// <see cref="landedScale"/>로 수렴시켜 두었으니 장판 크기는 종전과 같다. 다만
    /// <see cref="bombRadius"/>(스윕·중첩 판정 반경)는 스케일을 타지 않는 별개 값이므로,
    /// 스케일만 키우면 판정은 그대로라는 점을 기억할 것.
    /// 서버에서만 호출한다 — 복제는 루트 <c>NetworkTransform</c>(SyncScale)이 담당한다.
    /// </summary>
    void ApplyUniformScale(float scale)
    {
        transform.localScale = Vector3.one * scale;
    }

    #endregion

    #region Explosion Methods

    void Explode()
    {
        SetBombEnableClientRpc(false);
        PlayExplodeEffectClientRpc();
    }

    /// <summary>
    /// [ClientRpc] 폭발 이펙트를 각 피어가 자기 폭탄 위치에서 1회 재생한다.
    ///
    /// <b>좌표를 싣지 않는다.</b> 폭탄에 <c>NetworkTransform</c>이 있어 위치가 복제되고,
    /// 폭발 시점의 폭탄은 이미 착지해 멈춰 있으므로 각 피어의 로컬 위치가 곧 정답이다.
    /// 서버 좌표를 보내면 보간 지연만큼 어긋난다.
    ///
    /// <b>IsServer 가드가 없는 것은 의도다</b> — 연출은 각 피어가 자기 화면에 그려야 한다.
    /// </summary>
    [ClientRpc]
    void PlayExplodeEffectClientRpc()
    {
        if (explodeEffect == null || EffectManager.Instance == null) return;

        EffectManager.Instance.Play(explodeEffect, transform.position, Quaternion.identity);
    }

    void MakeFloor()
    {
        // 공중에서 플레이어·벽에 맞아 터진 경로는 보간이 끝나지 않았을 수 있다 — 장판은 항상 원래 크기로 깐다.
        ApplyUniformScale(landedScale);

        RaycastHit hit;

        // 바닥 판정은 GroundProbe로 통일한다. 여기서 실패하면 "폭탄이 공중에 뜬 채로 장판만 깔림"이 된다.
        if (GroundProbe.TryFindGround(transform.position, ground.value, out hit, out string report))
        {
            Quaternion slopeRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
            transform.rotation = _baseRot * slopeRot;

            // 0.01은 z-fighting이 나는 간격이었다 — 장판/폭탄 공통 표준 간격으로 띄운다.
            Vector3 pos = hit.point;
            pos.y = GroundProbe.SurfaceY(hit);
            transform.position = pos;
        }
        else
        {
            Edit.LogWarning(
                $"[No.23] 폭탄 착지 스냅을 건너뜁니다 — 위치 {transform.position}, {report}", this);
        }

        FloorAreaEffect bombAreaEffect = CheckDoubleExplosion();
        if (bombAreaEffect != null)
        {
            bombAreaEffect.OverlapGrow();

            BombController bombController = bombAreaEffect.GetComponentInParent<BombController>();
            if (bombController == null)
            {
                Edit.LogError("[No.23] BombController 컴포넌트가 연결되어 있지 않아 장판 타이머를 초기화하지 못했습니다.");
                DespawnBomb();
                return;
            }

            bombController.InitFloorTimer();
            DespawnBomb();
            return;
        }

        transform.rotation = Quaternion.identity;

        SetFloorEnableClientRpc(true);
        SetBombState(BombState.Floor);
    }

    FloorAreaEffect CheckDoubleExplosion()
    {
        FloorAreaEffect bombAreaEffect = null;
        Collider[] hits = Physics.OverlapSphere(transform.position, bombRadius, hazardArea);

        foreach (Collider collider in hits)
        {
            FloorAreaEffect currentFloorAreaEffect = collider.GetComponent<FloorAreaEffect>();

            if (currentFloorAreaEffect == null)
                continue;

            if (currentFloorAreaEffect.CanGrowOnOverlap == false)
                continue;

            if (currentFloorAreaEffect.FloorType != AreaType.GrowOnOverlap)
                continue;

            bombAreaEffect = currentFloorAreaEffect;
            break;
        }

        return bombAreaEffect;
    }

    #endregion

    #region 비행 궤적 VFX

    /// <summary>비행 중인가. 궤적은 이 구간에서만 재생된다.</summary>
    static bool IsFlying(BombState state)
        => state == BombState.InitFlight || state == BombState.Flight;

    /// <summary>
    /// 상태 전이의 <b>단일 통로</b>. 궤적 on/off를 여기 한 곳에 묶어 두면
    /// 전이 지점이 늘어나도 켜고 끄는 짝이 어긋나지 않는다.
    ///
    /// 궤적은 <see cref="_bombState"/>가 비행 구간에 <b>들어올 때</b> 켜고 <b>나갈 때</b> 끈다.
    /// InitFlight → Flight는 같은 구간 안의 이동이라 재생이 끊기지 않는다.
    /// </summary>
    void SetBombState(BombState next)
    {
        if (_bombState == next) return;

        bool wasFlying = IsFlying(_bombState);
        bool nowFlying = IsFlying(next);
        _bombState = next;

        // 상태 변경은 전부 서버에서 일어난다. 연출만 전 피어로 내보낸다.
        if (IsServer && wasFlying != nowFlying)
            SetTrailClientRpc(nowFlying);
    }

    /// <summary>
    /// [ClientRpc] 각 피어가 자기 폭탄에 궤적을 켜고 끈다.
    /// <b>IsServer 가드가 없는 것은 의도다</b> — 연출은 각자 그려야 한다.
    /// 폭탄에 <c>NetworkTransform</c>이 있어 위치가 복제되므로, 궤적은 로컬에서 따라가면 된다.
    /// </summary>
    [ClientRpc]
    void SetTrailClientRpc(bool play)
    {
        if (trailPlayer == null) return;

        if (play) trailPlayer.Play();
        else trailPlayer.Stop();
    }

    #endregion

    #region ClientRpc Methods

    [ClientRpc]
    void SetBombEnableClientRpc(bool enable)
    {
        SetBombEnable(enable);
    }

    /// <summary>
    /// 폭탄 본체의 표시/판정을 켜고 끈다(폭발 시 false).
    ///
    /// ⚠️ <c>bomb.SetActive</c>만으로는 부족하다 — 폭탄 비주얼을 아트 모델(fbx)로 교체하면서
    /// 그 인스턴스(<c>BombVisual</c>)가 <c>bomb</c>(Sphere)의 <b>형제</b>로 붙었다. 그래서 폭발해서
    /// 장판이 깔린 뒤에도 아트 모델만 그대로 남아 떠 있었다.
    /// 장판(<c>floor</c>) 계층은 유지해야 하므로 그쪽만 제외하고 남은 렌더러를 함께 토글한다
    /// (앞으로 비주얼을 더 붙여도 자동으로 따라온다).
    /// </summary>
    void SetBombEnable(bool enable)
    {
        if (bomb != null)
            bomb.SetActive(enable);

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (floor != null && renderer.transform.IsChildOf(floor.transform))
                continue;   // 장판은 폭발 후에 오히려 보여야 한다
            if (bomb != null && renderer.transform.IsChildOf(bomb.transform))
                continue;   // 위 SetActive가 이미 처리

            renderer.enabled = enable;
        }
    }

    /// <summary>
    /// 장판의 표시/판정을 켜고 끈다. 스폰 시 false, <see cref="MakeFloor"/>에서 true,
    /// 장판 시간이 끝나면 다시 false — Floor 상태의 시작과 끝이 정확히 여기로 모인다.
    ///
    /// <b>이펙트는 SetActive로 다루지 않는다.</b> 장판(Circle)에는 <c>NetworkTransform</c>이 있어
    /// GameObject를 껐다 켜면 문제가 됐다(그래서 렌더러 토글로 바꿨다). 파티클도 같은 이유로
    /// 오브젝트를 건드리지 않고 <c>Play</c>/<c>Stop</c>만 쓴다.
    ///
    /// ⚠️ 널 검사는 장식이 아니다. 이 메서드는 <see cref="OnNetworkSpawn"/> 초반에 불리므로,
    /// 여기서 터지면 <b>그 뒤의 컴포넌트 캐싱이 통째로 건너뛰어진다</b>. 실제로 장판의
    /// SpriteRenderer를 인스펙터에서 지웠더니 <c>_rigidbody</c>가 null로 남아
    /// 엉뚱하게 <c>UpdateFlight</c>에서 NRE가 났다.
    /// </summary>
    void SetFloorEnable(bool enable)
    {
        if (_floorCollider != null) _floorCollider.enabled = enable;

        if (floorEffect == null) return;

        // withChildren: true — 프리팹 루트만 잡아도 하위 파티클(fire/circle/particles)까지 함께 간다.
        if (enable)
            floorEffect.Play(true);
        else
            floorEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
