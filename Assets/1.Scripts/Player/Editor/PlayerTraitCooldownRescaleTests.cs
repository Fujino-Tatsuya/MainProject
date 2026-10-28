using NUnit.Framework;

/// <summary>
/// 할 일 125 (2/5) — 특성 쿨 배율이 바뀔 때 돌던 쿨의 진행률 보존(명세 §6): 새 남은 = 기존 남은 / 기존 전체 × 새 전체.
/// 준비된 스킬·전체 0 은 그대로, 장부는 마지막 Start 의 전체를 기억한다.
/// </summary>
public sealed class PlayerTraitCooldownRescaleTests
{
    const float Eps = 1e-4f;

    [Test]
    public void Rules_PreservesProgress()
    {
        Assert.AreEqual(3.4f, TraitModifierRules.RescaleRemaining(4f, 7f, 5.95f), Eps);
        Assert.AreEqual(6f, TraitModifierRules.RescaleRemaining(3f, 5f, 10f), Eps);
    }

    [Test]
    public void Rules_ReadyOrZeroFull_Unchanged()
    {
        Assert.AreEqual(0f, TraitModifierRules.RescaleRemaining(0f, 7f, 3f), "이미 준비 = 준비 유지");
        Assert.AreEqual(2f, TraitModifierRules.RescaleRemaining(2f, 0f, 5f), "기존 전체 0 = 나누지 않는다");
    }

    [Test]
    public void Ledger_Start_RemembersFull()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        Assert.AreEqual(0f, ledger.GetFull(0));
        ledger.Start(0, 10f, 5.95f);
        Assert.AreEqual(5.95f, ledger.GetFull(0), Eps);

        ledger.SetRemaining(0, 11f, 1f);
        Assert.AreEqual(5.95f, ledger.GetFull(0), Eps, "남은 시간만 바꾸는 미러·차감은 전체를 바꾸지 않는다");
    }

    [Test]
    public void Ledger_Rescale_RunningCooldown()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        ledger.Start(0, 0f, 7f);              // 기본 E 7초
        Assert.IsTrue(ledger.Rescale(0, 3f, 5.95f)); // 남은 4초에서 수호 재정비(0.85) 획득
        Assert.AreEqual(3.4f, ledger.GetRemaining(0, 3f), Eps);
        Assert.AreEqual(5.95f, ledger.GetFull(0), Eps);

        Assert.IsFalse(ledger.Rescale(0, 3f, 5.95f), "같은 전체 = 변화 없음");
        Assert.IsTrue(ledger.Rescale(0, 3f, 7f), "특성 초기화 = 원래 전체로 되돌림");
        Assert.AreEqual(4f, ledger.GetRemaining(0, 3f), Eps);
    }

    [Test]
    public void Ledger_Rescale_ReadySkillStaysReady()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        ledger.Start(0, 0f, 5f);
        Assert.IsFalse(ledger.Rescale(0, 6f, 2.5f));
        Assert.IsTrue(ledger.IsReady(0, 6f));
        Assert.AreEqual(2.5f, ledger.GetFull(0), Eps, "다음 진행률 기준은 새 전체");
    }

    [Test]
    public void Ledger_Rescale_NeverStarted_NoChange()
    {
        var ledger = new PlayerSkillCooldownLedger(2);
        Assert.IsFalse(ledger.Rescale(1, 1f, 4f));
        Assert.IsTrue(ledger.IsReady(1, 1f));
        Assert.IsFalse(ledger.Rescale(5, 1f, 4f), "범위 밖 인덱스");
    }
}
