/// <summary>
/// [서버] 몬스터가 상태(평상·취약·제압)에 따라 상시 슈퍼아머를 거는가 — <see cref="MonsterStatusEffect.HasSuperArmor"/> 가 공격별 슈퍼아머와 OR 한다.
/// 구현: 23호(평상·제압 on, 취약 off) · 중간보스 3종(취약 밖 on). 규칙 = <see cref="BossCrowdControlRules"/>.
/// </summary>
public interface IMonsterStateSuperArmor
{
    bool StateSuperArmor { get; }
}

/// <summary>
/// [서버] <c>MonsterState.Knockback</c> 을 쓰지 않는 자체 밀림 중인가(23호 취약 넉백) — 뇌진탕 대기 판정(넉백 중 = 대기)이 본다.
/// 밀림이 끝날 때 구현측이 <see cref="MonsterStatusEffect.ConsumeConcussionOnKnockbackEnd"/> 로 기절을 넘긴다.
/// </summary>
public interface IMonsterSelfKnockback
{
    bool IsSelfKnockedBack { get; }
}

/// <summary>
/// 보스·중간보스 상태별 CC 규칙(할 일 134 — 경석 handoff 보스넉백·기절). MonoBehaviour 밖 순수 계산(EditMode 테스트가 고정한다).
///
/// | 상태 | 밀치기 | 기절·뇌진탕 |
/// |---|---|---|
/// | 평상(공격 밖 포함) | 거부 | 거부 |
/// | 취약 | 허용 | 허용 |
/// | 제압 | 거부 | 거부 |
/// 피해는 어느 상태든 정상.
///
/// 거부 수단은 슈퍼아머 하나다 — 상태별 슈퍼아머 OR 공격별 슈퍼아머(SuperArmor 플래그)가
/// <see cref="Unit.HasSuperArmor"/>(넉백 입구 <see cref="Unit.Knockback"/>)와 <see cref="StatusEffectImmunityPolicy"/>(기절·뇌진탕)에 같이 들어간다.
/// </summary>
public static class BossCrowdControlRules
{
    /// <summary>23호 상태별 슈퍼아머 — 평상 on · 취약 off · 제압 on.</summary>
    public static bool StateSuperArmor(bool vulnerable, bool suppressed) => suppressed || !vulnerable;

    /// <summary>중간보스 상태별 슈퍼아머 — 취약(카운터 성공부터 8초) 밖이면 on(공격 밖 평상 포함). 중간보스 등급이 아니면 off.</summary>
    public static bool MidBossStateSuperArmor(bool isMidBoss, bool vulnerable) => isMidBoss && !vulnerable;

    /// <summary>
    /// 거부 판정에 넣는 활성 플래그 = 복제 플래그 + (상태별 슈퍼아머면 SuperArmor).
    /// <see cref="StatusEffectImmunityPolicy"/>·<see cref="ConcussionState"/> 에 이 값을 준다.
    /// </summary>
    public static StatusEffectType EffectiveActive(StatusEffectType active, bool stateSuperArmor) =>
        stateSuperArmor ? active | StatusEffectType.SuperArmor : active;

    /// <summary>
    /// 이번 공격에 공격별 슈퍼아머를 거는가 — 취약 중엔 걸지 않는다(취약 = 밀치기·기절 허용).
    /// 중간보스 <c>MonsterBase.UsesAttackSuperArmor</c>(취약 중 안 건다)와 같은 규칙이다.
    /// </summary>
    public static bool AppliesAttackSuperArmor(bool attackSuperArmor, bool vulnerable) => attackSuperArmor && !vulnerable;

    /// <summary>
    /// 23호 피격이 취약 밀림(<see cref="Unit.Knockback"/>)으로 이어지는가 — 취약 · 플레이어 · 넉백 세기 &gt; 0 인 일반 공격(팔라딘 Q·특성 밀치기).
    /// 🔴 경직 &gt; 0 조건은 없다(할 일 134) — 경직 없는 밀치기도 취약 중엔 민다. 경직은 밀린 뒤 따로 건다.
    /// </summary>
    public static bool QualifiesPushHit(bool vulnerable, bool interrupt, bool fromPlayer, float knockbackStrength) =>
        vulnerable && !interrupt && fromPlayer && knockbackStrength > 0f;

    /// <summary>
    /// 23호가 넉백 입구(<see cref="Unit.Knockback"/> → OnKnockback)로 들어온 넉백을 받는가 — 취약 · 필수 기믹 아님 · 점프 체인 아님 · 살아 있음.
    /// 받으면 세기와 무관하게 고정 거리(<c>vulnerableKnockbackDistance</c>)로 민다.
    /// </summary>
    public static bool AcceptsVulnerableKnockback(bool vulnerable, bool mandatorySequence, bool jumpChain, bool dead) =>
        vulnerable && !mandatorySequence && !jumpChain && !dead;
}
