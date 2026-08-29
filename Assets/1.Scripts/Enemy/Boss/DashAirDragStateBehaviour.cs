using UnityEngine;

/// <summary>
/// No.23의 DashAttack Animator 상태 수명에 <b>공기 저항 연출</b>(FX_Rage_Smash)을 맞춘다.
///
/// <see cref="DashTrailStateBehaviour"/>와 같은 자리에 나란히 붙는다. 궤적과 공기 저항을 한
/// Behaviour에 묶지 않는 이유는 둘이 서로 다른 소켓·다른 엔트리를 쓰고, 나중에 한쪽만
/// 끄거나 다른 상태(Rage)에 재사용할 수 있어야 하기 때문이다.
///
/// 순수 로컬 연출용이다. 각 피어가 자기 애니메이터로 같은 상태를 평가하므로 서버 가드나 RPC가 없다.
///
/// ⚠️ SMB는 <b>AnimatorController 애셋에 직렬화</b>된다 — 씬 오브젝트 참조를 담을 수 없고,
/// 컨트롤러를 여러 인스턴스가 공유한다. 그래서 필드를 두지 않고 콜백이 준
/// <paramref name="animator"/>에서 매번 런타임 인스턴스를 찾는다.
/// </summary>
public sealed class DashAirDragStateBehaviour : StateMachineBehaviour
{
    public override void OnStateEnter(
        Animator animator,
        AnimatorStateInfo stateInfo,
        int layerIndex)
    {
        ResolveAnimEvents(animator)?.DashAirDragStart();
    }

    public override void OnStateExit(
        Animator animator,
        AnimatorStateInfo stateInfo,
        int layerIndex)
    {
        ResolveAnimEvents(animator)?.DashAirDragEnd();
    }

    private static TwentyThreeAnimEvents ResolveAnimEvents(Animator animator)
    {
        return animator != null
            ? animator.GetComponentInParent<TwentyThreeAnimEvents>()
            : null;
    }
}
