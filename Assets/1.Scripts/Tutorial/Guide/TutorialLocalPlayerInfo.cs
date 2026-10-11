using System.Collections.Generic;

/// <summary>
/// 로컬 플레이어에서 문구 변형용 캐릭터와 {Q} {E} {P} 스킬 이름을 읽는다.
/// 캐릭터 id(로스터 인덱스)는 클라에 복제되지 않아, 캐릭터 고유 컴포넌트로 식별한다
/// (Player_Gunner = GunnerHeat, Player_Assassin = AssassinState, Player_Paladin = FirstMeleePassive — 2026-10-11 프리팹 확인).
/// </summary>
public static class TutorialLocalPlayerInfo
{
    public static TutorialCharacter ResolveCharacter(Player player)
    {
        if (player == null)
            return TutorialCharacter.None;

        if (player.GetComponentInChildren<GunnerHeat>(true) != null)
            return TutorialCharacter.Gunner;
        if (player.GetComponentInChildren<AssassinState>(true) != null)
            return TutorialCharacter.Assassin;
        if (player.GetComponentInChildren<FirstMeleePassive>(true) != null)
            return TutorialCharacter.Paladin;

        return TutorialCharacter.None;
    }

    /// <summary>
    /// 슬롯 툴팁 displayName 을 {Q} {E} {P} 로 넣는다. 이름이 비면 키를 넣지 않는다(자리표시자가 그대로 남는다).
    /// 경로는 SkillCooldownHUD(Q/E = 슬롯 스킬 Data)·PassiveHUD(P = IPlayerPassive → IPassiveTooltipProvider)와 같다.
    /// </summary>
    public static void AddSkillNames(Player player, IDictionary<string, string> args)
    {
        if (player == null || args == null)
            return;

        PlayerSkillController skills = player.GetComponent<PlayerSkillController>();
        if (skills != null)
        {
            Add(args, "Q", skills.GetSkill(PlayerSkillSlot.Main)?.Data);
            Add(args, "E", skills.GetSkill(PlayerSkillSlot.Sub)?.Data);
        }

        ISkillTooltipSource passive = player.GetComponent<IPlayerPassive>() as ISkillTooltipSource;
        if (passive == null)
            passive = player.GetComponentInChildren<IPassiveTooltipProvider>(true)?.PassiveTooltip;
        Add(args, "P", passive);
    }

    private static void Add(IDictionary<string, string> args, string key, ISkillTooltipSource source)
    {
        string name = source != null ? source.Tooltip.DisplayName : null;
        if (!string.IsNullOrEmpty(name))
            args[key] = name;
    }
}
