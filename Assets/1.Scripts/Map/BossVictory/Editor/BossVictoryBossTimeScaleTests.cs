using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 보스 노드(PLAN-boss-victory-glitch "255 후속 5") — 배율 순수 함수(<see cref="BossVictoryBossTimeScale"/>: 시각이 가장 늦은 노드가 이김),
/// 기본값(피격 애니 0 · 정지·슬로우 꺼짐 · 상태 getowned), 23호 컨트롤러 상태·클립(ParryHit_InPlace) 확인.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryBossTimeScale).
/// </summary>
public sealed class BossVictoryBossTimeScaleTests
{
    const float Slow = 0.1f;
    const float Tolerance = 1e-6f;
    const string ControllerPath = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";
    const string ParryHitClipGuid = "1481cf2ec3ed2704fae97d5c3f3f9eab";   // Boss_23_ParryHit_InPlace(SVN)

    static BossVictoryBossTimeScale.Nodes Nodes(bool hit, float hitAt, float freeze, float slow) =>
        new BossVictoryBossTimeScale.Nodes { HasHitAnim = hit, HitAt = hitAt, FreezeAt = freeze, SlowAt = slow };

    static float At(in BossVictoryBossTimeScale.Nodes n, float killerSlowAt, float t) => BossVictoryBossTimeScale.Scale(n, killerSlowAt, Slow, t);

    [Test]
    public void 피격_애니가_없으면_지금과_같다_처치_순간_0_처치자_느려짐에_슬로우()
    {
        var n = Nodes(false, 0f, 0f, 0f);
        Assert.AreEqual(0f, At(n, -1f, 0f), Tolerance, "마지막 피격 자세로 정지");
        Assert.AreEqual(0f, At(n, -1f, 3f), Tolerance, "처치자 느려짐 전");
        Assert.AreEqual(Slow, At(n, 1.5f, 1.5f), Tolerance, "처치자 느려짐 순간부터");
    }

    [Test]
    public void 기본_피격_애니는_처치_순간부터_1()
    {
        var n = Nodes(true, 0f, 0f, 0f);
        Assert.AreEqual(1f, At(n, -1f, 0f), Tolerance);
        Assert.AreEqual(1f, At(n, -1f, 1f), Tolerance);
        Assert.AreEqual(Slow, At(n, 1.2f, 1.3f), Tolerance, "보스 슬로우 끔 = 처치자 느려짐 순간");
        Assert.IsTrue(BossVictoryBossTimeScale.ShouldPlayHitAnim(n, 0f));
        Assert.IsFalse(BossVictoryBossTimeScale.ShouldPlayHitAnim(Nodes(false, 0f, 0f, 0f), 5f), "상태 없음 = 안 튼다");
    }

    [Test]
    public void 피격_애니_시작_전은_마지막_자세로_정지()
    {
        var n = Nodes(true, 0.5f, 0f, 0f);
        Assert.AreEqual(0f, At(n, -1f, 0.49f), Tolerance);
        Assert.IsFalse(BossVictoryBossTimeScale.ShouldPlayHitAnim(n, 0.49f));
        Assert.AreEqual(1f, At(n, -1f, 0.5f), Tolerance);
        Assert.IsTrue(BossVictoryBossTimeScale.ShouldPlayHitAnim(n, 0.5f));
    }

    [Test]
    public void 정지_뒤_슬로우면_슬로우로_다시_움직인다()
    {
        var n = Nodes(true, 0f, 0.6f, 1.2f);
        Assert.AreEqual(1f, At(n, -1f, 0.59f), Tolerance);
        Assert.AreEqual(0f, At(n, -1f, 0.6f), Tolerance, "정지");
        Assert.AreEqual(0f, At(n, -1f, 1.19f), Tolerance);
        Assert.AreEqual(Slow, At(n, -1f, 1.2f), Tolerance, "슬로우");
    }

    [Test]
    public void 슬로우_뒤_정지면_정지()
    {
        var n = Nodes(true, 0f, 1.2f, 0.6f);
        Assert.AreEqual(Slow, At(n, -1f, 0.6f), Tolerance);
        Assert.AreEqual(0f, At(n, -1f, 1.2f), Tolerance);
        Assert.AreEqual(0f, At(n, -1f, 9f), Tolerance);
    }

    [Test]
    public void 보스_슬로우가_켜져_있으면_처치자_느려짐을_따르지_않는다()
    {
        var n = Nodes(true, 0f, 0f, 2f);
        Assert.AreEqual(1f, At(n, 1f, 1.5f), Tolerance, "처치자는 1초에 느려졌지만 보스는 2초까지 ×1");
        Assert.AreEqual(Slow, At(n, 1f, 2f), Tolerance);
    }

    [Test]
    public void 같은_시각이면_피격_애니_정지_슬로우_순으로_뒤가_이긴다()
    {
        Assert.AreEqual(0f, At(Nodes(true, 0.5f, 0.5f, 0f), -1f, 0.5f), Tolerance, "피격 애니 < 정지");
        Assert.AreEqual(Slow, At(Nodes(true, 0.5f, 0.5f, 0.5f), -1f, 0.5f), Tolerance, "정지 < 슬로우");
        Assert.AreEqual(1f, At(Nodes(true, 0.8f, 0.5f, 0f), -1f, 0.9f), Tolerance, "정지 뒤 피격 애니 = 다시 1");
    }

    [Test]
    public void 기본값_피격_애니_0_정지_슬로우_꺼짐_상태_getowned()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.AreEqual(0f, tl.bossHitAnimTime);
        Assert.AreEqual(0f, tl.bossFreezeTime);
        Assert.AreEqual(0f, tl.bossSlowTime);
        Assert.IsTrue(BossVictoryTimeline.ZeroMeansOff(BossVictoryMarker.BossFreeze));
        Assert.IsTrue(BossVictoryTimeline.ZeroMeansOff(BossVictoryMarker.BossSlow));
        Assert.IsFalse(BossVictoryTimeline.ZeroMeansOff(BossVictoryMarker.BossHitAnim), "피격 애니 0 = 처치 순간");
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Assert.AreEqual("getowned", s.bossHitStateName);
            Assert.Greater(s.bossHitFadeSeconds, 0f);
        }
        finally { Object.DestroyImmediate(s); }
    }

    // 23호 컨트롤러에 기본 상태가 있고 그 클립이 ParryHit_InPlace(SVN 아트 — 없으면 건너뜀).
    [Test]
    public void 기본_상태는_23호_컨트롤러의_ParryHit_InPlace()
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (controller == null) Assert.Ignore($"{ControllerPath} 없음(SVN 아트)");
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            AnimationClip clip = AnimationEventRequirements.FindStateClip(controller, s.bossHitStateName);
            Assert.IsNotNull(clip, $"상태 {s.bossHitStateName}");
            Assert.AreEqual(ParryHitClipGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)), "클립 = Boss_23_ParryHit_InPlace");
            Assert.IsFalse(clip.isLooping, "한 번 재생");
        }
        finally { Object.DestroyImmediate(s); }
    }
}
