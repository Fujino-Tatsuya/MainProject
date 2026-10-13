/// <summary>
/// 머리 위 체력바 옆 대시 아이콘의 표시 여부·준비 상태 공급자. UnitOverheadHealthBar 가 자기/부모에서 찾아 매 프레임 묻는다.
/// 공급자가 없으면 아이콘을 숨긴다(몬스터 등). 플레이어 = PlayerOverheadHealthBarRule.
/// 표시·색 판정은 <see cref="OverheadDashIconRules"/>.
/// </summary>
public interface IOverheadDashIndicator
{
    /// <summary>아이콘을 띄울지 — 로컬(오너) 플레이어이고 살아 있을 때만.</summary>
    bool ShowDashIcon { get; }
    /// <summary>대시 충전이 1개 이상 남았는지(오너 예측 기준).</summary>
    bool DashReady { get; }
}
