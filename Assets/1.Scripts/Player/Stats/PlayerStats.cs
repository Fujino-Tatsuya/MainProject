using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 스탯 종합(할 일 83) — 서버·클라 모든 피어에서 같은 입력으로 같은 값을 낸다.
/// 입력: 기본값(xlsx → 빌드 포함, 피어마다 로컬) + 상태이상 기본%·전체%·최종 배율(NetworkList 복제) + 등록된 <see cref="IPlayerStatSource"/>.
/// 판정은 서버가 이 값으로 하고(권한 불변), 오너는 같은 값을 툴팁·HUD 에 쓰고 공격 요청에 실어 서버 2차 검증을 받는다.
/// </summary>
public sealed class PlayerStats
{
    /// <summary>스탯별 기본값 공급자 — Player 가 자기 직렬화 필드(xlsx 값)를 넘긴다.</summary>
    public delegate float BaseValueProvider(PlayerStatKind kind);

    private readonly BaseValueProvider baseValue;
    private readonly StatusEffectController statusEffects;
    private readonly List<IPlayerStatSource> sources = new List<IPlayerStatSource>();

    public PlayerStats(BaseValueProvider baseValue, StatusEffectController statusEffects)
    {
        this.baseValue = baseValue;
        this.statusEffects = statusEffects;
        if (statusEffects != null)
            statusEffects.EffectsChanged += MarkInputChanged;
    }

    /// <summary>입력(상태이상·출처)이 마지막으로 바뀐 Time.time. 서버 검증 유예 기준.</summary>
    public float LastInputChangedTime { get; private set; } = float.NegativeInfinity;

    public float BaseValue(PlayerStatKind kind) => baseValue(kind);

    public float BasePercent(PlayerStatKind kind)
    {
        PlayerStatFormula.StatusTypes(kind, out StatusEffectType type, out _);
        float sum = StatusPercent(type);
        for (int i = 0; i < sources.Count; i++)
            sum += sources[i].GetBasePercent(kind);
        return sum;
    }

    public float TotalPercent(PlayerStatKind kind)
    {
        PlayerStatFormula.StatusTypes(kind, out _, out StatusEffectType type);
        float sum = StatusPercent(type);
        for (int i = 0; i < sources.Count; i++)
            sum += sources[i].GetTotalPercent(kind);
        return sum;
    }

    public float DealtDamagePercent
    {
        get
        {
            float sum = StatusPercent(StatusEffectType.DealtDamageModifier);
            for (int i = 0; i < sources.Count; i++)
                sum += sources[i].DealtDamagePercent;
            return sum;
        }
    }

    /// <summary>최종 배율 상태이상의 곱(할 일 91). 최종 배율이 없는 스탯·상태이상 없음 = 1.</summary>
    public float FinalMultiplier(PlayerStatKind kind)
    {
        StatusEffectType type = PlayerStatFormula.FinalMultiplierType(kind);
        return type != StatusEffectType.None && statusEffects != null ? statusEffects.GetStatMultiplier(type) : 1f;
    }

    /// <summary>기본에 곱할 배율(최종 배율 포함) — 기본값을 따로 갖는 소비처(PlayerMovement)용.</summary>
    public float Multiplier(PlayerStatKind kind) =>
        PlayerStatFormula.Multiplier(BasePercent(kind), TotalPercent(kind), FinalMultiplier(kind));

    public float Final(PlayerStatKind kind) =>
        PlayerStatFormula.Final(baseValue(kind), BasePercent(kind), TotalPercent(kind), FinalMultiplier(kind));

    public int FinalInt(PlayerStatKind kind) =>
        PlayerStatFormula.FinalInt(Mathf.RoundToInt(baseValue(kind)), BasePercent(kind), TotalPercent(kind), FinalMultiplier(kind));

    public int FinalAttackDamage => FinalInt(PlayerStatKind.AttackDamage);
    public float FinalMoveSpeed => Final(PlayerStatKind.MoveSpeed);
    public float FinalAttackSpeed => Final(PlayerStatKind.AttackSpeed);
    public int FinalDefense => FinalInt(PlayerStatKind.Defense);
    public int FinalMaxHp => FinalInt(PlayerStatKind.MaxHp);

    /// <summary>
    /// 판정·툴팁 공용 피해 스냅샷 — round(최종 공격력 × 계수) + 고정값, 그 뒤 가하는 피해% × 특성 공격 계열 배율(할 일 125, 기본 1).
    /// 특성 배율은 <see cref="PlayerTraitModifiers.DamageMultiplier"/> 값을 호출자가 넘긴다(공격 계열을 아는 곳이 호출자라서).
    /// </summary>
    public int ResolveDamage(float coefficient, int flatBonus, float traitDamageMultiplier = 1f) =>
        PlayerStatFormula.Snapshot(FinalAttackDamage, coefficient, flatBonus, DealtDamagePercent, traitDamageMultiplier);

    public PlayerStatReport CreateReport() => new PlayerStatReport
    {
        attackDamage = FinalAttackDamage,
        moveSpeedMilli = PlayerStatFormula.ToMilli(FinalMoveSpeed),
        attackSpeedMilli = PlayerStatFormula.ToMilli(FinalAttackSpeed),
        defense = FinalDefense,
        maxHp = FinalMaxHp,
        dealtDamageMilli = PlayerStatFormula.ToMilli(DealtDamagePercent),
    };

    public void Register(IPlayerStatSource source)
    {
        if (source == null || sources.Contains(source))
            return;

        sources.Add(source);
        source.Changed += MarkInputChanged;
        MarkInputChanged();
    }

    public void Unregister(IPlayerStatSource source)
    {
        if (source == null || !sources.Remove(source))
            return;

        source.Changed -= MarkInputChanged;
        MarkInputChanged();
    }

    /// <summary>[서버] 오너 보고와 자기 값 비교 — 다르면 경고만 남긴다. 판정 값은 바꾸지 않는다.</summary>
    public void ValidateClientReport(PlayerStatReport client, ulong clientId, string request, float graceSeconds, UnityEngine.Object context)
    {
        PlayerStatReport server = CreateReport();
        if (!PlayerStatFormula.ShouldWarn(server, client, Time.time, LastInputChangedTime, graceSeconds))
            return;

        Debug.LogWarning($"[PlayerStats] 클라 {clientId} {request} 스탯 불일치 — 서버 {server} · 클라 {client}. " +
            "판정은 서버 값. 기본값(xlsx)·특성 SO 가 피어마다 같은지 확인할 것.", context);
    }

    public void Dispose()
    {
        if (statusEffects != null)
            statusEffects.EffectsChanged -= MarkInputChanged;

        for (int i = 0; i < sources.Count; i++)
            sources[i].Changed -= MarkInputChanged;
        sources.Clear();
    }

    private void MarkInputChanged() => LastInputChangedTime = Time.time;

    private float StatusPercent(StatusEffectType type) =>
        statusEffects != null ? statusEffects.GetStatPercentSum(type) : 0f;
}
