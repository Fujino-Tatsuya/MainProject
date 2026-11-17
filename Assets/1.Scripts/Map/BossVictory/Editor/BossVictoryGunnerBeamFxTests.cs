using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 301 — 처치 연출 거너 레이저 진동·광원·반동·통과 지점(BossVictoryGunnerCharge 순수 함수).</summary>
public class BossVictoryGunnerBeamFxTests
{
    const float Tolerance = 1e-4f;

    [Test]
    public void 진동_올림_유지_내림_길이_안에서_끝난다()
    {
        // 올림 0.1 · 길이 1 · 내림 0.4 → 0.6 부터 내림
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Envelope(-0.1f, 0.1f, 1f, 0.4f), Tolerance, "발사 전");
        Assert.AreEqual(0.5f, BossVictoryGunnerCharge.Envelope(0.05f, 0.1f, 1f, 0.4f), Tolerance);
        Assert.AreEqual(1f, BossVictoryGunnerCharge.Envelope(0.5f, 0.1f, 1f, 0.4f), Tolerance);
        Assert.AreEqual(0.5f, BossVictoryGunnerCharge.Envelope(0.8f, 0.1f, 1f, 0.4f), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Envelope(1f, 0.1f, 1f, 0.4f), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Envelope(0.5f, 0.1f, 0f, 0.4f), Tolerance, "길이 0 = 없음");
        Assert.AreEqual(1f, BossVictoryGunnerCharge.Envelope(0f, 0f, 1f, 0f), Tolerance, "올림 0 = 발사 순간 1");
    }

    [Test]
    public void 광원은_유지_끝까지_켜고_그_뒤_돌아온다()
    {
        // 차오름 0.1 · 유지 0.5 · 돌아옴 0.4
        Assert.AreEqual(1f, BossVictoryGunnerCharge.Envelope(0.5f - 1e-3f, 0.1f, 0.5f, 0.4f, recoverAfterEnd: true), 0.01f);
        Assert.AreEqual(0.5f, BossVictoryGunnerCharge.Envelope(0.7f, 0.1f, 0.5f, 0.4f, recoverAfterEnd: true), Tolerance);
        Assert.AreEqual(0f, BossVictoryGunnerCharge.Envelope(0.9f, 0.1f, 0.5f, 0.4f, recoverAfterEnd: true), Tolerance);
    }

    [Test]
    public void 반동은_등속()
    {
        Assert.AreEqual(0f, BossVictoryGunnerCharge.RecoilProgress(-0.2f, 0.5f), Tolerance);
        Assert.AreEqual(0.25f, BossVictoryGunnerCharge.RecoilProgress(0.125f, 0.5f), Tolerance);
        Assert.AreEqual(0.5f, BossVictoryGunnerCharge.RecoilProgress(0.25f, 0.5f), Tolerance);
        Assert.AreEqual(1f, BossVictoryGunnerCharge.RecoilProgress(2f, 0.5f), Tolerance);
    }

    [Test]
    public void 기본값이면_진동_광원_반동_모두_0()
    {
        var q = new BossVictoryGunnerQ();
        var times = new BossVictoryGunnerCharge.Times { ChargeStart = 0f, Stage1 = 0.5f, Stage2 = 1f, Stage3 = 1.5f, Fire = 2f };
        BossVictoryGunnerCharge.Frame f = BossVictoryGunnerCharge.Evaluate(times, q, 2.2f);
        Assert.IsTrue(f.Fired);
        Assert.AreEqual(0f, f.Vibration, Tolerance);
        Assert.AreEqual(0f, f.Recoil, Tolerance);
        Assert.AreEqual(0f, q.lightSceneDim, Tolerance);
        Assert.AreEqual(0f, q.lightContrast, Tolerance);
        Assert.AreEqual(0f, q.beamLightIntensity, Tolerance);
        Assert.IsFalse(q.useBeamPassPoint);
        Assert.AreEqual(0f, q.beamFlowSpeed, Tolerance);
    }

    [Test]
    public void 켜면_발사_뒤_진동_반동이_오른다()
    {
        var q = new BossVictoryGunnerQ { vibrationSeconds = 1f, vibrationRampIn = 0f, vibrationRampOut = 0f, recoilDistance = 2f, recoilSeconds = 0.4f };
        var times = new BossVictoryGunnerCharge.Times { ChargeStart = 0f, Stage1 = 0.5f, Stage2 = 1f, Stage3 = 1.5f, Fire = 2f };
        BossVictoryGunnerCharge.Frame before = BossVictoryGunnerCharge.Evaluate(times, q, 1.9f);
        Assert.AreEqual(0f, before.Vibration, Tolerance);
        Assert.AreEqual(0f, before.Recoil, Tolerance);
        BossVictoryGunnerCharge.Frame f = BossVictoryGunnerCharge.Evaluate(times, q, 2.2f);
        Assert.AreEqual(1f, f.Vibration, Tolerance);
        Assert.AreEqual(0.5f, f.Recoil, Tolerance);
        Assert.AreEqual(0.2f, f.SinceFire, Tolerance);
    }

    [Test]
    public void 통과_지점_방향은_3D_끄면_수평()
    {
        Vector3 muzzle = new Vector3(0f, 1f, 0f);
        Vector3 flat = new Vector3(0f, 0.5f, 10f);
        Assert.AreEqual(Vector3.forward, BossVictoryGunnerCharge.BeamDirection(muzzle, flat, false, Vector3.zero));
        Vector3 d = BossVictoryGunnerCharge.BeamDirection(muzzle, flat, true, new Vector3(0f, 0f, 1f));
        Assert.AreEqual(new Vector3(0f, -1f, 1f).normalized.y, d.y, Tolerance, "아래로 기운다");
        Assert.AreEqual(Vector3.forward, BossVictoryGunnerCharge.BeamDirection(muzzle, flat, true, muzzle), "통과 지점 = 총구면 수평으로");
    }

    [Test]
    public void 레이저_라이트는_등분_칸_가운데()
    {
        Vector3 a = Vector3.zero, b = new Vector3(0f, 0f, 30f);
        Assert.AreEqual(5f, BossVictoryGunnerCharge.BeamLightPoint(a, b, 0, 3).z, Tolerance);
        Assert.AreEqual(15f, BossVictoryGunnerCharge.BeamLightPoint(a, b, 1, 3).z, Tolerance);
        Assert.AreEqual(25f, BossVictoryGunnerCharge.BeamLightPoint(a, b, 2, 3).z, Tolerance);
    }
}
