using NUnit.Framework;

/// <summary>선입력 버퍼(할 일 44) — 기록·만료·덮어쓰기·소비·비우기·끔.</summary>
public sealed class PlayerInputBufferTests
{
    private const float Hold = 0.2f;

    [Test]
    public void Record_ThenPeek_ReturnsInput()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(buffer.Has(PlayerBufferedInput.Attack, 10f), Is.True);
        Assert.That(buffer.Has(PlayerBufferedInput.Dash, 10f), Is.False);
    }

    [Test]
    public void Peek_WithinHold_Keeps_AfterHold_Expires()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Dash, 0f);

        Assert.That(buffer.Peek(0.2f), Is.EqualTo(PlayerBufferedInput.Dash), "유지 시간과 같으면 아직 살아 있다");

        buffer.Record(PlayerBufferedInput.Dash, 10f);
        Assert.That(buffer.Peek(10.19f), Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(buffer.Peek(10.21f), Is.EqualTo(PlayerBufferedInput.None));
        Assert.That(buffer.Peek(10.1f), Is.EqualTo(PlayerBufferedInput.None), "한 번 만료되면 되살아나지 않는다");
    }

    [Test]
    public void Record_Overwrites_KeepsOnlyLast()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Record(PlayerBufferedInput.SkillMain, 10.1f);

        Assert.That(buffer.Peek(10.1f), Is.EqualTo(PlayerBufferedInput.SkillMain));
        Assert.That(buffer.Has(PlayerBufferedInput.Attack, 10.1f), Is.False);
        Assert.That(buffer.Peek(10.29f), Is.EqualTo(PlayerBufferedInput.SkillMain), "유지 시간은 마지막 입력 기준");
    }

    [Test]
    public void Consume_RemovesOnlyMatchingKind()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillSub, 10f);

        buffer.Consume(PlayerBufferedInput.Attack);
        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.SkillSub), "다른 종류 소비는 무시");

        buffer.Consume(PlayerBufferedInput.SkillSub);
        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillUltimate, 10f);
        buffer.Clear();

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void ZeroOrNegativeHold_DoesNotRecord()
    {
        var zero = new PlayerInputBuffer(0f);
        zero.Record(PlayerBufferedInput.Attack, 10f);
        Assert.That(zero.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));

        var negative = new PlayerInputBuffer(-1f);
        negative.Record(PlayerBufferedInput.Dash, 10f);
        Assert.That(negative.Peek(10f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void FromSkillSlot_MapsEverySlot()
    {
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Main), Is.EqualTo(PlayerBufferedInput.SkillMain));
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Sub), Is.EqualTo(PlayerBufferedInput.SkillSub));
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Interrupt), Is.EqualTo(PlayerBufferedInput.SkillInterrupt));
        Assert.That(PlayerInputBuffer.FromSkillSlot(PlayerSkillSlot.Ultimate), Is.EqualTo(PlayerBufferedInput.SkillUltimate));
    }
}
