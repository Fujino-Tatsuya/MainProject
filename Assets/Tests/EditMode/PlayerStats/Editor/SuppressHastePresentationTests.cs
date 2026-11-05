using NUnit.Framework;

// 할 일 174 — 제압 가속 연출: N타 줌인(기본 4타, 233 — 셈 테스트는 N=3 을 명시해 쓴다)(셈·리셋·펄스 모양·슬로우 줌 중 건너뜀) · 보스 체력바 잔상 멈춤.
public sealed class SuppressHastePresentationTests
{
    static readonly SuppressHasteHitZoomValues Values = new SuppressHasteHitZoomValues(3, 0.9f, 0.04f, 0.11f, 0.12f);

    // ── 3타 셈 ──

    [Test]
    public void HitZoom_EveryThirdHitStartsPulse()
    {
        var zoom = new SuppressHasteHitZoom();

        Assert.That(zoom.RegisterHit(true, 3), Is.False);
        Assert.That(zoom.RegisterHit(true, 3), Is.False);
        Assert.That(zoom.RegisterHit(true, 3), Is.True);
        Assert.That(zoom.PulseActive, Is.True);
        Assert.That(zoom.Hits, Is.EqualTo(0));

        Assert.That(zoom.RegisterHit(true, 3), Is.False);
        Assert.That(zoom.RegisterHit(true, 3), Is.False);
        Assert.That(zoom.RegisterHit(true, 3), Is.True);
    }

    [Test]
    public void HitZoom_HitsOutsideHaste_NotCounted()
    {
        var zoom = new SuppressHasteHitZoom();

        Assert.That(zoom.RegisterHit(false, 3), Is.False);
        Assert.That(zoom.RegisterHit(false, 3), Is.False);
        Assert.That(zoom.RegisterHit(false, 3), Is.False);
        Assert.That(zoom.Hits, Is.EqualTo(0));
        Assert.That(zoom.PulseActive, Is.False);
    }

    [Test]
    public void HitZoom_HasteEnd_ResetsCount()
    {
        var zoom = new SuppressHasteHitZoom();
        zoom.RegisterHit(true, 3);
        zoom.RegisterHit(true, 3);

        zoom.Step(false, false, 0.016f, Values);   // 가속 끝
        Assert.That(zoom.Hits, Is.EqualTo(0));

        // 다음 가속에서 다시 3타부터.
        Assert.That(zoom.RegisterHit(true, 3), Is.False);
        Assert.That(zoom.RegisterHit(true, 3), Is.False);
        Assert.That(zoom.RegisterHit(true, 3), Is.True);
    }

    [Test]
    public void HitZoom_EveryHitsBelowOne_TreatedAsOne()
    {
        var zoom = new SuppressHasteHitZoom();
        Assert.That(zoom.RegisterHit(true, 0), Is.True);
    }

    // ── 펄스 모양 ──

    [TestCase(0f, 1f)]
    [TestCase(0.02f, 0.95f)]
    [TestCase(0.04f, 0.9f)]
    [TestCase(0.10f, 0.9f)]
    [TestCase(0.21f, 0.95f)]
    [TestCase(0.27f, 1f)]
    [TestCase(1f, 1f)]
    public void PulseMultiplier_InHoldReturn(float t, float expected)
    {
        Assert.That(SuppressHasteHitZoom.PulseMultiplier(t, Values), Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void Step_PulseRunsAndEndsAtOne()
    {
        var zoom = new SuppressHasteHitZoom();
        zoom.RegisterHit(true, 1);

        float m = zoom.Step(true, false, 0.08f, Values);
        Assert.That(m, Is.EqualTo(0.9f).Within(1e-4f), "유지 구간");

        m = zoom.Step(true, false, 0.5f, Values);
        Assert.That(m, Is.EqualTo(1f), "끝나면 정확히 1");
        Assert.That(zoom.PulseActive, Is.False);
    }

    [Test]
    public void Step_NoPulse_IsOne()
    {
        var zoom = new SuppressHasteHitZoom();
        Assert.That(zoom.Step(true, false, 0.016f, Values), Is.EqualTo(1f));
    }

    [Test]
    public void Step_SlowZoomActive_SkipsPulse()
    {
        var zoom = new SuppressHasteHitZoom();
        zoom.RegisterHit(true, 1);

        // 슬로우 줌이 걸린 프레임 — 펄스를 버리고 1(곱하지 않음).
        Assert.That(zoom.Step(true, true, 0.05f, Values), Is.EqualTo(1f));
        Assert.That(zoom.PulseActive, Is.False);
        // 슬로우 줌이 끝나도 버린 펄스는 다시 나오지 않는다.
        Assert.That(zoom.Step(true, false, 0.05f, Values), Is.EqualTo(1f));
    }

    [Test]
    public void Step_HasteEndsMidPulse_PulseFinishes()
    {
        var zoom = new SuppressHasteHitZoom();
        zoom.RegisterHit(true, 1);

        float m = zoom.Step(false, false, 0.08f, Values);
        Assert.That(m, Is.EqualTo(0.9f).Within(1e-4f), "복귀 도중 튀지 않게 끝까지 돈다");
    }

    [Test]
    public void ZoomFactor_ClampedToPullOnly()
    {
        Assert.That(SuppressHasteHitZoom.ClampZoomFactor(1.5f), Is.EqualTo(1f));
        Assert.That(SuppressHasteHitZoom.ClampZoomFactor(0f), Is.EqualTo(SuppressHasteHitZoom.MinZoomFactor));
        Assert.That(SuppressHasteHitZoom.ClampZoomFactor(float.NaN), Is.EqualTo(1f));
    }

    [Test]
    public void DefaultValues_MatchSheetDefaults()
    {
        SuppressHasteHitZoomValues d = SuppressHasteHitZoomValues.Default;
        Assert.That(d.EveryHits, Is.EqualTo(4));   // 할 일 233 — 3타 → 4타
        Assert.That(d.ZoomFactor, Is.EqualTo(0.9f));
        Assert.That(d.InSeconds + d.HoldSeconds, Is.EqualTo(0.15f).Within(1e-4f), "약 0.15초 뒤 복귀 시작");
    }

    // ── 보스 체력바 잔상 멈춤 ──

    [Test]
    public void FreezeRule_FrozenDisplayed_NeverDrops_RisesToHp()
    {
        Assert.That(DelayedHealthFreezeRule.FrozenDisplayed(800f, 500), Is.EqualTo(800f));
        Assert.That(DelayedHealthFreezeRule.FrozenDisplayed(800f, 900), Is.EqualTo(900f));
        Assert.That(DelayedHealthFreezeRule.FrozenDisplayed(10f, -5), Is.EqualTo(10f));
    }

    [Test]
    public void FreezeRule_PendingOnRelease()
    {
        Assert.That(DelayedHealthFreezeRule.PendingOnRelease(800f, 500), Is.EqualTo(300));
        Assert.That(DelayedHealthFreezeRule.PendingOnRelease(500f, 600), Is.EqualTo(0));
        Assert.That(DelayedHealthFreezeRule.PendingOnRelease(500.6f, 500), Is.EqualTo(1));
    }

    [Test]
    public void DelayedBar_Frozen_AccumulatesDamage_ThenHoldsAndDrains()
    {
        var bar = new DelayedHealthBar();   // Image 없음 — 내부 값만 돈다
        bar.Bind(1000);
        bar.SetFrozen(true, 1000);

        // 제압 동안 여러 타 — 평소라면 0.4초 뒤 내려가고 조각 5개 초과는 놓지만, 멈춘 동안은 그대로 쌓인다.
        int hp = 1000;
        for (int i = 0; i < 8; i++)
        {
            bar.OnHpChanged(hp, hp - 50);
            hp -= 50;
            bar.Tick(0.5f, hp, 1000);
        }
        Assert.That(Displayed(bar), Is.EqualTo(1000f), "멈춘 동안 잔상은 제자리");

        // 제압 끝 — 쌓인 400 을 holdSeconds(0.4) 동안 붙잡는다.
        bar.SetFrozen(false, hp);
        bar.Tick(0.3f, hp, 1000);
        Assert.That(Displayed(bar), Is.EqualTo(1000f), "머무는 중");

        // 머문 뒤 평소 속도(최대 HP × 0.8 /초)로 내려간다.
        bar.Tick(0.2f, hp, 1000);   // 머묾 끝(0.5 ≥ 0.4) — 이번 프레임부터 내려감
        Assert.That(Displayed(bar), Is.LessThan(1000f));
        bar.Tick(1f, hp, 1000);
        Assert.That(Displayed(bar), Is.EqualTo(hp).Within(1e-3f));
    }

    [Test]
    public void DelayedBar_HealWhileFrozen_KeepsWhite()
    {
        var bar = new DelayedHealthBar();
        bar.Bind(1000);
        bar.SetFrozen(true, 1000);
        bar.OnHpChanged(1000, 600);
        bar.OnHpChanged(600, 700);   // 회복
        bar.Tick(0.1f, 700, 1000);
        Assert.That(Displayed(bar), Is.EqualTo(1000f));
    }

    [Test]
    public void DelayedBar_NotFrozen_BehavesAsBefore()
    {
        var bar = new DelayedHealthBar();
        bar.Bind(1000);
        bar.OnHpChanged(1000, 600);
        bar.Tick(0.3f, 600, 1000);
        Assert.That(Displayed(bar), Is.EqualTo(1000f), "0.4초 머묾");
        bar.Tick(0.2f, 600, 1000);
        bar.Tick(1f, 600, 1000);
        Assert.That(Displayed(bar), Is.EqualTo(600f).Within(1e-3f));
    }

    static float Displayed(DelayedHealthBar bar) =>
        (float)typeof(DelayedHealthBar)
            .GetField("displayed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(bar);
}
