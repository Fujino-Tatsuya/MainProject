using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 공격 스탯 공식(할 일 83) — 서버 판정·클라 표시(툴팁)·서버 2차 검증이 전부 이 한 곳을 쓴다.
///   최종 공격력 = round( 기본 × (1 + Σ기본공격력%) × (1 + Σ전체공격력%) ), 0 이상
///   피해 스냅샷 = round( (round(최종 공격력 × 계수) + 고정값) × (1 + Σ가하는피해%) ), 0 이상
/// 같은 단계 안의 % 는 더한다. 상세: Docs/tech/attack-stats.md
/// </summary>
public static class AttackStatFormula
{
    /// <summary>상태이상 인스턴스 1건의 % 기여 — magnitude 1.2 = +0.2, 0.8 = −0.2. 스택은 % × 스택 수.</summary>
    public static float PercentOf(float magnitude, int stackCount)
    {
        return (Mathf.Max(0f, magnitude) - 1f) * Mathf.Max(1, stackCount);
    }

    public static int FinalAttack(int baseAttack, float basePercent, float totalPercent)
    {
        float value = baseAttack * Mathf.Max(0f, 1f + basePercent) * Mathf.Max(0f, 1f + totalPercent);
        return Mathf.Max(0, Mathf.RoundToInt(value));
    }

    /// <summary>가하는 피해% 를 뺀 1차 스냅샷 — 기존 판정식 round(공격력 × 계수) + 고정값.</summary>
    public static int RawSnapshot(int finalAttack, float coefficient, int flatBonus)
    {
        return Mathf.Max(0, Mathf.RoundToInt(finalAttack * coefficient) + flatBonus);
    }

    public static int Snapshot(int finalAttack, float coefficient, int flatBonus, float dealtPercent)
    {
        int raw = RawSnapshot(finalAttack, coefficient, flatBonus);
        return Mathf.Max(0, Mathf.RoundToInt(raw * Mathf.Max(0f, 1f + dealtPercent)));
    }

    /// <summary>% 를 천분율 정수로 — 검증 보고에 실어 보낼 때 float 오차를 비교에서 뺀다.</summary>
    public static int ToPermille(float percent) => Mathf.RoundToInt(percent * 1000f);

    /// <summary>
    /// 서버 2차 검증 — 서버 값과 오너 보고가 다르고, 서버 쪽 입력이 바뀐 지 유예 시간이 지났으면 경고 대상.
    /// 유예 안의 불일치는 상태이상 복제가 아직 오너에 닿지 않은 것으로 본다.
    /// </summary>
    public static bool ShouldWarn(AttackStatReport server, AttackStatReport client,
        float now, float lastInputChangedTime, float graceSeconds)
    {
        if (server.Equals(client))
            return false;

        return now - lastInputChangedTime >= graceSeconds;
    }
}

/// <summary>오너가 공격·스킬 시작 요청에 싣는 자기 종합 결과. 서버는 비교만 하고 판정엔 자기 값을 쓴다.</summary>
public struct AttackStatReport : INetworkSerializable, System.IEquatable<AttackStatReport>
{
    public int finalAttack;
    public int dealtPermille;

    public AttackStatReport(int finalAttack, int dealtPermille)
    {
        this.finalAttack = finalAttack;
        this.dealtPermille = dealtPermille;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref finalAttack);
        serializer.SerializeValue(ref dealtPermille);
    }

    public bool Equals(AttackStatReport other) =>
        finalAttack == other.finalAttack && dealtPermille == other.dealtPermille;

    public override string ToString() => $"공격력 {finalAttack} / 가하는 피해 {dealtPermille / 10f:0.#}%";
}
