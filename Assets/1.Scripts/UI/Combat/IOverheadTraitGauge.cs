using UnityEngine;

/// <summary>
/// 머리 위 체력바 아래 특성 게이지 줄의 값·색·눈금 공급자. UnitOverheadHealthBar 가 부모에서 찾아 매 프레임 묻는다.
/// 공급자가 없으면 줄을 숨긴다(몬스터 등). 캐릭터별 구현이 자기 컴포넌트 값을 읽어 준다 —
/// 암살자 = AssassinOverheadTraitGauge, 거너 = GunnerOverheadTraitGauge, 가붕이 = FirstMeleePassiveOverheadTraitGauge.
/// 색 판정은 <see cref="OverheadTraitGaugeRules"/>.
/// </summary>
public interface IOverheadTraitGauge
{
    /// <summary>채움 비율(0~1).</summary>
    float Fill { get; }
    Color FillColor { get; }
    /// <summary>눈금 위치(0~1). 음수면 눈금을 숨긴다.</summary>
    float MarkerPosition { get; }
    /// <summary>게이지 줄을 보일지(예: 거너 정온 운용 특성 = 과열 없음 → false). 기본 true — 구현하지 않은 공급자는 그대로 보인다.</summary>
    bool Visible => true;
}
