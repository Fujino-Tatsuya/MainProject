using NUnit.Framework;

public sealed class CharacterRosterLookupTests
{
    [Test]
    public void MatchingHash_ReturnsItsIndex()
    {
        Assert.That(CharacterRosterLookup.FindIndex(22u, new uint[] { 11u, 22u, 33u }), Is.EqualTo(1));
    }

    [Test]
    public void UnknownHash_ReturnsMinusOne()
    {
        Assert.That(CharacterRosterLookup.FindIndex(44u, new uint[] { 11u, 22u, 33u }), Is.EqualTo(-1));
    }

    [Test]
    public void ZeroHash_NeverMatchesEmptyRow()
    {
        Assert.That(CharacterRosterLookup.FindIndex(0u, new uint[] { 0u, 22u }), Is.EqualTo(-1));
    }

    [Test]
    public void EmptyRow_IsSkipped()
    {
        Assert.That(CharacterRosterLookup.FindIndex(22u, new uint[] { 0u, 22u }), Is.EqualTo(1));
    }

    [Test]
    public void DuplicateHash_FirstRowWins()
    {
        Assert.That(CharacterRosterLookup.FindIndex(22u, new uint[] { 22u, 22u }), Is.EqualTo(0));
    }

    [Test]
    public void NullOrEmptyRoster_ReturnsMinusOne()
    {
        Assert.That(CharacterRosterLookup.FindIndex(22u, null), Is.EqualTo(-1));
        Assert.That(CharacterRosterLookup.FindIndex(22u, new uint[0]), Is.EqualTo(-1));
    }
}
