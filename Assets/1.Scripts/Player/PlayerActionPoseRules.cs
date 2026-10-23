using UnityEngine;

/// <summary>행동(스킬)이 끝날 때 Base(0)층을 어디로 돌려보낼지. <see cref="PlayerActionPoseRules.ResolveExit"/> 가 고른다.</summary>
public enum PlayerActionExitPose
{
    None,       // 0층을 건드리지 않는다 — 다음 상태가 자기 애니를 튼다(사망 등), 또는 상체 전용 스킬이라 하체가 그대로 이어진다
    KeepMoving, // 0층 CrossFade 없이 IsMoving 만 되살린다 — 상체 전용 스킬이 이동 중 정상 종료(하체 Run_Loop 유지)
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

    /// <param name="upperBodyOnly">상체 레이어에서만 재생된 행동 — 시작·종료 모두 0층(하체)을 건드리지 않는다.</param>
    /// <param name="nextPlaysOwnAnimation">다음 상태가 0층에 자기 애니를 따로 튼다 — 덮어쓰지 않는다.</param>
    /// <param name="returnsToLocomotion">정상 종료라 Idle/Move 로 돌아간다. 강제 종료(넉백·그랩 등)는 false — 이동 포즈를 고르지 않는다.</param>
    /// <param name="wantsMove">이 피어가 이동 입력을 쥐고 있고 이동할 수 있다(입력 주체만 참).</param>
    /// <param name="hasRunLoop">컨트롤러에 Run_Loop 상태가 있다.</param>
    public static PlayerActionExitPose ResolveExit(
        bool upperBodyOnly, bool nextPlaysOwnAnimation, bool returnsToLocomotion, bool wantsMove, bool hasRunLoop)
    {
        if (nextPlaysOwnAnimation)
            return PlayerActionExitPose.None;
        if (upperBodyOnly)
            return returnsToLocomotion && wantsMove ? PlayerActionExitPose.KeepMoving : PlayerActionExitPose.None;
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

    /// <summary>
    /// 스킬 시작 때 IsMoving 값. 0층 스킬은 끈다(기존). 상체 전용이면서 이동 허용 스킬은 이동 입력 중이면 켠 채로 둔다 — 하체 Run_Loop 유지.
    /// </summary>
    public static bool MovingOnSkillStart(bool upperBodyOnly, bool canMoveWhileActive, bool wantsMove) =>
        upperBodyOnly && canMoveWhileActive && wantsMove;

    /// <summary>상체 전용 = 상태가 0층에 없고 다른 층(상체 마스크 레이어)에만 있다.</summary>
    public static bool IsUpperBodyOnly(bool inBaseLayer, bool inOtherLayer) => !inBaseLayer && inOtherLayer;

    /// <summary>
    /// 이 상태가 상체 전용인가 — 스킬 SO 의 상태 이름이 컨트롤러 어느 층에 있는지로 판정한다(이름 하드코딩 없음).
    /// 컨트롤러가 없거나 상태가 어느 층에도 없으면 false(기존 0층 처리).
    /// </summary>
    public static bool IsUpperBodyOnlyState(Animator animator, int stateHash)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;

        bool inOther = false;
        for (int layer = BaseLayer + 1; layer < animator.layerCount && !inOther; layer++)
            inOther = animator.HasState(layer, stateHash);

        return IsUpperBodyOnly(animator.HasState(BaseLayer, stateHash), inOther);
    }
}
