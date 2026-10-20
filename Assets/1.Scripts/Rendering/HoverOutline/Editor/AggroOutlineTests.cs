using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// "나를 노리는 몬스터" 회색 외곽선(할 일 58 후속 3) — 서버 복제 값 규칙(상태별 대상 id),
/// 클라 판정(나 · 살아 있음), 고르기(빨강 우선 · 합집합 · 상한 · 가까운 순 · 사망 제외).
/// </summary>
public sealed class AggroOutlineTests
{
    const ulong Me = 7;
    const ulong Other = 12;

    sealed class Target
    {
        public readonly string Name;
        public Target(string name) => Name = name;
        public override string ToString() => Name;
    }

    static List<OutlineCandidate<Target>> Candidates(params (Target t, float d, bool ok)[] items)
    {
        var list = new List<OutlineCandidate<Target>>();
        foreach (var (t, d, ok) in items)
            list.Add(new OutlineCandidate<Target>(t, d, ok));
        return list;
    }

    // ── 서버 복제 값 ──

    [TestCase(MonsterState.Chase)]
    [TestCase(MonsterState.Attack)]
    public void 추적_공격_중이면_대상_id(MonsterState state)
    {
        Assert.AreEqual(Me, MonsterAggroTargetRule.ReplicatedTargetId(state, false, false, Me));
    }

    [TestCase(MonsterState.Idle)]
    [TestCase(MonsterState.Hit)]
    [TestCase(MonsterState.Groggy)]
    [TestCase(MonsterState.Return)]
    [TestCase(MonsterState.Dead)]
    [TestCase(MonsterState.Knockback)]
    public void 그_밖_상태는_0(MonsterState state)
    {
        Assert.AreEqual(MonsterAggroTargetRule.NoTarget, MonsterAggroTargetRule.ReplicatedTargetId(state, false, false, Me));
    }

    [Test]
    public void 사망이면_추적_중이어도_0()
    {
        Assert.AreEqual(MonsterAggroTargetRule.NoTarget,
            MonsterAggroTargetRule.ReplicatedTargetId(MonsterState.Chase, true, false, Me));
    }

    [Test]
    public void 휴면_연출정지면_0()
    {
        Assert.AreEqual(MonsterAggroTargetRule.NoTarget,
            MonsterAggroTargetRule.ReplicatedTargetId(MonsterState.Attack, false, true, Me));
    }

    [Test]
    public void 대상이_없으면_0()
    {
        Assert.AreEqual(MonsterAggroTargetRule.NoTarget,
            MonsterAggroTargetRule.ReplicatedTargetId(MonsterState.Chase, false, false, MonsterAggroTargetRule.NoTarget));
    }

    [Test]
    public void 대상_캐시는_null이면_0()
    {
        var cache = new MonsterAggroTargetRule.TargetIdCache();
        Assert.AreEqual(MonsterAggroTargetRule.NoTarget, cache.Resolve(null));
    }

    // ── 클라 판정 ──

    [Test]
    public void 내_id면_나를_노림()
    {
        Assert.IsTrue(MonsterAggroTargetRule.IsTargetingLocal(Me, Me, true));
    }

    [Test]
    public void 다른_플레이어를_노리면_아님()
    {
        Assert.IsFalse(MonsterAggroTargetRule.IsTargetingLocal(Other, Me, true));
    }

    [Test]
    public void 죽은_몬스터는_아님()
    {
        Assert.IsFalse(MonsterAggroTargetRule.IsTargetingLocal(Me, Me, false));
    }

    [Test]
    public void id_0끼리는_노림이_아님()
    {
        // 미스폰 등으로 로컬 id 가 0 이어도 "대상 없음(0)" 몬스터가 나를 노리는 것으로 보이면 안 된다.
        Assert.IsFalse(MonsterAggroTargetRule.IsTargetingLocal(MonsterAggroTargetRule.NoTarget, MonsterAggroTargetRule.NoTarget, true));
    }

    // ── 고르기 ──

    [Test]
    public void 여러_마리는_가까운_순()
    {
        Target a = new Target("a"), b = new Target("b"), c = new Target("c");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((c, 9f, true), (a, 1f, true), (b, 4f, true)),
            null, new List<Target>(), 8, result);
        CollectionAssert.AreEqual(new[] { a, b, c }, result);
    }

    [Test]
    public void 상한_8_넘으면_가까운_8마리()
    {
        var items = new List<(Target, float, bool)>();
        var expected = new List<Target>();
        for (int i = 0; i < 12; i++)
        {
            var t = new Target("m" + i);
            items.Add((t, 100f - i, true));   // 뒤로 갈수록 가깝다
        }
        for (int i = 11; i >= 4; i--)
            expected.Add(items[i].Item1);

        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates(items.ToArray()), null, new List<Target>(),
            MonsterAggroTargetRule.DefaultMaxTargets, result);
        Assert.AreEqual(8, MonsterAggroTargetRule.DefaultMaxTargets);
        CollectionAssert.AreEqual(expected, result);
    }

    [Test]
    public void 호버_대상은_빨강이_이긴다()
    {
        Target hovered = new Target("hover"), b = new Target("b");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((hovered, 1f, true), (b, 4f, true)),
            hovered, new List<Target>(), 8, result);
        CollectionAssert.AreEqual(new[] { b }, result);
    }

    [Test]
    public void 스킬_범위_대상은_빨강이_이긴다()
    {
        Target inRange = new Target("range"), b = new Target("b");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((inRange, 1f, true), (b, 4f, true)),
            null, new List<Target> { inRange }, 8, result);
        CollectionAssert.AreEqual(new[] { b }, result);
    }

    [Test]
    public void 빨강으로_빠진_자리는_상한에_안_센다()
    {
        Target r = new Target("range"), a = new Target("a"), b = new Target("b");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((r, 0f, true), (a, 1f, true), (b, 2f, true)),
            null, new List<Target> { r }, 2, result);
        CollectionAssert.AreEqual(new[] { a, b }, result);
    }

    [Test]
    public void 사망_휴면_후보는_제외()
    {
        // 휴면·연출 정지는 서버가 0 을 복제 → 클라 판정 false. 사망은 Selectable false.
        Target dead = new Target("dead"), dormant = new Target("dormant"), ok = new Target("ok");
        bool deadOk = MonsterAggroTargetRule.IsTargetingLocal(Me, Me, false);
        ulong dormantId = MonsterAggroTargetRule.ReplicatedTargetId(MonsterState.Chase, false, true, Me);
        bool dormantOk = MonsterAggroTargetRule.IsTargetingLocal(dormantId, Me, true);

        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((dead, 1f, deadOk), (dormant, 2f, dormantOk), (ok, 3f, true)),
            null, new List<Target>(), 8, result);
        CollectionAssert.AreEqual(new[] { ok }, result);
    }

    [Test]
    public void 상한_0이면_끔()
    {
        Target a = new Target("a");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((a, 1f, true)), null, new List<Target>(), 0, result);
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void 합집합_빨강과_나를노림은_겹치지_않는다()
    {
        // 빨강 세트(호버 h + 범위 r1) ∪ 나를 노림 세트 = 전체, 교집합 없음.
        Target h = new Target("h"), r1 = new Target("r1"), a1 = new Target("a1"), a2 = new Target("a2");
        var primary = new List<Target> { r1 };
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((h, 1f, true), (r1, 2f, true), (a1, 3f, true), (a2, 4f, true)),
            h, primary, 8, result);

        CollectionAssert.AreEqual(new[] { a1, a2 }, result);
        CollectionAssert.DoesNotContain(result, h);
        CollectionAssert.DoesNotContain(result, r1);
    }

    // ── 스킬 인디케이터 켜짐(할 일 58 후속 4) ──

    [Test]
    public void 인디케이터_켜짐이면_나를노림_0_빨강은_그대로()
    {
        Target h = new Target("h"), r1 = new Target("r1"), a1 = new Target("a1");
        var primary = new List<Target> { r1 };
        var range = new List<Target>(primary);
        var result = new List<Target> { a1 }; // 지난 프레임 결과가 남아 있어도 비운다
        MonsterAggroTargetRule.SelectAggro(Candidates((h, 1f, true), (r1, 2f, true), (a1, 3f, true)),
            h, primary, 8, result, skillIndicatorActive: true);

        Assert.AreEqual(0, result.Count);
        CollectionAssert.AreEqual(range, primary); // 빨강 세트는 건드리지 않는다
    }

    [Test]
    public void 인디케이터_꺼지면_나를노림_복귀()
    {
        Target a1 = new Target("a1"), a2 = new Target("a2");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((a1, 1f, true), (a2, 2f, true)),
            null, new List<Target>(), 8, result, skillIndicatorActive: true);
        Assert.AreEqual(0, result.Count);

        MonsterAggroTargetRule.SelectAggro(Candidates((a1, 1f, true), (a2, 2f, true)),
            null, new List<Target>(), 8, result, skillIndicatorActive: false);
        CollectionAssert.AreEqual(new[] { a1, a2 }, result);
    }

    [Test]
    public void 범위_대상_0이어도_인디케이터_켜짐이면_나를노림_0()
    {
        Target a1 = new Target("a1");
        var result = new List<Target>();
        MonsterAggroTargetRule.SelectAggro(Candidates((a1, 1f, true)),
            null, new List<Target>(), 8, result, skillIndicatorActive: true);
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void 도장은_찍은_프레임만_켜짐_보고_끊기면_자동_해제()
    {
        var stamp = new MonsterAggroTargetRule.FrameStamp();
        Assert.IsFalse(stamp.IsSet(0), "기본값은 꺼짐(프레임 0 포함)");

        stamp.Mark(10);
        Assert.IsTrue(stamp.IsSet(10));
        Assert.IsFalse(stamp.IsSet(11), "다음 프레임에 보고가 없으면 꺼짐");

        stamp.Mark(11);
        Assert.IsTrue(stamp.IsSet(11), "매 프레임 보고하면 계속 켜짐");
    }

    [Test]
    public void 도장_Clear는_같은_프레임에_해제()
    {
        var stamp = new MonsterAggroTargetRule.FrameStamp();
        stamp.Mark(5);
        stamp.Clear();
        Assert.IsFalse(stamp.IsSet(5), "시전·취소 프레임에 바로 나를 노림 복귀");
    }
}
