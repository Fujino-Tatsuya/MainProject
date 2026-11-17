using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 거너 레이저가 안 보이던 버그(할 일 298) — 레이저·차징 소켓 안전 회수(저작 1~3초)가 프로필 유지 시간(예: 31.6초)보다 짧아
/// 도중에 강제 회수됐다. 처치 연출 동안 <see cref="EffectSocketSafetyOverride"/> 로 끄고 정리 때 되돌린다.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictory).
/// </summary>
public sealed class BossVictoryGunnerLaserTests
{
    const float Tolerance = 1e-4f;

    static EffectSocketPlayer Socket(out GameObject go, float timeout)
    {
        go = new GameObject("BossVictoryGunnerLaserTests");
        var p = go.AddComponent<EffectSocketPlayer>();
        p.SetSafetyTimeout(timeout);
        return p;
    }

    [Test]
    public void 안전_회수는_끄면_0_되돌리면_처음_값()
    {
        EffectSocketPlayer beam = Socket(out GameObject a, 1f);
        EffectSocketPlayer loop = Socket(out GameObject b, 3f);
        try
        {
            var o = new EffectSocketSafetyOverride();
            o.Suspend(beam);
            o.Suspend(loop);
            o.Suspend(beam);   // 두 번 꺼도 원래 값은 처음 것
            o.Suspend(null);
            Assert.AreEqual(2, o.Count);
            Assert.AreEqual(0f, beam.SafetyTimeout, Tolerance, "처치 연출 동안 끔");
            Assert.AreEqual(0f, loop.SafetyTimeout, Tolerance);
            o.RestoreAll();
            Assert.AreEqual(1f, beam.SafetyTimeout, Tolerance, "정리 때 저작 값");
            Assert.AreEqual(3f, loop.SafetyTimeout, Tolerance);
            Assert.AreEqual(0, o.Count);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void 파괴된_소켓은_되돌리기에서_건너뛴다()
    {
        EffectSocketPlayer beam = Socket(out GameObject a, 1f);
        var o = new EffectSocketSafetyOverride();
        o.Suspend(beam);
        Object.DestroyImmediate(a);
        Assert.DoesNotThrow(o.RestoreAll);
        Assert.AreEqual(0, o.Count);
    }

    [Test]
    public void 긴_유지_시간이면_레이저는_유지_끝까지_켜져_있다()
    {
        // 사용자 프로필(11-17): 발사 3.12 · 유지 31.6 — 1초(안전 회수)를 훨씬 넘겨도 타임라인 상태는 켜짐·알파 1.
        var q = new BossVictoryGunnerQ { beamHoldSeconds = 31.6f };
        var t = new BossVictoryGunnerCharge.Times { ChargeStart = 0.39f, Stage1 = 0.7f, Stage2 = 1.56f, Stage3 = 2.41f, Fire = 3.12f };
        BossVictoryGunnerCharge.Frame f = BossVictoryGunnerCharge.Evaluate(t, q, 5.5f);
        Assert.IsTrue(f.LaserOn);
        Assert.AreEqual(1f, f.LaserFade, Tolerance, "밝기 0 경로 없음");
        Assert.IsFalse(BossVictoryGunnerCharge.Evaluate(t, q, 3.12f + 31.7f).LaserOn);
    }

    [Test]
    public void 사용자_구체_크기는_0이_아니다_작을_뿐()
    {
        // 사용자 값 startScale 0.05 · 0.1 · 0.3 · 1 — 보간이 0 을 내지 않는다(295 부터 파트 크기에 실제로 곱해져 작게 보이는 건 데이터).
        var q = new BossVictoryGunnerQ { startScale = 0.05f };
        q.stage1 = BossVictoryGunnerQ.Stage.Make(0.1f);
        q.stage2 = BossVictoryGunnerQ.Stage.Make(0.3f);
        q.stage3 = BossVictoryGunnerQ.Stage.Make(1f);
        var t = new BossVictoryGunnerCharge.Times { ChargeStart = 0.39f, Stage1 = 0.7f, Stage2 = 1.56f, Stage3 = 2.41f, Fire = 3.12f };
        Assert.AreEqual(0.05f, BossVictoryGunnerCharge.SphereScale(t, q, 0.39f), Tolerance);
        Assert.AreEqual(0.2f, BossVictoryGunnerCharge.SphereScale(t, q, (0.7f + 1.56f) / 2f), Tolerance);
        Assert.AreEqual(1f, BossVictoryGunnerCharge.SphereScale(t, q, 3f), Tolerance);
        Assert.IsTrue(BossVictoryGunnerCharge.Evaluate(t, q, 0.5f).SphereVisible, "sphereDelay 0 = 차징 시작부터 보임");
    }
}
