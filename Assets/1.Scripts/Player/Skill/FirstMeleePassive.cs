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
/// 쿨타임은 "끝나는 서버 시각(readyServerTime)" 하나로 표현하고 전 피어가 읽는다(오너 HUD fill·머리 위 특성 게이지).
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

    [Header("연출")]
    [Tooltip("버프 보유 중 칼날을 빛낸다. 칼의 MaterialFadeEffect 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private MaterialFadeEffect bladeGlow;

    [Tooltip("발동 순간 시전자에게 한 번 터뜨릴 회복 연출. 프리팹의 'VFX/PassiveSkill/Heal' 을 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer healBurst;

    [Tooltip("추가 피해를 맞은 대상에게 한 번 터뜨릴 연출. FX_Additional_Hit_Entry 를 물린다. 소켓이 아니라 엔트리인 이유는 붙을 자리가 '그때 맞은 남'이라서다")]
    [SerializeField] private EffectEntry additionalHit;

    [Tooltip("추가 피해 연출 배율")]
    [DataTableIgnore] [SerializeField, Min(0.01f)] private float additionalHitScale = 1f;

    [Tooltip("대상에게서 몸통 중심을 못 찾았을 때 쓸 발밑 기준 오프셋(미터)")]
    [SerializeField] private Vector3 additionalHitFallbackOffset = new Vector3(0f, 1f, 0f);

    // 쿨타임이 끝나는 서버 시각(ServerTime). 서버만 쓰고 전 피어가 읽는다 — 원격 플레이어의 머리 위 특성 게이지도 그린다.
    // 버프 보유 중에는 과거 시각이다. 클라도 같은 NetworkManager.ServerTime 으로 남은 시간을 계산한다.
    private readonly NetworkVariable<double> readyServerTime = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Player owner;
    private StatusEffectController statusEffects;
    // 칼날 발광에 마지막으로 반영한 버프 상태. null = 아직 한 번도 반영 안 함(늦게 접속한 클라 포함 첫 프레임에 맞춘다).
    private bool? appliedGlow;

    private bool HasGameplayAuthority => IsNetworkActive && IsServer;
    private double ServerNow => IsNetworkActive && NetworkManager != null
        ? NetworkManager.ServerTime.Time
        : Time.timeAsDouble;
    private ulong ChargeSourceId => NetworkObjectId;

    /// <summary>버프 보유 여부. 상태이상 목록이 복제되므로 전 피어에서 유효.</summary>
    // 부여·소모와 같은 키(type, sourceId)로 본다 — 타입만 보면 다른 출처의 PassiveCharge 를 자기 것으로 오인해
    // Remove 는 실패한 채 발동만 반복한다.
    private bool HasCharge => statusEffects != null && statusEffects.GetStackCount(StatusEffectType.PassiveCharge, ChargeSourceId) > 0;

    public float CooldownTime => cooldownTime;
    public bool IsReady => HasCharge;
    public float RemainingCooldown =>
        !HasCharge ? Mathf.Max(0f, (float)(readyServerTime.Value - ServerNow)) : 0f;
    public SkillTooltipText Tooltip => tooltip;
    public UnityEngine.Object TooltipValueSource => this;

    public SkillTooltipDamage GetTooltipDamage(Player player) =>
        SkillTooltipDamage.FromLinear(player, bonusDamageMultiplier, bonusFlatDamage);

    private void Awake()
    {
        owner = GetComponent<Player>();
        statusEffects = GetComponent<StatusEffectController>();
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

        // 버프 보유로 시작 — 쿨타임을 "이미 끝남" 으로 두면 아래 Update 가 첫 틱에 버프를 건다.
        if (HasGameplayAuthority)
            readyServerTime.Value = ServerNow;

        appliedGlow = null;
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        // [서버] 쿨타임이 끝났는데 버프가 없으면 건다. 연출 잠금 중이면 Apply 가 거부되고, 잠금이 풀린 뒤 다시 들어온다.
        if (HasGameplayAuthority && !HasCharge && ServerNow >= readyServerTime.Value)
            statusEffects.Apply(StatusEffectType.PassiveCharge, 1f, 0f, ChargeSourceId);

        // [전 피어] 칼날 발광 = 버프 보유. 버프가 복제되니 따로 동기화하지 않는다.
        bool charged = HasCharge;
        if (appliedGlow != charged)
        {
            appliedGlow = charged;
            ApplyBladeGlow(charged);
        }
    }

    // FadeIn/FadeOut 은 이미 그 상태면 조용한 no-op 이라 중복 호출을 걱정하지 않아도 된다.
    private void ApplyBladeGlow(bool charged)
    {
        if (bladeGlow == null)
            return;

        if (charged) bladeGlow.FadeIn();
        else bladeGlow.FadeOut();
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
        readyServerTime.Value = ServerNow + cooldownTime;

        int bonusDamage = Mathf.Max(0,
            Mathf.RoundToInt(owner.FinalAttackDamage * bonusDamageMultiplier) + bonusFlatDamage);

        int healAmount = Mathf.RoundToInt(owner.MaxHp * (healPercent / 100f));
        if (healAmount > 0)
            owner.HealHp(healAmount);

        ServerPlayProcVfx(target);

        Edit.Log($"[Passive] 불굴의 의지 발동 — {target.name}의 원래 공격에 추가피해 {bonusDamage} 합산, 회복 {healPercent}%({healAmount})", this);
        return bonusDamage;
    }

    // 내가 공격을 받았다. 쿨타임 중이면 데미지량과 무관하게 끝나는 시각을 앞당긴다.
    private void HandleAttackReceived(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (!HasGameplayAuthority)
            return;

        double now = ServerNow;
        if (readyServerTime.Value > now)
            readyServerTime.Value = Math.Max(now, readyServerTime.Value - hitCooldownReduction);
    }

    // ── 발동 연출 ───────────────────────────────────────────────────

    /// <summary>
    /// [서버] 발동 순간의 연출을 전 피어에 뿌린다 — 시전자 회복 1회 + 대상에게 추가 피해 1회.
    ///
    /// <b>창구가 필요 없다.</b> 이 컴포넌트 자체가 NetworkBehaviour라 RPC를 직접 단다
    /// (스킬들이 PlayerSkillVfx를 거치는 것은 부모 PlayerSkillBase가 MonoBehaviour이기 때문이다).
    ///
    /// <b>좌표가 아니라 대상을 보낸다.</b> 클라의 몹은 NetworkTransform 보간 때문에 서버보다 뒤에
    /// 그려진다 — 서버가 잰 월드 좌표를 그대로 재생하면 이펙트가 몸에서 떨어져 허공에 뜬다
    /// (MonsterBase.PlayHitVFXRpc가 공격자 위치 하나만 보내는 것과 같은 이유다).
    /// 각 피어가 자기 쪽 몹에서 위치를 다시 뽑으면 언제나 그 몹 위다.
    /// </summary>
    private void ServerPlayProcVfx(Unit target)
    {
        NetworkObjectReference targetRef = default;
        bool hasTarget = target != null && target.NetworkObject != null && target.NetworkObject.IsSpawned;
        if (hasTarget)
            targetRef = target.NetworkObject;

        ProcVfxRpc(targetRef, hasTarget);
    }

    // 쿨다운 주기당 한 번뿐인 클라이맥스라 Reliable 이다 — 유실되면 발동했다는 사실이 통째로 안 보인다.
    // (난타 중 한 발씩 빠져도 되는 피격 파문 계열과 갈리는 지점)
    [Rpc(SendTo.ClientsAndHost)]
    private void ProcVfxRpc(NetworkObjectReference target, bool hasTarget)
    {
        healBurst?.PlayOnce();

        if (hasTarget && target.TryGet(out NetworkObject targetObject))
            PlayAdditionalHitLocal(targetObject.transform);
    }

    /// <summary>
    /// 대상 한 명에게 추가 피해 연출을 찍는다.
    ///
    /// 타격점이 아니라 <b>몸통 중심</b>에 둔다. 접촉점은 기본 피격 이펙트가 이미 쓰고 있어서,
    /// 같은 자리에 겹치면 "한 대 더"가 아니라 "한 대가 밝아졌다"로 읽힌다.
    /// 중심은 SkinnedMeshRenderer의 월드 바운즈에서 뽑는다 — 몹 덩치에 맞춰 알아서 따라간다.
    /// </summary>
    private void PlayAdditionalHitLocal(Transform target)
    {
        if (target == null || additionalHit == null || EffectManager.Instance == null) return;

        SkinnedMeshRenderer body = target.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3 position = body != null
            ? body.bounds.center
            : target.position + additionalHitFallbackOffset;

        EffectManager.Instance.Play(additionalHit, position, Quaternion.identity, additionalHitScale);
    }
}
