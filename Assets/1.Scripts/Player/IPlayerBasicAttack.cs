using UnityEngine;

/// <summary>
/// 캐릭터별 기본 공격(좌클릭)의 공통 창구. base <c>Player.prefab</c> 은 기본 공격을 갖지 않고,
/// 캐릭터 Variant 가 구현 하나를 루트에 얹는다 — 가붕이 = <see cref="DefaultAttackController"/>(콤보),
/// 거너 = 연사 레이저. 상태기계·연출 잠금·애니 릴레이는 이 창구만 부른다. (PLAN-gunner.md G2)
/// </summary>
public interface IPlayerBasicAttack
{
    /// <summary>서버가 승인한 공격을 지금 시작할 수 있는가(상태 전이 조건).</summary>
    bool CanStartApprovedAttack { get; }

    /// <summary>
    /// 공격 중(AttackReady·Attack) 공용 대시로 끊을 수 있는가 — 오너가 매 프레임 묻는다.
    /// 세 캐릭터 공통으로 항상 예(할 일 9) — 판정이 나갔으면 피해는 그대로, 남은 동작·예약만 <see cref="CancelCurrentAttack"/> 이 끊는다.
    /// </summary>
    bool CanBeCanceledByDash { get; }

    /// <summary>
    /// 자원이 모자라 지금 새 공격을 시작할 수 없는가(거너 과열). 참이면 <see cref="TryStart"/> 거부 입력을
    /// 선입력 버퍼에서 바로 버린다 — 행동 중이라 못 쓴 입력만 들고 있는다(할 일 44). 자원이 없는 캐릭터는 항상 false.
    /// </summary>
    bool IsOutOfResource { get; }

    /// <summary>
    /// 좌클릭을 누르고 있기만 해도(누른 프레임이 아니어도) Idle/Move 에서 새 공격을 시작하는가 — 거너(홀드 연사) = 예,
    /// 가붕이·어쌔신 = 아니오(릴리즈 요구). 스킬이 끝났는데 계속 누르고 있으면 평타가 다시 이어진다(할 일 44).
    /// </summary>
    bool StartsOnHold { get; }

    /// <summary>
    /// 공격 중(AttackReady·Attack) 스킬 입력으로 이 공격을 끊고 스킬을 쓸 수 있는가 — 캐릭터 규칙(거너 = 예).
    /// 서버 승인도 이것만 본다(발 잠금은 오너 시계라 서버가 검증하지 않는다). 끊기는 상태 전이 → <see cref="CancelCurrentAttack"/>.
    /// </summary>
    bool CanBeCanceledBySkill { get; }

    /// <summary>
    /// [오너] 지금은 스킬로 끊기를 미뤄야 하는가(거너 발 잠금 — 발사 −ShotLockLead ~ +ShotLockTail).
    /// 참인 동안 스킬 입력은 선입력 버퍼에 남고(바쁨), 풀린 첫 틱에 다시 시도한다.
    /// </summary>
    bool IsSkillCancelDeferred { get; }

    /// <summary>오너 입력으로 공격 시작을 시도한다. 시작 요청이 나가면 true.</summary>
    bool TryStart();

    /// <summary>Attack 상태 진입 시 한 번.</summary>
    void BeginFromState();

    /// <summary>Attack 상태 Update.</summary>
    void Tick();

    /// <summary>Attack 상태 FixedUpdate — 공격 자체 이동.</summary>
    void FixedTickMovement();

    /// <summary>진행 중 공격을 즉시 끊는다(피격·연출 잠금·상태 이탈).</summary>
    void CancelCurrentAttack();

    /// <summary>마무리 동작(후딜) 중 이동 입력이 들어와 꼬리를 끊을 때.</summary>
    void CancelFinishingTailForMovement();

    /// <summary>애니 이벤트 경로(구형) — 공격 종료.</summary>
    void EndCurrentAttack();

    /// <summary>애니 이벤트 경로(구형) — 판정.</summary>
    void HitCurrentAttack();

    /// <summary>애니 이벤트 경로 — 판정·종료·콤보 창 등.</summary>
    void HandleAnimationEvent(DefaultAttackAnimationEventType eventType);

    /// <summary>루트모션 전달(OnAnimatorMove).</summary>
    void HandleAnimatorMove(Vector3 deltaPosition, Vector3 animatorForward);

    /// <summary>외형(Armature) 교체 후 Animator 재바인딩.</summary>
    void SetAnimator(Animator animator);
}

/// <summary>공격 중 스킬 입력의 처리(할 일 44) — 못 끊음(행동이 끝날 때까지 버퍼에 보관) / 미룸(발 잠금, 버퍼에 보관) / 지금 끊음.</summary>
public enum PlayerSkillCancelDecision
{
    NotAllowed,
    Deferred,
    Cancel
}

/// <summary>기본 공격과 다른 입력이 만나는 순수 규칙(할 일 44). MonoBehaviour 아님 — EditMode 테스트 대상.</summary>
public static class PlayerBasicAttackInputRules
{
    /// <summary>
    /// Idle/Move 에서 누른 프레임·버퍼 입력 없이 홀드만으로 공격을 시작하는가. 홀드 시작 캐릭터만, 공격 차단 효과 중이 아니고
    /// (홀드는 매 프레임이라 거부 피드백을 내지 않고 조용히 건너뛴다) 스킬이 실행·요청 중이 아닐 때.
    /// </summary>
    public static bool ShouldStartOnHold(bool startsOnHold, bool attackHeld, bool attackBlocked, bool skillBusy) =>
        startsOnHold && attackHeld && !attackBlocked && !skillBusy;

    /// <summary>공격 중 스킬 입력 — 캐릭터가 허락하지 않으면 못 끊고, 허락해도 발 잠금 중이면 미룬다.</summary>
    public static PlayerSkillCancelDecision ClassifySkillCancel(bool canBeCanceledBySkill, bool deferred)
    {
        if (!canBeCanceledBySkill)
            return PlayerSkillCancelDecision.NotAllowed;

        return deferred ? PlayerSkillCancelDecision.Deferred : PlayerSkillCancelDecision.Cancel;
    }

    /// <summary>
    /// 거너 발 잠금 구간인가(오너 시계) — 직전 발 요청 뒤 <paramref name="tail"/> 동안, 또는 다음 발 예정 <paramref name="lead"/> 전부터.
    /// 시각이 음수면 그 발이 없다(아직 안 쐈음 / 이어 쏠 발 없음).
    /// </summary>
    public static bool IsInShotLock(float now, float lastShotTime, float nextShotTime, float lead, float tail) =>
        (lastShotTime >= 0f && now < lastShotTime + tail) ||
        (nextShotTime >= 0f && now >= nextShotTime - lead);
}
