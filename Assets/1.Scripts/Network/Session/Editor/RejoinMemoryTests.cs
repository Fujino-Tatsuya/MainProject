using System;
using NUnit.Framework;

/// <summary>
/// 재합류 보관소(C14 R4c) — playerId 키 보관·꺼내면 지움(두 번 되돌리지 않음)·덮어쓰기·비우기 크기(누적 0).
/// </summary>
public sealed class RejoinMemoryTests
{
    static TraitRejoinSnapshot Traits(params int[] acquired) =>
        new TraitRejoinSnapshot(acquired, 0, false, 0, 0, false, 0, Array.Empty<int>());

    [Test]
    public void 보관한_특성은_꺼내면_지워진다()
    {
        var memory = new RejoinMemory();
        Assert.IsTrue(memory.SaveTraits("a", Traits(1, 2)));
        Assert.AreEqual(1, memory.TraitCount);

        Assert.IsTrue(memory.TryTakeTraits("a", out var snapshot));
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot.Acquired);
        Assert.AreEqual(0, memory.TraitCount);
        Assert.IsFalse(memory.TryTakeTraits("a", out _), "Drop 경로에서 같은 사람에게 또 와도 두 번 되돌리지 않는다");
    }

    [Test]
    public void 같은_사람이_다시_나가면_마지막_기록으로_덮는다()
    {
        var memory = new RejoinMemory();
        memory.SaveTraits("a", Traits(1));
        memory.SaveTraits("a", Traits(1, 5));

        Assert.AreEqual(1, memory.TraitCount);
        Assert.IsTrue(memory.TryTakeTraits("a", out var snapshot));
        CollectionAssert.AreEqual(new[] { 1, 5 }, snapshot.Acquired);
    }

    [Test]
    public void 빈_playerId_와_빈_기록은_보관하지_않는다()
    {
        var memory = new RejoinMemory();
        Assert.IsFalse(memory.SaveTraits(null, Traits(1)));
        Assert.IsFalse(memory.SaveTraits("", Traits(1)));
        Assert.IsFalse(memory.SaveTraits("a", null));
        Assert.IsFalse(memory.TryTakeTraits(null, out _));
        Assert.AreEqual(0, memory.Count);
    }

    [Test]
    public void 비우면_크기를_돌려주고_0_이_된다()
    {
        var memory = new RejoinMemory();
        memory.SaveTraits("a", Traits(1));
        memory.SaveTraits("b", Traits(2));

        Assert.AreEqual(2, memory.Clear());
        Assert.AreEqual(0, memory.Count);
        Assert.AreEqual(0, memory.Clear(), "두 번째 비우기는 0");

        memory.SaveTraits("c", Traits(3));
        Assert.AreEqual(1, memory.ClearTraits());
        Assert.IsFalse(memory.TryTakeTraits("c", out _));
    }

    // ─── 생명·체력(C14 R5c) ─────────────────────────────────────────────

    static PlayerRejoinLifeRecord Life(PlayerLifeState state, float ratio = 0f) => new PlayerRejoinLifeRecord(state, ratio);

    [Test]
    public void 디스폰_메모를_퇴장_때_playerId_기록으로_옮기고_꺼내면_지워진다()
    {
        var memory = new RejoinMemory();
        memory.NoteDespawnedLife(5, Life(PlayerLifeState.Alive, 0.4f));
        Assert.AreEqual(1, memory.DespawnedLifeCount);

        Assert.IsTrue(memory.CommitLifeOnDisconnect(5, "a", out var committed));
        Assert.AreEqual(Life(PlayerLifeState.Alive, 0.4f), committed);
        Assert.AreEqual(0, memory.DespawnedLifeCount, "메모는 옮기면 지운다");
        Assert.AreEqual(1, memory.LifeCount);

        Assert.IsTrue(memory.TryTakeLife("a", out var record));
        Assert.AreEqual(PlayerLifeState.Alive, record.State);
        Assert.AreEqual(0.4f, record.HpRatio, 1e-5f);
        Assert.IsFalse(memory.TryTakeLife("a", out _), "두 번 되돌리지 않는다");
    }

    [Test]
    public void 메모_없이_퇴장하면_이전_기록을_그대로_둔다()
    {
        // Soul 로 나감 → 재접속했지만 스폰 보류 중(PlayerObject 없음) 다시 나감 → 첫 기록 유지
        var memory = new RejoinMemory();
        memory.NoteDespawnedLife(5, Life(PlayerLifeState.Soul));
        memory.CommitLifeOnDisconnect(5, "a", out _);

        Assert.IsFalse(memory.CommitLifeOnDisconnect(9, "a", out _));
        Assert.IsTrue(memory.TryTakeLife("a", out var record));
        Assert.AreEqual(PlayerLifeState.Soul, record.State);
    }

    [Test]
    public void 같은_사람이_다시_나가면_생명도_마지막_기록으로_덮는다()
    {
        var memory = new RejoinMemory();
        memory.SaveLife("a", Life(PlayerLifeState.Alive, 0.9f));
        memory.NoteDespawnedLife(9, Life(PlayerLifeState.PermanentDead));
        memory.CommitLifeOnDisconnect(9, "a", out _);

        Assert.AreEqual(1, memory.LifeCount);
        Assert.IsTrue(memory.TryTakeLife("a", out var record));
        Assert.AreEqual(PlayerLifeState.PermanentDead, record.State);
        Assert.AreEqual(0f, record.HpRatio, "죽은 기록의 비율은 0");
    }

    [Test]
    public void 전멸_리스폰은_부재자_생명_기록을_전부_Alive_만피로_덮는다()
    {
        var memory = new RejoinMemory();
        memory.SaveLife("soul", Life(PlayerLifeState.Soul));
        memory.SaveLife("dead", Life(PlayerLifeState.PermanentDead));
        memory.SaveLife("hurt", Life(PlayerLifeState.Alive, 0.3f));
        memory.SaveLife("full", PlayerRejoinLifeRecord.AliveFull);
        memory.NoteDespawnedLife(7, Life(PlayerLifeState.Soul));

        Assert.AreEqual(3, memory.OverwriteAbsentLivesAfterWipe(), "이미 Alive·만피인 기록은 세지 않는다");
        foreach (var id in new[] { "soul", "dead", "hurt", "full" })
        {
            Assert.IsTrue(memory.TryTakeLife(id, out var record), id);
            Assert.AreEqual(PlayerRejoinLifeRecord.AliveFull, record, id);
        }

        Assert.AreEqual(1, memory.DespawnedLifeCount, "퇴장 확정 전 메모는 손대지 않는다");
        Assert.AreEqual(0, memory.OverwriteAbsentLivesAfterWipe(), "빈 보관소는 0");
    }

    [Test]
    public void 전체_수와_비우기에_생명_기록이_들어간다()
    {
        var memory = new RejoinMemory();
        memory.SaveTraits("a", Traits(1));
        memory.SaveLife("a", Life(PlayerLifeState.Soul));
        memory.NoteDespawnedLife(3, Life(PlayerLifeState.Alive, 1f));

        Assert.AreEqual(3, memory.Count);
        Assert.AreEqual(3, memory.Clear());
        Assert.AreEqual(0, memory.Count);

        memory.SaveTraits("a", Traits(1));
        memory.SaveLife("a", Life(PlayerLifeState.Soul));
        memory.NoteDespawnedLife(3, Life(PlayerLifeState.Alive, 1f));
        Assert.AreEqual(2, memory.ClearLives(), "판 재시작 — 생명 기록·메모만");
        Assert.AreEqual(1, memory.TraitCount);
        Assert.IsFalse(memory.TryTakeLife("a", out _));
    }

    [Test]
    public void 빈_playerId_는_생명도_보관하지_않는다()
    {
        var memory = new RejoinMemory();
        Assert.IsFalse(memory.SaveLife(null, Life(PlayerLifeState.Soul)));
        Assert.IsFalse(memory.SaveLife("", Life(PlayerLifeState.Soul)));
        Assert.IsFalse(memory.TryTakeLife(null, out _));
        Assert.AreEqual(0, memory.LifeCount);
    }
}
