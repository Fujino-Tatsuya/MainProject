/// <summary>
/// 전투 HUD 의 특성(증강) 진행도 규칙 — Unity 비의존 순수 함수. 표시는 <see cref="TraitProgressHUD"/>.
///  · 레벨 = 획득 특성 수 + 1(튜토리얼 밖 = 1).
///  · XP 바 = 지금 구간(획득 특성 수로 고른 복도 구간)의 게이트 중 열린 비율. 4장 다 얻으면 가득, 튜토리얼 밖 = 0.
///  · 스킬 상단 칸 = 그 슬롯의 쿨 감소 특성(<see cref="TutorialTraitEffectKind.CooldownReductionOnHit"/>) 개수, 최대 <see cref="MaxUpgradeStage"/>.
/// </summary>
public static class TraitProgressRules
{
    public const int MaxUpgradeStage = 4;

    // 복도 N 에 도착하기까지 지나는 게이트 — 복도1 = G1, 복도2 = G2·G3, 복도3 = G4·G5·G6, 복도4 = G7·G8·G9.
    // 특성은 복도마다 1장이라 '획득 특성 수' 번째 구간이 지금 채우는 구간이다.
    static readonly TutorialGate[][] Segments =
    {
        new[] { TutorialGate.G1_Corridor1Entry },
        new[] { TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry },
        new[] { TutorialGate.G4_Zone3Entry, TutorialGate.G5_SpinnerRealMode, TutorialGate.G6_Corridor3Entry },
        new[] { TutorialGate.G7_Zone4Entry, TutorialGate.G8_TimerStart, TutorialGate.G9_Corridor4Entry },
    };

    public static int SegmentCount => Segments.Length;

    public static int Level(bool inTutorial, int acquiredCount) => inTutorial ? ClampNonNegative(acquiredCount) + 1 : 1;

    /// <param name="openedGateMask">bit = <see cref="TutorialGate"/> 값(<c>TutorialDirector.IsGateOpen</c> 과 같은 배치).</param>
    public static float XpFill(bool inTutorial, int acquiredCount, int openedGateMask)
    {
        if (!inTutorial)
            return 0f;

        int segment = ClampNonNegative(acquiredCount);
        if (segment >= Segments.Length)
            return 1f;

        TutorialGate[] gates = Segments[segment];
        int opened = 0;
        foreach (TutorialGate gate in gates)
        {
            if ((openedGateMask & (1 << (int)gate)) != 0)
                opened++;
        }
        return (float)opened / gates.Length;
    }

    public static bool CountsForSlot(TutorialTraitEffectKind kind, PlayerSkillSlot traitSlot, PlayerSkillSlot slot) =>
        kind == TutorialTraitEffectKind.CooldownReductionOnHit && traitSlot == slot;

    public static int ClampUpgradeStage(int count) => count < 0 ? 0 : count > MaxUpgradeStage ? MaxUpgradeStage : count;

    static int ClampNonNegative(int value) => value < 0 ? 0 : value;
}
