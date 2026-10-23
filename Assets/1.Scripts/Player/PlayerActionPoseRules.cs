using UnityEngine;

/// <summary>행동(스킬)이 끝날 때 Base(0)층을 어디로 돌려보낼지. <see cref="PlayerActionPoseRules.ResolveExit"/> 가 고른다.</summary>
public enum PlayerActionExitPose
{
    None,   // 0층을 건드리지 않는다 — 다음 상태가 자기 애니를 튼다(사망 등)
    Idle,
    RunLoop // 이동 입력 중 정상 종료 — 하체를 달리기 루프로 바로 잇는다(IsMoving 도 함께 켠다)
}

/// <summary>
/// 행동 종료 때 0층 복귀 포즈를 고르는 순수 규칙(할 일 92). 상태 이름은 어쌔신·팔라딘·거너 컨트롤러 공통 이름이고,
/// 컨트롤러에 그 상태가 없으면(<see cref="Animator.HasState"/>) 호출부가 건너뛴다.
/// </summary>
public static class PlayerActionPoseRules
{
    public const int BaseLayer = 0;
    public const float ExitBlendSeconds = 0.05f;
    public static readonly int IdleHash = Animator.StringToHash("Idle");
    public static readonly int RunLoopHash = Animator.StringToHash("Run_Loop");

    /// <param name="nextPlaysOwnAnimation">다음 상태가 0층에 자기 애니를 따로 튼다 — 덮어쓰지 않는다.</param>
    /// <param name="returnsToLocomotion">정상 종료라 Idle/Move 로 돌아간다. 강제 종료(넉백·그랩 등)는 false — 이동 포즈를 고르지 않는다.</param>
    /// <param name="wantsMove">이 피어가 이동 입력을 쥐고 있고 이동할 수 있다(입력 주체만 참).</param>
    /// <param name="hasRunLoop">컨트롤러에 Run_Loop 상태가 있다.</param>
    public static PlayerActionExitPose ResolveExit(
        bool nextPlaysOwnAnimation, bool returnsToLocomotion, bool wantsMove, bool hasRunLoop)
    {
        if (nextPlaysOwnAnimation)
            return PlayerActionExitPose.None;
        if (returnsToLocomotion && wantsMove && hasRunLoop)
            return PlayerActionExitPose.RunLoop;
        return PlayerActionExitPose.Idle;
    }

    /// <summary>FSM 이 스킬 상태를 강제로 떠날 때의 종료 사유.</summary>
    public static SkillEndReason ForcedExitReason(PlayerActionState nextState) => nextState switch
    {
        PlayerActionState.Dead => SkillEndReason.CasterDied,
        PlayerActionState.Dash => SkillEndReason.DashCancelled,
        _ => SkillEndReason.Cancelled,
    };

    /// <summary>정상 종료(완료·홀드 해제·안전망)인가 — 이때만 Idle/Move 로 돌아간다.</summary>
    public static bool ReturnsToLocomotion(SkillEndReason reason) =>
        reason == SkillEndReason.Completed ||
        reason == SkillEndReason.Released ||
        reason == SkillEndReason.MaxDurationReached;

    /// <summary>
    /// 이 종료 사유의 다음 상태가 0층에 자기 애니를 트는가. 사망은 잔여 클립을 그대로 둔다(기존 동작).
    /// </summary>
    public static bool EndsIntoOwnAnimation(SkillEndReason reason) =>
        reason == SkillEndReason.CasterDied;
}
