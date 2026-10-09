using NUnit.Framework;

public sealed class BasicAttackDashCancelTests
{
    [Test]
    public void EventWindowAllowsCancelOnlyBeforeOpen()
    {
        Assert.That(BasicAttackDashCancel.BeforeComboWindowEvent(false), Is.True);
        Assert.That(BasicAttackDashCancel.BeforeComboWindowEvent(true), Is.False);
    }

    [Test]
    public void TimedWindowAllowsCancelBeforeFirstShot()
    {
        Assert.That(BasicAttackDashCancel.BeforeTimedComboWindow(10f, -1f, 0.05f), Is.True);
    }

    [Test]
    public void TimedWindowAllowsCancelRightAfterShot()
    {
        Assert.That(BasicAttackDashCancel.BeforeTimedComboWindow(10.04f, 10f, 0.05f), Is.True);
    }

    [Test]
    public void TimedWindowBlocksCancelOnceWindowOpens()
    {
        Assert.That(BasicAttackDashCancel.BeforeTimedComboWindow(10.06f, 10f, 0.05f), Is.False);
        Assert.That(BasicAttackDashCancel.BeforeTimedComboWindow(10.3f, 10f, 0.05f), Is.False);
    }

    [Test]
    public void TimedWindowReopensCancelForNextShot()
    {
        // 홀드 연사: 다음 발이 나가면 그 발 기준으로 다시 창 전이다.
        Assert.That(BasicAttackDashCancel.BeforeTimedComboWindow(10.36f, 10.35f, 0.05f), Is.True);
    }
}
