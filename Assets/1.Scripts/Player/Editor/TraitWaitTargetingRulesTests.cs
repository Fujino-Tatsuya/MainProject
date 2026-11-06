using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 증강(특성) 고른 뒤 파티 대기 중 타겟팅 스킬 금지(할 일 236) — 순수 규칙 + 기간 경계.
/// 기간 = 그 복도 특성 체크가 켜진 순간 ~ 그 복도 출구 게이트가 열릴 때까지, 맵씬 진행 중(끝나기 전)만.
/// </summary>
public sealed class TraitWaitTargetingRulesTests
{
    static MapScenePlayerProgress Progress(params MapSceneCheck[] checks)
    {
        var p = new MapScenePlayerProgress { ClientId = 0, Connected = true };
        foreach (MapSceneCheck c in checks) p.CheckMask |= 1 << (int)c;
        return p;
    }

    static System.Func<MapSceneGate, bool> Open(params MapSceneGate[] gates)
    {
        var set = new HashSet<MapSceneGate>(gates);
        return g => set.Contains(g);
    }

    // 복도 N 진행 중(입구까지 열림)
    static readonly MapSceneGate[] Corridor1Running = { MapSceneGate.G1_Corridor1Entry };
    static readonly MapSceneGate[] Corridor2Running =
        { MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry };

    // ─── 한 게이트 규칙 ─────────────────────────────────────────────────

    [Test]
    public void IsBlocked_고른뒤_게이트닫힘만_참()
    {
        Assert.IsTrue(TraitWaitTargetingRules.IsBlocked(gateOpen: false, pickedThisGate: true));
        Assert.IsFalse(TraitWaitTargetingRules.IsBlocked(gateOpen: true, pickedThisGate: true), "게이트 열림 = 풀림");
        Assert.IsFalse(TraitWaitTargetingRules.IsBlocked(gateOpen: false, pickedThisGate: false), "고르기 전(창 떠 있음·중간 지점 전)");
        Assert.IsFalse(TraitWaitTargetingRules.IsBlocked(gateOpen: true, pickedThisGate: false));
    }

    [Test]
    public void AppliesTo_타겟팅모드만()
    {
        Assert.IsFalse(TraitWaitTargetingRules.AppliesTo(SkillTargetingMode.None), "방향형 Q·E·우클릭·평타");
        Assert.IsTrue(TraitWaitTargetingRules.AppliesTo(SkillTargetingMode.SingleTarget), "가렌 R·거너 R");
        Assert.IsTrue(TraitWaitTargetingRules.AppliesTo(SkillTargetingMode.GroundPoint), "어쌔신 변신 E");
    }

    [Test]
    public void BlocksSkill_대기중_타겟팅만()
    {
        Assert.IsTrue(TraitWaitTargetingRules.BlocksSkill(SkillTargetingMode.SingleTarget, true));
        Assert.IsTrue(TraitWaitTargetingRules.BlocksSkill(SkillTargetingMode.GroundPoint, true));
        Assert.IsFalse(TraitWaitTargetingRules.BlocksSkill(SkillTargetingMode.None, true));
        Assert.IsFalse(TraitWaitTargetingRules.BlocksSkill(SkillTargetingMode.SingleTarget, false));
    }

    // ─── 기간 경계 ─────────────────────────────────────────────────────

    [Test]
    public void 기간_고르기전_허용_고른순간_금지_게이트열리면_허용()
    {
        // 복도1 중간 지점 전·창 떠 있음(체크 없음)
        Assert.IsFalse(TraitWaitTargetingRules.IsWaitingForParty(true, Progress(), Open(Corridor1Running)));
        // 고른 순간(TraitCorridor1 체크) — 출구 G2 닫힘
        Assert.IsTrue(TraitWaitTargetingRules.IsWaitingForParty(true, Progress(MapSceneCheck.TraitCorridor1), Open(Corridor1Running)));
        // G2 열림(전원 선택·60초 자동) — 풀림
        Assert.IsFalse(TraitWaitTargetingRules.IsWaitingForParty(true, Progress(MapSceneCheck.TraitCorridor1),
            Open(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry)));
    }

    [Test]
    public void 기간_지난복도_체크는_다음복도에_새지않음()
    {
        // 복도1 골랐고 G2 열림, 복도2 진행 중 — 복도2 아직 안 고름 → 허용
        Assert.IsFalse(TraitWaitTargetingRules.IsWaitingForParty(true, Progress(MapSceneCheck.TraitCorridor1), Open(Corridor2Running)));
        // 복도2 고름 → 출구 G4 닫힘이라 금지
        Assert.IsTrue(TraitWaitTargetingRules.IsWaitingForParty(true,
            Progress(MapSceneCheck.TraitCorridor1, MapSceneCheck.TraitCorridor2), Open(Corridor2Running)));
    }

    [Test]
    public void 기간_복도4_출구_G10_까지()
    {
        var beforeG10 = Open(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry,
            MapSceneGate.G4_Zone3Entry, MapSceneGate.G5_SpinnerRealMode, MapSceneGate.G6_Corridor3Entry, MapSceneGate.G7_Zone4Entry,
            MapSceneGate.G8_TimerStart, MapSceneGate.G9_Corridor4Entry);
        var all = Progress(MapSceneCheck.TraitCorridor1, MapSceneCheck.TraitCorridor2, MapSceneCheck.TraitCorridor3,
            MapSceneCheck.TraitCorridor4);
        Assert.IsTrue(TraitWaitTargetingRules.IsWaitingForParty(true, all, beforeG10));
        Assert.IsFalse(TraitWaitTargetingRules.IsWaitingForParty(true, all,
            g => beforeG10(g) || g == MapSceneGate.G10_BossEnterAndPad));
    }

    [Test]
    public void 기간_맵씬진행아님_또는_끝남이면_항상허용()
    {
        // 보스 타이머 만료 강제 이동 — 보스방 도착(Finished)인데 G10 이 닫힌 채일 수 있다
        Assert.IsFalse(TraitWaitTargetingRules.IsWaitingForParty(false, Progress(MapSceneCheck.TraitCorridor4), Open()));
        Assert.IsFalse(TraitWaitTargetingRules.IsWaitingForParty(true, Progress(MapSceneCheck.TraitCorridor1), null));
    }
}
