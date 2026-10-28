using NUnit.Framework;

/// <summary>
/// 할 일 134 — 보스·중간보스 상태별 CC 규칙(<see cref="BossCrowdControlRules"/>):
/// 23호 넉백 입구(Unit.Knockback → OnKnockback) 수락 조건 · 피격이 밀림으로 이어지는 조건(경직 조건 없음) · 취약 중 공격 슈퍼아머 안 걸기.
/// </summary>
public sealed class BossCrowdControlRulesTests
{
    // ── 23호 넉백 입구 ───────────────────────────────────────────────

    [TestCase(true, false, false, false, true)]   // 취약 = 민다
    [TestCase(false, false, false, false, false)] // 평상 = 무시
    [TestCase(true, true, false, false, false)]   // 필수 기믹 중 = 피해만
    [TestCase(true, false, true, false, false)]   // 점프 체인 중 = 피해만
    [TestCase(true, false, false, true, false)]   // 사망
    public void TwentyThree_OnKnockback_AcceptsOnlyWhenVulnerable(bool vulnerable, bool mandatory, bool jumpChain, bool dead, bool expected)
    {
        Assert.AreEqual(expected, BossCrowdControlRules.AcceptsVulnerableKnockback(vulnerable, mandatory, jumpChain, dead));
    }

    // ── 피격 → 밀림(팔라딘 Q·특성 밀치기) ─────────────────────────────

    [Test]
    public void TwentyThree_PaladinQ_PushesWhileVulnerable()
    {
        // 팔라딘 Q = 넉백 + 경직 — 회귀 없음
        Assert.IsTrue(BossCrowdControlRules.QualifiesPushHit(true, false, true, 10f));
    }

    [Test]
    public void TwentyThree_PushWithoutStagger_PushesWhileVulnerable()
    {
        // 경직 0 인 특성 밀치기도 취약 중엔 민다(옛 조건 '경직 > 0' 제거) — 시그니처에 경직이 아예 없다
        Assert.IsTrue(BossCrowdControlRules.QualifiesPushHit(true, false, true, 5f));
    }

    [TestCase(false, false, true, 10f)]  // 평상
    [TestCase(true, true, true, 10f)]    // 간파 스킬은 vulnerableHit 경로(따로)
    [TestCase(true, false, false, 10f)]  // 플레이어가 아님(벤트 등)
    [TestCase(true, false, true, 0f)]    // 넉백 세기 0 = 밀치기 아님
    public void TwentyThree_PushHit_Rejected(bool vulnerable, bool interrupt, bool fromPlayer, float strength)
    {
        Assert.IsFalse(BossCrowdControlRules.QualifiesPushHit(vulnerable, interrupt, fromPlayer, strength));
    }

    // ── 상태별 슈퍼아머 표(평상/취약/제압 × 공격 중) ───────────────────
    // MonsterStatusEffect 와 같은 조립: 밀치기 거부 = HasSuperArmor(공격별 플래그 OR 상태별),
    // 상태이상 거부 = RejectsStatus(공격별 = 차단류 전부, 상태별 = 기절·뇌진탕만).

    const float Stun = ConcussionState.DefaultStunSeconds;

    // 상태별 슈퍼아머가 거부하지 않는 차단류 — 평상·제압에서도 걸린다.
    const StatusEffectType OtherCrowdControl =
        StatusEffectType.Slowed | StatusEffectType.Rooted | StatusEffectType.Silenced |
        StatusEffectType.Debilitated | StatusEffectType.Airborne;

    public enum BossPhase { Normal, Vulnerable, Suppressed }

    readonly struct Armor
    {
        public readonly StatusEffectType Flags;   // 복제 플래그(공격별 SuperArmor)
        public readonly bool State;               // 상태별 슈퍼아머

        public Armor(StatusEffectType flags, bool state) { Flags = flags; State = state; }

        public bool RejectsKnockback => (Flags & StatusEffectType.SuperArmor) != 0 || State;
        public bool Rejects(StatusEffectType incoming) => BossCrowdControlRules.RejectsStatus(Flags, State, incoming);
    }

    static Armor TwentyThree(BossPhase phase, bool attacking)
    {
        bool vulnerable = phase == BossPhase.Vulnerable;
        bool suppressed = phase == BossPhase.Suppressed;
        // 23호 공격은 전부 superArmor: 1(No23*.asset) — 공격 중이면 행 값 true 로 본다
        StatusEffectType flags = attacking && BossCrowdControlRules.AppliesAttackSuperArmor(true, vulnerable)
            ? StatusEffectType.SuperArmor : StatusEffectType.None;
        return new Armor(flags, BossCrowdControlRules.StateSuperArmor(vulnerable, suppressed));
    }

    [TestCase(BossPhase.Normal, false, false)]
    [TestCase(BossPhase.Normal, true, false)]
    [TestCase(BossPhase.Vulnerable, false, true)]
    [TestCase(BossPhase.Vulnerable, true, true)]     // 취약 중 공격도 허용(공격 슈퍼아머를 안 건다)
    [TestCase(BossPhase.Suppressed, false, false)]
    public void TwentyThree_Table_KnockbackStunConcussion(BossPhase phase, bool attacking, bool allowed)
    {
        Armor armor = TwentyThree(phase, attacking);

        Assert.AreEqual(allowed, !armor.RejectsKnockback, "밀치기(Unit.Knockback 슈퍼아머 가드)");
        Assert.AreEqual(allowed, !armor.Rejects(StatusEffectType.Stunned), "기절");

        var concussion = new ConcussionState();
        Assert.AreEqual(allowed ? ConcussionState.ApplyResult.Pending : ConcussionState.ApplyResult.Rejected,
            concussion.Apply(Stun, true, armor.Rejects(StatusEffectType.Concussion)), "뇌진탕");
    }

    [TestCase(BossPhase.Normal, StatusEffectType.Slowed)]
    [TestCase(BossPhase.Normal, StatusEffectType.Rooted)]
    [TestCase(BossPhase.Normal, StatusEffectType.Silenced)]
    [TestCase(BossPhase.Normal, StatusEffectType.Debilitated)]
    [TestCase(BossPhase.Normal, StatusEffectType.Airborne)]
    [TestCase(BossPhase.Vulnerable, StatusEffectType.Slowed)]
    [TestCase(BossPhase.Vulnerable, StatusEffectType.Airborne)]
    [TestCase(BossPhase.Suppressed, StatusEffectType.Slowed)]
    [TestCase(BossPhase.Suppressed, StatusEffectType.Rooted)]
    [TestCase(BossPhase.Suppressed, StatusEffectType.Silenced)]
    [TestCase(BossPhase.Suppressed, StatusEffectType.Debilitated)]
    [TestCase(BossPhase.Suppressed, StatusEffectType.Airborne)]
    public void TwentyThree_StateArmor_AllowsOtherCrowdControl(BossPhase phase, StatusEffectType incoming)
    {
        // 공격 밖 — 상태별 슈퍼아머는 밀치기·기절·뇌진탕만 거부한다(둔화·속박·침묵·약화·띄우기는 예전처럼 걸린다)
        Assert.IsFalse(TwentyThree(phase, false).Rejects(incoming));
    }

    [TestCase(StatusEffectType.Slowed)]
    [TestCase(StatusEffectType.Rooted)]
    [TestCase(StatusEffectType.Silenced)]
    [TestCase(StatusEffectType.Debilitated)]
    [TestCase(StatusEffectType.Airborne)]
    [TestCase(StatusEffectType.Stunned)]
    [TestCase(StatusEffectType.Concussion)]
    public void TwentyThree_AttackSuperArmor_StillRejectsAllCrowdControl(StatusEffectType incoming)
    {
        // 공격별 슈퍼아머(평상 공격 중)는 기존대로 차단류 전부를 거부한다
        Assert.IsTrue(TwentyThree(BossPhase.Normal, true).Rejects(incoming));
    }

    [Test]
    public void TwentyThree_VulnerablePushEnd_ConvertsConcussionToStun()
    {
        Armor vulnerable = TwentyThree(BossPhase.Vulnerable, false);
        var concussion = new ConcussionState();

        // 취약 중 Q 적중 — 자체 밀림 중(IsSelfKnockedBack) 이라 대기
        Assert.AreEqual(ConcussionState.ApplyResult.Pending,
            concussion.Apply(Stun, true, vulnerable.Rejects(StatusEffectType.Concussion)));
        // 밀림 끝(FinishVulnerableKnockback → ConsumeConcussionAfterPush) — 아직 취약이면 기절
        Assert.AreEqual(Stun, concussion.ConsumeOnKnockbackEnd(vulnerable.Rejects(StatusEffectType.Stunned)), 1e-4f);
    }

    [TestCase(BossPhase.Normal)]       // 벽 충돌로 취약이 끝난 뒤 밀림이 끝남
    [TestCase(BossPhase.Suppressed)]   // 벽 충돌로 칸 0 → 제압
    public void TwentyThree_PushEnd_NotVulnerableAnymore_NoStun(BossPhase phaseAtEnd)
    {
        Armor vulnerable = TwentyThree(BossPhase.Vulnerable, false);
        Armor atEnd = TwentyThree(phaseAtEnd, false);
        var concussion = new ConcussionState();
        concussion.Apply(Stun, true, vulnerable.Rejects(StatusEffectType.Concussion));

        Assert.AreEqual(0f, concussion.ConsumeOnKnockbackEnd(atEnd.Rejects(StatusEffectType.Stunned)), 1e-4f, "넉백 종료 순간 자격 재확인");
        Assert.IsFalse(concussion.IsPending);
    }

    // ── 중간보스 ─────────────────────────────────────────────────────

    [TestCase(false, false)]   // 평상(공격 밖) = 거부 — 예전엔 걸렸다
    [TestCase(true, true)]     // 취약 = 허용
    public void MidBoss_StunAndConcussion_OnlyWhileVulnerable(bool vulnerable, bool allowed)
    {
        var armor = new Armor(StatusEffectType.None, BossCrowdControlRules.MidBossStateSuperArmor(true, vulnerable));

        Assert.AreEqual(allowed, !armor.RejectsKnockback, "밀치기");
        Assert.AreEqual(allowed, !armor.Rejects(StatusEffectType.Stunned), "기절");
        Assert.AreEqual(allowed, !armor.Rejects(StatusEffectType.Concussion), "뇌진탕");
    }

    [TestCase(false)]   // 평상(공격 밖)
    [TestCase(true)]    // 취약
    public void MidBoss_StateArmor_AllowsOtherCrowdControl(bool vulnerable)
    {
        var armor = new Armor(StatusEffectType.None, BossCrowdControlRules.MidBossStateSuperArmor(true, vulnerable));

        Assert.IsFalse(armor.Rejects(OtherCrowdControl), "둔화·속박·침묵·약화·띄우기는 상태와 무관하게 걸린다");
    }

    [Test]
    public void MidBoss_RuleOnlyForMidBossRank()
    {
        Assert.IsFalse(BossCrowdControlRules.MidBossStateSuperArmor(false, false), "중간보스 등급이 아니면 상시 슈퍼아머 없음");
    }

    [Test]
    public void RejectsStatus_StateArmorBlocksOnlyStunAndConcussion()
    {
        Assert.AreEqual(StatusEffectType.Stunned | StatusEffectType.Concussion, BossCrowdControlRules.StateSuperArmorBlocked);
        Assert.IsFalse(BossCrowdControlRules.RejectsStatus(StatusEffectType.None, true, StatusEffectType.SuperArmor),
            "SuperArmor 자체(공격별 슈퍼아머 걸기)는 거부하지 않는다");
        Assert.IsFalse(BossCrowdControlRules.RejectsStatus(StatusEffectType.None, false, StatusEffectType.Stunned), "슈퍼아머 없음 = 허용");
    }

    // ── 공격별 슈퍼아머 ───────────────────────────────────────────────

    [TestCase(true, false, true)]    // 평상 공격 = 건다
    [TestCase(true, true, false)]    // 취약 중 공격 = 안 건다(Q·간파 넉백이 공격을 끊는다 — 기획 취약 §4.4)
    [TestCase(false, false, false)]  // 슈퍼아머 없는 공격
    public void AttackSuperArmor_NotAppliedWhileVulnerable(bool entry, bool vulnerable, bool expected)
    {
        Assert.AreEqual(expected, BossCrowdControlRules.AppliesAttackSuperArmor(entry, vulnerable));
    }
}
