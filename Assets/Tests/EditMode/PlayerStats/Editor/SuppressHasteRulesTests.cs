using System.Collections.Generic;
using NUnit.Framework;

// 할 일 91 T6 — 제압 가속 버프: 대상 선정(생존만) · 지속 규칙(0/양수) · 쓰러짐 시 제거 · 재진입 갱신.
public sealed class SuppressHasteRulesTests
{
    const ulong Boss = 42;

    // StatusEffectController 의 (타입, 출처) 키 의미를 흉내 낸다 — maxStacks 1 = 덮어쓰기(갱신), 2+ = 스택.
    sealed class FakeTarget : ISuppressHasteTarget
    {
        public readonly Dictionary<(StatusEffectType, ulong), (float magnitude, float duration, int stacks)> Effects =
            new Dictionary<(StatusEffectType, ulong), (float, float, int)>();

        public FakeTarget(PlayerLifeState state) => LifeState = state;

        public PlayerLifeState LifeState { get; set; }

        public void Apply(StatusEffectType type, float magnitude, float duration, ulong sourceId, int maxStacks)
        {
            int stacks = 1;
            if (maxStacks > 1 && Effects.TryGetValue((type, sourceId), out var old))
                stacks = System.Math.Min(old.stacks + 1, maxStacks);
            Effects[(type, sourceId)] = (magnitude, duration, stacks);
        }

        public bool Remove(StatusEffectType type, ulong sourceId) => Effects.Remove((type, sourceId));

        // 진입 쿨 차감(할 일 133)은 PaladinTraitBasicTests 가 본다 — 여기선 쿨 없음.
        public float GetEffectiveCooldownTime(PlayerSkillSlot slot) => 0f;
        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) { }
    }

    static readonly SuppressHasteValues Values = new SuppressHasteValues(2f, 2f, 2f, 0f);

    [Test]
    public void Grant_OnlyAlivePlayers()
    {
        var alive = new FakeTarget(PlayerLifeState.Alive);
        var dying = new FakeTarget(PlayerLifeState.DeadPresentation);
        var soul = new FakeTarget(PlayerLifeState.Soul);
        var dead = new FakeTarget(PlayerLifeState.PermanentDead);

        int granted = SuppressHasteRules.Grant(new ISuppressHasteTarget[] { alive, dying, soul, dead }, Values, Boss, 5f);

        Assert.That(granted, Is.EqualTo(1));
        Assert.That(alive.Effects.Count, Is.EqualTo(3));
        Assert.That(dying.Effects, Is.Empty, "쓰러짐 연출도 제외");
        Assert.That(soul.Effects, Is.Empty);
        Assert.That(dead.Effects, Is.Empty);
    }

    [Test]
    public void Grant_AppliesThreeEffects_WithMultipliersAndSource()
    {
        var target = new FakeTarget(PlayerLifeState.Alive);
        SuppressHasteRules.Grant(new[] { target }, new SuppressHasteValues(1.5f, 1.8f, 2.5f, 0f), Boss, 5f);

        Assert.That(target.Effects[(StatusEffectType.MoveSpeedFinalMultiplier, Boss)].magnitude, Is.EqualTo(1.5f));
        Assert.That(target.Effects[(StatusEffectType.AttackSpeedFinalMultiplier, Boss)].magnitude, Is.EqualTo(1.8f));
        Assert.That(target.Effects[(StatusEffectType.CooldownRateModifier, Boss)].magnitude, Is.EqualTo(2.5f));
    }

    [TestCase(0f, 5f, 5f)]
    [TestCase(0f, 8f, 8f)]
    [TestCase(3f, 5f, 3f)]
    [TestCase(10f, 5f, 10f)]
    public void Duration_OverrideWinsWhenPositive_ElseSuppressLength(float durationOverride, float suppress, float expected)
    {
        Assert.That(SuppressHasteRules.TryResolveDuration(durationOverride, suppress, out float duration), Is.True);
        Assert.That(duration, Is.EqualTo(expected));

        var target = new FakeTarget(PlayerLifeState.Alive);
        SuppressHasteRules.Grant(new[] { target }, new SuppressHasteValues(2f, 2f, 2f, durationOverride), Boss, suppress);
        foreach (var effect in target.Effects.Values)
            Assert.That(effect.duration, Is.EqualTo(expected));
    }

    [TestCase(0f, 0f)]
    [TestCase(0f, -1f)]
    [TestCase(0f, float.NaN)]
    public void Duration_NonPositive_DoesNotGrant(float durationOverride, float suppress)
    {
        // 상태이상 지속 0 = 수동 해제형 — 걸면 영구 버프가 된다.
        Assert.That(SuppressHasteRules.TryResolveDuration(durationOverride, suppress, out _), Is.False);

        var target = new FakeTarget(PlayerLifeState.Alive);
        Assert.That(SuppressHasteRules.Grant(new[] { target }, new SuppressHasteValues(2f, 2f, 2f, durationOverride), Boss, suppress), Is.EqualTo(0));
        Assert.That(target.Effects, Is.Empty);
    }

    [Test]
    public void Reentry_RefreshesDuration_StackStaysOne()
    {
        var target = new FakeTarget(PlayerLifeState.Alive);
        SuppressHasteRules.Grant(new[] { target }, Values, Boss, 5f);
        SuppressHasteRules.Grant(new[] { target }, Values, Boss, 8f);

        Assert.That(target.Effects.Count, Is.EqualTo(3), "같은 출처 = 병존 추가 없음");
        foreach (var effect in target.Effects.Values)
        {
            Assert.That(effect.stacks, Is.EqualTo(1));
            Assert.That(effect.duration, Is.EqualTo(8f), "재진입 = 이번 제압 길이로 갱신");
            Assert.That(effect.magnitude, Is.EqualTo(2f), "배율이 곱으로 쌓이지 않는다");
        }
    }

    [TestCase(PlayerLifeState.DeadPresentation, true)]
    [TestCase(PlayerLifeState.Soul, true)]
    [TestCase(PlayerLifeState.PermanentDead, true)]
    [TestCase(PlayerLifeState.Alive, false)]
    public void ShouldRevoke_WhenLeavingAlive(PlayerLifeState next, bool expected)
    {
        Assert.That(SuppressHasteRules.ShouldRevoke(next), Is.EqualTo(expected));
    }

    [Test]
    public void Revoke_RemovesOnlyThatSource_AndReviveDoesNotRegrant()
    {
        const ulong other = 7;
        var target = new FakeTarget(PlayerLifeState.Alive);
        SuppressHasteRules.Grant(new[] { target }, Values, Boss, 5f);
        target.Apply(StatusEffectType.MoveSpeedFinalMultiplier, 1.3f, 5f, other, 1);

        target.LifeState = PlayerLifeState.Soul;
        Assert.That(SuppressHasteRules.Revoke(target, Boss), Is.True);

        Assert.That(target.Effects.Count, Is.EqualTo(1), "다른 출처는 남는다");
        Assert.That(target.Effects.ContainsKey((StatusEffectType.MoveSpeedFinalMultiplier, other)), Is.True);
        Assert.That(SuppressHasteRules.Revoke(target, Boss), Is.False, "두 번째는 뺄 게 없다");

        // 부활 — 버프는 제압 진입 순간에만 걸리므로 다시 생기지 않는다.
        target.LifeState = PlayerLifeState.Alive;
        Assert.That(target.Effects.ContainsKey((StatusEffectType.CooldownRateModifier, Boss)), Is.False);
    }

    [Test]
    public void Hud_ShowsOnlyRepresentative()
    {
        Assert.That(SuppressHasteRules.IsHudHidden(SuppressHasteRules.HudRepresentative), Is.False);
        Assert.That(SuppressHasteRules.IsHudHidden(StatusEffectType.MoveSpeedFinalMultiplier), Is.True);
        Assert.That(SuppressHasteRules.IsHudHidden(StatusEffectType.AttackSpeedFinalMultiplier), Is.True);
        Assert.That(SuppressHasteRules.IsHudHidden(StatusEffectType.MoveSpeedModifier), Is.False, "기존 타입은 그대로 보인다");
    }

    [Test]
    public void Buffs_AreClassifiedAsBuff()
    {
        // 디버프로 분류되면 보스방 진입 연출의 디버프 일괄 해제가 지운다(StatusEffectCategories).
        foreach (var effect in SuppressHasteRules.Effects(Values))
        {
            var instance = new StatusEffectInstance { type = effect.type, magnitude = effect.magnitude, stackCount = 1 };
            Assert.That(StatusEffectCategories.Of(instance), Is.EqualTo(StatusEffectCategory.Buff), effect.type.ToString());
        }
    }
}
