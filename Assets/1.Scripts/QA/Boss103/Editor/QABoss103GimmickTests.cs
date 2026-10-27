#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 103 C3b 순수 규칙 — 송전탑 배정(자리 순서·거리·이어 맡기·돕기·유지·풀림)·기믹 선택·드론 유도 자리·착탄 원·R 허용.</summary>
public sealed class QABoss103GimmickTests
{
    private const float Eps = 1e-4f;

    private static QABoss103PylonInfo Live(ulong id, float x, float z) => new QABoss103PylonInfo(id, x, z, false, true);
    private static QABoss103PylonInfo Rising(ulong id, float x, float z) => new QABoss103PylonInfo(id, x, z, true, false);
    private static QABoss103PylonInfo Down(ulong id, float x, float z) => new QABoss103PylonInfo(id, x, z, false, false);
    private static QABoss103BotSeat Seat(int slot, float x, float z, bool present = true) => new QABoss103BotSeat(slot, x, z, present);

    private static ulong Target(QABoss103PylonAssigner a, int slot)
    {
        Assert.IsTrue(a.TryGet(slot, out ulong id), $"자리 {slot} 배정 없음");
        return id;
    }

    // ── 배정 ────────────────────────────────────────────────

    [Test]
    public void 세명_네탑_자리순서로_가까운_탑을_하나씩()
    {
        var a = new QABoss103PylonAssigner();
        var pylons = new List<QABoss103PylonInfo> { Live(10, 0, 10), Live(11, 10, 0), Live(12, 0, -10), Live(13, -10, 0) };
        var seats = new List<QABoss103BotSeat> { Seat(2, -9, 0), Seat(0, 0, 9), Seat(1, 9, 1) };
        a.Tick(pylons, seats);
        Assert.AreEqual(10ul, Target(a, 0));
        Assert.AreEqual(11ul, Target(a, 1));
        Assert.AreEqual(13ul, Target(a, 2));
        Assert.AreEqual(3, a.Assignments);
    }

    [Test]
    public void 같은_탑이_가까우면_앞자리가_먼저_맡고_뒷자리는_다음_가까운_탑()
    {
        var a = new QABoss103PylonAssigner();
        var pylons = new List<QABoss103PylonInfo> { Live(1, 0, 0), Live(2, 5, 0) };
        var seats = new List<QABoss103BotSeat> { Seat(0, 0.5f, 0), Seat(1, 0.2f, 0) };
        a.Tick(pylons, seats);
        Assert.AreEqual(1ul, Target(a, 0), "자리 0 이 먼저 고른다");
        Assert.AreEqual(2ul, Target(a, 1), "자리 1 은 남은 탑");
    }

    [Test]
    public void 남는_탑은_먼저_끝낸_봇이_이어서_맡는다()
    {
        var a = new QABoss103PylonAssigner();
        var seats = new List<QABoss103BotSeat> { Seat(0, 0, 9), Seat(1, 9, 0), Seat(2, -9, 0) };
        a.Tick(new List<QABoss103PylonInfo> { Live(10, 0, 10), Live(11, 10, 0), Live(12, 0, -10), Live(13, -10, 0) }, seats);
        Assert.IsTrue(a.TryGet(0, out _) && a.TryGet(1, out _) && a.TryGet(2, out _));

        // 자리 1 의 탑(11)이 부서졌다 → 자리 1 이 아무도 안 맡은 12 를 맡는다. 다른 자리는 그대로.
        a.Tick(new List<QABoss103PylonInfo> { Live(10, 0, 10), Down(11, 10, 0), Live(12, 0, -10), Live(13, -10, 0) }, seats);
        Assert.AreEqual(10ul, Target(a, 0));
        Assert.AreEqual(12ul, Target(a, 1));
        Assert.AreEqual(13ul, Target(a, 2));
        Assert.AreEqual(4, a.Assignments);
    }

    [Test]
    public void 탑이_사람보다_적으면_남는_봇은_가장_가까운_탑을_돕는다()
    {
        var a = new QABoss103PylonAssigner();
        var pylons = new List<QABoss103PylonInfo> { Live(1, 0, 10), Live(2, 0, -10) };
        var seats = new List<QABoss103BotSeat> { Seat(0, 0, 9), Seat(1, 0, -9), Seat(2, 1, -5) };
        a.Tick(pylons, seats);
        Assert.AreEqual(1ul, Target(a, 0));
        Assert.AreEqual(2ul, Target(a, 1));
        Assert.AreEqual(2ul, Target(a, 2), "맡을 탑이 없으면 가장 가까운 탑을 함께");
    }

    [Test]
    public void 배정은_탑이_서있는_동안_거리가_바뀌어도_유지()
    {
        var a = new QABoss103PylonAssigner();
        var pylons = new List<QABoss103PylonInfo> { Live(1, 0, 10), Live(2, 0, -10) };
        a.Tick(pylons, new List<QABoss103BotSeat> { Seat(0, 0, 9) });
        Assert.AreEqual(1ul, Target(a, 0));
        a.Tick(pylons, new List<QABoss103BotSeat> { Seat(0, 0, -9) });
        Assert.AreEqual(1ul, Target(a, 0), "회피 등으로 다른 탑 쪽에 가도 맡은 탑 유지");
        Assert.AreEqual(1, a.Assignments);
    }

    [Test]
    public void 상승_중인_탑도_배정_후보이고_Live_로_바뀌어도_유지()
    {
        var a = new QABoss103PylonAssigner();
        var seats = new List<QABoss103BotSeat> { Seat(0, 0, 9) };
        a.Tick(new List<QABoss103PylonInfo> { Rising(1, 0, 10) }, seats);
        Assert.AreEqual(1ul, Target(a, 0), "올라오는 동안 미리 다가간다");
        a.Tick(new List<QABoss103PylonInfo> { Live(1, 0, 10) }, seats);
        Assert.AreEqual(1ul, Target(a, 0));
        Assert.AreEqual(1, a.Assignments);
    }

    [Test]
    public void 탑이_모두_내려가면_배정이_풀린다()
    {
        var a = new QABoss103PylonAssigner();
        var seats = new List<QABoss103BotSeat> { Seat(0, 0, 0), Seat(1, 1, 0) };
        a.Tick(new List<QABoss103PylonInfo> { Live(1, 0, 10) }, seats);
        a.Tick(new List<QABoss103PylonInfo> { Down(1, 0, 10) }, seats);
        Assert.IsFalse(a.TryGet(0, out _));
        Assert.IsFalse(a.TryGet(1, out _));
        a.Tick(new List<QABoss103PylonInfo>(), seats);
        Assert.IsFalse(a.TryGet(0, out _), "탑 목록이 비어도 배정 없음");
    }

    [Test]
    public void 쓰러진_자리는_배정을_잃고_그_탑은_다음에_끝낸_봇이_맡는다()
    {
        var a = new QABoss103PylonAssigner();
        var pylons = new List<QABoss103PylonInfo> { Live(1, 0, 10), Live(2, 10, 0), Live(3, 0, -10) };
        a.Tick(pylons, new List<QABoss103BotSeat> { Seat(0, 0, 9), Seat(1, 9, 0), Seat(2, 0, -9) });
        Assert.AreEqual(1ul, Target(a, 0));

        // 자리 0 사망 → 배정 풀림. 자리 1 이 자기 탑(2)을 끝내면 비어 있는 1·(남은 것) 중 가까운 것을 맡는다.
        var deadSeats = new List<QABoss103BotSeat> { Seat(0, 0, 9, false), Seat(1, 5, 5), Seat(2, 0, -9) };
        a.Tick(pylons, deadSeats);
        Assert.IsFalse(a.TryGet(0, out _));
        a.Tick(new List<QABoss103PylonInfo> { Live(1, 0, 10), Down(2, 10, 0), Live(3, 0, -10) }, deadSeats);
        Assert.AreEqual(1ul, Target(a, 1), "아무도 안 맡은 탑 1");
        Assert.AreEqual(3ul, Target(a, 2));
    }

    [Test]
    public void 같은_입력이면_같은_배정_봇마다_따로_굴려도_일치()
    {
        var pylons = new List<QABoss103PylonInfo> { Live(7, 3, 4), Live(5, -3, 4), Live(9, 0, -6), Live(8, 6, -2) };
        var seats = new List<QABoss103BotSeat> { Seat(1, 1, 1), Seat(0, -1, 2), Seat(2, 2, -3) };
        var a = new QABoss103PylonAssigner();
        var b = new QABoss103PylonAssigner();
        a.Tick(pylons, seats);
        seats.Reverse();
        pylons.Reverse();
        b.Tick(pylons, seats);
        for (int s = 0; s < 3; s++)
            Assert.AreEqual(Target(a, s), Target(b, s), $"자리 {s} — 목록 순서와 무관");
    }

    [Test]
    public void 같은_거리면_작은_id()
    {
        var pylons = new List<QABoss103PylonInfo> { Live(9, 1, 0), Live(4, -1, 0) };
        Assert.IsTrue(QABoss103PylonAssigner.TryNearest(pylons, 0, 0, null, out ulong id));
        Assert.AreEqual(4ul, id);
        Assert.IsFalse(QABoss103PylonAssigner.TryNearest(new List<QABoss103PylonInfo> { Down(1, 0, 0) }, 0, 0, null, out _), "내려간 탑은 후보 아님");
    }

    // ── 기믹 선택 ───────────────────────────────────────────

    [Test]
    public void 기믹_우선순위_실드_드론유도_송전탑()
    {
        Assert.AreEqual(QABoss103GimmickKind.Shield, QABoss103Gimmick.Decide(true, true, true, true));
        Assert.AreEqual(QABoss103GimmickKind.DroneGuide, QABoss103Gimmick.Decide(false, true, true, true));
        Assert.AreEqual(QABoss103GimmickKind.Pylon, QABoss103Gimmick.Decide(false, true, false, true), "Live 탑이 없으면 유도 대신 배정 탑(상승 대기)");
        Assert.AreEqual(QABoss103GimmickKind.Pylon, QABoss103Gimmick.Decide(false, false, true, true));
        Assert.AreEqual(QABoss103GimmickKind.None, QABoss103Gimmick.Decide(false, true, false, false), "탑 없는 드론 = 일반 회피");
        Assert.AreEqual(QABoss103GimmickKind.None, QABoss103Gimmick.Decide(false, false, false, false));
    }

    [Test]
    public void 대상_지정_R_은_보스_전용()
    {
        Assert.IsTrue(QABoss103Gimmick.AllowR(true, true), "보스 대상 지정 R");
        Assert.IsFalse(QABoss103Gimmick.AllowR(false, true), "탑에 팔라딘·거너 R 금지");
        Assert.IsTrue(QABoss103Gimmick.AllowR(false, false), "어쌔신 변신은 탑에도");
    }

    // ── 드론 유도 ───────────────────────────────────────────

    [Test]
    public void 유도_자리는_탑에서_봇_쪽_몸반경_더하기_여유()
    {
        QABoss103Gimmick.GuideSpot(10f, 0f, 0.6f, 20f, 0f, out float x, out float z);
        Assert.AreEqual(10f + 0.6f + QABoss103Gimmick.GuideStandOff, x, Eps);
        Assert.AreEqual(0f, z, Eps);

        QABoss103Gimmick.GuideSpot(0f, 0f, 1f, 0f, -5f, out x, out z);
        Assert.AreEqual(0f, x, Eps);
        Assert.AreEqual(-(1f + QABoss103Gimmick.GuideStandOff), z, Eps);

        QABoss103Gimmick.GuideSpot(2f, 3f, 0.5f, 2f, 3f, out x, out z);
        Assert.AreEqual(2f, x, Eps);
        Assert.AreEqual(3f + 0.5f + QABoss103Gimmick.GuideStandOff, z, Eps, "탑 중심에 있으면 +z 쪽");
    }

    [Test]
    public void 유도_자리에서_고정되면_착탄_원에_탑이_들어간다()
    {
        // 자리 = 표면 + 0.5m, 원 반경 2m(타일 4m × 1칸 지름) — 게임 판정식(중심 거리 ≤ 반경 + 0.75).
        QABoss103Gimmick.GuideSpot(0f, 0f, 0.6f, 5f, 5f, out float x, out float z);
        Assert.IsTrue(QABoss103Gimmick.InDroneReach(x, z, 2f, 0f, 0f));
        Assert.IsTrue(QABoss103Gimmick.InDroneReach(2.75f, 0f, 2f, 0f, 0f), "경계 포함");
        Assert.IsFalse(QABoss103Gimmick.InDroneReach(2.8f, 0f, 2f, 0f, 0f));
    }

    [Test]
    public void 로그_토큰()
    {
        Assert.AreEqual("pylon", QABoss103Gimmick.Token(QABoss103GimmickKind.Pylon));
        Assert.AreEqual("drone_guide", QABoss103Gimmick.Token(QABoss103GimmickKind.DroneGuide));
        Assert.AreEqual("shield", QABoss103Gimmick.Token(QABoss103GimmickKind.Shield));
        Assert.AreEqual("none", QABoss103Gimmick.Token(QABoss103GimmickKind.None));
        Assert.AreEqual("pylon_42", QABoss103Gimmick.PylonToken(42));
    }
}
#endif
