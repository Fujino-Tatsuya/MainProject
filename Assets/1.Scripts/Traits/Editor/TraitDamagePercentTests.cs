using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 171 — 특성 카드·획득 툴팁 설명 {dmg}: 평소 = 퍼센트 글자('공격력의 150%'), Shift 누르는 동안 = 계산 숫자(검 아이콘 + 숫자, 계산식 없음).
/// 공용 포매터(<see cref="SkillTooltipFormatter"/>)를 나눴어도 전투 HUD 스킬 툴팁 결과는 그대로여야 한다.
/// </summary>
public class TraitDamagePercentTests
{
    private const string Color = "#F4B860";
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    private static string Percent(string inner) => $"<color={Color}>{inner}</color>";

    private static string Line(string percent) =>
        TraitUiStrings.DamagePercent.Format(new Dictionary<string, string> { [TraitUiStrings.PercentToken] = percent });

    private static string LineFlat(string flat, string percent) =>
        TraitUiStrings.DamagePercentFlat.Format(new Dictionary<string, string>
        {
            [TraitUiStrings.FlatToken] = flat,
            [TraitUiStrings.PercentToken] = percent,
        });

    // ── 평소 = 퍼센트 글자 ─────────────────────────────────────────────

    [Test]
    public void Percent_SingleCoefficient()
    {
        string text = TraitTooltip.FormatDamagePercent(new SkillTooltipDamage(150, 150, 1.5f, 1.5f));
        Assert.AreEqual(Percent(Line("150%")), text);
        StringAssert.DoesNotContain("<sprite", text, "평소 표시는 검 아이콘 없음");
        StringAssert.DoesNotContain(">150<", text, "계산 숫자 아님");
    }

    [Test]
    public void Percent_KoreanDefault_ReadsAsAttackPercent()
    {
        // 기본 ko 문구 — 시트에 행이 생겨도 같은 뜻이어야 한다(문구 자체는 Strings 가 원본).
        // '공격력의 {percent}' — 설명 '{dmg} 피해'(설명에 '의' 없음)가 '공격력의 450% 피해' 가 되게.
        Assert.AreEqual("공격력의 {percent}", TraitUiStrings.DamagePercent.Ko);
        Assert.AreEqual("{flat} + 공격력의 {percent}", TraitUiStrings.DamagePercentFlat.Ko);
    }

    [Test]
    public void Percent_CoefficientRange_PercentOnce()
    {
        string text = TraitTooltip.FormatDamagePercent(new SkillTooltipDamage(100, 150, 1f, 1.5f));
        Assert.AreEqual(Percent(Line("100~150%")), text);
    }

    [Test]
    public void Percent_FlatBonus_Prefixed()
    {
        string text = TraitTooltip.FormatDamagePercent(new SkillTooltipDamage(170, 170, 1.5f, 1.5f, 20, 20));
        Assert.AreEqual(Percent(LineFlat("20", "150%")), text);
    }

    [Test]
    public void Percent_FlatRange()
    {
        string text = TraitTooltip.FormatDamagePercent(new SkillTooltipDamage(10, 20, 0.25f, 0.5f, 10, 20));
        Assert.AreEqual(Percent(LineFlat("10~20", "25~50%")), text);
    }

    [Test]
    public void Percent_FractionalCoefficient_OneDecimal()
    {
        string text = TraitTooltip.FormatDamagePercent(new SkillTooltipDamage(0, 0, 0.125f, 0.125f));
        Assert.AreEqual(Percent(Line("12.5%")), text);
    }

    // ── TraitTooltip.Format — 평소/Shift ────────────────────────────────

    private (TraitDefinition def, TraitContentRegistry registry, Player player) Setup(SkillTooltipDamage damage)
    {
        var def = TraitContentTestKit.Make<PaladinTraitDefinition>("PAL_2_DASH", "이름", "분류", "");
        created.Add(def);
        var registry = new TraitContentRegistry();
        registry.For(TutorialCharacter.Paladin).Damage("PAL_2_DASH", (t, p) => damage);
        var go = new GameObject("TraitDamagePercentTests.Player");
        go.hideFlags = HideFlags.HideAndDontSave;
        created.Add(go);
        return (def, registry, go.AddComponent<Player>());
    }

    [Test]
    public void Format_Default_IsPercentText()
    {
        var damage = new SkillTooltipDamage(180, 180, 1.5f, 1.5f, 20, 20);
        var (def, registry, player) = Setup(damage);
        Assert.IsTrue(TraitTooltip.Format(def, "{dmg} 피해", player, false, registry, out string text, out string error), error);
        Assert.AreEqual("<nobr>" + Percent(LineFlat("20", "150%")) + "</nobr> 피해", text);
    }

    [Test]
    public void Format_Default_KoreanReadsWithoutDoubleUi()
    {
        // ko 기본 문구 기준 — '공격력의 450% 피해'(표기 부분은 nobr 로 한 덩어리).
        var (def, registry, player) = Setup(new SkillTooltipDamage(450, 450, 4.5f, 4.5f));
        Assert.IsTrue(TraitTooltip.Format(def, "{dmg} 피해", player, false, registry, out string text, out string error), error);
        string plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        Assert.AreEqual(Line("450%") + " 피해", plain);
        Assert.AreEqual("공격력의 450% 피해", TraitUiStrings.DamagePercent.Ko.Replace("{percent}", "450%") + " 피해");
    }

    [Test]
    public void Format_DamageNotation_WrappedInNoBreak_BothModes()
    {
        var damage = new SkillTooltipDamage(100, 150, 1f, 1.5f, 20, 20);
        var (def, registry, player) = Setup(damage);
        Assert.IsTrue(TraitTooltip.Format(def, "앞 {dmg} 뒤", player, false, registry, out string normal, out string e1), e1);
        Assert.AreEqual("앞 <nobr>" + TraitTooltip.FormatDamagePercent(damage) + "</nobr> 뒤", normal);
        Assert.IsTrue(TraitTooltip.Format(def, "앞 {dmg} 뒤", player, true, registry, out string held, out string e2), e2);
        Assert.AreEqual("앞 <nobr>" + SkillTooltipFormatter.DamageValue(damage) + "</nobr> 뒤", held);
    }

    [Test]
    public void Format_ShiftHeld_IsCalculatedValueWithoutFormula()
    {
        var damage = new SkillTooltipDamage(180, 180, 1.5f, 1.5f, 20, 20);
        var (def, registry, player) = Setup(damage);
        Assert.IsTrue(TraitTooltip.Format(def, "{dmg} 피해", player, true, registry, out string text, out string error), error);
        Assert.AreEqual($"<nobr>{SkillTooltipFormatter.DamageValue(damage)}</nobr> 피해", text);
        Assert.AreEqual($"<nobr><color={Color}><sprite name=\"atk\" color={Color}>180</color></nobr> 피해", text);
        StringAssert.DoesNotContain(" = (", text, "계산식 없이 숫자만");
    }

    [Test]
    public void Format_NoPlayer_IsUnknownInBothModes()
    {
        var (def, registry, _) = Setup(new SkillTooltipDamage(1, 1, 1f, 1f));
        foreach (bool showValue in new[] { false, true })
        {
            Assert.IsTrue(TraitTooltip.Format(def, "{dmg}", null, showValue, registry, out string text, out string error), error);
            Assert.AreEqual(TraitUiStrings.DamageUnknown.Text, text, $"showValue={showValue}");
        }
    }

    // ── 스킬 툴팁 결과 불변 ────────────────────────────────────────────

    [Test]
    public void SkillTooltip_OutputUnchanged()
    {
        // 할 일 171 이전 FormatDamage 출력 그대로(SkillTooltipView 경로).
        var damage = new SkillTooltipDamage(120, 360, 0.6f, 2.7f, 10, 10);
        Assert.IsTrue(SkillTooltipFormatter.TryFormat("피해 {dmg}", null, damage, false, out string compact, out _, out string e1), e1);
        Assert.IsTrue(SkillTooltipFormatter.TryFormat("피해 {dmg}", null, damage, true, out string detailed, out _, out string e2), e2);

        string value = $"<color={Color}><sprite name=\"atk\" color={Color}>120~360</color>";
        Assert.AreEqual("피해 " + value, compact);
        Assert.AreEqual($"피해 {value} = (10 + <sprite name=\"atk\" color={Color}>60%~270%)", detailed);

        var single = new SkillTooltipDamage(150, 150, 1.5f, 1.5f);
        Assert.IsTrue(SkillTooltipFormatter.TryFormat("{dmg}", null, single, true, out string singleText, out _, out string e3), e3);
        Assert.AreEqual($"<color={Color}><sprite name=\"atk\" color={Color}>150</color> = (0 + <sprite name=\"atk\" color={Color}>150%)", singleText);
        StringAssert.DoesNotContain("<nobr>", compact + detailed + singleText, "nobr 감싸기는 특성 설명 전용");
        StringAssert.DoesNotContain("<b>", compact + detailed + singleText, "굵게는 특성 설명 전용");
    }

    // ── 화면 설명 본문 굵게(171 후속) ──────────────────────────────────

    [Test]
    public void DescriptionOf_WrapsWholeBodyInBold_SpacingCompensated()
    {
        var def = TraitContentTestKit.Make<PaladinTraitDefinition>("PAL_TEST_BOLD", "이름", "분류", "아군을 <style=heal>회복</style>합니다.");
        created.Add(def);
        string text = TraitCardView.DescriptionOf(def, null, null, false);
        Assert.AreEqual("<b><cspace=-0.07em>아군을 <style=heal>회복</style>합니다.</cspace></b>", text);
        Assert.AreEqual(text, TraitCardView.DescriptionOf(def, null, null, true), "Shift 도 같은 굵게");
    }

    [Test]
    public void Bold_Empty_StaysEmpty()
    {
        Assert.AreEqual(string.Empty, TraitTooltip.Bold(string.Empty));
        Assert.IsNull(TraitTooltip.Bold(null));
    }
}
