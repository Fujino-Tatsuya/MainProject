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

    // ── 공격별 슈퍼아머 ───────────────────────────────────────────────

    [TestCase(true, false, true)]    // 평상 공격 = 건다
    [TestCase(true, true, false)]    // 취약 중 공격 = 안 건다(Q·간파 넉백이 공격을 끊는다 — 기획 취약 §4.4)
    [TestCase(false, false, false)]  // 슈퍼아머 없는 공격
    public void AttackSuperArmor_NotAppliedWhileVulnerable(bool entry, bool vulnerable, bool expected)
    {
        Assert.AreEqual(expected, BossCrowdControlRules.AppliesAttackSuperArmor(entry, vulnerable));
    }
}
