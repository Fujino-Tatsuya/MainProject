using System;
using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 불굴의 의지 — 근접 캐릭터 자동 패시브 (슬롯 스킬 아님, 서버 권위 독립 컴포넌트).
///
/// 쿨다운(기본 30초)을 ① 시간 경과 + ② 내가 피격당할 때마다 고정 감소(데미지량 무관) 둘 다로 깎는다.
/// 쿨다운이 0이 되면 Ready. Ready 상태에서 내 기본공격이 적에게 명중하면 발동한다:
///  - 이번 스윙에 맞은 적 전원에게 추가 피해(최종공격력 × 계수 + 고정 보너스)
///  - 맞은 적 수 N 기준 체력 1회 회복 (N ≤ 최소타겟 → 최소%, 초과 → N × 타겟당%)
///  - 쿨다운 리셋
/// 허공 스윙(맞은 적 0)은 발동하지 않고 Ready를 유지한다.
///
/// 쿨다운은 "Ready가 되는 서버 시각(readyServerTime)"으로 표현한다. 시간 감소는 시각 비교로 자연 처리되고,
/// 피격/발동 같은 이산 이벤트에서만 이 시각을 갱신·복제한다(오너만 읽기 — 검 오라 VFX / HUD fill 바인딩).
/// 게임은 항상 온라인(리슨서버)이라 서버(호스트)에서 로직이 돈다. 데미지/힐은 기존 Unit NetworkVariable로 동기화.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerDefaultAttack))]
public class FirstMeleePassive : BaseNetworkBehaviour
{
    [Header("쿨다운")]
    [SerializeField, Min(0f)] private float cooldownTime = 30f;
    // 피격 1회당 감소(초). 데미지량 무관.
    [SerializeField, Min(0f)] private float hitCooldownReduction = 2f;

    [Header("발동 - 추가 피해")]
    // 추가 피해 = 최종공격력 × 계수 + 고정 보너스
    [SerializeField, Min(0f)] private float bonusDamageMultiplier = 1f;
    [SerializeField, Min(0)] private int bonusFlatDamage = 0;

    [Header("연출")]
    [Tooltip("Ready 상태에서 칼날을 빛낸다. 칼의 MaterialFadeEffect 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private MaterialFadeEffect bladeGlow;

    [Tooltip("발동 순간 시전자에게 한 번 터뜨릴 회복 연출. 프리팹의 'VFX/PassiveSkill/Heal' 을 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer healBurst;

    [Tooltip("추가 피해를 맞은 대상마다 한 번 터뜨릴 연출. FX_Additional_Hit_Entry 를 물린다. 소켓이 아니라 엔트리인 이유는 붙을 자리가 '그때 맞은 남'이라서다")]
    [SerializeField] private EffectEntry additionalHit;

    [Tooltip("추가 피해 연출 배율")]
    [SerializeField, Min(0.01f)] private float additionalHitScale = 1f;

    [Tooltip("대상에게서 몸통 중심을 못 찾았을 때 쓸 발밑 기준 오프셋(미터)")]
    [SerializeField] private Vector3 additionalHitFallbackOffset = new Vector3(0f, 1f, 0f);

    [Header("발동 - 체력 회복(%)")]
    // 맞은 적 수가 이 값 이하이면 최소 회복%, 초과이면 (적 수 × 타겟당 회복%)
    [SerializeField, Min(1)] private int minTargetThreshold = 5;
    [SerializeField, Min(0)] private int minHealPercent = 5;
    [SerializeField, Min(0)] private int perTargetHealPercent = 1;

    // Ready가 되는 서버 시각(GameTime). 서버만 쓰고 오너만 읽는다. VFX/HUD가 이 값으로 fill·Ready를 계산.
    private readonly NetworkVariable<double> readyServerTime = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Ready 여부를 <b>모든 피어에</b> 복제한다 — 칼날 발광처럼 남들도 봐야 하는 연출용.
    ///
    /// <b>왜 위의 <see cref="readyServerTime"/>을 넓히지 않았나.</b> 그 값은 피격마다 갱신돼서
    /// 권한을 Everyone으로 열면 <b>맞을 때마다</b> double이 전원에게 복제된다. 반면 이 bool은
    /// <b>Ready가 실제로 뒤집힐 때만</b> 바뀌므로 쿨다운 주기당 두 번이면 끝난다.
    ///
    /// <b>왜 RPC가 아닌가.</b> 켜짐/꺼짐은 이벤트가 아니라 <b>상태</b>다. NetworkVariable로 두면
    /// 늦게 접속한 클라가 현재 값을 자동으로 받고(RPC는 이미 지나간 것을 못 받는다),
    /// 한 발 유실돼도 상태가 수렴한다.
    /// </summary>
    private readonly NetworkVariable<bool> readyReplicated = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Player owner;
    private PlayerDefaultAttack defaultAttack;
    private bool lastReadyState;

    // 이번 발동에서 실제로 맞은 적. 매 발동마다 재사용해 할당을 없앤다.
    private readonly List<Unit> procTargets = new List<Unit>();

    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;
    private double ServerNow => IsNetworkActive && NetworkManager != null
        ? NetworkManager.ServerTime.Time
        : Time.timeAsDouble;
    // 오너/서버만 readyServerTime을 읽을 수 있다(권한). 그 외에는 판정 대상이 아니다.
    private bool CanReadState => IsOwner || IsServer;

    public float CooldownTime => cooldownTime;
    /// <summary>Ready 여부. 오너(HUD/VFX)·서버에서만 유효.</summary>
    public bool IsReady => CanReadState && ServerNow >= readyServerTime.Value;
    /// <summary>남은 쿨다운(초). HUD fill용. 오너·서버에서만 유효.</summary>
    public float RemainingCooldown => CanReadState ? Mathf.Max(0f, (float)(readyServerTime.Value - ServerNow)) : 0f;
    /// <summary>오너에서 Ready 전환 시 발생. 검 오라 VFX 토글 등에 구독.</summary>
    public event Action<bool> ReadyChanged;

    private void Awake()
    {
        owner = GetComponent<Player>();
        defaultAttack = GetComponent<PlayerDefaultAttack>();
    }

    private void OnEnable()
    {
        if (defaultAttack != null)
            defaultAttack.ServerHitEnemiesResolved += HandleHitEnemiesResolved;
    }

    private void OnDisable()
    {
        if (defaultAttack != null)
            defaultAttack.ServerHitEnemiesResolved -= HandleHitEnemiesResolved;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 서버가 초기 쿨다운을 건다(스폰/씬 진입 시 리셋). 오프라인 폴백 포함.
        if (HasGameplayAuthority)
        {
            readyServerTime.Value = ServerNow + cooldownTime;
            readyReplicated.Value = false;
        }

        lastReadyState = IsReady;

        // 늦게 접속한 클라는 이미 복제된 현재 값을 받는다 — 여기서 한 번 반영하면 상태가 맞는다.
        readyReplicated.OnValueChanged += HandleReadyReplicated;
        ApplyBladeGlow(readyReplicated.Value);
    }

    public override void OnNetworkDespawn()
    {
        readyReplicated.OnValueChanged -= HandleReadyReplicated;
        base.OnNetworkDespawn();
    }

    private void HandleReadyReplicated(bool previous, bool next) => ApplyBladeGlow(next);

    private void Update()
    {
        // Ready 전이 감지는 오너에서만 (VFX/HUD 소비자). 시간 기반이라 NetworkVariable OnValueChanged로는 못 잡음.
        if (!IsSpawned || !CanReadState)
            return;

        bool ready = IsReady;
        if (ready != lastReadyState)
        {
            lastReadyState = ready;

            // 연출은 여기서 직접 켜지 않는다 — 서버가 복제한 값(readyReplicated)이 전 피어를 한 경로로 몬다.
            // 여기는 오너 HUD 용 즉시 신호다(복제 지연 없이 자기 화면에 바로 반영).
            if (HasGameplayAuthority)
                readyReplicated.Value = ready;

            ReadyChanged?.Invoke(ready);
        }
    }

    // Ready 가 되면 칼날이 켜지고, 발동해서 쿨다운이 돌면 꺼진다.
    // FadeIn/FadeOut 은 이미 그 상태면 조용한 no-op 이라 중복 호출을 걱정하지 않아도 된다.
    private void ApplyBladeGlow(bool ready)
    {
        if (bladeGlow == null)
            return;

        if (ready) bladeGlow.FadeIn();
        else bladeGlow.FadeOut();
    }

    // ── 발동 연출 ───────────────────────────────────────────────────

    /// <summary>
    /// [서버] 발동 순간의 연출을 전 피어에 뿌린다 — 시전자 회복 1회 + 맞은 적마다 추가 피해 1회.
    ///
    /// <b>창구가 필요 없다.</b> 이 컴포넌트 자체가 NetworkBehaviour라 RPC를 직접 단다
    /// (스킬들이 PlayerSkillVfx를 거치는 것은 부모 PlayerSkillBase가 MonoBehaviour이기 때문이다).
    ///
    /// <b>좌표가 아니라 대상을 보낸다.</b> 클라의 몹은 NetworkTransform 보간 때문에 서버보다 뒤에
    /// 그려진다 — 서버가 잰 월드 좌표를 그대로 재생하면 이펙트가 몸에서 떨어져 허공에 뜬다
    /// (MonsterBase.PlayHitVFXRpc가 공격자 위치 하나만 보내는 것과 같은 이유다).
    /// 각 피어가 자기 쪽 몹에서 위치를 다시 뽑으면 언제나 그 몹 위다.
    /// </summary>
    private void ServerPlayProcVfx()
    {
        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive)
        {
            healBurst?.PlayOnce();
            for (int i = 0; i < procTargets.Count; i++)
                PlayAdditionalHitLocal(procTargets[i] != null ? procTargets[i].transform : null);
            return;
        }

        if (!IsServer) return;

        var targets = new NetworkObjectReference[procTargets.Count];
        int count = 0;
        for (int i = 0; i < procTargets.Count; i++)
        {
            Unit enemy = procTargets[i];
            if (enemy == null || enemy.NetworkObject == null || !enemy.NetworkObject.IsSpawned) continue;
            targets[count++] = enemy.NetworkObject;
        }

        if (count != targets.Length) System.Array.Resize(ref targets, count);

        ProcVfxRpc(targets);
    }

    // 쿨다운 주기당 한 번뿐인 클라이맥스라 Reliable 이다 — 유실되면 발동했다는 사실이 통째로 안 보인다.
    // (난타 중 한 발씩 빠져도 되는 피격 파문 계열과 갈리는 지점)
    [Rpc(SendTo.ClientsAndHost)]
    private void ProcVfxRpc(NetworkObjectReference[] targets)
    {
        healBurst?.PlayOnce();

        if (targets == null) return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i].TryGet(out NetworkObject targetObject))
                PlayAdditionalHitLocal(targetObject.transform);
        }
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

    /// <summary>내가 피격당했을 때 호출(Player.ReceiveAttack). 데미지량 무관하게 Ready 시각을 앞당긴다.</summary>
    public void NotifyOwnerHit()
    {
        if (!IsServer)
            return;

        double now = ServerNow;
        if (readyServerTime.Value > now)
            readyServerTime.Value = Math.Max(now, readyServerTime.Value - hitCooldownReduction);
    }

    // 기본공격 스윙이 적을 명중시킨 뒤(서버) 호출된다. Ready면 발동.
    private void HandleHitEnemiesResolved(IReadOnlyList<Unit> enemies)
    {
        if (!HasGameplayAuthority || enemies == null || enemies.Count == 0)
            return;

        if (ServerNow < readyServerTime.Value)
            return; // 아직 쿨다운

        int bonusDamage = Mathf.Max(0,
            Mathf.RoundToInt(owner.FinalAttackDamage * bonusDamageMultiplier) + bonusFlatDamage);
        AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform);

        procTargets.Clear();

        for (int i = 0; i < enemies.Count; i++)
        {
            Unit enemy = enemies[i];
            if (enemy == null || enemy == owner)
                continue;

            enemy.ReceiveAttack(new AttackInfo(bonusDamage, AttackType.Default), hitContext);
            procTargets.Add(enemy);
        }

        int hitCount = procTargets.Count;
        if (hitCount == 0)
            return;

        ServerPlayProcVfx();

        int healPercent = hitCount <= minTargetThreshold
            ? minHealPercent
            : hitCount * perTargetHealPercent;
        int healAmount = Mathf.RoundToInt(owner.MaxHp * (healPercent / 100f));
        if (healAmount > 0)
            owner.HealHp(healAmount);

        readyServerTime.Value = ServerNow + cooldownTime; // 쿨다운 리셋

        Edit.Log(
            $"[Passive] 불굴의 의지 발동 — 적 {hitCount}에 추가피해 {bonusDamage}, 회복 {healPercent}%({healAmount})",
            this);
    }
}
