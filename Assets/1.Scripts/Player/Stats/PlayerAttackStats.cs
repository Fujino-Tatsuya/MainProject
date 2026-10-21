using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 공격 스탯 종합(할 일 83) — 서버·클라 모든 피어에서 같은 입력으로 같은 값을 낸다.
/// 입력: 기본 공격력(xlsx → 빌드 포함, 피어마다 로컬) + 상태이상 공격 3단계(NetworkList 복제) + 등록된 <see cref="IAttackStatSource"/>.
/// 판정은 서버가 이 값으로 하고(권한 불변), 오너는 같은 값을 툴팁에 쓰고 공격 요청에 실어 서버 2차 검증을 받는다.
/// </summary>
public sealed class PlayerAttackStats
{
    private readonly Func<int> baseAttack;
    private readonly StatusEffectController statusEffects;
    private readonly List<IAttackStatSource> sources = new List<IAttackStatSource>();

    public PlayerAttackStats(Func<int> baseAttack, StatusEffectController statusEffects)
    {
        this.baseAttack = baseAttack;
        this.statusEffects = statusEffects;
        if (statusEffects != null)
            statusEffects.EffectsChanged += MarkInputChanged;
    }

    /// <summary>입력(상태이상·출처)이 마지막으로 바뀐 Time.time. 서버 검증 유예 기준.</summary>
    public float LastInputChangedTime { get; private set; } = float.NegativeInfinity;

    public float BaseAttackPercent => StatusPercent(StatusEffectType.BaseAttackDamageModifier) + SourceSum(s => s.BaseAttackPercent);
    public float TotalAttackPercent => StatusPercent(StatusEffectType.AttackDamageModifier) + SourceSum(s => s.TotalAttackPercent);
    public float DealtDamagePercent => StatusPercent(StatusEffectType.DealtDamageModifier) + SourceSum(s => s.DealtDamagePercent);

    public int FinalAttackDamage => AttackStatFormula.FinalAttack(baseAttack(), BaseAttackPercent, TotalAttackPercent);

    /// <summary>판정·툴팁 공용 피해 스냅샷 — round(최종 공격력 × 계수) + 고정값, 그 뒤 가하는 피해%.</summary>
    public int ResolveDamage(float coefficient, int flatBonus) =>
        AttackStatFormula.Snapshot(FinalAttackDamage, coefficient, flatBonus, DealtDamagePercent);

    public AttackStatReport CreateReport() =>
        new AttackStatReport(FinalAttackDamage, AttackStatFormula.ToPermille(DealtDamagePercent));

    public void Register(IAttackStatSource source)
    {
        if (source == null || sources.Contains(source))
            return;

        sources.Add(source);
        source.Changed += MarkInputChanged;
        MarkInputChanged();
    }

    public void Unregister(IAttackStatSource source)
    {
        if (source == null || !sources.Remove(source))
            return;

        source.Changed -= MarkInputChanged;
        MarkInputChanged();
    }

    /// <summary>[서버] 오너 보고와 자기 값 비교 — 다르면 경고만 남긴다. 판정 값은 바꾸지 않는다.</summary>
    public void ValidateClientReport(AttackStatReport client, ulong clientId, string request, float graceSeconds, UnityEngine.Object context)
    {
        AttackStatReport server = CreateReport();
        if (!AttackStatFormula.ShouldWarn(server, client, Time.time, LastInputChangedTime, graceSeconds))
            return;

        Debug.LogWarning($"[AttackStats] 클라 {clientId} {request} 공격 스탯 불일치 — 서버 {server} · 클라 {client}. " +
            "판정은 서버 값. 기본 공격력(xlsx)·특성 SO 가 피어마다 같은지 확인할 것.", context);
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

    private float SourceSum(Func<IAttackStatSource, float> pick)
    {
        float sum = 0f;
        for (int i = 0; i < sources.Count; i++)
            sum += pick(sources[i]);
        return sum;
    }
}
