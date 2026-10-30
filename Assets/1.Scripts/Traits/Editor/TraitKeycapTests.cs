using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 165 — 카드 C1 키캡 판정(<see cref="TraitKeycapRule"/>)·아이콘 맵 파싱(<see cref="TraitIconMapAuthoring.Parse"/>)·프로젝트 정의 아이콘 연결 확인.
/// </summary>
public class TraitKeycapTests
{
    [TestCase("Trait_Paladin_Q", TraitKeycap.Q)]
    [TestCase("Trait_Paladin_E", TraitKeycap.E)]
    [TestCase("Trait_Gunner_R", TraitKeycap.R)]
    [TestCase("Trait_Gunner_LMB", TraitKeycap.LeftMouse)]
    [TestCase("Trait_Gunner_RMB", TraitKeycap.RightMouse)]
    [TestCase("Trait_Paladin_Q_Change", TraitKeycap.Q)]
    [TestCase("Trait_Gunner_Q_Change", TraitKeycap.Q)]
    [TestCase("Trait_Assassin_E_Trans", TraitKeycap.E)]
    [TestCase("Trait_Assassin_E_Trans_Inactive", TraitKeycap.E)]
    [TestCase("Trait_Assassin_R_Trans", TraitKeycap.R)]
    [TestCase("Trait_Assassin_R_Remove", TraitKeycap.R)]
    [TestCase("Trait_Paladin_Passive", TraitKeycap.Blank)]
    [TestCase("Trait_Paladin_Passive_Change", TraitKeycap.Blank)]
    [TestCase("Trait_Gunner_Heat", TraitKeycap.Blank)]
    [TestCase("Trait_Gunner_Heat_Remove", TraitKeycap.Blank)]
    [TestCase("Trait_Assassin_Trans", TraitKeycap.Blank)]
    [TestCase("Trait_Empty", TraitKeycap.Blank)]
    [TestCase("Gunner/Trait_Gunner_Q.png", TraitKeycap.Q)]
    [TestCase("Trait_Gunner_q", TraitKeycap.Blank)]
    [TestCase("Trait_Gunner_Quick", TraitKeycap.Blank)]
    [TestCase("", TraitKeycap.Blank)]
    [TestCase(null, TraitKeycap.Blank)]
    public void FromIconName(string iconName, TraitKeycap expected)
    {
        Assert.AreEqual(expected, TraitKeycapRule.FromIconName(iconName));
    }

    [TestCase(TraitKeycap.Q, TraitCardSkillSource.Skill, PlayerSkillSlot.Main)]
    [TestCase(TraitKeycap.E, TraitCardSkillSource.Skill, PlayerSkillSlot.Sub)]
    [TestCase(TraitKeycap.R, TraitCardSkillSource.Skill, PlayerSkillSlot.Ultimate)]
    [TestCase(TraitKeycap.RightMouse, TraitCardSkillSource.Skill, PlayerSkillSlot.Interrupt)]
    public void SkillSource_KeyToSlot(TraitKeycap key, TraitCardSkillSource expected, PlayerSkillSlot expectedSlot)
    {
        Assert.AreEqual(expected, TraitCardSkillRule.SourceOf(key, out PlayerSkillSlot slot));
        Assert.AreEqual(expectedSlot, slot);
    }

    [TestCase(TraitKeycap.Blank, TraitCardSkillSource.Passive)]
    [TestCase(TraitKeycap.LeftMouse, TraitCardSkillSource.BasicAttack)]
    public void SkillSource_NonSlot(TraitKeycap key, TraitCardSkillSource expected)
    {
        Assert.AreEqual(expected, TraitCardSkillRule.SourceOf(key, out _));
    }

    /// <summary>기본 공격 아이콘만 보는 사람 색, 못 찾으면 흰색. 스킬·패시브 슬롯은 색이 있어도 흰색(재사용 시 초기화).</summary>
    [Test]
    public void SlotTint_OnlyBasicAttackTakesViewerColor()
    {
        var gunner = new Color(0.94f, 0.31f, 0.78f, 1f);
        Assert.AreEqual(gunner, TraitCardSkillRule.SlotTint(TraitCardSkillSource.BasicAttack, true, gunner));
        Assert.AreEqual(Color.white, TraitCardSkillRule.SlotTint(TraitCardSkillSource.BasicAttack, false, gunner));
        Assert.AreEqual(Color.white, TraitCardSkillRule.SlotTint(TraitCardSkillSource.Skill, true, gunner));
        Assert.AreEqual(Color.white, TraitCardSkillRule.SlotTint(TraitCardSkillSource.Passive, true, gunner));
    }

    /// <summary>검은 배경은 기본 공격(검) 슬롯만 — 스킬·패시브(폴백 포함)는 끈다.</summary>
    [TestCase(TraitCardSkillSource.BasicAttack, true)]
    [TestCase(TraitCardSkillSource.Skill, false)]
    [TestCase(TraitCardSkillSource.Passive, false)]
    public void ShowsBackdrop_OnlyBasicAttack(TraitCardSkillSource source, bool expected)
    {
        Assert.AreEqual(expected, TraitCardSkillRule.ShowsBackdrop(source));
    }

    /// <summary>LMB 키 → 기본 공격 출처 → 배경 켬. 같은 규칙 결과를 그대로 쓴다.</summary>
    [Test]
    public void ShowsBackdrop_LeftMouseKeyOn_QKeyOff()
    {
        Assert.IsTrue(TraitCardSkillRule.ShowsBackdrop(TraitCardSkillRule.SourceOf(TraitKeycap.LeftMouse, out _)));
        Assert.IsFalse(TraitCardSkillRule.ShowsBackdrop(TraitCardSkillRule.SourceOf(TraitKeycap.Q, out _)));
    }

    /// <summary>LMB 특성 5개는 기본 공격 아이콘 경로(아이콘 이름의 LMB 토큰).</summary>
    [TestCase("GUN_1_BASIC_DAMAGE")]
    [TestCase("GUN_3_PIERCE")]
    [TestCase("GUN_4_EXTRA_BASIC")]
    [TestCase("ASN_3_BASIC_CDR")]
    [TestCase("ASN_4_COMBO_EXTRA")]
    public void LmbTraits_UseBasicAttackSlot(string traitId)
    {
        TraitDefinition definition = null;
        foreach (string guid in AssetDatabase.FindAssets("t:TraitDefinition", new[] { "Assets/9.ScriptableObject/Traits" }))
        {
            var candidate = AssetDatabase.LoadAssetAtPath<TraitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.Id == traitId)
                definition = candidate;
        }

        Assert.IsNotNull(definition, traitId);
        Assert.IsNotNull(definition.Icon, traitId);
        Assert.AreEqual(TraitCardSkillSource.BasicAttack,
            TraitCardSkillRule.SourceOf(TraitKeycapRule.FromIconName(definition.Icon.name), out _), definition.Icon.name);
    }

    /// <summary>로스터 세 캐릭터 모두 공용 색이 들어 있다(흰색 기본값 그대로가 아님).</summary>
    [Test]
    public void ProjectRoster_ThemeColorsSet()
    {
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/9.ScriptableObject/Player/CharacterRoster.asset");
        Assert.IsNotNull(roster);
        foreach (CharacterRoster.Entry entry in roster.Entries)
            Assert.AreNotEqual(Color.white, entry.ThemeColor, entry.InlineDisplayName);
    }

    /// <summary>변신 특성은 일반 스킬 슬롯 그림(E_Trans → Sub, R_Trans·R_Remove → Ultimate), 키 없는 특성은 패시브.</summary>
    [TestCase("Trait_Assassin_E_Trans", TraitCardSkillSource.Skill, PlayerSkillSlot.Sub)]
    [TestCase("Trait_Assassin_R_Trans", TraitCardSkillSource.Skill, PlayerSkillSlot.Ultimate)]
    [TestCase("Trait_Assassin_R_Remove", TraitCardSkillSource.Skill, PlayerSkillSlot.Ultimate)]
    [TestCase("Trait_Gunner_Heat_Remove", TraitCardSkillSource.Passive, PlayerSkillSlot.Main)]
    [TestCase("Trait_Paladin_Passive_Change", TraitCardSkillSource.Passive, PlayerSkillSlot.Main)]
    public void SkillSource_FromIconName(string iconName, TraitCardSkillSource expected, PlayerSkillSlot expectedSlot)
    {
        TraitCardSkillSource source = TraitCardSkillRule.SourceOf(TraitKeycapRule.FromIconName(iconName), out PlayerSkillSlot slot);
        Assert.AreEqual(expected, source);
        if (source == TraitCardSkillSource.Skill)
            Assert.AreEqual(expectedSlot, slot);
    }

    [Test]
    public void Parse_ReadsIdAndIconColumns()
    {
        string csv = "﻿id,name,character,tier,icon,inactive_icon\r\n" +
                     "PAL_1_Q_DAMAGE,진격 강화,Paladin,1,Paladin/Trait_Paladin_Q.png,\r\n" +
                     "ASN_1_TRANS_E_DAMAGE,광란 강화,Assassin,1,Assassin/Trait_Assassin_E_Trans.png,Assassin/Trait_Assassin_E_Trans_Inactive.png\r\n" +
                     "BROKEN,빈칸,Paladin,1,,\r\n";

        Dictionary<string, string> map = TraitIconMapAuthoring.Parse(csv, out List<string> errors);

        Assert.AreEqual(2, map.Count);
        Assert.AreEqual("Paladin/Trait_Paladin_Q.png", map["PAL_1_Q_DAMAGE"]);
        Assert.AreEqual("Assassin/Trait_Assassin_E_Trans.png", map["ASN_1_TRANS_E_DAMAGE"]);
        Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
    }

    [Test]
    public void Parse_MissingColumns_ReportsError()
    {
        Dictionary<string, string> map = TraitIconMapAuthoring.Parse("id,name\nA,B\n", out List<string> errors);
        Assert.AreEqual(0, map.Count);
        Assert.AreEqual(1, errors.Count);
    }

    /// <summary>프로젝트 아이콘 맵의 모든 행이 특성 정의에 연결돼 있다(메뉴 '특성 아이콘 연결' 실행 결과).</summary>
    [Test]
    public void ProjectDefinitions_IconsMatchIconMap()
    {
        string text = System.IO.File.ReadAllText(TraitIconMapAuthoring.DefaultCsvPath);
        Dictionary<string, string> map = TraitIconMapAuthoring.Parse(text, out _);
        Assert.Greater(map.Count, 0);

        var problems = new List<string>();
        var found = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:TraitDefinition", new[] { "Assets/9.ScriptableObject/Traits" }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<TraitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition == null || !map.TryGetValue(definition.Id, out string relative))
                continue;

            found.Add(definition.Id);
            string expected = $"{TraitIconMapAuthoring.DefaultIconRoot}/{relative}";
            string actual = definition.Icon != null ? AssetDatabase.GetAssetPath(definition.Icon) : "(없음)";
            if (actual != expected)
                problems.Add($"{definition.Id}: {actual} ≠ {expected}");
        }

        foreach (string id in map.Keys)
        {
            if (!found.Contains(id))
                problems.Add($"{id}: 정의 없음");
        }

        Assert.IsEmpty(problems, string.Join("\n", problems));
    }
}
