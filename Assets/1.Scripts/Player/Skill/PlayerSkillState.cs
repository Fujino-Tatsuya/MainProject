using UnityEngine;

/// <summary>
/// 스킬 실행 중 FSM 상태. 모든 스킬이 이 단일 상태를 공유하고,
/// 이동/회전 허용 여부는 실행 중인 스킬 인스턴스에 위임한다 (E는 이동 자유, R은 완전 잠금).
/// 진입은 PlayerStateController.BeginSkill 경로로만 한다 (스킬 인스턴스 필요).
/// </summary>
public sealed class PlayerSkillState : PlayerStateBase
{
    private readonly PlayerSkillBase skill;

    public PlayerSkillState(PlayerStateContext context, PlayerSkillBase skill) : base(context)
    {
        this.skill = skill;
    }

    public override PlayerActionState StateType => PlayerActionState.Skill;
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

    public override void Exit(PlayerActionState nextState)
    {
        Context.Player.SetAnimatorMoving(false);
        Context.Skills?.HandleSkillStateExit(nextState);
    }
}
