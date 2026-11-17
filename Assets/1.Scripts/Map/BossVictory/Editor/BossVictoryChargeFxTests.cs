using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 거너 Q 차징 이펙트 세부 조절(할 일 295) — 순수 함수:
/// 새 칸 기본값 = 지금 모습(배율 1·라이트·흔들림 0) · 마커 사이 파트 값 보간(구체 크기와 같은 곡선) · 반복 간격 계단 규칙 ·
/// 레이저 페이드아웃 · 발사 번쩍 · 밖에서 쓰는 칸 기준값 추적 · 곡선 배율 · 렌더 모드 → 파트.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictory).
/// </summary>
public sealed class BossVictoryChargeFxTests
{
    const float Tolerance = 1e-4f;

    static BossVictoryGunnerCharge.Times Times(float start, float s1, float s2, float s3, float fire) =>
        new BossVictoryGunnerCharge.Times { ChargeStart = start, Stage1 = s1, Stage2 = s2, Stage3 = s3, Fire = fire };

    static BossVictoryGunnerQ Look()
    {
        var q = new BossVictoryGunnerQ { startScale = 0.2f };
        q.stage1 = BossVictoryGunnerQ.Stage.Make(0.5f);
        q.stage2 = BossVictoryGunnerQ.Stage.Make(0.8f);
        q.stage3 = BossVictoryGunnerQ.Stage.Make(1.2f);
        return q;
    }

    static void AssertIdentity(in BossVictoryGunnerCharge.PartSample p, string label)
    {
        Assert.AreEqual(1f, p.Size, Tolerance, label + " 크기");
        Assert.AreEqual(1f, p.Amount, Tolerance, label + " 양");
        Assert.AreEqual(0f, p.RepeatInterval, Tolerance, label + " 반복 간격(0 = 프리팹)");
        Assert.AreEqual(1f, p.Lifetime, Tolerance, label + " 수명");
        Assert.AreEqual(1f, p.Speed, Tolerance, label + " 속도");
        Assert.AreEqual(1f, p.Brightness, Tolerance, label + " 밝기");
        Assert.AreEqual(1f, p.Length, Tolerance, label + " 길이");
    }

    [Test]
    public void 새_칸_기본값은_지금_모습_배율_1_라이트_흔들림_0()
    {
        var q = new BossVictoryGunnerQ();
        Assert.AreEqual(1f, q.burstAmount); Assert.AreEqual(1f, q.burstBrightness);
        Assert.AreEqual(1f, q.overchargeAmount); Assert.AreEqual(1f, q.overchargeBrightness);
        Assert.AreEqual(1f, q.beamBrightness);
        Assert.IsFalse(q.beamColorOverride);
        Assert.AreEqual(0f, q.beamFadeSeconds);
        Assert.AreEqual(0f, q.fireShakeIntensity);
        Assert.AreEqual(0f, q.fireFlashIntensity);
        for (int k = 1; k <= 3; k++) Assert.AreEqual(0f, q.StageAt(k).shakeIntensity, $"{k}단계 흔들림");
        BossVictoryGunnerCharge.Times t = Times(1f, 2f, 3f, 4f, 5f);
        foreach (float at in new[] { 0f, 1.5f, 2.5f, 3.5f, 4.5f, 6f })
        {
            BossVictoryGunnerCharge.ChargeFxSample fx = BossVictoryGunnerCharge.EvaluateFx(t, q, at);
            AssertIdentity(fx.Line, $"{at} 빛줄기");
            AssertIdentity(fx.Particle, $"{at} 입자");
            AssertIdentity(fx.Core, $"{at} 코어");
            Assert.AreEqual(0f, fx.LightIntensity, Tolerance, $"{at} 라이트");
        }
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Evaluate(t, q, 5.05f).FlashIntensity, "번쩍 0");
    }

    [Test]
    public void FxAt_은_비어_있으면_기본값()
    {
        var q = new BossVictoryGunnerQ { fxStage2 = null };
        Assert.AreSame(BossVictoryChargeFx.Identity, q.FxAt(2));
        AssertIdentity(BossVictoryGunnerCharge.PartSample.From(null), "null 파트");
    }

    [Test]
    public void 구간은_마커_사이_곡선_비율이고_구체_크기는_그대로()
    {
        BossVictoryGunnerQ q = Look();
        BossVictoryGunnerCharge.Times t = Times(1f, 2f, 3f, 4f, 5f);
        BossVictoryGunnerCharge.Segment(t, q, 0.5f, out int from, out int to, out float w);
        Assert.AreEqual((0, 0, 0f), (from, to, w), "차징 전");
        BossVictoryGunnerCharge.Segment(t, q, 2.5f, out from, out to, out w);
        Assert.AreEqual(1, from); Assert.AreEqual(2, to); Assert.AreEqual(0.5f, w, Tolerance);
        BossVictoryGunnerCharge.Segment(t, q, 9f, out from, out to, out _);
        Assert.AreEqual(3, from); Assert.AreEqual(3, to);
        Assert.AreEqual(0.2f, BossVictoryGunnerCharge.SphereScale(t, q, 0f), Tolerance);
        Assert.AreEqual(0.65f, BossVictoryGunnerCharge.SphereScale(t, q, 2.5f), Tolerance, "0.5 → 0.8 중간");
        Assert.AreEqual(1.2f, BossVictoryGunnerCharge.SphereScale(t, q, 9f), Tolerance);
    }

    [Test]
    public void 파트_값은_앞_마커에서_이_마커로_이_마커_곡선으로_보간()
    {
        BossVictoryGunnerQ q = Look();
        q.stage2.ease = BossVictoryKeyCurve.EaseIn;   // 1 → 2단계 구간 곡선 = 도착 마커(2단계) 곡선
        q.fxStage1 = new BossVictoryChargeFx { lightIntensity = 0f, lightColor = Color.black };
        q.fxStage1.line.size = 1f;
        q.fxStage1.particle.amount = 2f;
        q.fxStage2 = new BossVictoryChargeFx { lightIntensity = 4f, lightColor = Color.white };
        q.fxStage2.line.size = 3f;
        q.fxStage2.particle.amount = 4f;
        BossVictoryGunnerCharge.Times t = Times(1f, 2f, 3f, 4f, 5f);

        BossVictoryGunnerCharge.ChargeFxSample mid = BossVictoryGunnerCharge.EvaluateFx(t, q, 2.5f);   // u 0.5 → EaseIn 0.25
        Assert.AreEqual(1.5f, mid.Line.Size, Tolerance, "1 → 3 의 0.25");
        Assert.AreEqual(2.5f, mid.Particle.Amount, Tolerance, "2 → 4 의 0.25");
        Assert.AreEqual(1f, mid.LightIntensity, Tolerance);
        Assert.AreEqual(0.25f, mid.LightColor.r, Tolerance);
        Assert.AreEqual(1f, mid.Core.Size, Tolerance, "안 바꾼 파트는 1");

        BossVictoryGunnerCharge.ChargeFxSample at2 = BossVictoryGunnerCharge.EvaluateFx(t, q, 3f);
        Assert.AreEqual(3f, at2.Line.Size, Tolerance, "마커 도착 = 그 마커 값");
        Assert.AreEqual(3.2f, BossVictoryGunnerCharge.Evaluate(t, q, 3.2f).Fx.LightIntensity, Tolerance, "Frame 에도 실림(2단계 4 → 3단계 기본 0 의 0.2)");
    }

    [Test]
    public void 반복_간격은_둘_다_값이면_보간_한쪽이_0이면_도착부터_계단()
    {
        var a = BossVictoryGunnerCharge.PartSample.One;
        var b = BossVictoryGunnerCharge.PartSample.One;
        a.RepeatInterval = 0.5f;
        b.RepeatInterval = 0.1f;
        Assert.AreEqual(0.3f, BossVictoryGunnerCharge.Lerp(a, b, 0.5f).RepeatInterval, Tolerance, "둘 다 값 = 보간");
        a.RepeatInterval = 0f;
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Lerp(a, b, 0.99f).RepeatInterval, Tolerance, "도착 전 = 앞(프리팹)");
        Assert.AreEqual(0.1f, BossVictoryGunnerCharge.Lerp(a, b, 1f).RepeatInterval, Tolerance, "도착 = 뒤 값");
        b.Size = -5f;
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Lerp(a, b, 1f).Size, Tolerance, "배율은 0 아래로 안 감");
    }

    [Test]
    public void 레이저_페이드아웃은_유지_뒤_선형으로_0()
    {
        Assert.AreEqual(1f, BossVictoryGunnerCharge.LaserFade(0.5f, 1f, 0.5f), Tolerance);
        Assert.AreEqual(0.5f, BossVictoryGunnerCharge.LaserFade(1.25f, 1f, 0.5f), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.LaserFade(2f, 1f, 0.5f), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.LaserFade(1f, 1f, 0f), Tolerance, "페이드 0 = 유지 끝에 바로");

        BossVictoryGunnerQ q = Look();
        q.beamHoldSeconds = 1f;
        q.beamFadeSeconds = 0.5f;
        BossVictoryGunnerCharge.Times t = Times(1f, 2f, 3f, 4f, 5f);
        Assert.IsTrue(BossVictoryGunnerCharge.Evaluate(t, q, 6.25f).LaserOn, "페이드 동안 레이저 켜짐");
        Assert.AreEqual(0.5f, BossVictoryGunnerCharge.Evaluate(t, q, 6.25f).LaserFade, Tolerance);
        Assert.IsFalse(BossVictoryGunnerCharge.Evaluate(t, q, 6.6f).LaserOn, "유지 + 페이드 뒤 끔");
    }

    [Test]
    public void 발사_번쩍은_발사부터_길이_동안_세기에서_0()
    {
        Assert.AreEqual(0f, BossVictoryGunnerCharge.FireFlash(8f, 0.2f, -0.01f), Tolerance, "발사 전");
        Assert.AreEqual(8f, BossVictoryGunnerCharge.FireFlash(8f, 0.2f, 0f), Tolerance);
        Assert.AreEqual(4f, BossVictoryGunnerCharge.FireFlash(8f, 0.2f, 0.1f), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.FireFlash(8f, 0.2f, 0.2f), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.FireFlash(0f, 0.2f, 0.05f), Tolerance, "세기 0 = 없음");
    }

    [Test]
    public void 밖에서_쓰는_칸은_마지막에_쓴_값과_다르면_새_기준()
    {
        Assert.AreEqual(1f, BossVictoryChargeFxApplier.TrackedBase(1f, 0f, 0f, false), Tolerance, "처음 = 지금 값");
        Assert.AreEqual(1f, BossVictoryChargeFxApplier.TrackedBase(2f, 2f, 1f, true), Tolerance, "내가 쓴 값 그대로 = 기준 유지");
        Assert.AreEqual(1.5f, BossVictoryChargeFxApplier.TrackedBase(1.5f, 2f, 1f, true), Tolerance, "밖에서 바뀜 = 그 값이 기준");
    }

    [Test]
    public void 곡선_배율은_상수는_값_곡선은_곡선_배율에()
    {
        ParticleSystem.MinMaxCurve c = BossVictoryChargeFxApplier.ScaleCurve(new ParticleSystem.MinMaxCurve(5f), 2f);
        Assert.AreEqual(10f, c.constant, Tolerance);
        c = BossVictoryChargeFxApplier.ScaleCurve(new ParticleSystem.MinMaxCurve(2f, 4f), 0.5f);
        Assert.AreEqual(1f, c.constantMin, Tolerance);
        Assert.AreEqual(2f, c.constantMax, Tolerance);
        c = BossVictoryChargeFxApplier.ScaleCurve(new ParticleSystem.MinMaxCurve(3f, AnimationCurve.Linear(0f, 0f, 1f, 1f)), 2f);
        Assert.AreEqual(6f, c.curveMultiplier, Tolerance);
    }

    [Test]
    public void 파트는_렌더_모드로_가른다()
    {
        Assert.AreEqual(BossVictoryChargeFxApplier.Part.Line, BossVictoryChargeFxApplier.PartOf(ParticleSystemRenderMode.Stretch));
        Assert.AreEqual(BossVictoryChargeFxApplier.Part.Particle, BossVictoryChargeFxApplier.PartOf(ParticleSystemRenderMode.Mesh));
        Assert.AreEqual(BossVictoryChargeFxApplier.Part.Core, BossVictoryChargeFxApplier.PartOf(ParticleSystemRenderMode.Billboard));
    }
}
