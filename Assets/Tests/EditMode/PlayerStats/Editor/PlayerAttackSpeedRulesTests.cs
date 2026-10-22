using NUnit.Framework;
using UnityEngine;

// 할 일 91 T5 — 공격속도 배율의 소비 규칙(재생 속도·시간 나누기·안전망)과 스킬별 적용/제외 범위.
public sealed class PlayerAttackSpeedRulesTests
{
    [TestCase(1f, 1f)]
    [TestCase(2f, 2f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(0f, PlayerAttackSpeedRules.MinRate)]
    [TestCase(-1f, PlayerAttackSpeedRules.MinRate)]
    [TestCase(float.NaN, 1f)]
    [TestCase(float.PositiveInfinity, 1f)]
    public void Rate_ClampsToMinimum_InvalidIsOne(float multiplier, float expected)
    {
        Assert.That(PlayerAttackSpeedRules.Rate(multiplier), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void Duration_DividesByRate()
    {
        Assert.That(PlayerAttackSpeedRules.Duration(0.6f, 2f), Is.EqualTo(0.3f).Within(1e-6f), "가속 ×2 → 절반");
        Assert.That(PlayerAttackSpeedRules.Duration(0.6f, 0.5f), Is.EqualTo(1.2f).Within(1e-6f), "감속 ×0.5 → 두 배");
        Assert.That(PlayerAttackSpeedRules.Duration(0.6f, 1f), Is.EqualTo(0.6f).Within(1e-6f), "배율 1 → 기존 값 그대로");
    }

    [Test]
    public void SafetyNet_StretchesOnSlow_NeverShrinksOnHaste()
    {
        // 가속 버프가 스킬 도중 끝나면 나머지 재생이 느려진다 — 시작 때 줄여 둔 그물이 먼저 끊으면 안 된다.
        Assert.That(PlayerAttackSpeedRules.SafetyNetDuration(1.5f, 2f), Is.EqualTo(1.5f).Within(1e-6f));
        Assert.That(PlayerAttackSpeedRules.SafetyNetDuration(1.5f, 0.5f), Is.EqualTo(3f).Within(1e-6f));
        Assert.That(PlayerAttackSpeedRules.SafetyNetDuration(1.5f, 1f), Is.EqualTo(1.5f).Within(1e-6f));
    }

    [Test]
    public void AnimatorSpeed_OnlyWhileScaling()
    {
        Assert.That(PlayerAttackSpeedRules.AnimatorSpeed(true, 2f), Is.EqualTo(2f));
        Assert.That(PlayerAttackSpeedRules.AnimatorSpeed(false, 2f), Is.EqualTo(1f), "공속을 안 타는 동작(대시·홀드 등)은 1 배속");
        Assert.That(PlayerAttackSpeedRules.AnimatorSpeed(true, 0f), Is.EqualTo(PlayerAttackSpeedRules.MinRate));
    }

    [Test]
    public void Rate_FromFinalMultiplier_MatchesStatFormula()
    {
        // 배율 = 최종 ÷ 기본 = (1+기본%)(1+전체%) × 최종 배율 — 제압 가속 ×2 단독이면 2.
        float multiplier = PlayerStatFormula.Multiplier(0f, 0f, 2f);
        Assert.That(PlayerAttackSpeedRules.Rate(multiplier), Is.EqualTo(2f).Within(1e-6f));
        Assert.That(PlayerAttackSpeedRules.Rate(PlayerStatFormula.Multiplier(0.1f, 0f, 2f)), Is.EqualTo(2.2f).Within(1e-5f));
    }

    // 평타 전진 거리는 공속과 무관하게 같다 — 시간만 나눈다(어쌔신 강타 전진 = 같은 규칙의 실제 소비처).
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(0.5f)]
    public void LungeDistance_SameAtAnyRate(float rate)
    {
        const float distance = 1.2f;
        const float duration = 0.3f;
        const float dt = 1f / 50f;
        float scaled = PlayerAttackSpeedRules.Duration(duration, rate);

        float remaining = distance;
        float traveled = 0f;
        int ticks = 0;
        while (remaining > 0f && ticks < 1000)
        {
            float step = AssassinLungeRules.StepDistance(remaining, distance, scaled, dt);
            remaining -= step;
            traveled += step;
            ticks++;
        }

        Assert.That(traveled, Is.EqualTo(distance).Within(1e-4f));
        Assert.That(ticks, Is.EqualTo(Mathf.CeilToInt(scaled / dt - 1e-4f)).Within(1), "시간은 배율만큼 줄거나 는다");
    }

    // ── 스킬별 적용/제외 (PlayerSkillBase.ScalesWithAttackSpeed) ──

    [Test] public void Paladin_Q_Hold_Excluded() => AssertScales<FirstMeleeMainSkill>(false);
    // 팔라딘 E 는 Animator.speed 가 아니라 상태 파라미터로 준비·해제·반격만 탄다(할 일 91 A5, FirstMeleeGuardAttackSpeedTests).
    [Test] public void Paladin_E_StateParameter_NotAnimatorSpeed() => AssertScales<FirstMeleeSubSkill>(false);
    [Test] public void Paladin_R_Scales() => AssertScales<FirstMeleeUltimateSkill>(true);
    [Test] public void Paladin_Interrupt_Scales() => AssertScales<FirstMeleeInterruptSkill>(true);
    [Test] public void Gunner_Q_Charge_Excluded() => AssertScales<GunnerChargeLaserSkill>(false);
    [Test] public void Gunner_E_Timer_Excluded() => AssertScales<GunnerCoolBackstepSkill>(false);
    [Test] public void Gunner_R_Timer_Excluded() => AssertScales<GunnerTrackingLaserSkill>(false);
    [Test] public void Gunner_Interrupt_Scales() => AssertScales<GunnerInterruptSkill>(true);
    [Test] public void Assassin_Q_Dash_Excluded() => AssertScales<AssassinDashStrikeSkill>(false);
    [Test] public void Assassin_E_Enhance_Scales() => AssertScales<AssassinEnhanceSkill>(true);
    [Test] public void Assassin_E_CircleStrike_Scales() => AssertScales<AssassinCircleStrikeSkill>(true);
    [Test] public void Assassin_R_Transform_Scales() => AssertScales<AssassinTransformSkill>(true);
    [Test] public void Assassin_Interrupt_Scales() => AssertScales<AssassinInterruptSkill>(true);

    private static void AssertScales<T>(bool expected) where T : PlayerSkillBase
    {
        var go = new GameObject(typeof(T).Name + "AttackSpeedTest");
        try
        {
            T skill = go.AddComponent<T>();
            Assert.That(skill.ScalesWithAttackSpeed, Is.EqualTo(expected), typeof(T).Name);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
