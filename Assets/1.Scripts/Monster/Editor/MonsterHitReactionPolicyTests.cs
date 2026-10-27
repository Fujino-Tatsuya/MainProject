using NUnit.Framework;

/// <summary>
/// 공격 커밋 규칙의 순수 판정부 테스트.
///
/// 왜 정책을 따로 떼어 테스트하는가 — 원본 조건은 <c>MonsterBase.TakeDamage</c> 안에 있어
/// 네트워크·상태머신 없이는 못 부른다. 판정만 static 으로 빼면 EditMode 에서 전 조합을 고정할 수 있다.
/// </summary>
public sealed class MonsterHitReactionPolicyTests
{
    // 인자 순서: 몬스터 상태 / 맞은 공격 종류 / 슈퍼아머 여부 / 기대값(자동 Hit 경직에 드는가)
    //
    // 평타 = AttackType.Default. 공격 중 평타만 경직을 건너뛴다.
    [TestCase(MonsterState.Attack,    AttackType.Default, false, false)] // 핵심: 공격 중 평타 → 경직 없음
    [TestCase(MonsterState.Chase,     AttackType.Default, false, true)]  // 공격 중이 아니면 기존대로 경직
    [TestCase(MonsterState.Attack,    AttackType.Skill,   false, true)]  // 공격 중이라도 스킬은 기존대로 경직
    [TestCase(MonsterState.Chase,     AttackType.Default, true,  false)] // 슈퍼아머는 기존대로 경직 면제
    [TestCase(MonsterState.Groggy,    AttackType.Default, false, false)] // 아래 3종은 기존 제외 상태 — 회귀 방지용
    [TestCase(MonsterState.Return,    AttackType.Default, false, false)]
    [TestCase(MonsterState.Knockback, AttackType.Default, false, false)]

    // 🔴 중간보스가 이 변경의 영향을 받지 않는 근거를 코드로 고정한다.
    //    (10-26 부터 중간보스는 등급 판정으로 자동 경직 자체가 없다 — 아래 MidBoss_NeverEntersAutomaticHit.)
    //    GauntletBot·SpinnerBot·WallBot 은 데이터(hasSuperArmorWhileAttacking: 1)로 공격 중 슈퍼아머가
    //    걸려 있어, 이 정책이 그들에겐 no-op 이다. 정책은 MonsterBase 공용 경로에 있으므로
    //    안전의 근거가 코드가 아니라 **데이터**라는 뜻이다 — 그 플래그를 끄면 공격 커밋도 딸려온다.
    [TestCase(MonsterState.Attack,    AttackType.Default, true,  false)]

    // 🔴 AttackType 기본값이 None 이라(BaseAttack.cs) 무기 프리팹을 Default 로 저작하지 않으면
    //    평타가 None 으로 들어오고 커밋 보호가 조용히 새어나간다. 그 동작을 명시적으로 고정해 둔다
    //    ("경직 그대로" = 기존 동작 유지 = 버그 재발). 새 캐릭터 추가 시 이 케이스를 근거로 저작을 확인할 것.
    [TestCase(MonsterState.Attack,    AttackType.None,    false, true)]
    public void AutomaticHitDecision_MatchesCommitmentRule(
        MonsterState state, AttackType attackType, bool armor, bool expected)
    {
        Assert.That(MonsterHitReactionPolicy.ShouldEnterAutomaticHit(state, attackType, armor),
            Is.EqualTo(expected));
    }

    // 🔴 중간보스는 자동 경직이 없다(팀장 10-26) — 평상시·취약(그로기) 모두, 평타·스킬·간파 모두 피해만.
    [TestCase(MonsterState.Chase,  AttackType.Default)]
    [TestCase(MonsterState.Idle,   AttackType.Skill)]
    [TestCase(MonsterState.Hit,    AttackType.SkillQ)]
    [TestCase(MonsterState.Groggy, AttackType.Default)]
    [TestCase(MonsterState.Groggy, AttackType.Skill)]
    public void MidBoss_NeverEntersAutomaticHit(MonsterState state, AttackType attackType)
    {
        Assert.That(MonsterHitReactionPolicy.ShouldEnterAutomaticHit(state, attackType, false, MonsterRank.MidBoss),
            Is.False);
    }

    // 일반 몹은 등급 인자를 넘겨도 기존 판정 그대로.
    [TestCase(MonsterState.Chase,  AttackType.Default, true)]
    [TestCase(MonsterState.Attack, AttackType.Default, false)]
    public void Normal_RankOverload_MatchesLegacy(MonsterState state, AttackType attackType, bool expected)
    {
        Assert.That(MonsterHitReactionPolicy.ShouldEnterAutomaticHit(state, attackType, false, MonsterRank.Normal),
            Is.EqualTo(expected));
    }

    // 인자 순서: 상태 / 슈퍼아머 / 등급 / 중간보스 취약 중 / 기대값(밀리는가)
    // 취약 = 카운터 성공부터 8초(그로기 1초 포함) — 팀장 10-26. 그로기가 끝나 공격·이동 중이어도 취약이면 밀린다.
    [TestCase(MonsterState.Chase,     false, MonsterRank.MidBoss, false, false)] // 평상시 중간보스 = 완전 면역
    [TestCase(MonsterState.Idle,      false, MonsterRank.MidBoss, false, false)]
    [TestCase(MonsterState.Groggy,    false, MonsterRank.MidBoss, false, false)] // 취약이 아닌 그로기(없어야 하지만) = 면역
    [TestCase(MonsterState.Groggy,    false, MonsterRank.MidBoss, true,  true)]  // 간파 성공 그로기 중 팔라딘 Q = 밀림
    [TestCase(MonsterState.Chase,     false, MonsterRank.MidBoss, true,  true)]  // 그로기 끝난 취약 — 추격 중에도 밀린다
    [TestCase(MonsterState.Attack,    false, MonsterRank.MidBoss, true,  true)]  // 취약 중 공격 — 공격 슈퍼아머를 안 거므로 밀린다
    [TestCase(MonsterState.Knockback, false, MonsterRank.MidBoss, true,  true)]  // 밀림 중 다시 맞으면 갱신
    [TestCase(MonsterState.Hit,       false, MonsterRank.MidBoss, true,  true)]  // 밀림 뒤 경직 중
    [TestCase(MonsterState.Groggy,    true,  MonsterRank.MidBoss, true,  false)] // 다른 곳이 건 슈퍼아머(튜토리얼 연습)는 취약이어도 막는다
    [TestCase(MonsterState.Dead,      false, MonsterRank.MidBoss, true,  false)]
    [TestCase(MonsterState.Return,    false, MonsterRank.MidBoss, true,  false)]
    [TestCase(MonsterState.Chase,     false, MonsterRank.Normal,  false, true)]  // 일반 몹 — 기존 규칙 그대로
    [TestCase(MonsterState.Groggy,    false, MonsterRank.Normal,  false, false)]
    [TestCase(MonsterState.Return,    false, MonsterRank.Normal,  false, false)]
    [TestCase(MonsterState.Chase,     true,  MonsterRank.Normal,  false, false)]
    public void KnockbackDecision_MatchesRankRule(
        MonsterState state, bool armor, MonsterRank rank, bool vulnerable, bool expected)
    {
        Assert.That(MonsterHitReactionPolicy.CanEnterKnockback(state, armor, rank, vulnerable),
            Is.EqualTo(expected));
    }

    // 인자 순서: 상태 / 등급 / 아키타입 / 피격 상태 있음 / 기대값(대기 자세로 멈춰 세우는가) — 팀장 10-26
    [TestCase(MonsterState.Knockback, MonsterRank.Normal,  MonsterArchetype.Melee,        false, true)]  // HumanoidBot Q 밀림
    [TestCase(MonsterState.Hit,       MonsterRank.Normal,  MonsterArchetype.Melee,        false, true)]  // 밀림 뒤 경직·평타 경직
    [TestCase(MonsterState.Hit,       MonsterRank.Normal,  MonsterArchetype.Melee,        true,  false)] // ChompBot — 피격 클립을 그대로 쓴다
    [TestCase(MonsterState.Knockback, MonsterRank.Normal,  MonsterArchetype.RangedMobile, true,  false)] // MortarBot
    [TestCase(MonsterState.Hit,       MonsterRank.Normal,  MonsterArchetype.RangedTurret, false, false)] // 터렛 대기 = 숨기·충전 자세
    [TestCase(MonsterState.Knockback, MonsterRank.MidBoss, MonsterArchetype.Melee,        false, true)]  // GauntletBot 취약 밀림 — 그로기 자세와 이어진다(10-26 C)
    [TestCase(MonsterState.Hit,       MonsterRank.MidBoss, MonsterArchetype.Melee,        true,  false)] // WallBot — 피격 클립 그대로
    [TestCase(MonsterState.Knockback, MonsterRank.Boss,    MonsterArchetype.Boss,         false, false)] // 23호는 손대지 않는다
    [TestCase(MonsterState.Attack,    MonsterRank.Normal,  MonsterArchetype.Melee,        false, false)] // 경직·밀림 밖은 손대지 않는다
    [TestCase(MonsterState.Groggy,    MonsterRank.Normal,  MonsterArchetype.Melee,        false, false)]
    [TestCase(MonsterState.Idle,      MonsterRank.Normal,  MonsterArchetype.Melee,        false, false)]
    public void HitReactionFreeze_OnlyNormalWithoutHitState(
        MonsterState state, MonsterRank rank, MonsterArchetype archetype, bool hasHitState, bool expected)
    {
        Assert.That(MonsterHitReactionPolicy.ShouldFreezeForHitReaction(state, rank, archetype, hasHitState),
            Is.EqualTo(expected));
    }
}
