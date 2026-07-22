// 마우스 상태머신의 상태. 조준 중 매 프레임 레이캐스트 결과로 전이하며,
// SkillCursorView가 이 상태를 구독해 커서 아이콘을 교체한다(현재는 훅만, 아이콘 에셋 미정).
public enum SkillCursorState
{
    Default,        // 조준 아님 (일반 게임플레이)
    Targeting,      // 조준 중이나 아직 유효 대상/지점이 아님
    ValidTarget,    // 사거리 내 유효 대상(SingleTarget) 또는 유효 지면(GroundPoint) 위
    InvalidTarget,  // 대상이 아닌 것 위 (SingleTarget에서 적이 아님)
    OutOfRange      // 대상/지점이 사거리 밖
}
