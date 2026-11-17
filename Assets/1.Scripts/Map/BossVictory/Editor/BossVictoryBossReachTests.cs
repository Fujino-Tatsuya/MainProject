using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 보스 손 뻗기·걷기 클립 노드(11-17) — 상체 = 손 뻗기(23호 magneticgrab, 상체 마스크 레이어), 하체 = 걷기(0층). 피격 애니와 같은 독립 노드,
/// 겹치면 나중에 시작한 쪽이 이김, 끝 자르기 = 손만 멈춤(배율 노드 아님), 0 = 끔.
/// 배율 <see cref="BossVictoryBossTimeScale"/> · 상체 가중치 <see cref="BossVictoryReachRig.UpperWeight"/> · 타임라인 <see cref="BossVictoryTimeline.BossNodes"/> · 23호 컨트롤러 저작 결과.
/// </summary>
public sealed class BossVictoryBossReachTests
{
    const float Slow = 0.1f;
    const float Tolerance = 1e-5f;

    static float At(in BossVictoryBossTimeScale.Nodes n, float t) => BossVictoryBossTimeScale.Scale(n, -1f, Slow, t);

    static BossVictoryBossTimeScale.Nodes Reach(float reachAt, bool hit = false, float hitAt = 0f, float freeze = 0f) => new BossVictoryBossTimeScale.Nodes
    {
        HasHitAnim = hit, HitAt = hitAt, FreezeAt = freeze,
        HasReach = true, ReachAt = reachAt,
    };

    [Test]
    public void 손_뻗기는_시각부터_보스_1()
    {
        var n = Reach(0.5f);
        Assert.AreEqual(0f, At(n, 0.4f), Tolerance, "전 = 마지막 자세");
        Assert.AreEqual(1f, At(n, 0.5f), Tolerance);
        Assert.AreEqual(1f, At(n, 3f), Tolerance, "끝 자르기는 배율 노드가 아니다 — 다리는 계속 걷는다");
        Assert.IsFalse(BossVictoryBossTimeScale.ShouldPlayReach(n, 0.4f));
        Assert.IsTrue(BossVictoryBossTimeScale.ShouldPlayReach(n, 0.5f));
    }

    [Test]
    public void 피격_애니와_겹치면_나중에_시작한_쪽이_이긴다()
    {
        // 피격 애니 0 → 자른 끝 0.4(정지) → 손 뻗기 0.8 = 다시 ×1
        var reachLater = Reach(0.8f, hit: true, hitAt: 0f);
        reachLater.Trimmed = true;
        reachLater.TrimAt = 0.4f;
        Assert.AreEqual(0f, At(reachLater, 0.6f), Tolerance, "피격 애니 자른 끝");
        Assert.AreEqual(1f, At(reachLater, 0.8f), Tolerance, "늦게 시작한 손 뻗기가 이김");
        // 손 뻗기 0.2 → 정지 0.5 → 피격 애니 1.0 = ×1
        var hitLater = Reach(0.2f, hit: true, hitAt: 1f, freeze: 0.5f);
        Assert.AreEqual(1f, At(hitLater, 0.3f), Tolerance);
        Assert.AreEqual(0f, At(hitLater, 0.7f), Tolerance, "정지");
        Assert.AreEqual(1f, At(hitLater, 1f), Tolerance, "늦게 시작한 피격 애니");
        var same = Reach(0.5f, freeze: 0.5f);
        Assert.AreEqual(0f, At(same, 0.5f), Tolerance, "같은 시각 정지 > 손 뻗기");
    }

    [Test]
    public void 상체_가중치는_페이드_인_뒤_피격_애니가_늦으면_페이드_아웃()
    {
        Assert.AreEqual(0f, BossVictoryReachRig.UpperWeight(5f, false, 1f, 0.2f, false, 0f, 0.1f), Tolerance, "안 틀었으면 0");
        Assert.AreEqual(0f, BossVictoryReachRig.UpperWeight(1f, true, 1f, 0.2f, false, 0f, 0.1f), Tolerance);
        Assert.AreEqual(0.5f, BossVictoryReachRig.UpperWeight(1.1f, true, 1f, 0.2f, false, 0f, 0.1f), Tolerance);
        Assert.AreEqual(1f, BossVictoryReachRig.UpperWeight(2f, true, 1f, 0.2f, false, 0f, 0.1f), Tolerance);
        Assert.AreEqual(1f, BossVictoryReachRig.UpperWeight(1f, true, 1f, 0f, false, 0f, 0.1f), Tolerance, "페이드 0 = 바로");
        // 피격 애니 3.0 (뒤) → 0.1초 동안 1→0
        Assert.AreEqual(0.5f, BossVictoryReachRig.UpperWeight(3.05f, true, 1f, 0.2f, true, 3f, 0.1f), Tolerance);
        Assert.AreEqual(0f, BossVictoryReachRig.UpperWeight(3.2f, true, 1f, 0.2f, true, 3f, 0.1f), Tolerance);
        // 피격 애니 0.5 (앞) → 손 뻗기가 이김
        Assert.AreEqual(1f, BossVictoryReachRig.UpperWeight(2f, true, 1f, 0.2f, true, 0.5f, 0.1f), Tolerance);
        // 같은 시각 → 피격 애니가 이김
        Assert.AreEqual(0f, BossVictoryReachRig.UpperWeight(1f, true, 1f, 0f, true, 1f, 0f), Tolerance);
    }

    [Test]
    public void 꺼짐이면_지금과_같다()
    {
        var off = new BossVictoryTimeline { bossHitAnimTime = 0f, bossFreezeTime = 0.6f, bossSlowTime = 1.2f };
        var withFlag = off.BossNodes(true, -1f, true);
        Assert.IsFalse(withFlag.HasReach, "시각 0 = 끔");
        var plain = off.BossNodes(true);
        for (float t = 0f; t < 2f; t += 0.05f)
            Assert.AreEqual(At(plain, t), At(withFlag, t), Tolerance, $"t {t}");
        var on = new BossVictoryTimeline { bossReachTime = 0.3f };
        Assert.IsTrue(on.BossNodes(false, -1f, true).HasReach);
        Assert.IsFalse(on.BossNodes(false, -1f, false).HasReach, "상체 상태 이름 없음 = 안 틂");
        Assert.AreEqual(0f, BossVictoryTimeline.CreateDefault().bossReachTime, "코드 기본 = 끔");
        Assert.IsTrue(BossVictoryTimeline.ZeroMeansOff(BossVictoryMarker.BossReachAnim));
    }

    [Test]
    public void 마커_시각_자르기_복사()
    {
        var src = new BossVictoryTimeline { bossReachTime = 1.1f, bossReachTrimSeconds = 0.4f };
        var dst = new BossVictoryTimeline();
        dst.CopyMarkerFrom(src, BossVictoryMarker.BossReachAnim);
        Assert.AreEqual(1.1f, dst.MarkerTime(BossVictoryMarker.BossReachAnim), Tolerance);
        Assert.AreEqual(0.4f, dst.bossReachTrimSeconds, Tolerance);
        dst.SetMarkerTime(BossVictoryMarker.BossReachAnim, 2f);
        Assert.AreEqual(2f, dst.bossReachTime, Tolerance);
    }

    [Test]
    public void 애니_속도_기본_1_0이하는_1_복사도_같이()
    {
        var tl = new BossVictoryTimeline();
        Assert.AreEqual(1f, tl.ReachUpperAnimSpeed, Tolerance);
        Assert.AreEqual(1f, tl.ReachWalkAnimSpeed, Tolerance);
        tl.bossReachUpperAnimSpeed = 0f;
        tl.bossReachWalkAnimSpeed = -1f;
        Assert.AreEqual(1f, tl.ReachUpperAnimSpeed, Tolerance, "0 이하 = 1(옛 데이터)");
        Assert.AreEqual(1f, tl.ReachWalkAnimSpeed, Tolerance);
        var src = new BossVictoryTimeline { bossReachUpperAnimSpeed = 0.5f, bossReachWalkAnimSpeed = 1.5f };
        tl.CopyMarkerFrom(src, BossVictoryMarker.BossReachAnim);
        Assert.AreEqual(0.5f, tl.ReachUpperAnimSpeed, Tolerance);
        Assert.AreEqual(1.5f, tl.ReachWalkAnimSpeed, Tolerance);
    }

    [Test]
    public void 걷기_발_속도_측정값이_코드_기본값과_맞다()
    {
        float v = BossVictoryReachAuthoring.MeasureWalkGroundSpeed(out string detail);
        if (v <= 0f) Assert.Ignore("측정 불가 — " + detail);
        Assert.AreEqual(BossVictoryReachRig.MeasuredWalkGroundSpeed, v, 0.25f, $"걷기 클립이 바뀌었으면 MeasuredWalkGroundSpeed 갱신 — {detail}");
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try { Assert.AreEqual(BossVictoryReachRig.MeasuredWalkGroundSpeed, s.bossReachWalkGroundSpeed, Tolerance); }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 옛_돌진_준비_값을_그대로_읽는다()
    {
        // 에셋 직렬화(YAML)는 FormerlySerializedAs 를 따른다(JsonUtility 는 안 따라 속성만 확인).
        Assert.AreEqual("bossDashPrepTime", FormerName(nameof(BossVictoryTimeline.bossReachTime)));
        Assert.AreEqual("bossDashPrepTrimSeconds", FormerName(nameof(BossVictoryTimeline.bossReachTrimSeconds)));
    }

    static string FormerName(string field)
    {
        var attr = (UnityEngine.Serialization.FormerlySerializedAsAttribute)System.Attribute.GetCustomAttribute(
            typeof(BossVictoryTimeline).GetField(field), typeof(UnityEngine.Serialization.FormerlySerializedAsAttribute));
        return attr?.oldName;
    }

    [Test]
    public void 상체_마스크_뼈_고르기()
    {
        foreach (string up in new[] { "c_spine_01.x", "spine_02.x", "c_breast_01.l", "shoulder.r", "arm_stretch.l", "c_arm_twist_offset.r", "forearm_twist.l", "hand.r", "c_index1.l", "c_thumb2.r", "c_pinky1_base.l", "neck.x", "c_head.x", "head_scale_fix.x", "c_root_master.x", "c_root.x", "root.x" })
            Assert.IsTrue(BossVictoryReachAuthoring.IsUpperBone(up), up);
        foreach (string low in new[] { "rig", "c_pos", "c_traj", "c_thigh_b.l", "thigh_stretch.r", "leg_twist.l", "foot.r", "toes_01.l", "c_toes_index1.l", "c_toes_ring1.r", "c_toes_thumb1.l", "slot_rider.x", "c_tail_01.x" })
            Assert.IsFalse(BossVictoryReachAuthoring.IsUpperBone(low), low);
    }

    [Test]
    public void 기본_설정과_23호_컨트롤러_저작_결과()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Assert.AreEqual(BossVictoryReachRig.UpperLayer, s.bossReachUpperLayer);
            Assert.AreEqual(BossVictoryReachRig.UpperState, s.bossReachUpperState);
            Assert.AreEqual(BossVictoryReachRig.UpperSpeedParam, s.bossReachUpperSpeedParam);
            Assert.AreEqual(BossVictoryReachRig.LowerState, s.bossReachLowerState);
        }
        finally { Object.DestroyImmediate(s); }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BossVictoryReachAuthoring.ControllerPath);
        if (controller == null) Assert.Ignore($"{BossVictoryReachAuthoring.ControllerPath} 없음");
        AnimatorControllerLayer upper = controller.layers.FirstOrDefault(l => l.name == BossVictoryReachRig.UpperLayer);
        Assert.IsNotNull(upper, "상체 레이어 — 저작 메뉴 'Tools/Boss Victory/보스 손 뻗기·걷기 레이어 만들기'");
        Assert.AreEqual(0f, upper.defaultWeight, Tolerance, "게임 중 영향 없음");
        Assert.IsNotNull(upper.avatarMask, "상체 마스크");
        Assert.Greater(upper.avatarMask.transformCount, 0);
        AnimatorState reach = upper.stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name == BossVictoryReachRig.UpperState);
        Assert.IsNotNull(reach);
        Assert.AreEqual("Boss_23_magneticgrab", reach.motion != null ? reach.motion.name : null);
        Assert.IsTrue(reach.speedParameterActive);
        Assert.AreEqual(BossVictoryReachRig.UpperSpeedParam, reach.speedParameter);
        Assert.AreEqual(0, reach.transitions.Length);
        Assert.AreEqual("Empty", upper.stateMachine.defaultState.name);
        AnimatorState walk = controller.layers[0].stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name == BossVictoryReachRig.LowerState);
        Assert.IsNotNull(walk);
        Assert.AreEqual("Boss_23_walk", walk.motion != null ? walk.motion.name : null);
        Assert.IsTrue(walk.speedParameterActive);
        Assert.AreEqual(BossVictoryReachRig.WalkSpeedParam, walk.speedParameter);
        Assert.AreEqual(0, walk.transitions.Length, "처치 연출만 직접 튼다");
        Assert.IsTrue(controller.parameters.Any(p => p.name == BossVictoryReachRig.UpperSpeedParam && p.type == AnimatorControllerParameterType.Float && Mathf.Approximately(p.defaultFloat, 1f)));
    }
}
