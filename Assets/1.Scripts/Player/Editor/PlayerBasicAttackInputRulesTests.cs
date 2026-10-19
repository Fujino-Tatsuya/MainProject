using NUnit.Framework;

/// <summary>
/// 기본 공격 × 다른 입력(할 일 44 5차) — 거너 홀드 시작, 공격 중 스킬 입력은 공격을 끊지 않고 '지금 발 마무리 뒤'로 예약.
/// 예약 = 다음 발 금지·만료 없음·마지막 1개·같은 스킬 연타 1회·거부(차단 효과·쿨타임)면 예약 안 함.
/// </summary>
public sealed class PlayerBasicAttackInputRulesTests
{
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
    public void ClassifySkillDuringAttack_NonReservingCharacter_LeavesInputToBuffer()
    {
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillDuringAttack(false, false, false),
            Is.EqualTo(PlayerSkillDuringAttackDecision.NotReserving), "가붕이·어쌔신 — 0.2초 선입력 버퍼 그대로");
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillDuringAttack(false, true, true),
            Is.EqualTo(PlayerSkillDuringAttackDecision.NotReserving), "거부 판정도 하지 않는다(공격이 끝난 뒤 Idle/Move 가 한다)");
    }

    [Test]
    public void ClassifySkillDuringAttack_RejectsAtPress_EffectBeforeCooldown()
    {
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillDuringAttack(true, true, false),
            Is.EqualTo(PlayerSkillDuringAttackDecision.RejectByEffect));
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillDuringAttack(true, true, true),
            Is.EqualTo(PlayerSkillDuringAttackDecision.RejectByEffect), "봉인 + 쿨타임이면 봉인 하나만(Idle/Move 와 같은 순서)");
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillDuringAttack(true, false, true),
            Is.EqualTo(PlayerSkillDuringAttackDecision.RejectByCooldown));
        Assert.That(PlayerBasicAttackInputRules.ClassifySkillDuringAttack(true, false, false),
            Is.EqualTo(PlayerSkillDuringAttackDecision.Reserve));
    }

    [Test]
    public void ShouldChainNextShot_ReservedSkill_StopsHoldAndWindowQueue()
    {
        Assert.That(PlayerBasicAttackInputRules.ShouldChainNextShot(true, false, false), Is.True, "홀드 = 다음 발");
        Assert.That(PlayerBasicAttackInputRules.ShouldChainNextShot(false, true, false), Is.True, "창 예약 = 다음 발");
        Assert.That(PlayerBasicAttackInputRules.ShouldChainNextShot(false, false, false), Is.False, "뗐음 = 마무리");
        Assert.That(PlayerBasicAttackInputRules.ShouldChainNextShot(true, true, true), Is.False, "스킬 예약 = 홀드·창 예약 무시하고 마무리");
    }

    [Test]
    public void Reservation_KeepsLastSkillOnly()
    {
        var reservation = new PlayerSkillReservation();
        Assert.That(reservation.HasReservation, Is.False);

        Assert.That(reservation.Reserve(PlayerBufferedInput.SkillSub), Is.True);
        Assert.That(reservation.Reserve(PlayerBufferedInput.SkillMain), Is.True, "다른 스킬은 덮는다");
        Assert.That(reservation.Reserved, Is.EqualTo(PlayerBufferedInput.SkillMain), "마지막에 누른 1개");
    }

    [Test]
    public void Reservation_RepeatedSameSkill_CountsOnce()
    {
        var reservation = new PlayerSkillReservation();
        Assert.That(reservation.Reserve(PlayerBufferedInput.SkillSub), Is.True);
        Assert.That(reservation.Reserve(PlayerBufferedInput.SkillSub), Is.False, "E 연타 = 1번");
        Assert.That(reservation.Reserve(PlayerBufferedInput.SkillSub), Is.False);
        Assert.That(reservation.Reserved, Is.EqualTo(PlayerBufferedInput.SkillSub));

        reservation.Clear();
        Assert.That(reservation.HasReservation, Is.False, "실행·거부 뒤 비우면 없음");
        Assert.That(reservation.Reserve(PlayerBufferedInput.None), Is.False);
        Assert.That(reservation.HasReservation, Is.False);
    }

    [Test]
    public void Reservation_OutlivesInputBufferHold()
    {
        // 홀드 연사 중 발사 직후 E — 마무리는 ShotRecovery 뒤라 0.2초 버퍼라면 경계에서 놓칠 수 있다. 예약은 시간과 무관하다.
        const float press = 50f;
        var buffer = new PlayerInputBuffer(0.2f);
        var reservation = new PlayerSkillReservation();

        buffer.Record(PlayerBufferedInput.SkillSub, press);
        reservation.Reserve(PlayerBufferedInput.SkillSub);
        buffer.Consume(PlayerBufferedInput.SkillSub); // 예약으로 옮겨 간다

        Assert.That(buffer.Has(PlayerBufferedInput.SkillSub, press + 0.5f), Is.False, "버퍼는 이미 비었다(0.2초 만료와 무관)");
        Assert.That(reservation.Reserved, Is.EqualTo(PlayerBufferedInput.SkillSub), "평타가 끝날 때까지 들고 있다(만료 없음)");
    }

    [Test]
    public void Reservation_RejectedPress_DoesNotReserve_AttackKeepsChaining()
    {
        // 쿨타임 중 E — 누른 순간 버림(OutOfResource), 예약 없음 → 홀드 중이면 다음 발이 그대로 이어진다.
        var reservation = new PlayerSkillReservation();
        var buffer = new PlayerInputBuffer(0.2f);
        buffer.Record(PlayerBufferedInput.SkillSub, 60f);

        PlayerSkillDuringAttackDecision decision = PlayerBasicAttackInputRules.ClassifySkillDuringAttack(true, false, true);
        Assert.That(decision, Is.EqualTo(PlayerSkillDuringAttackDecision.RejectByCooldown));
        buffer.Resolve(PlayerBufferedInput.SkillSub, PlayerActionAttempt.OutOfResource);

        Assert.That(reservation.HasReservation, Is.False);
        Assert.That(buffer.Has(PlayerBufferedInput.SkillSub, 60f), Is.False, "버림");
        Assert.That(PlayerBasicAttackInputRules.ShouldChainNextShot(true, false, reservation.HasReservation), Is.True);
    }

    [Test]
    public void TryGetSkillSlot_InverseOfFromSkillSlot()
    {
        foreach (PlayerSkillSlot slot in new[] { PlayerSkillSlot.Main, PlayerSkillSlot.Sub, PlayerSkillSlot.Interrupt, PlayerSkillSlot.Ultimate })
        {
            Assert.That(PlayerInputBuffer.TryGetSkillSlot(PlayerInputBuffer.FromSkillSlot(slot), out PlayerSkillSlot back), Is.True);
            Assert.That(back, Is.EqualTo(slot));
        }

        Assert.That(PlayerInputBuffer.TryGetSkillSlot(PlayerBufferedInput.Attack, out _), Is.False);
        Assert.That(PlayerInputBuffer.TryGetSkillSlot(PlayerBufferedInput.None, out _), Is.False);
    }
}
