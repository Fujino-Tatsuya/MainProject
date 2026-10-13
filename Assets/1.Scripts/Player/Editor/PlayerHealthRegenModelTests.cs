using NUnit.Framework;

/// <summary>플레이어 기본 체력 재생(할 일 31) — 지연·틱·상한·피격 리셋·누적기·차단. 원본: Docs/tech/party-lives-respawn.md §8.</summary>
public sealed class PlayerHealthRegenModelTests
{
    const float Delay = 5f;
    const float Rate = 0.02f;
    const int Max = 100;

    static PlayerHealthRegenModel Started(double now)
    {
        var model = new PlayerHealthRegenModel();
        Assert.AreEqual(0, model.Tick(now, false, 50, Max, Max, Delay, Rate)); // 첫 틱 = 기준 시각만
        return model;
    }

    static int Tick(PlayerHealthRegenModel model, double now, int hp, bool blocked = false)
        => model.Tick(now, blocked, hp, Max, Max, Delay, Rate);

    [Test]
    public void FirstTick_OnlyAnchors_NoHeal()
    {
        var model = new PlayerHealthRegenModel();
        Assert.AreEqual(0, model.Tick(100.0, false, 50, Max, Max, Delay, Rate));
        Assert.AreEqual(0, Tick(model, 104.9, 50));
    }

    [Test]
    public void Delay_NoHealBefore_HealsOnlyTimeAfterDelay()
    {
        var model = Started(0.0);
        Assert.AreEqual(0, Tick(model, 4.0, 50));
        Assert.AreEqual(0, Tick(model, 5.0, 50));
        // 5초 경계부터 센다 — 4초 틱 → 6초 틱이면 지연 뒤 1초분(2HP)만.
        var model2 = Started(0.0);
        Assert.AreEqual(0, Tick(model2, 4.0, 50));
        Assert.AreEqual(2, Tick(model2, 6.0, 50));
    }

    [Test]
    public void Rate_IsPercentOfRateBasePerSecond()
    {
        var model = Started(0.0);
        Assert.AreEqual(0, Tick(model, 5.0, 50));
        Assert.AreEqual(10, Tick(model, 10.0, 50)); // 5초 × 2HP
    }

    [Test]
    public void RateBase_DiffersFromCap()
    {
        var model = new PlayerHealthRegenModel();
        model.Tick(0.0, false, 50, 100, 200, Delay, Rate);
        Assert.AreEqual(4, model.Tick(6.0, false, 50, 100, 200, Delay, Rate)); // 200 × 2% × 1초
    }

    [Test]
    public void Cap_StopsAtMax_AndDropsFraction()
    {
        var model = Started(0.0);
        Assert.AreEqual(3, Tick(model, 15.0, 97)); // 20HP 분량이지만 모자란 3만
        Assert.AreEqual(0.0, model.PendingFraction);
        Assert.AreEqual(0, Tick(model, 16.0, Max));
    }

    [Test]
    public void FullHealth_DoesNotBankRegen()
    {
        var model = Started(0.0);
        Assert.AreEqual(0, Tick(model, 30.0, Max)); // 가득 찬 채 오래 대기
        Assert.AreEqual(0, Tick(model, 30.1, 90));  // 깎인 직후 한꺼번에 튀지 않는다(0.2HP 분량)
    }

    [Test]
    public void Accumulator_SmallTicksLoseNothing()
    {
        var model = Started(0.0);
        Tick(model, 5.0, 50);
        int healed = 0;
        int hp = 50;
        // 1/60초 틱 × 300 = 5초 → 10HP. 한 틱 분량(0.033HP)은 정수 미만이다.
        for (int i = 1; i <= 300; i++)
        {
            int h = Tick(model, 5.0 + i / 60.0, hp);
            healed += h;
            hp += h;
        }
        // 부동소수 합이 9.999… 로 끝날 수 있어 아주 짧은 틱 하나로 마무리한다.
        healed += Tick(model, 10.01, hp);
        Assert.AreEqual(10, healed);
    }

    [Test]
    public void Hit_StopsImmediately_RestartsDelay_DropsFraction()
    {
        var model = Started(0.0);
        Assert.AreEqual(1, Tick(model, 5.75, 50)); // 1.5HP → 1, 0.5 남음
        Assert.Greater(model.PendingFraction, 0.0);

        model.NotifyHit(6.0);
        Assert.AreEqual(0.0, model.PendingFraction);
        Assert.AreEqual(0, Tick(model, 10.9, 40));
        Assert.AreEqual(0, Tick(model, 11.0, 40));
        Assert.AreEqual(2, Tick(model, 12.0, 40));
    }

    [Test]
    public void Blocked_NoHeal_AndDelayRestartsWhenUnblocked()
    {
        var model = Started(0.0);
        Assert.AreEqual(0, Tick(model, 10.0, 50, blocked: true)); // 지연은 지났지만 차단
        Assert.AreEqual(0, Tick(model, 20.0, 50, blocked: true));
        // 부활·리스폰 직후 = 차단이 풀린 첫 틱부터 다시 지연. 20초 기준이라 25초까지는 0.
        Assert.AreEqual(0, Tick(model, 24.9, 50));
        Assert.AreEqual(2, Tick(model, 26.0, 50));
    }

    [Test]
    public void ZeroHealth_IsBlocked()
    {
        var model = Started(0.0);
        Assert.AreEqual(0, Tick(model, 10.0, 0));
        Assert.AreEqual(0, Tick(model, 14.0, 1)); // 0 이었던 시점(10초)부터 다시 지연
        Assert.AreEqual(2, Tick(model, 16.0, 1));
    }

    [Test]
    public void ZeroRate_NoHeal()
    {
        var model = new PlayerHealthRegenModel();
        model.Tick(0.0, false, 50, Max, Max, Delay, 0f);
        Assert.AreEqual(0, model.Tick(100.0, false, 50, Max, Max, Delay, 0f));
    }

    [Test]
    public void ZeroDelay_HealsFromFirstInterval()
    {
        var model = new PlayerHealthRegenModel();
        model.Tick(0.0, false, 50, Max, Max, 0f, Rate);
        Assert.AreEqual(2, model.Tick(1.0, false, 50, Max, Max, 0f, Rate));
    }
}
