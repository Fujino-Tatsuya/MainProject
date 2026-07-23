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

    [Header("발동 - 체력 회복(%)")]
    // 맞은 적 수가 이 값 이하이면 최소 회복%, 초과이면 (적 수 × 타겟당 회복%)
    [SerializeField, Min(1)] private int minTargetThreshold = 5;
    [SerializeField, Min(0)] private int minHealPercent = 5;
    [SerializeField, Min(0)] private int perTargetHealPercent = 1;

    // Ready가 되는 서버 시각(GameTime). 서버만 쓰고 오너만 읽는다. VFX/HUD가 이 값으로 fill·Ready를 계산.
    private readonly NetworkVariable<double> readyServerTime = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

    private Player owner;
    private PlayerDefaultAttack defaultAttack;
    private bool lastReadyState;

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
            readyServerTime.Value = ServerNow + cooldownTime;

        lastReadyState = IsReady;
    }

    private void Update()
    {
        // Ready 전이 감지는 오너에서만 (VFX/HUD 소비자). 시간 기반이라 NetworkVariable OnValueChanged로는 못 잡음.
        if (!IsSpawned || !CanReadState)
            return;

        bool ready = IsReady;
        if (ready != lastReadyState)
        {
            lastReadyState = ready;
            ReadyChanged?.Invoke(ready);
        }
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

        int hitCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            Unit enemy = enemies[i];
            if (enemy == null || enemy == owner)
                continue;

            enemy.ReceiveAttack(new AttackInfo(bonusDamage, AttackType.Default), hitContext);
            hitCount++;
        }

        if (hitCount == 0)
            return;

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
