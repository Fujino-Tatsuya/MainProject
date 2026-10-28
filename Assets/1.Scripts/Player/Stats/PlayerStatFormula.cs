using Unity.Netcode;
using UnityEngine;

/// <summary>플레이어 스탯 종류(할 일 83). 새 스탯 = 여기 한 줄 + <see cref="PlayerStatFormula.StatusTypes"/> 한 줄.</summary>
public enum PlayerStatKind
{
    AttackDamage,
    MoveSpeed,
    AttackSpeed,
    Defense,
    MaxHp,
}

/// <summary>
/// 플레이어 스탯 공식(할 일 83) — 서버 판정·클라 표시(툴팁·HUD)·서버 2차 검증이 전부 이 한 곳을 쓴다.
///   최종 스탯   = 기본 × (1 + Σ기본%) × (1 + Σ전체%) × Π최종배율   (정수 스탯은 반올림), 0 이상
///   피해 스냅샷 = round( (round(최종 공격력 × 계수) + 고정값) × (1 + Σ가하는피해%) ), 0 이상
/// 같은 단계 안의 % 는 더하고, 최종 배율(할 일 91 — 이동속도·공격속도만)끼리는 곱한다. 상세: Docs/tech/player-stats.md
/// </summary>
public static class PlayerStatFormula
{
    /// <summary>스탯 → 최종 배율 상태이상. 최종 배율이 없는 스탯은 None.</summary>
    public static StatusEffectType FinalMultiplierType(PlayerStatKind kind)
    {
        switch (kind)
        {
            case PlayerStatKind.MoveSpeed: return StatusEffectType.MoveSpeedFinalMultiplier;
            case PlayerStatKind.AttackSpeed: return StatusEffectType.AttackSpeedFinalMultiplier;
            default: return StatusEffectType.None;
        }
    }

    /// <summary>스탯 → (기본% 상태이상, 전체% 상태이상).</summary>
    public static void StatusTypes(PlayerStatKind kind, out StatusEffectType basePercent, out StatusEffectType totalPercent)
    {
        switch (kind)
        {
            case PlayerStatKind.AttackDamage:
                basePercent = StatusEffectType.BaseAttackDamageModifier; totalPercent = StatusEffectType.AttackDamageModifier; return;
            case PlayerStatKind.MoveSpeed:
                basePercent = StatusEffectType.MoveSpeedBaseModifier; totalPercent = StatusEffectType.MoveSpeedModifier; return;
            case PlayerStatKind.AttackSpeed:
                basePercent = StatusEffectType.AttackSpeedBaseModifier; totalPercent = StatusEffectType.AttackSpeedModifier; return;
            case PlayerStatKind.Defense:
                basePercent = StatusEffectType.DefenseBaseModifier; totalPercent = StatusEffectType.DefenseModifier; return;
            case PlayerStatKind.MaxHp:
                basePercent = StatusEffectType.MaxHpBaseModifier; totalPercent = StatusEffectType.MaxHpModifier; return;
            default:
                basePercent = StatusEffectType.None; totalPercent = StatusEffectType.None; return;
        }
    }

    /// <summary>상태이상 인스턴스 1건의 % 기여 — magnitude 1.2 = +0.2, 0.8 = −0.2. 스택은 % × 스택 수.</summary>
    public static float PercentOf(float magnitude, int stackCount)
    {
        return (Mathf.Max(0f, magnitude) - 1f) * Mathf.Max(1, stackCount);
    }

    /// <summary>기본에 곱할 배율 (1 + Σ기본%) × (1 + Σ전체%) × Π최종배율, 0 이상. 최종 배율 없음 = 1.</summary>
    public static float Multiplier(float basePercent, float totalPercent, float finalMultiplier = 1f)
    {
        return Mathf.Max(0f, 1f + basePercent) * Mathf.Max(0f, 1f + totalPercent) * Mathf.Max(0f, finalMultiplier);
    }

    public static float Final(float baseValue, float basePercent, float totalPercent, float finalMultiplier = 1f)
    {
        return Mathf.Max(0f, baseValue * Multiplier(basePercent, totalPercent, finalMultiplier));
    }

    public static int FinalInt(int baseValue, float basePercent, float totalPercent, float finalMultiplier = 1f)
    {
        return Mathf.Max(0, Mathf.RoundToInt(baseValue * Multiplier(basePercent, totalPercent, finalMultiplier)));
    }

    /// <summary>가하는 피해% 를 뺀 1차 스냅샷 — 기존 판정식 round(공격력 × 계수) + 고정값.</summary>
    public static int RawSnapshot(int finalAttack, float coefficient, int flatBonus)
    {
        return Mathf.Max(0, Mathf.RoundToInt(finalAttack * coefficient) + flatBonus);
    }

    /// <summary>
    /// 피해 스냅샷 = round(1차 스냅샷 × (1 + 가하는 피해%) × 특성 공격 계열 배율). 특성 배율(할 일 125 2/5, 명세 §5 Dref)은 중간에 정수로 자르지 않고
    /// 마지막 반올림 한 번에 같이 넣는다. 1 미만(페널티)도 그대로, 0 미만은 0. 기본 1 이면 이전 식과 같다.
    /// </summary>
    public static int Snapshot(int finalAttack, float coefficient, int flatBonus, float dealtPercent, float traitMultiplier = 1f)
    {
        int raw = RawSnapshot(finalAttack, coefficient, flatBonus);
        return Mathf.Max(0, Mathf.RoundToInt(raw * Mathf.Max(0f, 1f + dealtPercent) * Mathf.Max(0f, traitMultiplier)));
    }

    /// <summary>실수 값을 천분율 정수로 — 검증 보고에 실어 보낼 때 float 오차를 비교에서 뺀다.</summary>
    public static int ToMilli(float value) => Mathf.RoundToInt(value * 1000f);

    /// <summary>
    /// 서버 2차 검증 — 서버 값과 오너 보고가 다르고, 서버 쪽 입력이 바뀐 지 유예 시간이 지났으면 경고 대상.
    /// 유예 안의 불일치는 상태이상 복제가 아직 오너에 닿지 않은 것으로 본다.
    /// </summary>
    public static bool ShouldWarn(PlayerStatReport server, PlayerStatReport client,
        float now, float lastInputChangedTime, float graceSeconds)
    {
        if (server.Equals(client))
            return false;

        return now - lastInputChangedTime >= graceSeconds;
    }
}

/// <summary>오너가 공격·스킬 시작 요청에 싣는 자기 종합 결과. 서버는 비교만 하고 판정엔 자기 값을 쓴다. 실수 스탯은 천분율(최종 배율까지 곱한 값).</summary>
public struct PlayerStatReport : INetworkSerializable, System.IEquatable<PlayerStatReport>
{
    public int attackDamage;
    public int moveSpeedMilli;
    public int attackSpeedMilli;
    public int defense;
    public int maxHp;
    public int dealtDamageMilli;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref attackDamage);
        serializer.SerializeValue(ref moveSpeedMilli);
        serializer.SerializeValue(ref attackSpeedMilli);
        serializer.SerializeValue(ref defense);
        serializer.SerializeValue(ref maxHp);
        serializer.SerializeValue(ref dealtDamageMilli);
    }

    public bool Equals(PlayerStatReport other) =>
        attackDamage == other.attackDamage && moveSpeedMilli == other.moveSpeedMilli &&
        attackSpeedMilli == other.attackSpeedMilli && defense == other.defense &&
        maxHp == other.maxHp && dealtDamageMilli == other.dealtDamageMilli;

    public override string ToString() =>
        $"공격력 {attackDamage} · 이동 {moveSpeedMilli / 1000f:0.###} · 공속 {attackSpeedMilli / 1000f:0.###} · " +
        $"방어 {defense} · 최대 체력 {maxHp} · 가하는 피해 {dealtDamageMilli / 10f:0.#}%";
}
