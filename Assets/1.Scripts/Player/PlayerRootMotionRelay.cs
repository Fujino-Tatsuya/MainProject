using UnityEngine;

public class PlayerRootMotionRelay : MonoBehaviour
{
    private Animator animator;
    private IPlayerBasicAttack defaultAttack;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        defaultAttack = GetComponentInParent<IPlayerBasicAttack>();
    }

    private void OnAnimatorMove()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (defaultAttack == null)
            defaultAttack = GetComponentInParent<IPlayerBasicAttack>();

        if (animator == null || defaultAttack == null)
            return;

        defaultAttack.HandleAnimatorMove(animator.deltaPosition, animator.transform.forward);
    }
}
