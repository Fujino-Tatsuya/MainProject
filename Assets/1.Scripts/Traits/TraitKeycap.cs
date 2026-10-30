using System;
using UnityEngine;

/// <summary>특성 카드 C1 의 키캡 자리 표시(할 일 165). 키캡 3종 + 마우스 표시 2종, Blank = 키 입력 아님(표시 안 함).</summary>
public enum TraitKeycap
{
    Blank,
    Q,
    E,
    R,
    LeftMouse,
    RightMouse,
}

/// <summary>
/// 특성 아이콘 파일 이름 → 키캡 판정(할 일 165) — 규칙은 여기 한 곳.
/// 이름을 <c>_</c> 로 나눈 토큰 중 <c>Q</c>/<c>E</c>/<c>R</c>/<c>LMB</c>/<c>RMB</c> 와 정확히 같은 것이 있으면 그 키(왼쪽부터 첫 토큰).
/// 그래서 <c>Trait_Gunner_Q_Change</c>·<c>Trait_Assassin_E_Trans</c>·<c>Trait_Assassin_R_Remove</c> 처럼 키 뒤에 접미사가 붙어도 그 키다.
/// 키 토큰이 없으면(<c>Passive</c>·<c>Heat</c>·<c>Heat_Remove</c> 등) <see cref="TraitKeycap.Blank"/> — 카드는 키캡을 숨긴다(피드백 1). 대소문자는 구분한다.
/// </summary>
public static class TraitKeycapRule
{
    /// <param name="iconName">스프라이트 이름 또는 파일 이름/경로(확장자·폴더는 무시). 비면 빈 키캡.</param>
    public static TraitKeycap FromIconName(string iconName)
    {
        if (string.IsNullOrEmpty(iconName))
            return TraitKeycap.Blank;

        string name = iconName;
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0)
            name = name.Substring(slash + 1);
        int dot = name.LastIndexOf('.');
        if (dot > 0)
            name = name.Substring(0, dot);

        string[] tokens = name.Split('_');
        for (int i = 0; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "Q": return TraitKeycap.Q;
                case "E": return TraitKeycap.E;
                case "R": return TraitKeycap.R;
                case "LMB": return TraitKeycap.LeftMouse;
                case "RMB": return TraitKeycap.RightMouse;
            }
        }

        return TraitKeycap.Blank;
    }
}

/// <summary>특성 카드 스킬 슬롯 그림의 출처(할 일 165 피드백 1).</summary>
public enum TraitCardSkillSource
{
    /// <summary>스킬 슬롯 스킬(<see cref="PlayerSkillController.GetSkill"/>)의 툴팁 아이콘.</summary>
    Skill,
    /// <summary>패시브 툴팁 아이콘(PassiveHUD 와 같은 경로).</summary>
    Passive,
    /// <summary>기본 공격(LMB) — 스킬 데이터가 없어 카드의 기본 공격 아이콘(검)을 보는 사람 캐릭터 색으로 틴트.</summary>
    BasicAttack,
}

/// <summary>
/// 키캡 → 카드 스킬 슬롯 그림 출처(할 일 165 피드백 1) — 규칙은 여기 한 곳.
/// Q = Main, E = Sub, R = Ultimate, RMB = Interrupt, 키 없음(Blank) = 패시브, LMB = 기본 공격 아이콘(후속 — 검 + 캐릭터 색).
/// </summary>
public static class TraitCardSkillRule
{
    /// <param name="slot"><see cref="TraitCardSkillSource.Skill"/> 일 때만 의미가 있다.</param>
    public static TraitCardSkillSource SourceOf(TraitKeycap key, out PlayerSkillSlot slot)
    {
        slot = PlayerSkillSlot.Main;
        switch (key)
        {
            case TraitKeycap.Q: slot = PlayerSkillSlot.Main; return TraitCardSkillSource.Skill;
            case TraitKeycap.E: slot = PlayerSkillSlot.Sub; return TraitCardSkillSource.Skill;
            case TraitKeycap.R: slot = PlayerSkillSlot.Ultimate; return TraitCardSkillSource.Skill;
            case TraitKeycap.RightMouse: slot = PlayerSkillSlot.Interrupt; return TraitCardSkillSource.Skill;
            case TraitKeycap.Blank: return TraitCardSkillSource.Passive;
            default: return TraitCardSkillSource.BasicAttack;
        }
    }

    /// <summary>
    /// 슬롯 그림 색 — 기본 공격 아이콘만 보는 사람 캐릭터 색(로스터 <see cref="CharacterRoster.Entry.ThemeColor"/>), 못 찾으면 흰색.
    /// 실제 스킬·패시브 아이콘은 늘 흰색(틴트 없음 — 카드 재사용 때 이전 색이 남지 않게 매번 이 값으로 덮는다).
    /// </summary>
    public static Color SlotTint(TraitCardSkillSource source, bool hasViewerColor, Color viewerColor) =>
        source == TraitCardSkillSource.BasicAttack && hasViewerColor ? viewerColor : Color.white;
}
