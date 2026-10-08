using NUnit.Framework;

/// <summary>머리 위 체력바 게이지 계산 고정 — 실드 정규화, 잔상 지연·수렴·회복 즉시.</summary>
public sealed class OverheadHealthBarModelTests
{
    [Test]
    public void Layout_NoShield_FillsByMaxHealth()
    {
        var layout = OverheadHealthBarLayout.Compute(50, 100, 0);

        Assert.That(layout.Scale, Is.EqualTo(100f));
        Assert.That(layout.HealthFill, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(layout.ShieldFill, Is.EqualTo(0.5f).Within(1e-5f), "실드 없으면 체력 끝과 같다");
    }

    [Test]
    public void Layout_ShieldWithinMax_AppendsAfterHealth()
    {
        var layout = OverheadHealthBarLayout.Compute(50, 100, 20);

        Assert.That(layout.HealthFill, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(layout.ShieldFill, Is.EqualTo(0.7f).Within(1e-5f));
    }

    [Test]
    public void Layout_ShieldOverMax_NormalizesToHealthPlusShield()
    {
        var layout = OverheadHealthBarLayout.Compute(100, 100, 50);

        Assert.That(layout.Scale, Is.EqualTo(150f), "max(최대 체력, 체력+실드)");
        Assert.That(layout.HealthFill, Is.EqualTo(100f / 150f).Within(1e-5f));
        Assert.That(layout.ShieldFill, Is.EqualTo(1f), "넘치지 않는다");
    }

    [Test]
    public void Layout_ZeroMaxHealth_DoesNotDivideByZero()
    {
        var layout = OverheadHealthBarLayout.Compute(0, 0, 0);

        Assert.That(layout.HealthFill, Is.EqualTo(0f));
        Assert.That(layout.ShieldFill, Is.EqualTo(0f));
        Assert.That(layout.ToFill(10f), Is.EqualTo(0f));
    }

    [Test]
    public void Layout_ZeroMaxHealthWithShield_ShowsShieldOnly()
    {
        var layout = OverheadHealthBarLayout.Compute(0, 0, 30);

        Assert.That(layout.HealthFill, Is.EqualTo(0f));
        Assert.That(layout.ShieldFill, Is.EqualTo(1f));
    }

    [Test]
    public void Layout_Dead_ShowsZeroHealth()
    {
        var layout = OverheadHealthBarLayout.Compute(-5, 100, 0);

        Assert.That(layout.HealthFill, Is.EqualTo(0f), "음수 체력도 0 으로");
    }

    [Test]
    public void Trail_FirstStep_SnapsToHealth()
    {
        var trail = new OverheadHealthTrail();

        Assert.That(trail.Step(80f, 0.1f, 0.5f, 100f), Is.EqualTo(80f));
    }

    [Test]
    public void Trail_Damage_HoldsThenConvergesToHealth()
    {
        var trail = new OverheadHealthTrail();
        trail.Step(100f, 0f, 0.5f, 100f);

        trail.Step(60f, 0.1f, 0.5f, 100f); // 피격 — 지연 시작
        Assert.That(trail.Value, Is.EqualTo(100f), "피격 프레임엔 그대로");

        trail.Step(60f, 0.3f, 0.5f, 100f);
        Assert.That(trail.Value, Is.EqualTo(100f), "지연 중엔 그대로");

        trail.Step(60f, 0.3f, 0.5f, 100f); // 지연 소진
        trail.Step(60f, 0.2f, 0.5f, 100f); // 100/s × 0.2s = 20 내려감
        Assert.That(trail.Value, Is.EqualTo(80f).Within(1e-4f));

        for (int i = 0; i < 20; i++)
            trail.Step(60f, 0.1f, 0.5f, 100f);
        Assert.That(trail.Value, Is.EqualTo(60f), "체력에 수렴하고 그 아래로 안 내려간다");
    }

    [Test]
    public void Trail_RepeatedDamage_RestartsDelay()
    {
        var trail = new OverheadHealthTrail();
        trail.Step(100f, 0f, 0.5f, 100f);
        trail.Step(80f, 0.1f, 0.5f, 100f);
        trail.Step(80f, 0.4f, 0.5f, 100f);

        trail.Step(60f, 0.1f, 0.5f, 100f); // 지연 거의 끝났을 때 또 맞음
        trail.Step(60f, 0.4f, 0.5f, 100f);
        Assert.That(trail.Value, Is.EqualTo(100f), "새 피격마다 지연을 처음부터");
    }

    [Test]
    public void Trail_Heal_SnapsImmediately()
    {
        var trail = new OverheadHealthTrail();
        trail.Step(100f, 0f, 0.5f, 100f);
        trail.Step(40f, 0.1f, 0.5f, 100f);

        trail.Step(120f, 0.1f, 0.5f, 100f);
        Assert.That(trail.Value, Is.EqualTo(120f), "회복은 즉시 맞춘다");

        trail.Step(120f, 1f, 0.5f, 100f);
        Assert.That(trail.Value, Is.EqualTo(120f));
    }

    [Test]
    public void Trail_HealDuringTrail_AboveTrail_Snaps_BelowTrail_KeepsTrail()
    {
        var trail = new OverheadHealthTrail();
        trail.Step(100f, 0f, 0.5f, 100f);
        trail.Step(40f, 0.1f, 0.5f, 100f);

        trail.Step(70f, 0.1f, 0.5f, 100f); // 잔상(100) 아래로 회복 — 잔상은 남아 계속 지연/수렴
        Assert.That(trail.Value, Is.EqualTo(100f));
        for (int i = 0; i < 30; i++)
            trail.Step(70f, 0.1f, 0.5f, 100f);
        Assert.That(trail.Value, Is.EqualTo(70f));
    }
}
