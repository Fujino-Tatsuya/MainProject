#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 130 — 야간 맵 직행 단계 판정(<see cref="QABoss103Plan.LobbyStepsFor"/>)·자리별 캐릭터 id 매핑(<see cref="QABoss103Plan.TryMapSlotCharacterIds"/>).</summary>
public sealed class QABoss103DirectLaunchTests
{
    // 로스터 칸 순서 예 — 0 팔라딘 · 1 못 쓰는 칸 · 2 거너 · 3 어쌔신.
    private static readonly QABoss103Character?[] Roster =
        { QABoss103Character.Paladin, null, QABoss103Character.Gunner, QABoss103Character.Assassin };

    [Test]
    public void 야간은_로비_점검_선택대_출격_집결을_건너뛰고_맵_직행()
    {
        QABoss103LobbySteps s = QABoss103Plan.LobbyStepsFor(true);
        Assert.AreEqual(QABoss103LobbySteps.DirectLaunch, s);
        Assert.AreEqual(QABoss103LobbySteps.None, s & (QABoss103LobbySteps.LobbySmoke | QABoss103LobbySteps.PickCharacter | QABoss103LobbySteps.Sortie));
    }

    [Test]
    public void 수동은_선택대_걷기와_출격_집결_그대로()
    {
        QABoss103LobbySteps s = QABoss103Plan.LobbyStepsFor(false);
        Assert.AreEqual(QABoss103LobbySteps.PickCharacter | QABoss103LobbySteps.Sortie, s);
        Assert.AreEqual(QABoss103LobbySteps.None, s & QABoss103LobbySteps.DirectLaunch);
    }

    [Test]
    public void 자리_순서대로_로스터_id_를_고른다()
    {
        var order = new[] { QABoss103Character.Gunner, QABoss103Character.Paladin, QABoss103Character.Assassin };
        var ids = new List<ulong> { 5, 0, 2 };   // 정렬 무관 — 자리 = 더 작은 id 수
        Assert.IsTrue(QABoss103Plan.TryMapSlotCharacterIds(ids, slot => order[slot], Roster, out List<QABoss103SlotPick> picks, out string failure));
        Assert.IsNull(failure);
        Assert.AreEqual(3, picks.Count);
        Assert.AreEqual(0UL, picks[0].ClientId);
        Assert.AreEqual(0, picks[0].Slot);
        Assert.AreEqual(QABoss103Character.Gunner, picks[0].Character);
        Assert.AreEqual(2, picks[0].CharacterId);
        Assert.AreEqual(2UL, picks[1].ClientId);
        Assert.AreEqual(0, picks[1].CharacterId);
        Assert.AreEqual(5UL, picks[2].ClientId);
        Assert.AreEqual(3, picks[2].CharacterId);
        Assert.AreEqual("자리0 client0 Gunner=id2, 자리1 client2 Paladin=id0, 자리2 client5 Assassin=id3", QABoss103Plan.FormatSlotPicks(picks));
    }

    [Test]
    public void 같은_캐릭터_칸이_여럿이면_처음_칸()
    {
        var roster = new QABoss103Character?[] { QABoss103Character.Gunner, QABoss103Character.Gunner };
        Assert.IsTrue(QABoss103Plan.TryMapSlotCharacterIds(new List<ulong> { 0 }, _ => QABoss103Character.Gunner, roster, out List<QABoss103SlotPick> picks, out _));
        Assert.AreEqual(0, picks[0].CharacterId);
    }

    [Test]
    public void 로스터에_없는_캐릭터면_실패()
    {
        var roster = new QABoss103Character?[] { QABoss103Character.Paladin, null };
        Assert.IsFalse(QABoss103Plan.TryMapSlotCharacterIds(new List<ulong> { 0, 1 }, slot => slot == 0 ? QABoss103Character.Paladin : QABoss103Character.Assassin,
            roster, out _, out string failure));
        Assert.AreEqual("자리 1(client 1) Assassin 로스터 칸 없음", failure);
        Assert.IsFalse(QABoss103Plan.TryMapSlotCharacterIds(new List<ulong> { 0 }, _ => QABoss103Character.Paladin, null, out _, out _));
    }
}
#endif
