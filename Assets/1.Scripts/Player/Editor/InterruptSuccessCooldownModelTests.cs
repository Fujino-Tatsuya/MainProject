using NUnit.Framework;

/// <summary>간파 성공 쿨 감소(할 일 26) — 감소량·클램프·시전당 1회.</summary>
public sealed class InterruptSuccessCooldownModelTests
{
    [Test]
    public void Reduction_IsFullCooldownTimesRatio()
    {
        Assert.That(InterruptSuccessCooldownModel.ComputeReduction(10f, 0.8f), Is.EqualTo(8f).Within(1e-5f));
        Assert.That(InterruptSuccessCooldownModel.ComputeReduction(10f, 0f), Is.Zero);
    }

    [Test]
    public void Reduction_ClampsRatioAndNegativeCooldown()
    {
        Assert.That(InterruptSuccessCooldownModel.ComputeReduction(10f, 1.5f), Is.EqualTo(10f));
        Assert.That(InterruptSuccessCooldownModel.ComputeReduction(10f, -0.5f), Is.Zero);
        Assert.That(InterruptSuccessCooldownModel.ComputeReduction(-3f, 0.8f), Is.Zero);
        Assert.That(InterruptSuccessCooldownModel.ComputeReduction(float.NaN, 0.8f), Is.Zero);
    }

    [Test]
    public void NoSuccess_NoReduction()
    {
        var model = new InterruptSuccessCooldownModel();
        model.BeginCast();

        Assert.That(model.TryClaimReduction(10f, 0.8f, out float seconds), Is.False);
        Assert.That(seconds, Is.Zero);
    }

    [Test]
    public void ManySuccessesInOneCast_ReduceOnce()
    {
        var model = new InterruptSuccessCooldownModel();
        model.BeginCast();
        model.NotifySuccess();
        model.NotifySuccess();
        model.NotifySuccess();

        Assert.That(model.TryClaimReduction(10f, 0.8f, out float seconds), Is.True);
        Assert.That(seconds, Is.EqualTo(8f).Within(1e-5f));
        Assert.That(model.TryClaimReduction(10f, 0.8f, out _), Is.False, "같은 시전에서 두 번째 감소 없음");
    }

    [Test]
    public void NextCast_StartsClean()
    {
        var model = new InterruptSuccessCooldownModel();
        model.BeginCast();
        model.NotifySuccess();
        model.TryClaimReduction(10f, 0.8f, out _);

        model.BeginCast();
        Assert.That(model.HasSucceeded, Is.False);
        Assert.That(model.TryClaimReduction(10f, 0.8f, out _), Is.False, "새 시전은 성공 기록 없이 시작");

        model.NotifySuccess();
        Assert.That(model.TryClaimReduction(10f, 0.8f, out _), Is.True);
    }

    [Test]
    public void ZeroRatio_ClaimsButReducesNothing()
    {
        var model = new InterruptSuccessCooldownModel();
        model.BeginCast();
        model.NotifySuccess();

        Assert.That(model.TryClaimReduction(10f, 0f, out float seconds), Is.False);
        Assert.That(seconds, Is.Zero);
        Assert.That(model.HasSucceeded, Is.True, "비율 0 이어도 성공 자체는 남는다(성공 이벤트는 난다)");
    }

    [Test]
    public void LedgerApply_SubtractsFromRemaining_AndClampsAtZero()
    {
        // 승인(t=0)에 쿨 10s 시작 → 판정(t=0.3) 성공 → 8s 차감 → 남은 1.7s.
        var ledger = new PlayerSkillCooldownLedger(1);
        ledger.Start(0, 0f, 10f);
        ledger.Reduce(0, 0.3f, InterruptSuccessCooldownModel.ComputeReduction(10f, 0.8f));
        Assert.That(ledger.GetRemaining(0, 0.3f), Is.EqualTo(1.7f).Within(1e-4f));

        // 남은 쿨이 감소량보다 작으면 0 에서 멈춘다.
        ledger.Start(0, 0f, 10f);
        ledger.Reduce(0, 5f, InterruptSuccessCooldownModel.ComputeReduction(10f, 0.8f));
        Assert.That(ledger.GetRemaining(0, 5f), Is.Zero);
        Assert.That(ledger.IsReady(0, 5f), Is.True);
    }

    [Test]
    public void LedgerReset_EndsCooldownNow()
    {
        var ledger = new PlayerSkillCooldownLedger(2);
        ledger.Start(0, 0f, 10f);
        ledger.Start(1, 0f, 10f);

        ledger.SetRemaining(0, 2f, 0f);

        Assert.That(ledger.IsReady(0, 2f), Is.True);
        Assert.That(ledger.GetRemaining(1, 2f), Is.EqualTo(8f), "다른 스킬 장부는 그대로");
    }
}
