using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 불굴의 의지 — 근접 캐릭터 자동 패시브 (슬롯 스킬 아님, 서버 권위 독립 컴포넌트). 버프 모델(PLAN-passive-onhit.md).
///
/// ① 쿨타임이 끝나면 <see cref="StatusEffectType.PassiveCharge"/> 버프가 생긴다(스폰 직후에도 보유로 시작).
/// ② 버프 보유 중 <b>적중 시 발동 공격</b>(기본 = 평타)이 첫 Unit 대상을 때리기 직전
///    버프를 소모하고 그 한 방에 추가 피해(최종공격력 × 계수 + 고정)를 합산하며, 자신은 최대체력 healPercent% 회복.
/// ③ 소모되면 쿨타임(기본 30초)이 시작된다. 쿨타임 중 피격당할 때마다 고정 감소(데미지량 무관).
///
/// 버프는 <see cref="StatusEffectController"/> 가 들고 전 피어에 복제한다 — 칼날 발광은 이걸 본다.
/// 쿨타임은 플레이어 쿨 시계(<see cref="PlayerCooldownClock"/> — 쿨 속도 배율 반영) 위의 끝 시각(readyAt)이다.
/// 피어마다 시계 원점이 달라 서버는 "남은 초 + 적은 서버 시각"(<see cref="PassiveCooldownStamp"/>)을 복제하고,
/// 각 피어가 자기 시계의 끝 시각으로 바꿔 읽는다(오너 HUD fill·머리 위 특성 게이지). 할 일 91 T4.
///
/// 🔴 버프 부여는 서버 Update 가 "쿨타임 끝 + 버프 없음" 을 볼 때마다 한다. 이 한 줄이 스폰 직후 부여와,
///    보스 연출이 상태이상을 걷어낸 뒤(연출 중엔 Apply 거부) 다시 붙는 것까지 같이 처리한다.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(StatusEffectController))]
[DataTableSheet("Paladin", Order = 1)]
public class FirstMeleePassive : BaseNetworkBehaviour, IPlayerPassive, IPlayerOnHitBonus, ISkillTooltipSource
{
    [Header("툴팁")]
    [SerializeField] private SkillTooltipText tooltip;

    [Header("쿨다운")]
    [SerializeField, Min(0f)] private float cooldownTime = 30f;
    // 피격 1회당 감소(초). 데미지량 무관. 쿨타임 중에만 의미가 있다.
    [SerializeField, Min(0f)] private float hitCooldownReduction = 2f;

    [Header("발동 - 추가 피해 (처음 맞은 한 명)")]
    // 추가 피해 = 최종공격력 × 계수 + 고정 보너스
    [SerializeField, Min(0f)] private float bonusDamageMultiplier = 1f;
    [SerializeField, Min(0)] private int bonusFlatDamage = 0;

    [Header("발동 - 체력 회복")]
    // 발동 1회당 자신 최대체력의 %.
    [FormerlySerializedAs("minHealPercent")]
    [SerializeField, Min(0)] private int healPercent = 5;

    // 칼날 발광·발동 연출(추가 피해·자기 힐)의 칸과 RPC 는 팔라딘 연출 창구가 든다(2026-11-02).
    [Header("연출")]
    [Tooltip("팔라딘 연출 창구(칼날 발광·발동 연출). 플레이어 루트의 PaladinSkillView 를 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private PaladinSkillView skillView;

    // 쿨 복제값(남은 초 + 적은 서버 시각). 서버만 쓰고 전 피어가 읽는다 — 원격 플레이어의 머리 위 특성 게이지도 그린다.
    // 서버가 끝 시각을 바꿀 때(소모·피격 감소·스폰)만 다시 적는다.
    private readonly NetworkVariable<PassiveCooldownStamp> cooldownStamp = new NetworkVariable<PassiveCooldownStamp>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Player owner;
    private StatusEffectController statusEffects;
    private PlayerCooldownClock cooldownClock;
    // 쿨이 끝나는 이 피어 쿨 시계 시각. 서버 = 원본, 다른 피어 = 복제값을 환산한 값. 버프 보유 중에는 과거 시각이다.
    private double readyAt;
    // 칼날 발광에 마지막으로 반영한 버프 상태. null = 아직 한 번도 반영 안 함(늦게 접속한 클라 포함 첫 프레임에 맞춘다).
    private bool? appliedGlow;

    private bool HasGameplayAuthority => IsNetworkActive && IsServer;
    // 복제값 도장용 서버 시각. 쿨 판정은 이 값이 아니라 쿨 시계(ClockNow)로 한다.
    private double ServerNow => IsNetworkActive && NetworkManager != null
        ? NetworkManager.ServerTime.Time
        : Time.timeAsDouble;
    private double ClockNow => cooldownClock != null ? cooldownClock.SkillNow : Time.timeAsDouble;
    private ulong ChargeSourceId => NetworkObjectId;

    /// <summary>버프 보유 여부. 상태이상 목록이 복제되므로 전 피어에서 유효.</summary>
    // 부여·소모와 같은 키(type, sourceId)로 본다 — 타입만 보면 다른 출처의 PassiveCharge 를 자기 것으로 오인해
    // Remove 는 실패한 채 발동만 반복한다.
    private bool HasCharge => statusEffects != null && statusEffects.GetStackCount(StatusEffectType.PassiveCharge, ChargeSourceId) > 0;

    public float CooldownTime => cooldownTime;
    public bool IsReady => HasCharge;
    public float RemainingCooldown =>
        !HasCharge ? PassiveCooldownSync.Remaining(readyAt, ClockNow) : 0f;
    public SkillTooltipText Tooltip => tooltip.Keyed(this, nameof(tooltip));
    public UnityEngine.Object TooltipValueSource => this;

    public SkillTooltipDamage GetTooltipDamage(Player player) =>
        SkillTooltipDamage.FromLinear(player, bonusDamageMultiplier, bonusFlatDamage);

    private void Awake()
    {
        owner = GetComponent<Player>();
        statusEffects = GetComponent<StatusEffectController>();
        cooldownClock = PlayerCooldownClock.For(this);
    }

    private void OnEnable()
    {
        if (owner == null)
            return;

        owner.ServerAttackReceived += HandleAttackReceived;
    }

    private void OnDisable()
    {
        if (owner == null)
            return;

        owner.ServerAttackReceived -= HandleAttackReceived;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        cooldownStamp.OnValueChanged += HandleCooldownStampChanged;

        // 버프 보유로 시작 — 쿨타임을 "이미 끝남" 으로 두면 아래 Update 가 첫 틱에 버프를 건다.
        if (HasGameplayAuthority)
            SetReadyAtServer(ClockNow);
        else
            AdoptCooldownStamp(cooldownStamp.Value);

        appliedGlow = null;
    }

    public override void OnNetworkDespawn()
    {
        cooldownStamp.OnValueChanged -= HandleCooldownStampChanged;
        base.OnNetworkDespawn();
    }

    // [서버] 끝 시각을 바꾸고 복제값을 다시 적는다.
    private void SetReadyAtServer(double value)
    {
        readyAt = value;
        cooldownStamp.Value = PassiveCooldownSync.Stamp(readyAt, ClockNow, ServerNow);
    }

    private void HandleCooldownStampChanged(PassiveCooldownStamp previous, PassiveCooldownStamp current)
    {
        if (!HasGameplayAuthority)
            AdoptCooldownStamp(current);
    }

    // [서버 외 피어] 복제값을 이 피어 쿨 시계의 끝 시각으로 바꾼다.
    private void AdoptCooldownStamp(PassiveCooldownStamp stamp)
    {
        float rate = cooldownClock != null ? cooldownClock.Rate : 1f;
        readyAt = PassiveCooldownSync.LocalReadyAt(stamp, ClockNow, ServerNow, rate);
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        // [서버] 쿨타임이 끝났는데 버프가 없으면 건다. 연출 잠금 중이면 Apply 가 거부되고, 잠금이 풀린 뒤 다시 들어온다.
        if (HasGameplayAuthority && !HasCharge && ClockNow >= readyAt)
            statusEffects.Apply(StatusEffectType.PassiveCharge, 1f, 0f, ChargeSourceId);

        // [전 피어] 칼날 발광 = 버프 보유. 버프가 복제되니 따로 동기화하지 않는다.
        bool charged = HasCharge;
        if (appliedGlow != charged)
        {
            appliedGlow = charged;
            skillView?.SetBladeGlow(charged);
        }
    }

    // ── 서버 판정 ───────────────────────────────────────────────────

    // 첫 Unit 대상에게 기본 피해를 넣기 직전 호출된다. 소모 상태와 부수효과를 여기서 확정하고
    // 반환값만 원래 공격 피해에 합산한다 — 별도 추가타를 만들지 않는다.
    public int ServerConsumeOnHitBonus(Unit target)
    {
        if (!HasGameplayAuthority || !HasCharge || target == null || target == owner || target.CurrentHealth <= 0)
            return 0;

        // 소모 → 쿨타임 시작을 먼저 확정한다. 실제 피해 적용은 이 메서드가 반환한 뒤 이어진다.
        statusEffects.Remove(StatusEffectType.PassiveCharge, ChargeSourceId);
        SetReadyAtServer(ClockNow + cooldownTime);

        // 특성 사건(할 일 126 — 불굴의 순환 Q·E 중 긴 쪽 −1초). 발동 1회 = 사건 1회(시전 ID 0 = 매번 새 발동).
        // 아래 회복·초과 전달·보호막은 이 사건을 다시 내지 않는다(명세 — 추가 패시브 발동 아님).
        PlayerTraitEffects.For(owner).RaiseServer(
            new TraitTriggerEvent(TraitTrigger.PassiveProc, AttackType.None, TraitAttackForm.Normal, 0, false));

        // 패시브 추가피해 = 공격 계열 미지정(AttackType.None) 키 — 평타·Q 등 계열을 고른 배율은 붙지 않고 '모든 공격' 배율만 붙는다(할 일 125).
        int bonusDamage = owner.ResolveDamage(bonusDamageMultiplier, bonusFlatDamage, new TraitAttackKey(AttackType.None));

        // 특성 회복 경로(할 일 125 3/5) — 특성 없으면 회복 배율 1·전달 없음이라 예전 HealHp 와 같다.
        int healAmount = Mathf.RoundToInt(owner.MaxHp * (healPercent / 100f));
        int healed = healAmount > 0 ? PlayerTraitEffects.For(owner).HealServer(healAmount) : 0;

        // 발동 연출(대상 추가 피해 + 실제로 회복했을 때만 자기 힐)은 창구가 전 피어에 뿌린다.
        skillView?.ServerPlayPassiveProc(target, healed > 0);

        Edit.Log($"[Passive] 불굴의 의지 발동 — {target.name}의 원래 공격에 추가피해 {bonusDamage} 합산, 회복 {healPercent}%({healAmount})", this);
        return bonusDamage;
    }

    // 내가 공격을 받았다. 쿨타임 중이면 데미지량과 무관하게 끝나는 시각을 앞당긴다.
    private void HandleAttackReceived(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (!HasGameplayAuthority)
            return;

        double now = ClockNow;
        if (readyAt > now)
            SetReadyAtServer(PassiveCooldownSync.Reduce(readyAt, now, hitCooldownReduction));
    }
}
