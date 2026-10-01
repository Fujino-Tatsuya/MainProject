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

    /// <summary>공격 중(AttackReady·Attack) 공용 대시로 끊을 수 있는가. 가붕이 콤보 = 아니오(기존 동작), 거너 = 예(D1).</summary>
    bool CanBeCanceledByDash { get; }

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
