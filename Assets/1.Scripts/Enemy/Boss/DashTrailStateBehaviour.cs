using UnityEngine;

/// <summary>
/// No.23의 DashAttack Animator 상태 수명에 대시 궤적 연출을 맞춘다.
///
/// 순수 로컬 연출용이다. NetworkAnimator로 같은 상태를 평가하는 각 피어가 자기 화면에서
/// EffectSocketPlayer를 재생하므로 서버 가드나 RPC를 추가하지 않는다.
/// </summary>
public sealed class DashTrailStateBehaviour : StateMachineBehaviour
{
    public override void OnStateEnter(
        Animator animator,
        AnimatorStateInfo stateInfo,
        int layerIndex)
    {
        ResolveAnimEvents(animator)?.DashAttackTrailStart();
    }

    public override void OnStateExit(
        Animator animator,
        AnimatorStateInfo stateInfo,
        int layerIndex)
    {
        ResolveAnimEvents(animator)?.DashAttackTrailEnd();
    }

    private static TwentyThreeAnimEvents ResolveAnimEvents(Animator animator)
    {
        return animator != null
            ? animator.GetComponentInParent<TwentyThreeAnimEvents>()
            : null;
    }
}
