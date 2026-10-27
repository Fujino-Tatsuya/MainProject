using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 할 일 114 — 툴팁 레이아웃 순수 로직(슬롯 줄·태그 칩 줄바꿈). 러너 = 데이터 테이블 EditMode 테스트.
public sealed class SkillTooltipLayoutTests
{
    [Test]
    public void SlotLine_UsesSubtitle_ElseKeyBadge()
    {
        Assert.That(SkillTooltipView.SlotLineText("Q 스킬", "Q"), Is.EqualTo("Q 스킬"));
        Assert.That(SkillTooltipView.SlotLineText("", PassiveHUD.DisplayKey), Is.EqualTo(SkillTooltipView.KeyBadgeText(PassiveHUD.DisplayKey)));
        Assert.That(SkillTooltipView.SlotLineText("  ", null), Is.Empty);
    }

    [Test]
    public void FlowTags_FitsOnOneRow()
    {
        var widths = new List<float> { 50f, 60f };
        var positions = new List<Vector2>();

        float height = SkillTooltipView.FlowTags(widths, 200f, 6f, 26f, positions);

        Assert.That(height, Is.EqualTo(26f));
        Assert.That(positions, Is.EqualTo(new[] { new Vector2(0f, 0f), new Vector2(56f, 0f) }));
    }

    [Test]
    public void FlowTags_WrapsWhenRowIsFull()
    {
        var widths = new List<float> { 80f, 80f, 80f };
        var positions = new List<Vector2>();

        float height = SkillTooltipView.FlowTags(widths, 200f, 6f, 26f, positions);

        Assert.That(positions[1], Is.EqualTo(new Vector2(86f, 0f)));
        Assert.That(positions[2], Is.EqualTo(new Vector2(0f, -32f)));
        Assert.That(height, Is.EqualTo(58f));
    }

    [Test]
    public void FlowTags_ChipWiderThanRow_IsClampedOnItsOwnRow()
    {
        var widths = new List<float> { 40f, 500f };
        var positions = new List<Vector2>();

        SkillTooltipView.FlowTags(widths, 200f, 6f, 26f, positions);

        Assert.That(widths[1], Is.EqualTo(200f));
        Assert.That(positions[1], Is.EqualTo(new Vector2(0f, -32f)));
    }

    [Test]
    public void FlowTags_NoTags_IsZeroHeight()
    {
        var positions = new List<Vector2> { Vector2.one };

        Assert.That(SkillTooltipView.FlowTags(new List<float>(), 200f, 6f, 26f, positions), Is.EqualTo(0f));
        Assert.That(positions, Is.Empty);
    }
}
