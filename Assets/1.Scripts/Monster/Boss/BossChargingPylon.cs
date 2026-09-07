using System.Collections.Generic;
using UnityEngine;

// 송전탑(차징 기둥). 서버 권한으로 숨김↔활성 위치를 왕복하고, 활성 중에만 피격된다.
//
// 아레나(`bossroom.prefab`)의 `Env_Mv_bosscharger_upper` 4개에 붙는다.
// 평상시엔 바닥 아래에 숨어 통행을 방해하지 않고, 차징 시퀀스가 시작되면 **올라온다**.
// 부서지면 **다시 내려간다**(팀장 확정 2026-08-07).
//
// ─── 레거시 `Enemy/Boss/ChargingObject` 에서 승격한 것 ──────────────────────
// 그 코드가 요구사항을 이미 정확히 구현하고 있었으므로 로직을 그대로 가져왔다.
// 바뀐 것은 두 가지뿐이다:
//   ① **정적 레지스트리**(<see cref="Active"/>) — 기둥은 아레나에, 매니저는 보스에 붙으므로
//      부모-자식 탐색으로는 서로를 못 찾는다. `AreaZone.Active` 와 같은 패턴이다.
//   ② `Unit.Initialize` 호출을 서버에서만 하도록 유지(원본과 동일).
//
// 🔴 위치는 **로컬 좌표 기준**이다. 이전 구현은 Awake 시점의 월드 Y 를 최저점으로 잡아서
//    보스룸이 맵 밖 좌표(x≈+500)로 옮겨지거나 같은 세션에서 패턴을 두 번 돌리면 목표 높이가 어긋났다.
//    위치 복제는 루트의 NetworkTransform 이 담당한다 — 서버만 위치를 쓴다.
//
// ⚠️ `Hurtbox` 를 붙이지 않아도 된다. `BaseAttack.TryResolveHit` 은 Hurtbox 가 없으면
//    `GetComponentInParent<Unit>()` 로 폴백하고, 이 클래스가 `Unit` 파생이라 그대로 잡힌다.
public class BossChargingPylon : Unit
{
    /// <summary>서버 전용 활성 기둥 목록. 차징 매니저가 여기서 필요한 개수만 고른다.</summary>
    public static readonly List<BossChargingPylon> Active = new List<BossChargingPylon>();

    public enum PylonState
    {
        Hidden,
        Rising,
        Live,       // 상승 완료 — 이때만 피격된다
        Lowering,
    }

    [Header("체력")]
    [SerializeField, Min(1)] int maxHp = 200;
    [SerializeField, Min(0)] int defense = 0;

    [Header("이동")]
    [SerializeField, Min(0.01f)]
    [Tooltip("숨김 위치에서 얼마나 올라오는지(m).")]
    float riseHeight = 1f;
    [SerializeField, Min(0.01f)]
    [Tooltip("상승·하강 속도(m/s).")]
    float moveSpeed = 1f;

    Collider _collider;
    Vector3 _hiddenLocal;
    Vector3 _liveLocal;
    PylonState _state = PylonState.Hidden;

    /// <summary>상승을 완료해 피격 가능한 상태인가.</summary>
    public bool IsLive => _state == PylonState.Live;

    /// <summary>이 시퀀스에서 부서졌는가(매니저가 완료 판정에 쓴다).</summary>
    public bool WasDestroyed { get; private set; }

    /// <summary>이번 시퀀스에 참여 중인가(매니저가 활성시킨 기둥만 true).</summary>
    public bool IsEngaged { get; private set; }

    public PylonState State => _state;

    void Awake()
    {
        _collider = GetComponent<Collider>();
        _hiddenLocal = transform.localPosition;
        _liveLocal = _hiddenLocal + Vector3.up * riseHeight;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SetColliderEnabled(false);

        if (!IsServer) return;

        Initialize(0, 0, 0, maxHp, defense);
        Active.Add(this);

        // 시작은 항상 숨김 — 평상시 통행을 방해하지 않는다.
        _state = PylonState.Hidden;
        transform.localPosition = _hiddenLocal;
    }

    public override void OnNetworkDespawn()
    {
        Active.Remove(this);
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsServer) return;

        TickMoving();

        // 활성 중 체력이 0 이면 내려간다(= 부서졌다).
        if (_state == PylonState.Live && CurrentHealth <= 0)
        {
            WasDestroyed = true;
            BeginLowering();
        }
    }

    // 🔴 활성(상승 완료) 상태에서만 피격된다 — 숨김·이동 중에는 무적이다.
    //    이 가드가 없으면 바닥 아래 숨은 기둥이 장판·광역기에 맞아 조용히 부서진다.
    public override void TakeDamage(AttackInfo attackInfo)
    {
        if (!IsServer || _state != PylonState.Live) return;
        base.TakeDamage(attackInfo);
    }

    void TickMoving()
    {
        if (_state != PylonState.Rising && _state != PylonState.Lowering) return;

        Vector3 target = _state == PylonState.Rising ? _liveLocal : _hiddenLocal;
        Vector3 next = Vector3.MoveTowards(transform.localPosition, target, moveSpeed * Time.deltaTime);
        transform.localPosition = next;

        if ((next - target).sqrMagnitude > 0.0000001f) return;

        if (_state == PylonState.Rising)
        {
            _state = PylonState.Live;
            SetColliderEnabled(true);
            return;
        }

        _state = PylonState.Hidden;
        IsEngaged = false;
    }

    /// <summary>서버 전용. 체력을 되살리고 상승을 시작한다. 반복 호출해도 안전하다.</summary>
    public void BeginCharge()
    {
        if (!IsServer) return;

        Revive();
        WasDestroyed = false;
        IsEngaged = true;
        _state = PylonState.Rising;
        SetColliderEnabled(false);
    }

    /// <summary>서버 전용·멱등. 어느 상태에서든 숨김 위치로 되돌린다.</summary>
    public void EndCharge()
    {
        if (!IsServer) return;
        if (_state == PylonState.Hidden || _state == PylonState.Lowering) return;

        BeginLowering();
    }

    void BeginLowering()
    {
        _state = PylonState.Lowering;
        SetColliderEnabled(false);
    }

    void SetColliderEnabled(bool value)
    {
        if (_collider != null) _collider.enabled = value;
    }
}
