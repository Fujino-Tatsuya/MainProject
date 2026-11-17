using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 거너 Q 3단계 차징·큰 레이저(할 일 294) — 순수 함수(<see cref="BossVictoryGunnerCharge"/>): 마커 시각·정렬·단계·구체 크기 보간·상태 한 장·오프셋·레이저 끝점,
/// 옛 프로필(시작 → 목표 크기·성장 곡선) 이관, 정본 거너 프로필 마커 순서.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictory).
/// </summary>
public sealed class BossVictoryGunnerQTests
{
    const float Tolerance = 1e-4f;
    const string GunnerProfilePath = "Assets/Resources/BossVictory/Profiles/BossVictoryProfile_Gunner.asset";

    static BossVictoryGunnerCharge.Times Times(float start, float s1, float s2, float s3, float fire) =>
        new BossVictoryGunnerCharge.Times { ChargeStart = start, Stage1 = s1, Stage2 = s2, Stage3 = s3, Fire = fire };

    static BossVictoryGunnerQ Look(float start = 0.2f, float s1 = 0.5f, float s2 = 0.8f, float s3 = 1.2f)
    {
        var q = new BossVictoryGunnerQ { startScale = start };
        q.stage1 = BossVictoryGunnerQ.Stage.Make(s1);
        q.stage2 = BossVictoryGunnerQ.Stage.Make(s2);
        q.stage3 = BossVictoryGunnerQ.Stage.Make(s3);
        return q;
    }

    static void AreEqual(Vector3 expected, Vector3 actual, string message)
    {
        Assert.AreEqual(expected.x, actual.x, Tolerance, message + " x");
        Assert.AreEqual(expected.y, actual.y, Tolerance, message + " y");
        Assert.AreEqual(expected.z, actual.z, Tolerance, message + " z");
    }

    // ── 마커 ──

    [Test]
    public void 거너_Q_마커_다섯은_한_줄이고_시각을_읽고_쓴다()
    {
        var tl = new BossVictoryTimeline { backDashStartTime = 0.5f };
        BossVictoryMarker[] gunner =
        {
            BossVictoryMarker.ChargeStart, BossVictoryMarker.GunnerStage1, BossVictoryMarker.GunnerStage2,
            BossVictoryMarker.GunnerStage3, BossVictoryMarker.GunnerFire,
        };
        float t = 1f;
        foreach (BossVictoryMarker m in gunner)
        {
            Assert.IsTrue(BossVictoryTimeline.IsGunnerQ(m), m.ToString());
            Assert.IsFalse(BossVictoryTimeline.ZeroMeansOff(m), "0 = 처치 순간(끔 아님)");
            tl.SetMarkerTime(m, t);
            Assert.AreEqual(t, tl.MarkerTime(m), Tolerance, m.ToString());
            t += 0.25f;
        }
        Assert.IsFalse(BossVictoryTimeline.IsGunnerQ(BossVictoryMarker.BackDashStart));
        tl.lobbyTransitionTime = 1.5f;
        Assert.AreEqual(2f, tl.Duration, Tolerance, "창 시간축 = 발사 마커까지 포함");
        BossVictoryGunnerCharge.Times times = BossVictoryGunnerCharge.From(tl);
        Assert.AreEqual(1f, times.ChargeStart, Tolerance);
        Assert.AreEqual(2f, times.Fire, Tolerance);
    }

    [Test]
    public void 새_타임라인_단계_마커는_차징_시작_뒤_차례로()
    {
        var tl = BossVictoryTimeline.CreateDefault();
        BossVictoryGunnerCharge.Times t = BossVictoryGunnerCharge.From(tl);
        Assert.Less(t.ChargeStart, t.Stage1);
        Assert.Less(t.Stage1, t.Stage2);
        Assert.Less(t.Stage2, t.Stage3);
        Assert.Less(t.Stage3, t.Fire);
        Assert.AreEqual(t.ChargeStart + BossVictoryGunnerCharge.LegacyChargeSeconds, t.Fire, Tolerance, "발사 기본 = 차징 시작 + 옛 1.2초");
    }

    [Test]
    public void 앞_마커보다_앞인_마커는_앞_마커_시각으로()
    {
        BossVictoryGunnerCharge.Times o = Times(1f, 0.5f, 2f, 1.5f, 0f).Ordered();
        Assert.AreEqual(1f, o.Stage1, Tolerance, "1단계 < 차징 시작 → 차징 시작");
        Assert.AreEqual(2f, o.Stage2, Tolerance);
        Assert.AreEqual(2f, o.Stage3, Tolerance, "3단계 < 2단계 → 2단계");
        Assert.AreEqual(2f, o.Fire, Tolerance, "발사 < 3단계 → 3단계");
    }

    // ── 단계·구체 크기 ──

    [Test]
    public void 단계는_지난_단계_마커_수()
    {
        BossVictoryGunnerCharge.Times o = Times(0f, 1f, 2f, 3f, 4f).Ordered();
        Assert.AreEqual(0, BossVictoryGunnerCharge.StageAt(o, 0.99f), "차징 시작~1단계 전 = 0(한계 돌파 색 0)");
        Assert.AreEqual(1, BossVictoryGunnerCharge.StageAt(o, 1f));
        Assert.AreEqual(2, BossVictoryGunnerCharge.StageAt(o, 2.5f));
        Assert.AreEqual(3, BossVictoryGunnerCharge.StageAt(o, 3f));
        Assert.AreEqual(3, BossVictoryGunnerCharge.StageAt(o, 10f), "3단계 뒤 = 3");
    }

    [Test]
    public void 구체_크기는_마커마다_도달_크기_사이는_곡선_보간()
    {
        BossVictoryGunnerCharge.Times o = Times(1f, 2f, 3f, 4f, 5f).Ordered();
        BossVictoryGunnerQ q = Look(0.2f, 0.6f, 1f, 2f);
        Assert.AreEqual(0.2f, BossVictoryGunnerCharge.SphereScale(o, q, 0f), Tolerance, "차징 시작 전 = 시작 크기");
        Assert.AreEqual(0.2f, BossVictoryGunnerCharge.SphereScale(o, q, 1f), Tolerance, "차징 시작 = 시작 크기");
        Assert.AreEqual(0.4f, BossVictoryGunnerCharge.SphereScale(o, q, 1.5f), Tolerance, "시작 → 1단계 가운데(선형)");
        Assert.AreEqual(0.6f, BossVictoryGunnerCharge.SphereScale(o, q, 2f), Tolerance, "1단계 도달 크기");
        Assert.AreEqual(0.8f, BossVictoryGunnerCharge.SphereScale(o, q, 2.5f), Tolerance);
        Assert.AreEqual(2f, BossVictoryGunnerCharge.SphereScale(o, q, 4f), Tolerance, "3단계 도달 크기");
        Assert.AreEqual(2f, BossVictoryGunnerCharge.SphereScale(o, q, 4.7f), Tolerance, "3단계 ~ 발사 = 유지");

        q.stage2.ease = BossVictoryKeyCurve.EaseIn;   // 1단계 → 2단계 구간 = x²
        Assert.AreEqual(0.6f + 0.4f * 0.25f, BossVictoryGunnerCharge.SphereScale(o, q, 2.5f), Tolerance, "곡선 = 그 단계(도착 마커) 칸");
        q.stage2.ease = BossVictoryKeyCurve.Step;
        Assert.AreEqual(0.6f, BossVictoryGunnerCharge.SphereScale(o, q, 2.99f), Tolerance, "계단 = 도착 순간 튐");
    }

    [Test]
    public void 같은_시각의_마커는_뒤_마커_크기로_바로()
    {
        BossVictoryGunnerCharge.Times o = Times(1f, 1f, 1f, 2f, 3f).Ordered();
        BossVictoryGunnerQ q = Look(0.2f, 0.6f, 1f, 2f);
        Assert.AreEqual(1f, BossVictoryGunnerCharge.SphereScale(o, q, 1f), Tolerance, "차징 시작 = 1·2단계 → 2단계 크기");
        Assert.AreEqual(2, BossVictoryGunnerCharge.StageAt(o, 1f));
    }

    // ── 상태 한 장 ──

    [Test]
    public void 상태_차징_구체_지연_발사_레이저_유지()
    {
        BossVictoryGunnerCharge.Times times = Times(1f, 1.3f, 1.6f, 1.9f, 2.2f);
        BossVictoryGunnerQ q = Look();
        q.sphereDelay = 0.1f;
        q.beamHoldSeconds = 0.5f;

        BossVictoryGunnerCharge.Frame f = BossVictoryGunnerCharge.Evaluate(times, q, 0.9f);
        Assert.IsFalse(f.Charging, "차징 시작 전");
        Assert.IsFalse(f.SphereVisible);

        f = BossVictoryGunnerCharge.Evaluate(times, q, 1.05f);
        Assert.IsTrue(f.Charging);
        Assert.IsFalse(f.SphereVisible, "구체 지연 0.1초 안");

        f = BossVictoryGunnerCharge.Evaluate(times, q, 1.1f);
        Assert.IsTrue(f.SphereVisible, "지연 지남");
        Assert.AreEqual(0, f.Stage);

        f = BossVictoryGunnerCharge.Evaluate(times, q, 2f);
        Assert.AreEqual(3, f.Stage);
        Assert.IsFalse(f.Fired);

        f = BossVictoryGunnerCharge.Evaluate(times, q, 2.2f);
        Assert.IsTrue(f.Fired, "발사 마커 = 발사(고정 1.2초 대기 없음)");
        Assert.IsFalse(f.Charging, "발사 뒤 차징 끝");
        Assert.IsFalse(f.SphereVisible);
        Assert.IsTrue(f.LaserOn);

        f = BossVictoryGunnerCharge.Evaluate(times, q, 2.69f);
        Assert.IsTrue(f.LaserOn, "유지 0.5초 안");
        f = BossVictoryGunnerCharge.Evaluate(times, q, 2.7f);
        Assert.IsFalse(f.LaserOn, "유지 끝");
        Assert.IsTrue(f.Fired);
    }

    // ── 오프셋·레이저 ──

    [Test]
    public void 오프셋은_총구_기준_옆_위_앞()
    {
        Vector3 muzzle = new Vector3(1f, 2f, 3f);
        Vector3 facingX = new Vector3(5f, 0f, 0f);   // 바라봄 = +X
        AreEqual(muzzle + new Vector3(2f, 0f, 0f), BossVictoryGunnerCharge.OffsetPoint(muzzle, facingX, new Vector3(0f, 0f, 2f)), "앞 2m = 바라보는 쪽");
        AreEqual(muzzle + new Vector3(0f, 0.5f, 0f), BossVictoryGunnerCharge.OffsetPoint(muzzle, facingX, new Vector3(0f, 0.5f, 0f)), "위 = 월드 위");
        AreEqual(muzzle + new Vector3(0f, 0f, -1f), BossVictoryGunnerCharge.OffsetPoint(muzzle, facingX, new Vector3(1f, 0f, 0f)), "옆(+) = 바라봄 기준 오른쪽");
        AreEqual(muzzle + new Vector3(0f, 0f, 2f), BossVictoryGunnerCharge.OffsetPoint(muzzle, new Vector3(0f, 4f, 1f), new Vector3(0f, 0f, 2f)),
            "바라봄의 세로 성분은 무시(수평)");
    }

    [Test]
    public void 레이저_끝은_수평_방향으로_길이만큼_벽_무시()
    {
        Vector3 start = new Vector3(0f, 1f, 0f);
        AreEqual(new Vector3(0f, 1f, 30f), BossVictoryGunnerCharge.BeamEnd(start, new Vector3(0f, -3f, 2f), 30f), "높이 그대로·수평 30m");
        AreEqual(new Vector3(12f, 1f, 0f), BossVictoryGunnerCharge.BeamEnd(start, Vector3.right * 0.1f, 12f), "방향 크기 무관");
    }

    // ── 옛 프로필 이관 ──

    [Test]
    public void 옛_시작_목표_크기는_단계_마커로_옮긴다()
    {
        var tl = new BossVictoryTimeline { backDashStartTime = 0.2f };   // 차징 시작 자동 = 0.4
        var q = new BossVictoryGunnerQ();
        BossVictoryGunnerCharge.Migrate(q, tl, 0.5f, 2f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        Assert.AreEqual(0.4f, tl.ChargeStartTime, Tolerance, "옛 'Q 차징 시작' = 차징 시작 그대로");
        Assert.AreEqual(0.7f, tl.gunnerStage1Time, Tolerance);
        Assert.AreEqual(1.0f, tl.gunnerStage2Time, Tolerance);
        Assert.AreEqual(1.3f, tl.gunnerStage3Time, Tolerance);
        Assert.AreEqual(1.6f, tl.gunnerFireTime, Tolerance, "발사 = 옛 발사 시각(차징 시작 + 1.2)");
        Assert.AreEqual(0.5f, q.startScale, Tolerance);
        Assert.AreEqual(1f, q.stage1.sphereScale, Tolerance, "곡선(1/3)");
        Assert.AreEqual(1.5f, q.stage2.sphereScale, Tolerance, "곡선(2/3)");
        Assert.AreEqual(2f, q.stage3.sphereScale, Tolerance, "3단계 = 옛 목표 크기");

        var eased = new BossVictoryGunnerQ();
        BossVictoryGunnerCharge.Migrate(eased, tl, 0f, 1f, null);
        Assert.AreEqual(1f / 3f, eased.stage1.sphereScale, Tolerance, "곡선 없음 = 선형");
    }

    [Test]
    public void 프로필_이관은_옛_칸이_있을_때만_한_번()
    {
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            Assert.IsFalse(p.MigrateLegacyGunnerCharge(), "새 프로필 = 옮길 것 없음");
            float stage3 = p.gunnerQ.stage3.sphereScale;
            JsonUtility.FromJsonOverwrite("{\"chargeScaleFrom\":0.5,\"chargeScaleTo\":3.0}", p);
            Assert.IsTrue(p.MigrateLegacyGunnerCharge(), "옛 칸 → 옮김");
            Assert.AreEqual(0.5f, p.gunnerQ.startScale, Tolerance);
            Assert.AreEqual(3f, p.gunnerQ.stage3.sphereScale, Tolerance);
            Assert.AreNotEqual(stage3, p.gunnerQ.stage3.sphereScale);
            p.gunnerQ.stage3.sphereScale = 9f;   // 사용자 값
            Assert.IsFalse(p.MigrateLegacyGunnerCharge(), "옮긴 뒤 = 다시 안 함(멱등)");
            Assert.AreEqual(9f, p.gunnerQ.stage3.sphereScale, Tolerance, "사용자 값 유지");
        }
        finally { Object.DestroyImmediate(p); }
    }

    [Test]
    public void 정본_거너_프로필_마커_순서()
    {
        var gunner = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(GunnerProfilePath);
        Assert.IsNotNull(gunner);
        Assert.IsNotNull(gunner.gunnerQ);
        BossVictoryGunnerCharge.Times t = BossVictoryGunnerCharge.From(gunner.timeline);
        Assert.LessOrEqual(t.ChargeStart, t.Stage1, "차징 시작 ≤ 1단계");
        Assert.LessOrEqual(t.Stage1, t.Stage2);
        Assert.LessOrEqual(t.Stage2, t.Stage3);
        Assert.LessOrEqual(t.Stage3, t.Fire, "3단계 ≤ 발사");
        Assert.Less(t.Fire, gunner.timeline.lobbyTransitionTime, "발사는 로비 전환 전");
        Assert.Greater(gunner.gunnerQ.beamLength, 0f);
        Assert.Greater(gunner.gunnerQ.beamHoldSeconds, 0f);
    }
}
