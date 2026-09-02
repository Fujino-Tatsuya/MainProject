/// <summary>
/// 평타 피격이 <b>자동 Hit 경직</b>을 유발하는가를 정하는 순수 판정.
///
/// 왜 분리했나 — 원래 이 조건은 <c>MonsterBase.TakeDamage</c> 안에 인라인으로 있어서
/// 네트워크·상태머신 없이는 한 줄도 검증할 수 없었다. 판정만 static 으로 빼면 전 조합을
/// EditMode 에서 고정할 수 있다(<c>MonsterHitReactionPolicyTests</c>).
///
/// 🔴 여기서 정하는 것은 <b>자동 경직뿐</b>이다. 데미지·실드 감소·HitFlash 는 이 판정 앞에서
///    이미 끝났고, 명시적 넉백(<c>TryEnterKnockback</c>)과 인터럽트 누적 그로기는 별도 경로다.
///    "공격 중엔 안 아프다"가 아니라 "공격 중엔 평타에 <b>밀리지</b> 않는다"는 규칙이다.
/// </summary>
public static class MonsterHitReactionPolicy
{
    /// <param name="state">피격 시점의 몬스터 상태.</param>
    /// <param name="type">맞은 공격의 종류. 평타는 <c>AttackType.Default</c>.</param>
    /// <param name="armor">슈퍼아머(경직 면역)가 걸려 있는가.</param>
    public static bool ShouldEnterAutomaticHit(MonsterState state, AttackType type, bool armor)
    {
        // 기존 제외 조건 — 이 셋은 이번 변경 전부터 자동 경직 대상이 아니었다.
        // Groggy/Return 은 자기 상태를 유지해야 하고, Knockback 은 밀림을 Hit 로 덮으면 안 된다.
        if (armor
            || state == MonsterState.Groggy
            || state == MonsterState.Return
            || state == MonsterState.Knockback)
        {
            return false;
        }

        // 이번에 추가되는 유일한 규칙: 공격 커밋(= MonsterState.Attack 전 구간, 선딜·후딜 포함) 중
        // 들어온 **평타**만 경직을 건너뛴다. 스킬은 기존대로 공격을 끊는다.
        //
        // 🔴 AttackType 의 기본값은 None 이다(BaseAttack.cs). 무기 프리팹을 Default 로 저작하지 않은
        //    공격은 여기서 "평타 아님"으로 갈려 기존대로 경직시킨다 — 안전한 방향이지만, 새 캐릭터의
        //    평타가 저작 누락으로 None 이면 이 보호가 **조용히 빠진다**. 캐릭터 추가 시 프리팹의
        //    attackType 이 1(Default)인지 확인할 것.
        return state != MonsterState.Attack || type != AttackType.Default;
    }
}
