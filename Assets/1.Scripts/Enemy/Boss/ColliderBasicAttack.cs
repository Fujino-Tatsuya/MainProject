using System.Collections.Generic;
using UnityEngine;

public enum TriggerMode
{
    OnlyEnter,
    OnlyStay,
    OnlyExit,
    OnlyStayAndKnockBack
}

/// <summary>
/// 콜라이더 트리거로 판정하는 공용 공격. 모드에 따라 진입/지속/이탈 중 하나에서 피해를 준다.
///
/// <b>지속 모드(OnlyStay / OnlyStayAndKnockBack)의 구조.</b> 겹친 대상을 <see cref="_stayTargets"/>에
/// 모아두고 <see cref="FixedUpdate"/>가 <b>스텝당 한 번</b> 타이머를 돌린 뒤 <b>전원에게</b> 적용한다.
/// 예전에는 <c>OnTriggerStay</c> 안에서 타이머를 직접 굴렸는데, 그 콜백은 "겹친 콜라이더마다"
/// 불리는 반면 타이머는 필드 하나라 두 가지가 동시에 깨져 있었다:
/// <list type="number">
/// <item>대상이 아닌 콜라이더(바닥·벽·이펙트)까지 타이머를 올리고, 임계값을 넘긴 뒤
///   <c>TryResolveHit</c>이 실패해도 타이머를 리셋했다. 폭탄 장판은 반경 절반이 바닥에 파묻혀
///   있어 <b>바닥이 매 스텝 임계값을 가로채</b> 플레이어가 영원히 피해를 입지 않았다.</item>
/// <item>겹친 대상이 N명이면 타이머가 N배로 빨리 차고, 그 순간의 한 명만 맞았다.
///   2인 협동에서 틱 간격이 절반이 되고 피해는 번갈아 들어갔다.</item>
/// </list>
///
/// <b>왜 Enter/Exit로 목록을 유지하는가.</b> <c>OnTriggerStay</c>는 양쪽 액터가 모두 잠들면
/// 호출이 끊긴다. 폭탄 장판은 착지 후 정지한 kinematic Rigidbody라 정확히 그 조건에 걸린다.
/// Enter/Exit는 상태 변화 이벤트라 그 영향을 받지 않는다. <c>Stay</c>는 안전망으로만 남겨
/// (이미 겹친 상태에서 콜라이더가 켜지는 등) Enter를 놓친 경우를 메운다.
/// </summary>
public class ColliderBasicAttack : BaseAttack
{
    [SerializeField] private TriggerMode triggerMode;

    [Tooltip("지속 모드에서 피해가 들어가는 주기(초). 0이면 매 물리 스텝마다 들어간다")]
    [SerializeField] private float stayTime;

    KnockbackAttack _knockbackAttack;

    // 현재 겹쳐 있는 '대상' 콜라이더들. 대상 레이어가 아닌 것은 애초에 담기지 않는다.
    readonly List<Collider> _stayTargets = new List<Collider>();

    // 한 틱에 같은 유닛을 두 번 때리지 않기 위한 재사용 버퍼(유닛 하나에 허트박스가 여러 개일 수 있다).
    readonly List<Transform> _hitRoots = new List<Transform>();

    private float _stayTimer = 0f;

    bool IsStayMode =>
        triggerMode == TriggerMode.OnlyStay || triggerMode == TriggerMode.OnlyStayAndKnockBack;

    private void Awake()
    {
        InitializeAttackInfo();
        _knockbackAttack = GetComponent<KnockbackAttack>();
    }

    void OnDisable()
    {
        // 꺼졌다 켜지면 처음부터 다시 센다. 남은 목록으로 부활 직후 즉시 때리지 않게.
        _stayTargets.Clear();
        _stayTimer = 0f;
    }

    /// <summary>
    /// 지속 피해를 스텝당 한 번 처리한다. 트리거 콜백은 물리 시뮬레이션 <b>뒤</b>에 오므로
    /// 여기서 보는 목록은 직전 스텝에 모인 것이다(한 스텝 지연은 무시할 수 있다).
    /// </summary>
    void FixedUpdate()
    {
        if (!IsStayMode || !IsServer)
            return;

        PruneStayTargets();

        if (_stayTargets.Count == 0)
        {
            _stayTimer = 0f;
            return;
        }

        _stayTimer += Time.fixedDeltaTime;
        if (_stayTimer < stayTime)
            return;

        _stayTimer = 0f;
        FlushStayHits();
    }

    #region Trigger Callbacks

    private void OnTriggerEnter(Collider other)
    {
        OnAttackTriggerEnter(other);
    }

    public void OnAttackTriggerEnter(Collider other)
    {
        if (!IsServer)
            return;

        if (IsStayMode)
        {
            TrackStayTarget(other);
            return;
        }

        if (triggerMode != TriggerMode.OnlyEnter)
            return;

        TryResolveHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        OnAttackTriggerStay(other);
    }

    public void OnAttackTriggerStay(Collider other)
    {
        if (!IsServer || !IsStayMode)
            return;

        // 안전망일 뿐 타이머는 건드리지 않는다 — 목록에 이미 있으면 아무 일도 하지 않는다.
        TrackStayTarget(other);
    }

    private void OnTriggerExit(Collider other)
    {
        OnAttackTriggerExit(other);
    }

    public void OnAttackTriggerExit(Collider other)
    {
        if (!IsServer)
            return;

        if (IsStayMode)
        {
            _stayTargets.Remove(other);
            return;
        }

        if (triggerMode != TriggerMode.OnlyExit)
            return;

        TryResolveHit(other);
    }

    #endregion

    #region Stay Bookkeeping

    /// <summary>겹침 목록에 대상을 등록한다. 대상 레이어가 아니거나 이미 있으면 무시한다.</summary>
    void TrackStayTarget(Collider other)
    {
        if (other == null || !IsInTargetLayer(other))
            return;

        if (_stayTargets.Contains(other))
            return;

        _stayTargets.Add(other);
    }

    /// <summary>
    /// 파괴·비활성된 콜라이더를 걷어낸다. 보통은 <c>OnTriggerExit</c>이 알아서 빼주지만,
    /// 대상이 통째로 사라지는 경로(디스폰·씬 전환)에서는 이탈 이벤트가 오지 않는다.
    /// </summary>
    void PruneStayTargets()
    {
        for (int i = _stayTargets.Count - 1; i >= 0; i--)
        {
            Collider target = _stayTargets[i];

            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy)
                _stayTargets.RemoveAt(i);
        }
    }

    /// <summary>겹쳐 있는 대상 전원에게 한 틱분의 피해를 적용한다.</summary>
    void FlushStayHits()
    {
        _hitRoots.Clear();

        for (int i = 0; i < _stayTargets.Count; i++)
        {
            Collider target = _stayTargets[i];
            if (target == null)
                continue;

            // 유닛당 한 번. 허트박스가 여러 개인 유닛이 한 틱에 중복으로 맞지 않게 한다.
            Transform root = target.transform.root;
            if (_hitRoots.Contains(root))
                continue;
            _hitRoots.Add(root);

            TryResolveHit(target);

            if (triggerMode == TriggerMode.OnlyStayAndKnockBack)
                _knockbackAttack?.ApplyKnockbackAttack(target.gameObject);
        }
    }

    #endregion
}
