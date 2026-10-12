using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// "MapScene.GoToResult" 가변 페이로드 직렬화 왕복(PLAN-result-stats).
/// </summary>
public sealed class SessionResultPayloadTests
{
    static List<PlayerSessionStats> MakeRows(int count)
    {
        var rows = new List<PlayerSessionStats>();
        for (int i = 0; i < count; i++)
        {
            rows.Add(new PlayerSessionStats
            {
                ClientId = (ulong)i,
                CharacterId = i % 2,
                Damage = 1000 + i,
                Kills = 10 + i,
                Counters = i,
                Deaths = 2 * i
            });
        }
        return rows;
    }

    static bool RoundTrip(bool hasValue, SessionOutcome outcome, float seconds, List<PlayerSessionStats> rows,
        out bool readHasValue, out SessionOutcome readOutcome, out float readSeconds, List<PlayerSessionStats> readRows)
    {
        int size = SessionResultPayload.GetSize(rows != null ? rows.Count : 0);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        var payloadWriter = writer;
        SessionResultPayload.Write(ref payloadWriter, hasValue, outcome, seconds, rows);

        using var reader = new FastBufferReader(writer, Allocator.Temp);
        var payloadReader = reader;
        return SessionResultPayload.TryRead(ref payloadReader, out readHasValue, out readOutcome, out readSeconds, readRows);
    }

    [Test]
    public void 행이_그대로_왕복한다()
    {
        List<PlayerSessionStats> rows = MakeRows(3);
        var read = new List<PlayerSessionStats>();

        Assert.IsTrue(RoundTrip(true, SessionOutcome.Aborted, 123.5f, rows,
            out bool hasValue, out SessionOutcome outcome, out float seconds, read));

        Assert.IsTrue(hasValue);
        Assert.AreEqual(SessionOutcome.Aborted, outcome);
        Assert.AreEqual(123.5f, seconds, 1e-5f);
        Assert.AreEqual(rows.Count, read.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.AreEqual(rows[i].ClientId, read[i].ClientId);
            Assert.AreEqual(rows[i].CharacterId, read[i].CharacterId);
            Assert.AreEqual(rows[i].Damage, read[i].Damage);
            Assert.AreEqual(rows[i].Kills, read[i].Kills);
            Assert.AreEqual(rows[i].Counters, read[i].Counters);
            Assert.AreEqual(rows[i].Deaths, read[i].Deaths);
        }
    }

    [Test]
    public void 결과_없음도_정상_페이로드다()
    {
        var read = new List<PlayerSessionStats> { new PlayerSessionStats() };
        Assert.IsTrue(RoundTrip(false, SessionOutcome.Cleared, 10f, MakeRows(2),
            out bool hasValue, out _, out _, read));
        Assert.IsFalse(hasValue);
        Assert.AreEqual(0, read.Count, "읽기 버퍼는 비운다");
    }

    [Test]
    public void 행이_없어도_왕복한다()
    {
        var read = new List<PlayerSessionStats>();
        Assert.IsTrue(RoundTrip(true, SessionOutcome.Wiped, 5f, new List<PlayerSessionStats>(),
            out bool hasValue, out SessionOutcome outcome, out _, read));
        Assert.IsTrue(hasValue);
        Assert.AreEqual(SessionOutcome.Wiped, outcome);
        Assert.AreEqual(0, read.Count);
    }

    [Test]
    public void 상한을_넘으면_잘라서_보낸다()
    {
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
            new System.Text.RegularExpressions.Regex("상한"));

        List<PlayerSessionStats> rows = MakeRows(SessionResultPayload.MaxPlayers + 3);
        var read = new List<PlayerSessionStats>();
        Assert.IsTrue(RoundTrip(true, SessionOutcome.Cleared, 1f, rows,
            out bool hasValue, out _, out _, read));
        Assert.IsTrue(hasValue);
        Assert.AreEqual(SessionResultPayload.MaxPlayers, read.Count);
        Assert.AreEqual(rows[0].ClientId, read[0].ClientId, "clientId 작은 쪽(앞)부터 남는다");
    }

    [Test]
    public void 크기는_행_수에_비례하고_상한에서_멈춘다()
    {
        int one = SessionResultPayload.GetSize(1) - SessionResultPayload.GetSize(0);
        Assert.Greater(one, 0);
        Assert.AreEqual(SessionResultPayload.GetSize(0) + one * 3, SessionResultPayload.GetSize(3));
        Assert.AreEqual(SessionResultPayload.GetSize(SessionResultPayload.MaxPlayers),
            SessionResultPayload.GetSize(SessionResultPayload.MaxPlayers + 5));
    }

    [Test]
    public void 알_수_없는_결과_종류는_거부한다()
    {
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe(true);
        writer.WriteValueSafe((byte)200);
        writer.WriteValueSafe(1f);
        writer.WriteValueSafe((byte)0);

        using var reader = new FastBufferReader(writer, Allocator.Temp);
        var payloadReader = reader;
        var read = new List<PlayerSessionStats>();
        Assert.IsFalse(SessionResultPayload.TryRead(ref payloadReader, out bool hasValue, out _, out _, read));
        Assert.IsFalse(hasValue);
    }

    [Test]
    public void 길이가_모자란_페이로드는_거부한다()
    {
        using var writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe(true);
        writer.WriteValueSafe((byte)SessionOutcome.Cleared);
        writer.WriteValueSafe(1f);
        writer.WriteValueSafe((byte)2); // 행 2개라고 해 놓고 하나도 안 씀

        using var reader = new FastBufferReader(writer, Allocator.Temp);
        var payloadReader = reader;
        var read = new List<PlayerSessionStats>();
        Assert.IsFalse(SessionResultPayload.TryRead(ref payloadReader, out bool hasValue, out _, out _, read));
        Assert.IsFalse(hasValue);
        Assert.AreEqual(0, read.Count);
    }
}
