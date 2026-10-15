using NUnit.Framework;

/// <summary>할 일 15 후속 — PrefabIdHash → 로스터 id → TutorialCharacter 역조회 · 실패 시 폴백 판정.</summary>
public sealed class TutorialCharacterLookupTests
{
    static readonly TutorialRosterRow[] Roster =
    {
        new TutorialRosterRow(913233600, TutorialCharacter.Paladin),
        new TutorialRosterRow(816596077, TutorialCharacter.Gunner),
        new TutorialRosterRow(3304217410, TutorialCharacter.Assassin),
    };

    [Test]
    public void Resolve_MatchesHashToRosterIdAndCharacter()
    {
        Assert.AreEqual(TutorialCharacterLookupStatus.Resolved,
            TutorialCharacterLookup.Resolve(816596077, Roster, out int rosterId, out TutorialCharacter character));
        Assert.AreEqual(1, rosterId);
        Assert.AreEqual(TutorialCharacter.Gunner, character);

        TutorialCharacterLookup.Resolve(3304217410, Roster, out rosterId, out character);
        Assert.AreEqual(2, rosterId);
        Assert.AreEqual(TutorialCharacter.Assassin, character);
    }

    [Test]
    public void Resolve_NoRosterWhenNullOrEmpty()
    {
        Assert.AreEqual(TutorialCharacterLookupStatus.NoRoster,
            TutorialCharacterLookup.Resolve(913233600, null, out int rosterId, out TutorialCharacter character));
        Assert.AreEqual(-1, rosterId);
        Assert.AreEqual(TutorialCharacter.None, character);
        Assert.AreEqual(TutorialCharacterLookupStatus.NoRoster,
            TutorialCharacterLookup.Resolve(913233600, new TutorialRosterRow[0], out _, out _));
    }

    [Test]
    public void Resolve_HashNotFoundWhenNoRowMatches()
    {
        Assert.AreEqual(TutorialCharacterLookupStatus.HashNotFound,
            TutorialCharacterLookup.Resolve(12345, Roster, out int rosterId, out TutorialCharacter character));
        Assert.AreEqual(-1, rosterId);
        Assert.AreEqual(TutorialCharacter.None, character);
    }

    [Test]
    public void Resolve_ZeroHashNeverMatchesEmptyRow()
    {
        var rows = new[] { new TutorialRosterRow(0, TutorialCharacter.Paladin) };

        Assert.AreEqual(TutorialCharacterLookupStatus.HashNotFound,
            TutorialCharacterLookup.Resolve(0, rows, out _, out _));
    }

    [Test]
    public void Resolve_NoMappingWhenRowCharacterIsNone()
    {
        var rows = new[] { new TutorialRosterRow(42, TutorialCharacter.None) };

        Assert.AreEqual(TutorialCharacterLookupStatus.NoMapping,
            TutorialCharacterLookup.Resolve(42, rows, out int rosterId, out _));
        Assert.AreEqual(0, rosterId);
    }

    [Test]
    public void Resolve_FirstRowWinsOnDuplicateHash()
    {
        var rows = new[]
        {
            new TutorialRosterRow(7, TutorialCharacter.Paladin),
            new TutorialRosterRow(7, TutorialCharacter.Gunner),
        };

        TutorialCharacterLookup.Resolve(7, rows, out int rosterId, out TutorialCharacter character);
        Assert.AreEqual(0, rosterId);
        Assert.AreEqual(TutorialCharacter.Paladin, character);
    }

    [Test]
    public void Choose_UsesResolvedOnlyOnSuccess()
    {
        Assert.AreEqual(TutorialCharacter.Gunner, TutorialCharacterLookup.Choose(
            TutorialCharacterLookupStatus.Resolved, TutorialCharacter.Gunner, TutorialCharacter.Paladin));
        Assert.AreEqual(TutorialCharacter.Paladin, TutorialCharacterLookup.Choose(
            TutorialCharacterLookupStatus.HashNotFound, TutorialCharacter.None, TutorialCharacter.Paladin));
        Assert.AreEqual(TutorialCharacter.Assassin, TutorialCharacterLookup.Choose(
            TutorialCharacterLookupStatus.NoMapping, TutorialCharacter.None, TutorialCharacter.Assassin));
        Assert.AreEqual(TutorialCharacter.None, TutorialCharacterLookup.Choose(
            TutorialCharacterLookupStatus.NoRoster, TutorialCharacter.None, TutorialCharacter.None));
    }
}
