using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 돌진 준비 애니 클립 노드(11-17) — 피격 애니와 같은 독립 노드, 겹치면 나중에 시작한 쪽이 이김, 끝 자르기 = 그 프레임에서 정지, 0 = 끔.
/// 배율 순수 함수 <see cref="BossVictoryBossTimeScale"/> · 타임라인 <see cref="BossVictoryTimeline.BossNodes"/> · 23호 컨트롤러 DashPrep 상태.
/// </summary>
public sealed class BossVictoryBossDashPrepTests
{
    const float Slow = 0.1f;
    const float Tolerance = 1e-6f;
    const string ControllerPath = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";

    static float At(in BossVictoryBossTimeScale.Nodes n, float t) => BossVictoryBossTimeScale.Scale(n, -1f, Slow, t);

    static BossVictoryBossTimeScale.Nodes Dash(float dashAt, bool hit = false, float hitAt = 0f, float dashTrimAt = -1f, float freeze = 0f) => new BossVictoryBossTimeScale.Nodes
    {
        HasHitAnim = hit, HitAt = hitAt, FreezeAt = freeze,
        HasDashPrep = true, DashPrepAt = dashAt, DashTrimmed = dashTrimAt >= 0f, DashTrimAt = dashTrimAt,
    };

    [Test]
    public void 돌진_준비는_시각부터_1_자른_끝에서_0()
    {
        var n = Dash(0.5f, dashTrimAt: 1.2f);
        Assert.AreEqual(0f, At(n, 0.4f), Tolerance, "전 = 마지막 자세");
        Assert.AreEqual(1f, At(n, 0.5f), Tolerance);
        Assert.AreEqual(1f, At(n, 1.1f), Tolerance);
        Assert.AreEqual(0f, At(n, 1.2f), Tolerance, "자른 끝 = 정지");
        Assert.IsFalse(BossVictoryBossTimeScale.ShouldPlayDashPrep(n, 0.4f));
        Assert.IsTrue(BossVictoryBossTimeScale.ShouldPlayDashPrep(n, 0.5f));
    }

    [Test]
    public void 피격_애니와_겹치면_나중에_시작한_쪽이_이긴다()
    {
        // 피격 애니 0 → 자른 끝 0.4(정지) → 돌진 준비 0.8 = 다시 ×1
        var dashLater = Dash(0.8f, hit: true, hitAt: 0f);
        dashLater.Trimmed = true;
        dashLater.TrimAt = 0.4f;
        Assert.AreEqual(0f, At(dashLater, 0.6f), Tolerance, "피격 애니 자른 끝");
        Assert.AreEqual(1f, At(dashLater, 0.8f), Tolerance, "늦게 시작한 돌진 준비가 이김");
        // 돌진 준비 0.2 자른 끝 0.5 → 피격 애니 1.0 = ×1
        var hitLater = Dash(0.2f, hit: true, hitAt: 1f, dashTrimAt: 0.5f);
        Assert.AreEqual(0f, At(hitLater, 0.1f), Tolerance);
        Assert.AreEqual(1f, At(hitLater, 0.3f), Tolerance);
        Assert.AreEqual(0f, At(hitLater, 0.7f), Tolerance, "돌진 준비 자른 끝");
        Assert.AreEqual(1f, At(hitLater, 1f), Tolerance, "늦게 시작한 피격 애니가 이김");
        // 같은 시각 = 피격 애니가 이김(같은 배율 1, 자른 끝은 각 애니 것만)
        var same = Dash(0.5f, hit: true, hitAt: 0.5f, freeze: 0.5f);
        Assert.AreEqual(0f, At(same, 0.5f), Tolerance, "같은 시각 정지 > 돌진 준비");
    }

    [Test]
    public void 꺼짐이면_지금과_같다()
    {
        var off = new BossVictoryTimeline { bossHitAnimTime = 0f, bossFreezeTime = 0.6f, bossSlowTime = 1.2f };
        var withFlag = off.BossNodes(true, -1f, true, -1f);
        Assert.IsFalse(withFlag.HasDashPrep, "시각 0 = 끔");
        var plain = off.BossNodes(true);
        for (float t = 0f; t < 2f; t += 0.05f)
            Assert.AreEqual(At(plain, t), At(withFlag, t), Tolerance, $"t {t}");
        var on = new BossVictoryTimeline { bossDashPrepTime = 0.3f };
        Assert.IsTrue(on.BossNodes(false, -1f, true).HasDashPrep);
        Assert.IsFalse(on.BossNodes(false, -1f, false).HasDashPrep, "상태 이름 없음 = 안 틂");
        Assert.AreEqual(0f, BossVictoryTimeline.CreateDefault().bossDashPrepTime, "코드 기본 = 끔");
        Assert.IsTrue(BossVictoryTimeline.ZeroMeansOff(BossVictoryMarker.BossDashPrepAnim));
    }

    [Test]
    public void 마커_시각_자르기_복사()
    {
        var src = new BossVictoryTimeline { bossDashPrepTime = 1.1f, bossDashPrepTrimSeconds = 0.4f };
        var dst = new BossVictoryTimeline();
        dst.CopyMarkerFrom(src, BossVictoryMarker.BossDashPrepAnim);
        Assert.AreEqual(1.1f, dst.MarkerTime(BossVictoryMarker.BossDashPrepAnim), Tolerance);
        Assert.AreEqual(0.4f, dst.bossDashPrepTrimSeconds, Tolerance);
        dst.SetMarkerTime(BossVictoryMarker.BossDashPrepAnim, 2f);
        Assert.AreEqual(2f, dst.bossDashPrepTime, Tolerance);
    }

    [Test]
    public void 기본_상태_DashPrep_이_23호_컨트롤러에_있다()
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (controller == null) Assert.Ignore($"{ControllerPath} 없음(SVN 아트)");
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Assert.AreEqual("DashPrep", s.bossDashPrepStateName);
            Assert.IsNotNull(AnimationEventRequirements.FindStateClip(controller, s.bossDashPrepStateName), $"상태 {s.bossDashPrepStateName}");
        }
        finally { Object.DestroyImmediate(s); }
    }
}
