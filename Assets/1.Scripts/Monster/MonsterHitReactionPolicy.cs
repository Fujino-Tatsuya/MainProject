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

    /// <summary>
    /// 등급까지 본 자동 경직 판정. 🔴 <b>중간보스는 자동 경직이 없다</b>(팀장 10-26).
    ///
    /// 왜 — 10-14 P1(공격이 끝나면 슈퍼아머 해제) 뒤로 중간보스가 추격 중 피격마다 0.4초 경직(갱신형)을
    /// 받았다. 분산 교전에서 멀리 있는 대상을 쫓는 동안 옆 사람이 때리면 경직이 끊기지 않아 제자리에 섰다
    /// (Gauntlet 은 피격 클립이 없어 그냥 서 있는 것으로 보였다). 취약 중에도 평타·간파 스킬은 피해만이고,
    /// 경직은 팔라딘 Q 밀림과 함께 오는 것뿐이다 — 그 경로는 <see cref="CanEnterKnockback"/> 이 정한다.
    /// </summary>
    public static bool ShouldEnterAutomaticHit(MonsterState state, AttackType type, bool armor, MonsterRank rank)
    {
        if (rank == MonsterRank.MidBoss) return false;
        return ShouldEnterAutomaticHit(state, type, armor);
    }

    /// <summary>
    /// 밀림(넉백)에 들어가는가. 밀림 값이 있는 공격만 여기 온다 — 플레이어 쪽은 팔라딘 Q 하나뿐이다.
    ///
    /// 일반 몹: 슈퍼아머·그로기·복귀·사망이면 거절(기존 규칙 그대로).
    /// 🔴 중간보스: <b>취약 중에만</b> 밀린다(팀장 10-26). 취약 = 카운터 성공부터 8초(<c>MonsterCounterWindow.vulnerableDuration</c>)이고
    ///    그로기(1초)는 그 앞부분일 뿐이다 — 그로기가 끝나 공격·이동 중이어도 취약이면 밀린다. 평상시에는 완전 면역이다.
    ///    공격 중 슈퍼아머는 취약 동안 아예 걸리지 않으므로(<c>MonsterBase.UsesAttackSuperArmor</c>) 여기 <paramref name="armor"/> 는
    ///    그 밖의 슈퍼아머(튜토리얼 스피너 연습의 무한 슈퍼아머 등)만 남는다 — 그건 취약이어도 막는다.
    /// </summary>
    /// <param name="vulnerable">중간보스 취약 중인가(<c>MonsterBase.IsMidBossVulnerable</c>). 일반 몹은 무시.</param>
    public static bool CanEnterKnockback(MonsterState state, bool armor, MonsterRank rank, bool vulnerable)
    {
        if (armor || state == MonsterState.Return || state == MonsterState.Dead) return false;

        if (rank == MonsterRank.MidBoss)
            return vulnerable;

        return state != MonsterState.Groggy;
    }

    /// <summary>
    /// 경직·밀림(Hit/Knockback) 동안 <b>대기 자세로 멈춰 세우는가</b>(팀장 10-26 — 피격 클립이 없으면 경직은 "가만히 서 있음"으로 보인다).
    ///
    /// 왜 — 피격 상태가 없는 컨트롤러(HumanoidBot: 공격·대기·걷기 클립뿐)는 <c>hitTrigger</c> 를 받을 곳이 없어
    /// 공격 클립이 밀림·경직 내내 계속 돌았다. 판정은 이미 끊겨 있어도(<c>NotifyAttackHit</c> 는 Attack 상태에서만 유효)
    /// 화면에는 "Q 를 맞고도 공격을 끝까지 휘두르는" 것으로 보였다.
    ///
    /// 중간보스도 같다(팀장 10-26) — 취약 중 Q 로 밀릴 때 GauntletBot(피격 클립 없음)이 대기 모션을 그냥 재생해
    /// "취약이 끊긴" 것처럼 보였다. 그로기 자세(대기 정지)와 같은 자세로 이어진다. WallBot 은 피격 상태가 있어 제외된다.
    /// 🔴 23호는 밀리지 않으므로 제외. 고정 터렛(PeekABot·TeslaBot)은 대기 상태가 숨기·충전 자세라 제외한다.
    /// </summary>
    /// <param name="hasHitState">애니메이터에 <c>hitTrigger</c> 와 같은 이름의 상태가 있는가(ChompBot·MortarBot·WallBot = 있음).</param>
    public static bool ShouldFreezeForHitReaction(MonsterState state, MonsterRank rank, MonsterArchetype archetype,
                                                  bool hasHitState)
    {
        if (state != MonsterState.Hit && state != MonsterState.Knockback) return false;
        if (rank == MonsterRank.Boss || archetype == MonsterArchetype.RangedTurret) return false;
        return !hasHitState;
    }
}
