using UnityEngine;

/// <summary>
/// 머리 위 체력바의 색·표시·라벨 규칙. UnitOverheadHealthBar 가 자기 자신이나 부모에서 찾아 매 프레임 묻는다.
/// 규칙이 없으면 바는 항상 표시 + 바의 기본색 + 라벨 없음으로 동작한다.
/// 플레이어 = PlayerOverheadHealthBarRule. 몬스터 등 다른 대상은 이 인터페이스를 구현한 컴포넌트를 붙인다.
/// </summary>
public interface IOverheadHealthBarRule
{
    bool ShouldShow { get; }
    Color FillColor { get; }
    /// <summary>머리 위 이름. null/빈 문자열이면 라벨을 숨긴다.</summary>
    string Label { get; }
}
