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

    // 할 일 146 — 거너 E 와 같은 '스킬 뒤 홀드 이어가기'를 어쌔신 E·돌파의 방패 Q 에. 정상 종료만 잇는다.
    [TestCase(SkillEndReason.Completed, true)]
    [TestCase(SkillEndReason.Released, true)]
    [TestCase(SkillEndReason.MaxDurationReached, false)]
    [TestCase(SkillEndReason.Cancelled, false)]
    [TestCase(SkillEndReason.CasterDied, false)]
    [TestCase(SkillEndReason.DashCancelled, false)]
    public void ArmsResumeAfterSkill_OnlyNormalEnd(SkillEndReason reason, bool expected)
    {
        Assert.That(PlayerBasicAttackInputRules.ArmsResumeAfterSkill(true, reason), Is.EqualTo(expected));
        Assert.That(PlayerBasicAttackInputRules.ArmsResumeAfterSkill(false, reason), Is.False, "이어가기를 허락하지 않은 스킬은 안 잇는다");
    }

    [Test]
    public void ResumeAfterSkill_StartsLikeHoldCharacter_OnlyWhileHeld()
    {
        bool resume = PlayerBasicAttackInputRules.ArmsResumeAfterSkill(true, SkillEndReason.Completed);
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(false || resume, true, false, false), Is.True, "끝난 뒤 아직 누르고 있음 = 평타 이어감");
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(false || resume, false, false, false), Is.False, "끝나기 전에 뗐으면 안 이음");
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(false || resume, true, true, false), Is.False, "공격 차단 효과 중");
        Assert.That(PlayerBasicAttackInputRules.ShouldStartOnHold(false || resume, true, false, true), Is.False, "다른 스킬 요청 중");
    }

    [Test]
    public void ResumeAfterSkill_PrefabSkills_AssassinEAlways_PaladinQNeedsTrait_GunnerByHoldStart()
    {
        var assassin = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab");
        var paladin = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab");
        Assert.That(assassin.GetComponent<AssassinEnhanceSkill>().ResumesBasicAttackOnHold, Is.True, "어쌔신 일반 E = 항상");
        Assert.That(paladin.GetComponent<FirstMeleeMainSkill>().ResumesBasicAttackOnHold, Is.False, "특성 없는 가붕이 Q = 원래 Q — 이어가기 없음");
        Assert.That(assassin.GetComponent<AssassinBasicAttack>().StartsOnHold, Is.False, "어쌔신은 평소엔 홀드 시작이 아니다 — E 뒤 한 번만");
    }

    // 변신 E(원형 5타)도 어쌔신 일반 E 와 같은 규칙 — 정상 종료(완료·홀드 해제)만 잇고 대시·피격·사망 끊김은 안 잇는다.
    [TestCase(SkillEndReason.Completed, true)]
    [TestCase(SkillEndReason.Released, true)]
    [TestCase(SkillEndReason.Cancelled, false)]
    [TestCase(SkillEndReason.CasterDied, false)]
    [TestCase(SkillEndReason.DashCancelled, false)]
    public void ResumeAfterSkill_AssassinCircleStrike_SameRuleAsAssassinE(SkillEndReason reason, bool expected)
    {
        var assassin = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab");
        var circleStrike = assassin.GetComponentInChildren<AssassinCircleStrikeSkill>(true);
        var enhance = assassin.GetComponent<AssassinEnhanceSkill>();
        Assert.That(circleStrike, Is.Not.Null, "변신 E 가 어쌔신 프리팹에 있다");
        Assert.That(circleStrike.ResumesBasicAttackOnHold, Is.True, "변신 E = 항상");
        Assert.That(PlayerBasicAttackInputRules.ArmsResumeAfterSkill(circleStrike.ResumesBasicAttackOnHold, reason), Is.EqualTo(expected));
        Assert.That(PlayerBasicAttackInputRules.ArmsResumeAfterSkill(circleStrike.ResumesBasicAttackOnHold, reason),
            Is.EqualTo(PlayerBasicAttackInputRules.ArmsResumeAfterSkill(enhance.ResumesBasicAttackOnHold, reason)), "일반 E 와 같은 결과");
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
        buffer.Remove(PlayerBufferedInput.SkillSub); // 예약으로 옮겨 간다

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

    // ── 근접 평타 홀드 중 스킬(할 일 77 후속) ─────────────────────────────

    // PlayerInputReader 기본 우선순위와 같게 맞춘 버퍼.
    private static PlayerInputBuffer CreatePrioritizedBuffer()
    {
        var buffer = new PlayerInputBuffer(0.2f);
        buffer.SetPriority(PlayerBufferedInput.Dash, 5);
        buffer.SetPriority(PlayerBufferedInput.SkillInterrupt, 4);
        buffer.SetPriority(PlayerBufferedInput.SkillUltimate, 3);
        buffer.SetPriority(PlayerBufferedInput.SkillMain, 2);
        buffer.SetPriority(PlayerBufferedInput.SkillSub, 2);
        buffer.SetPriority(PlayerBufferedInput.Attack, 1);
        return buffer;
    }

    /// <summary>
    /// 가붕이 콤보 한 타를 프레임 단위로 돈다 — 오너 틱(스킬 버퍼 → 마무리 래치, 홀드 → 창 안 예약)을 상태 머신·DefaultAttackController 와 같은 규칙으로.
    /// 창 = [windowOpen, windowClose], End = stepEnd. 다음 타로 이어지면 true.
    /// </summary>
    private static bool SimulateMeleeStep(
        PlayerInputBuffer buffer, float stepStart, float windowOpen, float windowClose, float stepEnd,
        bool attackHeld, ref bool finishingChain, float skillPressAt = -1f,
        PlayerBufferedInput skill = PlayerBufferedInput.None)
    {
        const float frame = 1f / 60f;
        bool queued = false;
        for (float t = stepStart; t < stepEnd; t += frame)
        {
            if (skillPressAt >= 0f && t >= skillPressAt && t < skillPressAt + frame)
                buffer.Record(skill, t);

            bool skillAlive = false;
            foreach (PlayerBufferedInput kind in new[] {
                         PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub,
                         PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.SkillUltimate })
                skillAlive |= buffer.ContainsAbove(kind, PlayerBufferedInput.Attack, t);

            if (PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(false, skillAlive, true, false, false))
            {
                finishingChain = true;
                queued = false; // 서버가 받은 예약도 지운다(RequestFinishChainRpc)
            }

            bool inWindow = t >= windowOpen && t <= windowClose;
            if (inWindow && PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(attackHeld, false, finishingChain))
                queued = true;
        }

        return queued;
    }

    [Test]
    public void MeleeHold_NoSkillInput_KeepsChaining()
    {
        var buffer = CreatePrioritizedBuffer();
        bool finishing = false;
        // 창 0.30~0.45, End 0.55 — 스킬 입력이 없으면 홀드로 매 타 이어진다.
        for (int step = 0; step < 6; step++)
        {
            float s = step * 0.55f;
            Assert.That(SimulateMeleeStep(buffer, s, s + 0.30f, s + 0.45f, s + 0.55f, true, ref finishing), Is.True,
                $"{step}타 — 홀드 연타 유지");
        }

        Assert.That(finishing, Is.False);
    }

    [Test]
    public void MeleeHold_SkillPressedInWindow_EndsChain_SkillStartsAtEnd()
    {
        var buffer = CreatePrioritizedBuffer();
        bool finishing = false;

        // 1타는 이어진다.
        Assert.That(SimulateMeleeStep(buffer, 0f, 0.30f, 0.45f, 0.55f, true, ref finishing), Is.True);

        // 2타 창이 열린 뒤(이미 홀드로 예약됨) Q — 받은 예약도 지우고 이 타로 마무리.
        bool chained = SimulateMeleeStep(buffer, 0.55f, 0.85f, 1.00f, 1.10f, true, ref finishing,
            skillPressAt: 0.95f, skill: PlayerBufferedInput.SkillMain);
        Assert.That(chained, Is.False, "체인 종료");
        Assert.That(finishing, Is.True);

        // 이 타가 끝난 첫 틱(Idle) — 꺼낼 입력은 Q.
        Assert.That(buffer.Peek(1.10f), Is.EqualTo(PlayerBufferedInput.SkillMain), "현재 타가 끝난 첫 틱에 Q 시작");
    }

    [Test]
    public void MeleeHold_SkillAlive_StopsRequestEvenAfterWindowReopens()
    {
        // 마무리 래치는 공격이 끝날 때까지 — 스킬 입력이 0.2초 만료된 뒤에도 홀드가 다시 예약하지 않는다.
        Assert.That(PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(true, false, true), Is.False);
        Assert.That(PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(false, true, true), Is.False);
        Assert.That(PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(true, false, false), Is.True);
        Assert.That(PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(false, true, false), Is.True);
        Assert.That(PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(false, false, false), Is.False);
    }

    [Test]
    public void ShouldFinishChainForBufferedSkill_OnlyUsableSkillOnNonReservingCharacter()
    {
        Assert.That(PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(false, true, true, false, false), Is.True);
        Assert.That(PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(true, true, true, false, false), Is.False,
            "거너는 공격 뒤 예약 경로(§0.1)");
        Assert.That(PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(false, false, true, false, false), Is.False,
            "스킬 입력 없음 = 홀드 연타 그대로");
        Assert.That(PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(false, true, false, false, false), Is.False, "빈 슬롯");
        Assert.That(PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(false, true, true, true, false), Is.False, "차단 효과(침묵)");
        Assert.That(PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(false, true, true, false, true), Is.False, "쿨타임");
    }

    [Test]
    public void ContainsAbove_SkillAboveAttack_EvenWhenOutrankedByDash()
    {
        var buffer = CreatePrioritizedBuffer();
        buffer.Record(PlayerBufferedInput.SkillMain, 10f);
        buffer.Record(PlayerBufferedInput.Dash, 10f);

        Assert.That(buffer.Peek(10f), Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(buffer.ContainsAbove(PlayerBufferedInput.SkillMain, PlayerBufferedInput.Attack, 10f), Is.True,
            "대시에 밀려 있어도 살아 있으면 참");
        Assert.That(buffer.ContainsAbove(PlayerBufferedInput.Attack, PlayerBufferedInput.Attack, 10f), Is.False, "같은 우선순위는 아님");
        Assert.That(buffer.ContainsAbove(PlayerBufferedInput.SkillMain, PlayerBufferedInput.Attack, 10.3f), Is.False, "만료");
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
