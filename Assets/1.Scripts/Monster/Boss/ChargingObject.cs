using System;
using UnityEngine;

/// <summary>
/// No.23 충전 패턴의 피격 대상 기둥. 서버 권한으로 숨김↔활성 위치를 왕복한다.
///
/// 위치는 <b>로컬 좌표 기준</b>이다(승인 계획 Task 6). 이전 구현은 Awake 시점의 월드 Y를 최저점,
/// 컨트롤러의 절대값을 최고점으로 잡아서 보스룸이 맵 밖 좌표(x≈+500)로 옮겨지거나 같은 세션에서
/// 패턴을 두 번 돌리면 목표 높이가 어긋났다.
///
/// 위치 복제는 루트의 NetworkTransform이 담당한다 — 서버만 위치를 쓴다.
/// </summary>
public class ChargingObject : Unit
{
    public enum ChargeState
    {
        Hidden,
        Rising,
        Active,
        Lowering
    }

    [SerializeField] int maxHp;
    [SerializeField] int defense;

    [Header("이동")]
    [Tooltip("숨김 위치에서 얼마나 올라오는지(m). ChargeController.SetMinMaxY가 덮어쓸 수 있다.")]
    [SerializeField, Min(0.01f)] float riseHeight = 1f;

    [Tooltip("상승·하강 속도(m/s).")]
    [SerializeField, Min(0.01f)] float moveSpeed = 1f;

    Collider _collider;

    Vector3 _hiddenLocalPosition;
    Vector3 _activeLocalPosition;
    ChargeState _state = ChargeState.Hidden;

    /// <summary>활성 위치에 도달해 피격 가능한 상태인지. BT 조건이 참조한다.</summary>
    public bool IsReached => _state == ChargeState.Active;

    /// <summary>현재 상태(디버그·검증용).</summary>
    public ChargeState State => _state;

    public event EventHandler DestroyEvent;
    public event EventHandler ReachEvent;

    void Awake()
    {
        _collider = GetComponent<Collider>();
        CacheLocalPositions();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SetColliderEnabled(false);

        if (!IsServer) return;

        Initialize(0, 0, 0, maxHp, defense);

        // 시작은 항상 숨김 — 평상시 통행을 방해하지 않는다.
        _state = ChargeState.Hidden;
        transform.localPosition = _hiddenLocalPosition;
    }

    void CacheLocalPositions()
    {
        _hiddenLocalPosition = transform.localPosition;
        _activeLocalPosition = _hiddenLocalPosition + Vector3.up * riseHeight;
    }

    /// <summary>
    /// 상승 높이 설정. ChargeController가 리스트 등록 시 호출한다.
    /// (이전 시그니처 유지 — 값의 의미는 절대 Y가 아니라 <b>상승 높이</b>다.)
    /// </summary>
    public void SetMinMaxY(float rise)
    {
        riseHeight = Mathf.Max(0.01f, rise);
        _activeLocalPosition = _hiddenLocalPosition + Vector3.up * riseHeight;
    }

    void Update()
    {
        if (!IsServer) return;

        TickMoving();
        CheckHp();
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        // 활성(상승 완료) 상태에서만 피격된다 — 숨김·이동 중에는 무적.
        if (!IsServer || _state != ChargeState.Active) return;

        base.TakeDamage(attackInfo);
    }

    void CheckHp()
    {
        if (_state != ChargeState.Active) return;

        if (CurrentHealth <= 0)
        {
            BeginLowering();
            DestroyEvent?.Invoke(this, EventArgs.Empty);
        }
    }

    void TickMoving()
    {
        if (_state != ChargeState.Rising && _state != ChargeState.Lowering) return;

        Vector3 target = _state == ChargeState.Rising ? _activeLocalPosition : _hiddenLocalPosition;
        Vector3 next = Vector3.MoveTowards(transform.localPosition, target, moveSpeed * Time.deltaTime);
        transform.localPosition = next;

        if ((next - target).sqrMagnitude > 0.0000001f)
            return;

        if (_state == ChargeState.Rising)
        {
            _state = ChargeState.Active;
            SetColliderEnabled(true);
            ReachEvent?.Invoke(this, EventArgs.Empty);
            return;
        }

        _state = ChargeState.Hidden;
    }

    /// <summary>
    /// 서버 전용. 체력·상태를 초기화하고 상승을 시작한다. 같은 세션에서 반복 호출해도 안전하다.
    /// </summary>
    public void StartCharge()
    {
        if (!IsServer) return;

        Revive();
        _state = ChargeState.Rising;
        SetColliderEnabled(false);
    }

    /// <summary>서버 전용·멱등. 어느 상태에서든 숨김 위치로 되돌린다.</summary>
    public void EndCharge()
    {
        if (!IsServer) return;

        if (_state == ChargeState.Hidden || _state == ChargeState.Lowering) return;

        BeginLowering();
    }

    void BeginLowering()
    {
        _state = ChargeState.Lowering;
        SetColliderEnabled(false);
    }

    void SetColliderEnabled(bool enabled)
    {
        if (_collider != null)
            _collider.enabled = enabled;
    }
}
