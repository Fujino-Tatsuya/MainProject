using UnityEngine;

public class PlayerRootMotionRelay : MonoBehaviour
{
    private Animator animator;
    private DefaultAttackController defaultAttack;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        defaultAttack = GetComponentInParent<DefaultAttackController>();
    }

    private void OnAnimatorMove()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (defaultAttack == null)
            defaultAttack = GetComponentInParent<DefaultAttackController>();

        if (animator == null || defaultAttack == null)
            return;

        defaultAttack.HandleAnimatorMove(animator.deltaPosition, animator.transform.forward);
    }
}
