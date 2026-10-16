using NUnit.Framework;

/// <summary>
/// 기본 공격 × 다른 입력(할 일 44 5차) — 거너 홀드 시작, 공격 중 스킬 입력의 끊기·발 잠금 미룸, 미룬 입력이 선입력 버퍼에 남는지.
/// </summary>
public sealed class PlayerBasicAttackInputRulesTests
{
    private const float Lead = 0.1f;
    private const float Tail = 0.1f;
    private const float Windup = 0.25f;
    private const float Interval = 0.35f;

    [Test]
    public void ShouldStartOnHold_OnlyHoldCharacter_WhileHeld()
    {
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(true, true, false, false), Is.True, "거너 홀드 = 시작");
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(false, true, false, false), Is.False, "가붕이·어쌔신은 홀드로 시작 안 함");
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(true, false, false, false), Is.False, "안 누르고 있으면 시작 안 함");
    }

    [Test]
    public void ShouldStartOnHold_SkipsWhenAttackBlockedOrSkillBusy()
    {
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(true, true, true, false), Is.False, "공격 차단 효과 중");
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(true, true, false, true), Is.False, "스킬 실행·요청 중");
    }

    [Test]
    public void ClassifySkillCancel_ByCharacterRuleThenShotLock()
    {
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillCancel(false, false),
            Is.EqualTo(PlayerSkillCancelDecision.NotAllowed), "가붕이·어쌔신 — 공격이 끝날 때까지 버퍼");
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillCancel(false, true),
            Is.EqualTo(PlayerSkillCancelDecision.NotAllowed));
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillCancel(true, true),
            Is.EqualTo(PlayerSkillCancelDecision.Deferred), "거너 발 잠금 중 — 미룸");
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillCancel(true, false),
            Is.EqualTo(PlayerSkillCancelDecision.Cancel), "거너 잠금 밖 — 지금 끊음");
    }

    [Test]
    public void ShotLock_FirstShot_LocksFromLeadBeforeWindupEnd()
    {
        const float start = 10f;
        float firstShot = start + Windup;

        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(start, -1f, firstShot, Lead, Tail), Is.False,
            "준비 초반은 발 잠금 밖 — 첫 발 전에 끊을 수 있다");
        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(firstShot - Lead + 0.001f, -1f, firstShot, Lead, Tail), Is.True);
        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(firstShot, -1f, firstShot, Lead, Tail), Is.True);
    }

    [Test]
    public void ShotLock_AfterShot_UnlocksAfterTail_ThenRelocksBeforeNextShot()
    {
        const float shot = 20f;
        float next = shot + Interval;

        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(shot + 0.05f, shot, next, Lead, Tail), Is.True, "발사 직후 Tail 동안 잠김");
        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(shot + Tail + 0.01f, shot, next, Lead, Tail), Is.False, "Tail 뒤 ~ 다음 발 Lead 전은 풀림");
        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(next - Lead + 0.001f, shot, next, Lead, Tail), Is.True, "다음 발 Lead 전부터 다시 잠김");
    }

    [Test]
    public void ShotLock_NoNextShot_StaysUnlockedAfterTail()
    {
        const float shot = 30f;

        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(shot + Tail + 0.01f, shot, -1f, Lead, Tail), Is.False,
            "뗀 뒤(이어 쏠 발 없음) 후딜 중에는 바로 끊는다");
        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(shot + 1f, shot, -1f, Lead, Tail), Is.False);
    }

    [Test]
    public void DeferredSkillInput_StaysInBufferUntilLockEnds()
    {
        // 홀드 연사 중 다음 발 Lead 직후(잠금 시작 무렵)에 E — 잠금은 다음 발 + Tail 에 풀린다(Lead + Tail = 0.2초 ≤ 버퍼 유지).
        const float shot = 40f;
        float next = shot + Interval;
        float press = next - Lead + 0.01f;
        float unlock = next + Tail + 0.001f; // 풀린 직후 첫 틱(경계 시각은 float 오차로 판정이 흔들려 살짝 뒤를 본다)

        var buffer = new PlayerInputBuffer(0.2f);
        buffer.Record(PlayerBufferedInput.SkillSub, press);
        buffer.Resolve(PlayerBufferedInput.SkillSub, PlayerActionAttempt.Blocked); // 미룸 = 바쁨 — 남긴다

        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(unlock - 0.02f, next, -1f, Lead, Tail), Is.True);
        Assert.That(buffer.Has(PlayerBufferedInput.SkillSub, unlock - 0.02f), Is.True, "잠금 중에는 버퍼가 들고 있다");
        Assert.That(PlayerBasicAttackInputRules.IsInShotLock(unlock, next, -1f, Lead, Tail), Is.False, "풀린 첫 틱");
        Assert.That(buffer.Has(PlayerBufferedInput.SkillSub, unlock), Is.True, "풀리는 첫 틱에 아직 살아 있다");
    }
}
