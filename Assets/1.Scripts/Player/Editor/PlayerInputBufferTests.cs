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
    public void Resolve_Started_ClearsEverything()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Resolve(PlayerBufferedInput.Dash, PlayerActionAttempt.Started);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.None), "시작하면 다른 종류도 비운다");
    }

    [TestCase(PlayerBufferedInput.SkillMain)]
    [TestCase(PlayerBufferedInput.Dash)]
    [TestCase(PlayerBufferedInput.Attack)]
    public void Resolve_OutOfResource_DropsThatInput(PlayerBufferedInput input)
    {
        // 쿨타임(스킬)·충전 없음(대시)·과열(거너 평타) — 재시도 없이 바로 버린다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(input, 10f);
        buffer.Resolve(input, PlayerActionAttempt.OutOfResource);

        Assert.That(buffer.Peek(10.01f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void Resolve_OutOfResource_KeepsOtherKind()
    {
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.Attack, 10f);
        buffer.Resolve(PlayerBufferedInput.Dash, PlayerActionAttempt.OutOfResource);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.Attack), "거부된 종류만 버린다");
    }

    [Test]
    public void Resolve_Blocked_KeepsUntilExpiry()
    {
        // 행동·요청 중(바쁨)·CC — 유지 시간 동안 들고 있다가 다시 시도한다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillSub, 10f);
        buffer.Resolve(PlayerBufferedInput.SkillSub, PlayerActionAttempt.Blocked);

        Assert.That(buffer.Peek(10.19f), Is.EqualTo(PlayerBufferedInput.SkillSub));
        Assert.That(buffer.Peek(10.21f), Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void CooldownReject_OnePressRaisesFeedbackOnce()
    {
        // FSM TryStartSkill 과 같은 순서: 눌린 프레임 또는 버퍼 → 쿨타임이면 버리고 피드백 1회.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        int feedback = SimulateIdleFrames(buffer, PlayerBufferedInput.SkillMain, pressedAt: 10f,
            from: 10f, to: 10.5f, busyUntil: 0f, cooldownUntil: 99f);

        Assert.That(feedback, Is.EqualTo(1), "입력 1회 = 피드백 1회 — 쿨타임 동안 프레임마다 다시 나지 않는다");
    }

    [Test]
    public void CooldownReject_PressDuringBusy_RaisesOnceWhenFree()
    {
        // 행동 중에 누른 입력은 바쁨이라 들고 있다가, 풀린 첫 틱에 쿨타임이면 그때 한 번만 거부된다.
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        int feedback = SimulateIdleFrames(buffer, PlayerBufferedInput.SkillMain, pressedAt: 10f,
            from: 10f, to: 10.5f, busyUntil: 10.1f, cooldownUntil: 99f);

        Assert.That(feedback, Is.EqualTo(1));
    }

    [Test]
    public void CooldownReject_NotRaisedWhileBusyOnly()
    {
        // 바쁨이 유지 시간보다 길면 입력은 만료되고 거부 피드백은 나지 않는다(쿨타임 거부가 아니다).
        var buffer = new PlayerInputBuffer(Hold);
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);

        int feedback = SimulateIdleFrames(buffer, PlayerBufferedInput.SkillMain, pressedAt: 10f,
            from: 10f, to: 10.5f, busyUntil: 11f, cooldownUntil: 99f);

        Assert.That(feedback, Is.EqualTo(0));
    }

    // 60fps 로 프레임을 돌리며 PlayerStateBase.TryStartSkill 의 판정 순서를 흉내 낸다. 반환 = 쿨타임 거부 피드백 횟수.
    private static int SimulateIdleFrames(
        PlayerInputBuffer buffer, PlayerBufferedInput input, float pressedAt,
        float from, float to, float busyUntil, float cooldownUntil)
    {
        const float Frame = 1f / 60f;
        int feedback = 0;
        for (float now = from; now <= to; now += Frame)
        {
            bool pressedThisFrame = now == pressedAt;
            if (!pressedThisFrame && !buffer.Has(input, now))
                continue;

            if (now < busyUntil)
            {
                buffer.Resolve(input, PlayerActionAttempt.Blocked);
                continue;
            }

            if (now < cooldownUntil)
            {
                buffer.Resolve(input, PlayerActionAttempt.OutOfResource);
                feedback++;
                continue;
            }

            buffer.Resolve(input, PlayerActionAttempt.Started);
        }

        return feedback;
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
