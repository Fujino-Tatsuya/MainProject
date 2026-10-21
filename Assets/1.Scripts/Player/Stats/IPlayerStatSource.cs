using System;

/// <summary>
/// 상태이상 밖의 영구 스탯 출처(특성·성장 등, 할 일 83). 각 피어가 같은 SO 에서 수치를 읽어 기여를 돌려준다 —
/// 서버는 "무엇을 얻었는지"(id)만 복제하고 수치는 복제하지 않는다. 등록은 <see cref="PlayerStats.Register"/>.
/// </summary>
public interface IPlayerStatSource
{
    float GetBasePercent(PlayerStatKind kind);
    float GetTotalPercent(PlayerStatKind kind);
    float DealtDamagePercent { get; }

    /// <summary>기여가 바뀌면 낸다 — 서버는 이 시각부터 검증 유예를 다시 센다.</summary>
    event Action Changed;
}
