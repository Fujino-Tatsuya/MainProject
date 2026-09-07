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
}
