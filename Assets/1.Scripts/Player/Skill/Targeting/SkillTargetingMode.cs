// 스킬 시전 전 조준 방식. PlayerSkillData에 저장되어 조준 모드 진입 여부와 대상 해석을 결정한다.
public enum SkillTargetingMode
{
    None,         // 조준 없음 — 키 입력 즉시 시전 (기존 Q/E/우클릭 동작 유지)
    SingleTarget, // 사거리 내 적 Unit 하나를 지정 (마우스 레이캐스트로 선택 → target 전달)
    GroundPoint   // 사거리 내 지면 지점을 지정 (장판/도약 등 → aimPoint 전달)
}
