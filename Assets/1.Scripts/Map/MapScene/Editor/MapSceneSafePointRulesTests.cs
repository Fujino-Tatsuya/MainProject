using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 스텝별 고정 안전 지점(할 일 80) 순수 규칙(<see cref="MapSceneSafePointRules"/>) —
/// 복귀 구간(스텝·보스방 이동 카운트다운 = 보스 진입 지점·엘리베이터 대기 = 기존·보스전 = 재합류 모서리 + 오프셋)·지금 스텝 구간(부활과 같음)·저작 기본 위치·항상 지금 스텝 지점(대체 없음)·점유 = 정적 장애물만·막히면 주변 링·링 실패 시 지점 그대로·복귀 높이. 문서: Docs/tech/tutorial-director.md §12.
/// </summary>
public sealed class MapSceneSafePointRulesTests
{
    // 존1 (0..10) → 복도1 (10..20, 폭 4) → 존2 (20..30). 진행 = +X.
    static MapSceneAreaTracker Layout()
    {
        var t = new MapSceneAreaTracker();
        t.SetArea(MapSceneArea.Zone1, MapSceneRect.FromMinMax(0f, -5f, 10f, 5f));
        t.SetArea(MapSceneArea.Corridor1, MapSceneRect.FromMinMax(10f, -2f, 20f, 2f));
        t.SetArea(MapSceneArea.Zone2, MapSceneRect.FromMinMax(20f, -5f, 30f, 5f));
        return t;
    }

    static int Mask(params MapSceneGate[] gates)
    {
        int m = 0;
        foreach (MapSceneGate g in gates) m |= 1 << (int)g;
        return m;
    }

    // ─── 감시 조건 ────────────────────────────────────────────────────────

    [Test]
    public void ReturnPhase_StepCountdownElevatorBossFight()
    {
        Assert.AreEqual(MapSceneReturnPhase.Step, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.Zone2, false, false));
        Assert.AreEqual(MapSceneReturnPhase.None, MapSceneSafePointRules.ReturnPhase(false, MapSceneStep.Zone2, false, false), "튜토리얼 아님");
        Assert.AreEqual(MapSceneReturnPhase.None, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.None, false, false), "활성 전");

        // 카운트다운 중엔 IsEncounterBusy 도 true(코루틴 대기) — 카운트다운이 우선.
        Assert.AreEqual(MapSceneReturnPhase.BossCountdown, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.BossEnter, true, true), "패드 카운트다운");
        Assert.AreEqual(MapSceneReturnPhase.BossCountdown, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.Zone4Combat, true, true), "타이머 만료 강제 이동 카운트다운");

        // 카운트다운 끝 → 엘리베이터 연출·도착 대기 = 기존 경로 그대로(할 일 89).
        Assert.AreEqual(MapSceneReturnPhase.None, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.BossEnter, false, true), "엘리베이터 대기는 변경 없음");

        Assert.AreEqual(MapSceneReturnPhase.BossFight, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.Finished, false, false), "보스방 도착 뒤 = 보스전");
        Assert.AreEqual(MapSceneReturnPhase.None, MapSceneSafePointRules.ReturnPhase(true, MapSceneStep.Finished, false, true), "Finished 여도 이동 연출 중이면 기존 경로");
    }

    [Test]
    public void FixedPointArea_CountdownIsBossEnter_StepIsStepArea()
    {
        Assert.AreEqual(MapSceneArea.Zone2, MapSceneSafePointRules.FixedPointArea(MapSceneReturnPhase.Step, MapSceneArea.Zone2));
        Assert.AreEqual(MapSceneArea.BossEnter, MapSceneSafePointRules.FixedPointArea(MapSceneReturnPhase.BossCountdown, MapSceneArea.Corridor4),
            "카운트다운 중엔 스텝 구간과 무관하게 보스 진입 지점");
        Assert.AreEqual(MapSceneArea.None, MapSceneSafePointRules.FixedPointArea(MapSceneReturnPhase.BossFight, MapSceneArea.Corridor4), "보스전 = 모서리");
        Assert.AreEqual(MapSceneArea.None, MapSceneSafePointRules.FixedPointArea(MapSceneReturnPhase.None, MapSceneArea.Corridor4), "엘리베이터 대기 = 기존 경로");
    }

    [Test]
    public void BossRoomFallPoint_IsFarthestRejoinCorner_PlusDropHeight()
    {
        var corners = new List<UnityEngine.Pose>
        {
            new UnityEngine.Pose(new UnityEngine.Vector3(-10f, 0.5f, -10f), UnityEngine.Quaternion.identity),
            new UnityEngine.Pose(new UnityEngine.Vector3(10f, 0.5f, -10f), UnityEngine.Quaternion.identity),
            new UnityEngine.Pose(new UnityEngine.Vector3(10f, 0.5f, 10f), UnityEngine.Quaternion.identity),
            new UnityEngine.Pose(new UnityEngine.Vector3(-10f, 0.5f, 10f), UnityEngine.Quaternion.identity),
        };
        var others = new List<UnityEngine.Vector3> { new UnityEngine.Vector3(-9f, 0f, -9f) };
        var bosses = new List<UnityEngine.Vector3> { new UnityEngine.Vector3(0f, 0f, -9f) };

        UnityEngine.Vector3 p = MapSceneSafePoints.BossRoomFallPoint(corners, others, bosses, 3f, out int corner);

        Assert.AreEqual(RejoinSpawnPicker.FarthestCorner(corners, others, bosses), corner, "재합류 모서리 선택 그대로");
        Assert.AreEqual(2, corner, "다른 플레이어·보스에서 가장 먼 모서리");
        Assert.AreEqual(10f, p.x, 1e-4f);
        Assert.AreEqual(10f, p.z, 1e-4f);
        Assert.AreEqual(3.5f, p.y, 1e-4f, "모서리 Y + 오프셋 3");
    }

    // ─── 지금 스텝 구간 = 자동 부활 ──────────────────────────────────────

    [Test]
    public void StepArea_MatchesRevive_PerPlayer()
    {
        int mask = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry);
        Assert.AreEqual(MapSceneArea.Zone2, MapSceneSafePointRules.StepArea(MapSceneStep.Zone2, mask, 0), "단계 = 존2");
        Assert.AreEqual(MapSceneArea.Corridor1, MapSceneSafePointRules.StepArea(MapSceneStep.Corridor1, mask, 0));

        int passed = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry);
        Assert.AreEqual(MapSceneRevivePointRules.ResolveStageArea(MapSceneStep.Corridor1, mask, passed),
            MapSceneSafePointRules.StepArea(MapSceneStep.Corridor1, mask, passed), "그 플레이어가 지나온 최전방 구간 반영(부활과 같음)");
    }

    // ─── 저작 기본 위치 ──────────────────────────────────────────────────

    [Test]
    public void DefaultAnchor_Zone1Center_OthersInsideEntryBarrier()
    {
        MapSceneAreaTracker t = Layout();
        var specs = new List<MapSceneBarrierSpec>
        {
            new MapSceneBarrierSpec(MapSceneGate.G1_Corridor1Entry, 1, true, MapSceneRect.FromMinMax(10f, -2.3f, 10.4f, 2.3f), true, 1f),
            new MapSceneBarrierSpec(MapSceneGate.G2_Zone2Entry, 1, false, MapSceneRect.FromMinMax(19.6f, -2.3f, 20f, 2.3f), true, 1f),
        };

        Assert.IsTrue(MapSceneSafePointRules.TryDefaultAnchor(MapSceneArea.Zone1, t, specs, 2.5f, out float x, out float z));
        Assert.AreEqual(5f, x, 1e-4);
        Assert.AreEqual(0f, z, 1e-4);

        Assert.IsTrue(MapSceneSafePointRules.TryDefaultAnchor(MapSceneArea.Corridor1, t, specs, 1.5f, out x, out z));
        Assert.AreEqual(11.9f, x, 1e-4, "입구 배리어 앞면 10.4 + 1.5");
        Assert.AreEqual(0f, z, 1e-4);

        Assert.IsTrue(MapSceneSafePointRules.TryDefaultAnchor(MapSceneArea.Zone2, t, specs, 2.5f, out x, out z));
        Assert.AreEqual(22.5f, x, 1e-4, "복도1 출구 배리어 앞면 20 + 2.5");

        Assert.IsFalse(MapSceneSafePointRules.TryDefaultAnchor(MapSceneArea.Zone3, t, specs, 2.5f, out _, out _), "구간 없음");
    }

    [Test]
    public void DefaultAnchor_NegativeForward_GoesTowardMinus()
    {
        var t = new MapSceneAreaTracker();
        t.SetArea(MapSceneArea.Corridor2, MapSceneRect.FromMinMax(-2f, -190f, 2f, -180f));
        var specs = new List<MapSceneBarrierSpec>
        {
            // 복도2 입구 — 진행 = −Z (z 큰 쪽에서 작은 쪽으로)
            new MapSceneBarrierSpec(MapSceneGate.G3_Corridor2Entry, 2, true, MapSceneRect.FromMinMax(-2.3f, -180.4f, 2.3f, -180f), false, -1f),
        };
        Assert.IsTrue(MapSceneSafePointRules.TryDefaultAnchor(MapSceneArea.Corridor2, t, specs, 1.5f, out float x, out float z));
        Assert.AreEqual(0f, x, 1e-4);
        Assert.AreEqual(-181.9f, z, 1e-4, "앞면 −180.4 에서 −Z 로 1.5");
    }

    // ─── 고르기 · 점유(정적 장애물만) · 주변 링 · 높이 ─────────────────────

    [Test]
    public void PickStepPoint_AlwaysStepArea_NoOtherAreaFallback()
    {
        Assert.IsTrue(MapSceneSafePointRules.TryPickStepPoint(MapSceneArea.Zone2, _ => true, out MapSceneArea area));
        Assert.AreEqual(MapSceneArea.Zone2, area, "지금 스텝 구간 지점");

        Assert.IsFalse(MapSceneSafePointRules.TryPickStepPoint(MapSceneArea.Zone2, a => a != MapSceneArea.Zone2, out area),
            "지금 구간 지점이 없으면 뒤 구간으로 대체하지 않고 기존 추락 복구");
        Assert.AreEqual(MapSceneArea.None, area);

        Assert.IsFalse(MapSceneSafePointRules.TryPickStepPoint(MapSceneArea.None, _ => true, out area), "구간 없음");
        Assert.IsFalse(MapSceneSafePointRules.TryPickStepPoint(MapSceneArea.Zone2, null, out area));
    }

    [Test]
    public void BlockingLayers_StaticOnly_MonsterOverlapKeepsPoint()
    {
        foreach (string moving in new[] { "Enemy", "EnemyHurtBox", "Player", "PlayerHurtbox", "Corpse", "Soul", "Weapon", "Projectile", "" })
            Assert.IsFalse(MapSceneSafePointRules.IsBlockingLayer(moving), $"{moving} — 움직이는 것은 지점을 무효로 만들지 않는다");
        foreach (string fixedLayer in new[] { "Default", "Ground", "Env", "Wall", "Barrier" })
            Assert.IsTrue(MapSceneSafePointRules.IsBlockingLayer(fixedLayer), $"{fixedLayer} — 정적 장애물");

        // 몬스터가 지점 위에 있어도 정적 장애물이 아니면 막힘 아님 → 링을 보지 않고 지점 그대로.
        MapSceneRect zone1 = MapSceneRect.FromMinMax(0f, -5f, 10f, 5f);
        int ringChecks = 0;
        Assert.AreEqual(MapSceneSafeSpotKind.Point,
            MapSceneSafePointRules.PickSpot(5f, 0f, false, zone1, (_, __) => { ringChecks++; return true; }, out float x, out float z));
        Assert.AreEqual(0, ringChecks, "막히지 않았으면 링을 보지 않는다");
        Assert.AreEqual(5f, x, 1e-6);
        Assert.AreEqual(0f, z, 1e-6);
    }

    [Test]
    public void PickSpot_ObstacleOnPoint_ClosestRingSpotInsideArea()
    {
        MapSceneRect zone1 = MapSceneRect.FromMinMax(0f, -5f, 10f, 5f);
        // 지점 (5,0) 위에 반경 1m 기둥.
        System.Func<float, float, bool> free = (x, z) => (x - 5f) * (x - 5f) + z * z > 1f;

        Assert.AreEqual(MapSceneSafeSpotKind.Ring, MapSceneSafePointRules.PickSpot(5f, 0f, true, zone1, free, out float sx, out float sz));
        Assert.AreEqual(6.5f, sx, 1e-4, "0.75m 링은 전부 막힘 → 1.5m 링 첫 방향(+X)");
        Assert.AreEqual(0f, sz, 1e-4);

        // 구간 가장자리 지점 (9.5,0) — +X 쪽 후보는 구간 밖이라 건너뛰고 구간 안 빈자리.
        System.Func<float, float, bool> freeEdge = (px, pz) => (px - 9.5f) * (px - 9.5f) + pz * pz > 1f;
        Assert.AreEqual(MapSceneSafeSpotKind.Ring, MapSceneSafePointRules.PickSpot(9.5f, 0f, true, zone1, freeEdge, out sx, out sz));
        Assert.IsTrue(zone1.Contains(sx, sz), "구간 밖(벽 너머)은 고르지 않는다");
        Assert.AreEqual(9.5f, sx, 1e-4);
        Assert.AreEqual(1.5f, sz, 1e-4);
    }

    [Test]
    public void PickSpot_RingFails_StaysAtStepPoint_NoStartFallback()
    {
        MapSceneRect zone2 = MapSceneRect.FromMinMax(20f, -5f, 30f, 5f);
        Assert.AreEqual(MapSceneSafeSpotKind.PointOverlap,
            MapSceneSafePointRules.PickSpot(22.5f, 0f, true, zone2, (_, __) => false, out float sx, out float sz), "전부 막힘 → 지점 그대로(겹침 허용)");
        Assert.AreEqual(22.5f, sx, 1e-6, "씬 시작 지점·다른 구간이 아니라 지금 스텝 지점");
        Assert.AreEqual(0f, sz, 1e-6);

        Assert.AreEqual(MapSceneSafeSpotKind.PointOverlap,
            MapSceneSafePointRules.PickSpot(22.5f, 0f, true, null, (_, __) => true, out sx, out sz), "구간 사각형 없음 → 링 없이 지점 그대로");
        Assert.AreEqual(22.5f, sx, 1e-6);
    }

    [Test]
    public void DropY_PointPlusOffset_Default3m()
    {
        Assert.AreEqual(3f, MapSceneSafePointRules.DefaultDropHeight, 1e-6);
        Assert.AreEqual(4.5f, MapSceneSafePointRules.DropY(1.5f, MapSceneSafePointRules.DefaultDropHeight), 1e-5, "고른 자리(지점·링) Y + 3");
        Assert.AreEqual(-7f, MapSceneSafePointRules.DropY(-10f, 3f), 1e-5);
        Assert.AreEqual(2f, MapSceneSafePointRules.DropY(2f, 0f), 1e-5, "0 = 지점 높이 그대로");
        Assert.AreEqual(2f, MapSceneSafePointRules.DropY(2f, -1f), 1e-5, "음수는 0");
    }
}
