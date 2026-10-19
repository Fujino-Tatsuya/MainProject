using NUnit.Framework;

/// <summary>캐릭터 선택 저장소 — 재접속(새 clientId)에도 playerId 로 마지막 선택을 되살린다(PLAN-48 Q4).</summary>
public sealed class CharacterSelectionMemoryTests
{
    [Test]
    public void 선택을_clientId_로_조회한다()
    {
        var memory = new CharacterSelectionMemory();
        memory.BindPlayer(1, "booth-2");
        memory.Set(1, 2);

        Assert.That(memory.TryGet(1, out var characterId), Is.True);
        Assert.That(characterId, Is.EqualTo(2));
    }

    [Test]
    public void 재접속하면_새_clientId_에_마지막_선택을_되살린다()
    {
        var memory = new CharacterSelectionMemory();
        memory.BindPlayer(1, "booth-2");
        memory.Set(1, 2);

        memory.RemoveClient(1);
        Assert.That(memory.TryGet(1, out _), Is.False);

        memory.BindPlayer(5, "booth-2");
        Assert.That(memory.TryGet(5, out var characterId), Is.True);
        Assert.That(characterId, Is.EqualTo(2));
    }

    [Test]
    public void 처음_보는_playerId_는_선택이_없다()
    {
        var memory = new CharacterSelectionMemory();
        memory.BindPlayer(3, "booth-3");

        Assert.That(memory.TryGet(3, out _), Is.False);
    }

    [Test]
    public void playerId_없이_기록한_선택은_퇴장하면_사라진다()
    {
        var memory = new CharacterSelectionMemory();
        memory.Set(4, 1);
        memory.RemoveClient(4);
        memory.BindPlayer(6, "booth-4");

        Assert.That(memory.TryGet(6, out _), Is.False);
    }

    [Test]
    public void 빈_playerId_바인드는_무시한다()
    {
        var memory = new CharacterSelectionMemory();
        memory.BindPlayer(1, "");
        memory.Set(1, 2);
        memory.RemoveClient(1);
        memory.BindPlayer(2, "");

        Assert.That(memory.TryGet(2, out _), Is.False);
    }

    [Test]
    public void 다른_playerId_끼리는_섞이지_않는다()
    {
        var memory = new CharacterSelectionMemory();
        memory.BindPlayer(1, "booth-2");
        memory.BindPlayer(2, "booth-3");
        memory.Set(1, 1);
        memory.Set(2, 2);

        memory.RemoveClient(1);
        memory.RemoveClient(2);
        memory.BindPlayer(7, "booth-3");

        Assert.That(memory.TryGet(7, out var characterId), Is.True);
        Assert.That(characterId, Is.EqualTo(2));
    }

    [Test]
    public void Clear_는_playerId_기억까지_지운다()
    {
        var memory = new CharacterSelectionMemory();
        memory.BindPlayer(1, "booth-2");
        memory.Set(1, 2);
        memory.Clear();
        memory.BindPlayer(1, "booth-2");

        Assert.That(memory.TryGet(1, out _), Is.False);
    }
}
