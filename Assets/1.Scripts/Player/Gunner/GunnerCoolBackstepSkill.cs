using UnityEngine;

/// <summary>
/// 거너 E — 냉각 백스텝 (character_gunner.md §7, D9, PLAN-gunner.md G5).
///
/// 사용 즉시(서버) 과열도 0 · 과열 해제(과열도 0 이어도 이동용으로 쓸 수 있다). 입력 순간의 조준을 저장하고
/// 그 정반대로 짧게 이동 — 이동 입력·이후 마우스는 무시. 적·아군·벽·오브젝트를 통과하지 못하고 막히면 그 앞에서 멈춘다
/// (공용 대시는 아군 통과지만 E 는 모터의 일시 아군 차단을 켠다). 피해·상태이상·무적 없음.
/// 동작 중에는 다른 행동 불가(Skill 상태), 끝나면 기본 공격 포함 모두 가능.
/// </summary>
public class GunnerCoolBackstepSkill : PlayerSkillBase
{
    private GunnerHeat heat;
    private PlayerMotor motor;

    // 시뮬레이션 피어(오너 + 서버) 공통
    private Vector3 moveDirection;
    private float moveSpeed;
    private float remaining;

    // 서버
    private float endTime;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;
    public override bool CanMoveWhileActive => false;

    private GunnerCoolBackstepData EData => Data as GunnerCoolBackstepData;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        heat = owner.GetComponent<GunnerHeat>();
        motor = owner.GetComponent<PlayerMotor>();
    }

    public override bool CanUse(Vector3 direction, Unit target) => EData != null;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Active;
        endTime = Time.time + EData.MoveDuration;

        // 막혀서 거리가 짧아져도 냉각은 정상 적용(§7.3)
        heat?.ServerResetToZero();
        Edit.Log("[Gunner/E] 냉각 백스텝 — 과열도 0", this);
    }

    // 전 피어. 방향은 서버 승인 시점의 조준(= 입력 순간). 무기 전방으로 냉기 → 반동으로 정반대 이동.
    public override void OnClientPlay(Vector3 direction)
    {
        GunnerCoolBackstepData data = EData;
        direction.y = 0f;
        moveDirection = direction.sqrMagnitude > 0.001f ? -direction.normalized : -owner.transform.forward;
        remaining = data != null ? data.Distance : 0f;
        moveSpeed = data != null ? data.Distance / data.MoveDuration : 0f;

        if (motor != null)
            motor.BlockOtherPlayersOverride = true;
    }

    public override void OnFixedTick()
    {
        if (remaining <= 0f || motor == null || !owner.IsSimulating)
            return;

        float step = Mathf.Min(moveSpeed * Time.fixedDeltaTime, remaining);
        remaining -= step;
        motor.AddGroundedDisplacement(moveDirection * step); // 막히면 모터 스윕이 그 앞에서 멈춘다
    }

    public override void OnTick()
    {
        if (State == SkillState.Active && Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        remaining = 0f;
        if (motor != null)
            motor.BlockOtherPlayersOverride = false;
        base.OnEnd(reason);
    }
}
