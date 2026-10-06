using UnityEngine;

/// <summary>
/// 공격 시작 또는 복제된 피격 이후 일정 시간 동안 어쌔신의 전투 대기 자세를 켠다.
/// 네트워크 상태가 아니라 각 피어가 이미 받은 공격 연출/체력 복제 신호로 계산하는 로컬 표현이다.
/// </summary>
[RequireComponent(typeof(Player))]
public sealed class AssassinCombatIdle : MonoBehaviour
{
    private static readonly int IsCombatIdleHash = Animator.StringToHash("IsCombatIdle");

    [SerializeField] private AssassinBasicAttackData data;
    [SerializeField] private Animator animator;

    private Player player;
    private float lastCombatTime = float.NegativeInfinity;
    private bool applied;
    private bool appliedValue;

    private void Awake()
    {
        player = GetComponent<Player>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        if (player == null)
            player = GetComponent<Player>();
        if (player != null)
            player.ClientDamaged += NotifyDamaged;
    }

    private void OnDisable()
    {
        if (player != null)
            player.ClientDamaged -= NotifyDamaged;
    }

    private void Update()
    {
        float duration = data != null ? data.CombatIdleSeconds : 5f;
        bool inCombat = Time.time - lastCombatTime <= duration;
        if (applied && appliedValue == inCombat)
            return;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator == null)
            return;

        animator.SetBool(IsCombatIdleHash, inCombat);
        applied = true;
        appliedValue = inCombat;
    }

    public void NotifyAttackStarted()
    {
        lastCombatTime = Time.time;
    }

    private void NotifyDamaged()
    {
        lastCombatTime = Time.time;
    }

    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator == null)
            return;

        animator = newAnimator;
        applied = false;
    }
}
