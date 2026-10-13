using NUnit.Framework;
using UnityEngine;

/// <summary>머리 위 대시 아이콘 판정 고정 — 로컬·생존만 표시, 준비/미준비 색 선택. 에임 인디케이터 대시 틴트 토글.</summary>
public sealed class OverheadDashIconRulesTests
{
    [TestCase(true, PlayerLifeState.Alive, true)]
    [TestCase(true, PlayerLifeState.DeadPresentation, false)]
    [TestCase(true, PlayerLifeState.Soul, false)]
    [TestCase(true, PlayerLifeState.PermanentDead, false)]
    [TestCase(false, PlayerLifeState.Alive, false)]
    [TestCase(false, PlayerLifeState.Soul, false)]
    public void ShouldShow_OnlyLocalAndAlive(bool isLocal, PlayerLifeState state, bool expected)
    {
        Assert.That(OverheadDashIconRules.ShouldShow(isLocal, true, state), Is.EqualTo(expected));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public void ShouldShow_WithoutLifeCycle_TreatsAsAlive(bool isLocal, bool expected)
    {
        // 생명주기가 없으면 상태값은 무시한다.
        Assert.That(OverheadDashIconRules.ShouldShow(isLocal, false, PlayerLifeState.Soul), Is.EqualTo(expected));
    }

    [Test]
    public void Tint_PicksReadyOrNotReadyColor()
    {
        Color ready = Color.white;
        Color notReady = new Color(0.35f, 0.35f, 0.35f, 0.5f);

        Assert.That(OverheadDashIconRules.Tint(true, ready, notReady), Is.EqualTo(ready));
        Assert.That(OverheadDashIconRules.Tint(false, ready, notReady), Is.EqualTo(notReady));
    }

    [TestCase(false, true, false)]
    [TestCase(false, false, false)] // 기본(꺼짐) — 재충전 중에도 원래 색
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public void AimIndicator_DimsOnlyWhenTintEnabledAndNotReady(bool tintEnabled, bool ready, bool expected)
    {
        Assert.That(PlayerAimIndicator.IsDashTintDimmed(tintEnabled, ready), Is.EqualTo(expected));
    }
}
