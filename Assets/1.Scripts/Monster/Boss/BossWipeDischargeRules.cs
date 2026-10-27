/// <summary>
/// 전멸기(방 전체 즉사 방전) 판정 규칙 — 순수 함수(할 일 105 후속, 사용자 결정 10-27).
/// 전멸기가 발동하는 순간 칸 판정·발 위치·벽 끼임·무적과 무관하게 살아 있는 참가자 전원을 사망 처리한다
/// (<see cref="BossPatternTargets.Kill"/> — 기존 사망 경로). 안전 칸이 있는 즉사 방전은 지금처럼 칸 판정 + 무적 무시 즉사 피해.
/// 기획·판단: Docs/tech/handoff-boss-lethal-invuln-bypass.md.
/// </summary>
public static class BossWipeDischargeRules
{
    /// <summary>
    /// 이 방전이 전멸기인가 — 방 전체 범위(<see cref="BossTileGrid.AllMask"/>) + 즉사 + 무적으로 못 피함(데이터 0/1 칸 = 1).
    /// 즉사가 아니면(④ <c>raidSelfDestructBlastLethal</c> 끔) 일반 피해 방전이라 전멸기가 아니다.
    /// </summary>
    public static bool IsWipe(ulong mask, bool lethal, bool unavoidable) =>
        unavoidable && lethal && mask == BossTileGrid.AllMask;

    /// <summary>
    /// 전멸기로 사망 처리할 대상인가 — 살아 있고(Alive) 23호가 실제로 쥐고 있지 않음.
    /// 쓰러짐·영혼·완전 사망은 이미 죽었고, 붙잡힘은 기획 boss-electric-floor.md §6.4 의 장판 면제 그대로 둔다.
    /// </summary>
    public static bool ShouldKill(bool alive, bool heldByBoss) => alive && !heldByBoss;
}
