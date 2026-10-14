using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 공용 벽 충돌 순수 판정(할 일 32) — 스윕 결과 → 벽 충돌인가 · 제외 규칙(자기·배리어·유닛·상자·시작 겹침) · 에피소드당 1회.
/// 러너: Tools/Tests/플레이어 EditMode 테스트 실행.
/// </summary>
public sealed class UnitWallImpactJudgeTests
{
    const int Default = 0, Player = 6, Wall = 7, Enemy = 8, EnemyHurtBox = 14, Barrier = 20;
    const int WallMask = 1 << Wall;
    static readonly Vector3 East = Vector3.right;

    static WallSweepHit Hit(int layer, float distance, Vector3 normal, bool self = false, bool overlap = false) =>
        new WallSweepHit { Layer = layer, Distance = distance, Normal = normal, IsSelf = self, StartedOverlapping = overlap };

    static WallJudgeResult Judge(float reach, params WallSweepHit[] hits) =>
        UnitWallImpactJudge.Judge(hits, hits.Length, WallMask, East, reach, out _);

    [Test]
    public void 정면_벽이_판정_거리_안이면_충돌()
    {
        var hits = new[] { Hit(Wall, 0.3f, Vector3.left) };
        Assert.AreEqual(WallJudgeResult.Wall, UnitWallImpactJudge.Judge(hits, 1, WallMask, East, 0.4f, out int i));
        Assert.AreEqual(0, i);
    }

    [Test]
    public void 판정_거리_밖이면_아니다()
    {
        Assert.AreEqual(WallJudgeResult.OutOfReach, Judge(0.4f, Hit(Wall, 0.6f, Vector3.left)));
    }

    [Test]
    public void 아무것도_없으면_None()
    {
        Assert.AreEqual(WallJudgeResult.None, Judge(0.4f));
    }

    [TestCase(Barrier)]
    [TestCase(Player)]
    [TestCase(Enemy)]
    [TestCase(EnemyHurtBox)]
    [TestCase(Default)]
    public void 벽_아닌_것은_레이어로_제외되고_먼저_닿으면_벽을_가린다(int layer)
    {
        Assert.AreEqual(WallJudgeResult.BlockedByOther, Judge(0.4f, Hit(layer, 0.2f, Vector3.left)), "벽 아님");
        Assert.AreEqual(WallJudgeResult.BlockedByOther, Judge(0.4f, Hit(Wall, 0.35f, Vector3.left), Hit(layer, 0.1f, Vector3.left)),
                        "벽 앞에 끼어 있으면 벽 충돌 아님");
        Assert.AreEqual(WallJudgeResult.Wall, Judge(0.4f, Hit(Wall, 0.1f, Vector3.left), Hit(layer, 0.3f, Vector3.left)),
                        "벽이 더 가까우면 벽 충돌");
    }

    [Test]
    public void 자기_콜라이더는_무시()
    {
        Assert.AreEqual(WallJudgeResult.Wall, Judge(0.4f, Hit(Enemy, 0f, Vector3.left, self: true), Hit(Wall, 0.2f, Vector3.left)));
        Assert.AreEqual(WallJudgeResult.None, Judge(0.4f, Hit(Wall, 0f, Vector3.left, self: true)), "자기 자신이 Wall 레이어여도");
    }

    [Test]
    public void 시작부터_겹친_바닥_유닛은_앞을_막지_않는다()
    {
        Assert.AreEqual(WallJudgeResult.Wall,
                        Judge(0.4f, Hit(Default, 0f, -East, overlap: true), Hit(Player, 0f, -East, overlap: true), Hit(Wall, 0.25f, Vector3.left)));
    }

    [Test]
    public void 시작부터_겹친_벽은_맞닿은_것으로_충돌()
    {
        // PhysX 는 시작 겹침에 normal = −방향을 준다 — 정면으로 본다.
        Assert.AreEqual(WallJudgeResult.Wall, Judge(0.4f, Hit(Wall, 0f, -East, overlap: true)));
    }

    [Test]
    public void 비스듬히_스치거나_벽에서_멀어지면_아니다()
    {
        Vector3 along = new Vector3(-0.2f, 0f, 1f).normalized;   // 진행 방향과 거의 수직인 벽 면
        Assert.AreEqual(WallJudgeResult.Grazing, Judge(0.4f, Hit(Wall, 0.2f, along)));
        Assert.AreEqual(WallJudgeResult.Grazing, Judge(0.4f, Hit(Wall, 0.2f, East)), "벽 반대쪽으로 밀림");
        Vector3 slanted = new Vector3(-1f, 0f, 1f).normalized;   // 45° — 정면 하한(0.3) 안
        Assert.AreEqual(WallJudgeResult.Wall, Judge(0.4f, Hit(Wall, 0.2f, slanted)));
    }

    [Test]
    public void 위를_향한_면은_벽이_아니다()
    {
        Assert.AreEqual(WallJudgeResult.Grazing, Judge(0.4f, Hit(Wall, 0.2f, Vector3.up)), "Wall 레이어의 윗면(수평 법선 없음)");
    }

    [Test]
    public void 막혔으면_판정_거리가_넓어진다()
    {
        Assert.AreEqual(0.4f, UnitWallImpactJudge.Reach(0.5f, 0.5f, 0.4f, 1.6f), 1e-5f, "막힘 없음");
        Assert.AreEqual(1.6f, UnitWallImpactJudge.Reach(0.5f, 0.1f, 0.4f, 1.6f), 1e-5f, "요청의 절반 미만 = 막힘(NavMesh 끝)");
        Assert.AreEqual(0.4f, UnitWallImpactJudge.Reach(0f, 0f, 0.4f, 1.6f), 1e-5f, "요청 0(물리·복제 위치 경로)은 막힘 아님");
    }

    [Test]
    public void 충격_속도는_법선_반대_성분()
    {
        Assert.AreEqual(6f, UnitWallImpactJudge.ImpactSpeed(6f, East, Vector3.left), 1e-4f);
        Assert.AreEqual(6f * 0.70710678f, UnitWallImpactJudge.ImpactSpeed(6f, East, new Vector3(-1f, 0f, 1f)), 1e-4f);
        Assert.AreEqual(0f, UnitWallImpactJudge.ImpactSpeed(6f, East, East), 1e-4f);
    }

    [Test]
    public void 에피소드당_한_번만()
    {
        var e = new WallImpactEpisode();
        Assert.IsFalse(e.TryConsume(), "외부 힘 전");
        e.Begin();
        Assert.IsTrue(e.TryConsume());
        Assert.IsFalse(e.TryConsume(), "같은 넉백에서 두 번째 충돌 없음");
        e.Begin();
        Assert.IsTrue(e.TryConsume(), "새 넉백이면 다시");
        e.End();
        e.Begin();
        e.End();
        Assert.IsFalse(e.TryConsume(), "끝난 넉백");
    }
}
