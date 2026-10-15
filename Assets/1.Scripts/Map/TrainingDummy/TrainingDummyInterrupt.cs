using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 허수아비 인터럽트 성공 한 건(서버). 공격자가 플레이어가 아니면 AttackerClientId 는 ulong.MaxValue,
/// 출처 유닛을 모르면(TakeDamage 경로) SourceUnit 은 null.
/// </summary>
public readonly struct TrainingDummyInterruptSuccess
{
    public TrainingDummyInterruptSuccess(ulong attackerClientId, Unit sourceUnit)
    {
        AttackerClientId = attackerClientId;
        SourceUnit = sourceUnit;
    }

    public ulong AttackerClientId { get; }
    public Unit SourceUnit { get; }
}

/// <summary>
/// 허수아비 인터럽트(간파) 연습. 프리팹 루트에 붙인다 — 붙어 있지 않으면 인터럽트는 데미지만 들어간다.
///
/// 몬스터 중간보스 카운터 창과 같은 판정(<see cref="CounterWindow"/>, 1회 소비)·같은 시각(노란 베이스 틴트)을 쓴다.
/// 다른 점은 창이 <b>무기한</b>이라는 것 하나 — 그래서 몬스터의 간파 표시 조기 소등은 없다.
///
/// 인터럽트 성공 후 <b>취약</b>(보라)일 때만 CC(넉백·스턴 등)가 들어간다. 보스 23호의 취약(넉백·게이지)과는 별개 — 허수아비 연습용.
/// 막는 수단은 코어의 슈퍼아머 규칙 그대로다 — 취약이 아닐 때 <see cref="StatusEffectType.SuperArmor"/> 를
/// 무기한으로 걸어 두면 넉백(<see cref="Unit.HasSuperArmor"/>)과 차단류 상태이상(<see cref="StatusEffectImmunityPolicy"/>)이 거부된다.
/// 데미지는 상태와 무관하게 항상 들어간다.
///
/// <list type="bullet">
/// <item><b>서버</b> — <see cref="TrainingDummy"/> 가 피격 경로에서 <see cref="ServerTryInterrupt"/> 를 부른다.
///       상태 전이는 <see cref="TrainingDummyInterruptCycle"/> 이 하고, 결과만 복제값·슈퍼아머에 쓴다.</item>
/// <item><b>전 피어</b> — 복제값 변화로 틴트를 갱신한다. 늦게 합류한 클라도 스폰 때 현재 값을 적용한다.
///       슈퍼아머는 상태이상 NetworkList 로 복제된다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrainingDummy))]
[DataTableSheet("Map", Order = 0)]
public sealed class TrainingDummyInterrupt : NetworkBehaviour, IInterruptSlowMotionTarget
{
    [Header("순환")]
    [SerializeField, Min(0f), FormerlySerializedAs("groggyDuration")]
    [Tooltip("인터럽트 성공 후 취약 시간(초). 이 동안만 CC 가 들어가고, 인터럽트는 데미지만 들어간다. " +
             "보스 23호의 취약(넉백·게이지)과는 별개 — 허수아비 연습용.")]
    float vulnerableDuration = 3f;

    [SerializeField, Min(0f)]
    [Tooltip("취약이 끝나고 다시 인터럽트 가능해지기까지 대기(초). 0 = 취약이 끝나면 바로.")]
    float idleDuration = 1f;

    [Header("연출")]
    [DataTableIgnore]
    [SerializeField]
    [Tooltip("인터럽트 가능한 동안의 틴트 색. 기본 = 몬스터 카운터 창과 같은 노란색.")]
    Color interruptibleTint = new Color(1f, 0.85f, 0.2f, 1f);

    [DataTableIgnore]
    [SerializeField]
    [Tooltip("취약(CC 가 들어가는) 동안의 틴트 색.")]
    Color vulnerableTint = new Color(0.62f, 0.3f, 1f, 1f);

    [Tooltip("인터럽트 성공 순간의 섬광. 비워두면 연출만 빠진다")]
    [SerializeField] EffectSocketPlayer interruptFlash;

    readonly NetworkVariable<TrainingDummyInterruptState> _state = new NetworkVariable<TrainingDummyInterruptState>(
        TrainingDummyInterruptState.Interruptible,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    TrainingDummyInterruptCycle _cycle; // 서버 전용
    TrainingDummyHitFlash _flash;
    TrainingDummy _dummy;

    // 슈퍼아머 출처 키. 같은 (타입, 출처)로 Apply/Remove 가 짝을 이룬다.
    ulong SuperArmorSourceId => NetworkObjectId;

    /// <summary>[전 피어] 지금 인터럽트를 받는가. 복제값이라 클라에서도 읽힌다.</summary>
    public bool IsInterruptible => _state.Value == TrainingDummyInterruptState.Interruptible;

    /// <summary>[전 피어] 지금 취약(CC 가 들어감)인가. 복제값이라 클라에서도 읽힌다.</summary>
    public bool IsVulnerable => _state.Value == TrainingDummyInterruptState.Vulnerable;

    /// <summary>[전 피어] 현재 상태.</summary>
    public TrainingDummyInterruptState State => _state.Value;

    /// <summary>[서버] 슬로우 모션 예측 — 지금 인터럽트를 받는가. 순환 판정 원본(<see cref="TrainingDummyInterruptCycle"/>)을 직접 본다.</summary>
    bool IInterruptSlowMotionTarget.ServerIsInterruptible => IsServer && _cycle != null && _cycle.IsInterruptible;

    /// <summary>[서버] 슬로우 모션 성공 판별 — 타격 전후로 비교한다.</summary>
    int IInterruptSlowMotionTarget.ServerInterruptSuccessCount => _cycle != null ? _cycle.SuccessCount : 0;

    /// <summary>[서버] 인터럽트 성공. 취약 진입 직후에 한 번 발화한다.</summary>
    public event Action<TrainingDummyInterruptSuccess> ServerInterruptSucceeded;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _dummy = GetComponent<TrainingDummy>();

        if (IsServer)
        {
            _cycle = new TrainingDummyInterruptCycle(vulnerableDuration, idleDuration);
            _state.Value = _cycle.State;
            ServerRefreshSuperArmor();
        }

        _state.OnValueChanged += OnStateChanged;
        ApplyTint(_state.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= OnStateChanged;
        _cycle = null;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// [서버] 인터럽트 공격 하나를 판정한다. 인터럽트 가능 상태면 성공 — 창이 닫히고 취약으로 넘어간다.
    /// 취약·idle 중이면 false(데미지는 호출측이 이미 처리했다).
    /// </summary>
    public bool ServerTryInterrupt(ulong attackerClientId, Unit sourceUnit)
    {
        if (!IsServer || _cycle == null || !_cycle.TryInterrupt())
            return false;

        _state.Value = _cycle.State;
        ServerRefreshSuperArmor();
        ServerInterruptSucceeded?.Invoke(new TrainingDummyInterruptSuccess(attackerClientId, sourceUnit));
        PlayInterruptFlashRpc();
        return true;
    }

    /// <summary>
    /// [서버] 슈퍼아머를 현재 상태에 맞춘다 — 취약이면 해제, 아니면 무기한(duration 0)으로 건다.
    /// 상태 전이마다 부르고, 상태이상을 전부 지우는 경로(<see cref="TrainingDummy"/> 자리 복귀)도 뒤에 부른다.
    /// 취약이 끝날 때 이미 걸린 CC 는 지우지 않는다 — 코어 규칙대로 자연 만료된다.
    /// </summary>
    public void ServerRefreshSuperArmor()
    {
        if (!IsServer || _cycle == null)
            return;

        StatusEffectController status = _dummy != null ? _dummy.StatusEffects : null;
        if (status == null)
            return;

        if (_cycle.AllowsCrowdControl)
            status.Remove(StatusEffectType.SuperArmor, SuperArmorSourceId);
        else if (status.GetStackCount(StatusEffectType.SuperArmor, SuperArmorSourceId) == 0)
            status.Apply(StatusEffectType.SuperArmor, 0f, SuperArmorSourceId);
    }

    void Update()
    {
        if (!IsServer || _cycle == null)
            return;

        if (_cycle.Tick(Time.deltaTime))
        {
            _state.Value = _cycle.State;
            ServerRefreshSuperArmor();
        }
    }

    void OnStateChanged(TrainingDummyInterruptState previous, TrainingDummyInterruptState current) => ApplyTint(current);

    // BossCounterTelegraph 와 같은 방식 — 베이스 틴트로 넣어야 피격 플래시가 이 색 위에서 Lerp 하고 끝나도 돌아온다.
    void ApplyTint(TrainingDummyInterruptState state)
    {
        if (_flash == null)
            _flash = GetComponent<TrainingDummyHitFlash>();
        if (_flash == null)
            return;

        if (state == TrainingDummyInterruptState.Interruptible)
            _flash.SetBaseTint(interruptibleTint);
        else if (state == TrainingDummyInterruptState.Vulnerable)
            _flash.SetBaseTint(vulnerableTint);
        else
            _flash.ClearBaseTint();
    }

    /// <summary>
    /// [전 피어] 인터럽트 성공 섬광. Unreliable — 순수 연출이고, 성공 자체는 상태 복제(보라 틴트)로 전달된다.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayInterruptFlashRpc()
    {
        if (interruptFlash != null)
            interruptFlash.PlayOnce();
    }
}
