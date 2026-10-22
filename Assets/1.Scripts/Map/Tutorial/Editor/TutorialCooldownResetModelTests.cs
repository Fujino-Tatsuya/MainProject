using NUnit.Framework;

/// <summary>
/// 복도1 첫 진입 쿨타임 초기화(v8 C1-0) — 플레이어별 1회 · G1 뒤 · 복도1 안 · 진행 중 행동 종료까지 예약 · 초기화 범위.
/// 원본: Docs/design/tutorial-scenario.md §3.2.
/// </summary>
public sealed class TutorialCooldownResetModelTests
{
    const ulong A = 0, B = 1;

    [Test]
    public void G1_전이거나_복도1_밖이면_초기화하지_않는다()
    {
        var m = new TutorialCooldownResetModel();
        Assert.IsFalse(m.TryBegin(A, g1Open: false, inCorridor1: true, busy: false), "존1 클리어 전");
        Assert.IsFalse(m.TryBegin(A, g1Open: true, inCorridor1: false, busy: false), "아직 존1");
        Assert.IsFalse(m.HasReset(A));
    }

    [Test]
    public void 플레이어별로_한_번만()
    {
        var m = new TutorialCooldownResetModel();
        Assert.IsTrue(m.TryBegin(A, true, true, false));
        Assert.IsFalse(m.TryBegin(A, true, true, false), "복도1 에 머무는 동안 반복 없음");
        Assert.IsFalse(m.TryBegin(A, true, false, false));
        Assert.IsFalse(m.TryBegin(A, true, true, false), "왕복·부활 재진입 반복 없음");
        Assert.IsTrue(m.TryBegin(B, true, true, false), "동료는 각자 첫 진입 때");
    }

    [Test]
    public void 첫_진입한_사람_때문에_동료를_미리_초기화하지_않는다()
    {
        var m = new TutorialCooldownResetModel();
        Assert.IsTrue(m.TryBegin(A, true, true, false));
        Assert.IsFalse(m.HasReset(B));
        Assert.IsFalse(m.TryBegin(B, true, false, false), "B 는 아직 존1");
    }

    [Test]
    public void 이미_복제_기록이_있으면_다시_하지_않는다()
    {
        var m = new TutorialCooldownResetModel();
        Assert.IsFalse(m.TryBegin(A, true, true, false, alreadyRecorded: true));
        Assert.IsTrue(m.HasReset(A));
    }

    [Test]
    public void 진행_중_행동이_있으면_끝날_때_한_번_더()
    {
        var m = new TutorialCooldownResetModel();
        Assert.IsTrue(m.TryBegin(A, true, true, busy: true), "지금 초기화는 한다");
        Assert.IsTrue(m.IsReserved(A));

        Assert.IsFalse(m.TickReservation(A, busy: true), "변신 중 — 대기");
        Assert.IsTrue(m.TickReservation(A, busy: false), "끝난 순간 한 번 더");
        Assert.IsFalse(m.IsReserved(A));
        Assert.IsFalse(m.TickReservation(A, busy: false), "한 번만");
    }

    [Test]
    public void 행동이_없으면_예약도_없다()
    {
        var m = new TutorialCooldownResetModel();
        m.TryBegin(A, true, true, busy: false);
        Assert.IsFalse(m.IsReserved(A));
        Assert.IsFalse(m.TickReservation(A, false));
    }

    [Test]
    public void 이탈하면_예약만_지운다()
    {
        var m = new TutorialCooldownResetModel();
        m.TryBegin(A, true, true, busy: true);
        m.Forget(A);
        Assert.IsFalse(m.IsReserved(A));
        Assert.IsTrue(m.HasReset(A));
    }

    [Test]
    public void 초기화_범위()
    {
        Assert.IsTrue(TutorialCooldownResetScope.Corridor1Includes(PlayerSkillSlot.Main));
        Assert.IsTrue(TutorialCooldownResetScope.Corridor1Includes(PlayerSkillSlot.Sub));
        Assert.IsTrue(TutorialCooldownResetScope.Corridor1Includes(PlayerSkillSlot.Ultimate));
        Assert.IsTrue(TutorialCooldownResetScope.Corridor1Includes(PlayerSkillSlot.Interrupt));
    }
}
