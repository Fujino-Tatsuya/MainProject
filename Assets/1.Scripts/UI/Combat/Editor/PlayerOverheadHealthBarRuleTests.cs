using NUnit.Framework;

/// <summary>플레이어 머리 위 체력바 연출 판정 고정 — 숨김·엘리베이터 입력 잠금·보스 등장 잠금 중 하나라도 걸리면 감춘다.</summary>
public sealed class PlayerOverheadHealthBarRuleTests
{
    [TestCase(false, false, false, true)]
    [TestCase(true, false, false, false)]  // 연출 숨김
    [TestCase(false, true, false, false)]  // 엘리베이터 입력 잠금
    [TestCase(false, false, true, false)]  // 보스 등장 잠금
    [TestCase(true, true, false, false)]
    [TestCase(false, true, true, false)]
    [TestCase(true, false, true, false)]
    [TestCase(true, true, true, false)]
    public void IsVisibleDuringEncounter_HidesWhileAnyLockHeld(bool hidden, bool inputLocked, bool cinematicLocked, bool expected)
    {
        Assert.That(PlayerOverheadHealthBarRule.IsVisibleDuringEncounter(hidden, inputLocked, cinematicLocked), Is.EqualTo(expected));
    }

    [Test]
    public void IsVisibleDuringEncounter_StaysHiddenUntilLastLockReleased()
    {
        // 엘리베이터 입력 잠금이 풀려도 보스 등장 잠금이 남아 있으면 켜지지 않는다.
        Assert.That(PlayerOverheadHealthBarRule.IsVisibleDuringEncounter(false, true, true), Is.False);
        Assert.That(PlayerOverheadHealthBarRule.IsVisibleDuringEncounter(false, false, true), Is.False);
        Assert.That(PlayerOverheadHealthBarRule.IsVisibleDuringEncounter(false, false, false), Is.True);
    }
}
