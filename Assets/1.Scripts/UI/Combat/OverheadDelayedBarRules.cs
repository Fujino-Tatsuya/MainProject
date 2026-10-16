using UnityEngine;

/// <summary>
/// 머리 위/발밑 바의 잔상(<see cref="DelayedHealthBar"/>)을 현재 체력으로 스냅할 때. 잔상 동작 자체는 DelayedHealthBar 가 한다.
/// - 바가 숨겨지는 순간: 숨김 동안 남은 조각이 다음 표시 때 옛 값에서 튀어 내려오지 않게. 숨김 중에도 Tick 은 돌아
///   숨김 중 피격(= 표시를 켜는 첫 타)의 잔상은 살아 있다 — 그래서 "표시 순간"이 아니라 "숨김 순간"에 스냅한다.
/// - 표시 중 게이지 색이 바뀔 때: 규칙의 모드 전환(튜토리얼 스피너 연습 회색 ↔ 본 모드 빨강)에서 이전 모드 잔상을 끊는다.
/// </summary>
public static class OverheadDelayedBarRules
{
    public static bool ShouldSnap(bool wasShown, bool shown, Color previousFillColor, Color fillColor)
    {
        if (wasShown && !shown)
            return true;
        return wasShown && shown && previousFillColor != fillColor;
    }
}
