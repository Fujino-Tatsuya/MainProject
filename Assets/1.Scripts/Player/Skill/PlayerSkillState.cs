using UnityEngine;

/// <summary>
/// 스킬 실행 중 FSM 상태. 모든 스킬이 이 단일 상태를 공유하고,
/// 이동/회전 허용 여부는 실행 중인 스킬 인스턴스에 위임한다 (E는 이동 자유, R은 완전 잠금).
/// 진입은 PlayerStateController.BeginSkill 경로로만 한다 (스킬 인스턴스 필요).
/// </summary>
public sealed class PlayerSkillState : PlayerStateBase
{
    private readonly PlayerSkillBase skill;
    private readonly PlayerActionState phase;

    /// <param name="phase">스킬 상태 계열의 단계 — Skill(기본) 또는 Focus(거너 Q 충전)</param>
    public PlayerSkillState(PlayerStateContext context, PlayerSkillBase skill, PlayerActionState phase) : base(context)
    {
        this.skill = skill;
        this.phase = phase;
    }

    public PlayerSkillBase Skill => skill;
    public override PlayerActionState StateType => phase;
    public override bool RequiresStateAuthorityTick => true;

    public bool AllowsMovement => skill != null && skill.CanMoveWhileActive;
    public bool AllowsMovementRotate => skill != null && skill.CanMovementRotateWhileActive;

    public override void Enter(PlayerActionState previousState)
    {
        if (!AllowsMovement)
            Context.Player.SetAnimatorMoving(false);
    }

    public override void Tick()
    {
        Context.Player.SetAnimatorMoving(AllowsMovement && Context.Input.HasMoveInput);
        Context.Skills?.Tick();
    }

    public override void FixedTick()
    {
        Context.Skills?.FixedTick();
    }

    public override void Exit(PlayerActionState nextState)
    {
        // 같은 스킬의 단계 전환(Focus → Skill)은 스킬을 끝내지 않는다 — PlayerStateController.ChangeSkillPhase.
        if (PlayerStateController.IsSkillState(nextState))
            return;

        Context.Player.SetAnimatorMoving(false);
        Context.Skills?.HandleSkillStateExit(nextState);
    }
}
