using NUnit.Framework;

public sealed class SoundCatalogLookupTests
{
    [Test]
    public void TryGet_ReturnsMatchingEntry()
    {
        var expected = new SoundEntry("player.hit");
        var lookup = new SoundCatalogLookup(new[] { expected });

        Assert.That(lookup.TryGet("player.hit", out SoundEntry actual), Is.True);
        Assert.That(actual, Is.SameAs(expected));
    }

    [Test]
    public void TryGet_ReturnsFalseForMissingKey()
    {
        var lookup = new SoundCatalogLookup(new[] { new SoundEntry("player.hit") });

        Assert.That(lookup.TryGet("player.missing", out SoundEntry entry), Is.False);
        Assert.That(entry, Is.Null);
    }

    [Test]
    public void Constructor_ReportsDuplicateAndKeepsFirstEntry()
    {
        var first = new SoundEntry("player.hit");
        var second = new SoundEntry("player.hit");
        var lookup = new SoundCatalogLookup(new[] { first, second });

        Assert.That(lookup.DuplicateKeys, Is.EqualTo(new[] { "player.hit" }));
        Assert.That(lookup.TryGet("player.hit", out SoundEntry entry), Is.True);
        Assert.That(entry, Is.SameAs(first));
    }

    [Test]
    public void Constructor_ReportsNullEmptyAndWhitespaceKeys()
    {
        var lookup = new SoundCatalogLookup(new SoundEntry[]
        {
            null,
            new SoundEntry(string.Empty),
            new SoundEntry("   ")
        });

        Assert.That(lookup.EmptyKeyIndices, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(lookup.TryGet(string.Empty, out _), Is.False);
    }
}
