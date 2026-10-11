/// <summary>
/// 런타임에 이름/경로로 찾아 강조하는 CombatHUD 안 칸. CombatHUD 프리팹은 고치지 않고 오버레이만 얹는다.
/// </summary>
public enum TutorialHudTarget
{
    None,
    SkillSlotP,
    SkillSlotQ,
    SkillSlotE,
    SkillSlotR,
    SkillSlotRmb,
    BossTimer,
    Minimap,
    /// <summary>미니맵 보스방 아이콘 — MinimapController 가 런타임에 만드는 <c>Role_Boss</c>.</summary>
    MinimapBossIcon,
}

/// <summary>CombatHUD 루트 기준 경로(2026-10-11 CombatHUD.prefab 확인). 바뀌면 여기만 고친다.</summary>
public static class TutorialHudTargetPaths
{
    /// <summary>MinimapController 가 보스방 마커에 붙이는 이름(<c>Role_{label}</c>).</summary>
    public const string MinimapBossIconName = "Role_Boss";

    public static string PathOf(TutorialHudTarget target) => target switch
    {
        TutorialHudTarget.SkillSlotP => "Layer_Combat/SkillBar/Slot_P",
        TutorialHudTarget.SkillSlotQ => "Layer_Combat/SkillBar/Slot_Q",
        TutorialHudTarget.SkillSlotE => "Layer_Combat/SkillBar/Slot_E",
        TutorialHudTarget.SkillSlotR => "Layer_Combat/SkillBar/Slot_R",
        TutorialHudTarget.SkillSlotRmb => "Layer_Combat/SkillBar/Slot_RMB",
        TutorialHudTarget.BossTimer => "Layer_Combat/BossTimerSlot",
        TutorialHudTarget.Minimap => "Layer_Combat/MinimapSlot",
        _ => null,
    };
}
