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
    /// 공격 중(AttackReady·Attack) 스킬 입력을 '공격 뒤 스킬'로 예약하고 이 공격을 지금 발에서 마무리하는가 — 거너 = 예(할 일 44).
    /// 공격은 끊지 않는다: 현재 발은 끝까지 가고 다음 발만 잇지 않는다(<see cref="FinishWithoutChaining"/>), 정상 종료 뒤
    /// Idle/Move 첫 틱에 예약한 스킬을 기존 경로로 요청한다. 가붕이·어쌔신 = 아니오 — 공격 중 스킬 입력은 0.2초 선입력 버퍼 그대로이고,
    /// 버퍼에 평타보다 높은 스킬 입력이 살아 있으면 <see cref="FinishWithoutChaining"/> 으로 다음 타만 잇지 않는다(할 일 77 후속).
    /// </summary>
    bool ReservesSkillAfterAttack { get; }

    /// <summary>
    /// [오너] 다음 발(타)을 잇지 않고 지금 발로 공격을 마무리한다 — 누르고 있음·창 예약을 무시(버튼을 뗀 것처럼).
    /// 끝은 기존 정상 종료 경로(거너: 발사 + ShotRecovery 뒤 서버 EndRpc / 가붕이·어쌔신: 이 타 End 이벤트 뒤 서버 종료)다.
    /// 오너와 서버가 같은 래치를 들고 공격이 끝나면(정상 종료·취소) 풀린다.
    /// </summary>
    void FinishWithoutChaining();

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

/// <summary>
/// 기본 공격 중 스킬 입력의 처리(할 일 44) — 예약 안 하는 캐릭터(0.2초 선입력 버퍼 그대로) / 차단 효과 거부 / 쿨타임 거부 / 공격 뒤로 예약.
/// 거부는 누른 순간 기존 규칙(버림·피드백 이벤트)이고 예약하지 않으므로 공격은 그대로 이어진다.
/// </summary>
public enum PlayerSkillDuringAttackDecision
{
    NotReserving,
    RejectByEffect,
    RejectByCooldown,
    Reserve
}

/// <summary>
/// 기본 공격 뒤에 쓸 스킬 예약(할 일 44, 거너) — 오너 로컬. 마지막에 누른 스킬 1개만 기억하고(다른 스킬이 덮는다),
/// 같은 스킬 연타는 1번으로 본다. 선입력 버퍼와 달리 만료가 없다 — 현재 발 남은 간격 + ShotRecovery 가 버퍼 유지 시간보다
/// 길 수 있어 평타가 끝날 때까지 들고 있어야 한다. 비우기는 호출자가 한다(실행·거부·행동 끊김·입력 차단).
/// MonoBehaviour 아님 — EditMode 테스트 대상.
/// </summary>
public sealed class PlayerSkillReservation
{
    public PlayerBufferedInput Reserved { get; private set; } = PlayerBufferedInput.None;
    public bool HasReservation => Reserved != PlayerBufferedInput.None;

    /// <summary>예약한다. 새로 예약했거나 다른 스킬로 바꿨으면 true, 같은 스킬 연타(이미 예약됨)·None 이면 false.</summary>
    public bool Reserve(PlayerBufferedInput input)
    {
        if (input == PlayerBufferedInput.None || input == Reserved)
            return false;

        Reserved = input;
        return true;
    }

    public void Clear() => Reserved = PlayerBufferedInput.None;
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

    /// <summary>
    /// 기본 공격 중 스킬 입력 — 예약 캐릭터가 아니면 손대지 않는다. 판정 순서는 Idle/Move 와 같다:
    /// 차단 효과(상태이상) → 쿨타임 → 예약. 사용 조건(CanUse)·조준 진입은 공격이 끝나 실제로 시도할 때 본다.
    /// </summary>
    public static PlayerSkillDuringAttackDecision ClassifySkillDuringAttack(
        bool reservesSkillAfterAttack, bool blockedByEffect, bool blockedByCooldown)
    {
        if (!reservesSkillAfterAttack)
            return PlayerSkillDuringAttackDecision.NotReserving;
        if (blockedByEffect)
            return PlayerSkillDuringAttackDecision.RejectByEffect;
        if (blockedByCooldown)
            return PlayerSkillDuringAttackDecision.RejectByCooldown;

        return PlayerSkillDuringAttackDecision.Reserve;
    }

    /// <summary>
    /// 거너가 다음 발을 잇는가 — 누르고 있거나 창 예약이 있을 때. 스킬이 예약됐으면 둘 다 무시하고 지금 발로 마무리한다(할 일 44).
    /// </summary>
    public static bool ShouldChainNextShot(bool attackHeld, bool windowQueued, bool skillReserved) =>
        !skillReserved && (attackHeld || windowQueued);

    /// <summary>
    /// 예약하지 않는 캐릭터(가붕이·어쌔신)가 공격 중 버퍼의 스킬 입력 때문에 다음 타를 잇지 않고 지금 타로 마무리하는가(할 일 77 후속).
    /// 평타보다 우선순위가 높은 스킬 입력이 살아 있고, 지금 쓸 수 있어 보일 때만(배정됨·차단 효과 아님·쿨타임 아님) —
    /// 쓸 수 없는 입력은 체인을 끊지 않고 버퍼 규칙(만료·Idle 에서 거부 피드백)에 맡긴다.
    /// </summary>
    public static bool ShouldFinishChainForBufferedSkill(
        bool reservesSkillAfterAttack, bool skillAboveAttackAlive, bool assigned, bool blockedByEffect, bool blockedByCooldown) =>
        !reservesSkillAfterAttack && skillAboveAttackAlive && assigned && !blockedByEffect && !blockedByCooldown;

    /// <summary>
    /// 근접 콤보(가붕이)가 이번 틱 다음 타 예약을 요청하는가 — 누르고 있거나 누른 프레임, 단 스킬 때문에 마무리 중이면 안 한다.
    /// 창(ComboWindowOpen~Close) 판정은 따로(오프라인 = 로컬, 네트워크 = 서버)다.
    /// </summary>
    public static bool ShouldRequestNextMeleeAttack(bool attackHeld, bool attackPressed, bool finishingChain) =>
        !finishingChain && (attackHeld || attackPressed);
}
