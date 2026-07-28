// 조준 모드에서 시전을 확정하는 방식. 현재는 ClickToConfirm만 구현하고 나머지는 예약(추후 확장).
public enum SkillConfirmMode
{
    ClickToConfirm,  // 조준 모드 진입 → 좌클릭으로 확정, Esc/재입력으로 취소 (기본, 구현됨)
    HoldRelease,     // 키를 누르는 동안 조준, 릴리즈 순간 확정 (예약 — 미구현)
    InstantAtCursor  // 키 누르는 즉시 현재 커서 위치/대상으로 확정 (예약 — 미구현)
}
