using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttack))]
[RequireComponent(typeof(PlayerStateController))]
[RequireComponent(typeof(StatusEffectController))]
public class Player : Unit
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    [SerializeField] private Animator animator;
    [SerializeField] private float interruptDuration = 0.5f;
    [SerializeField] private float interruptForwardDistance = 0.5f;

    [Header("\n초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;
    [SerializeField] int maxShield;

    private PlayerStateController stateController;
    private DefaultAttack defaultAttack;

    public PlayerActionState CurrentState => stateController != null ? stateController.CurrentState : PlayerActionState.Idle;
    public bool CanMove => stateController == null || stateController.CanMove;
    public bool CanMovementRotate => stateController == null || stateController.CanMovementRotate;
    public float InterruptDuration => interruptDuration;
    public float InterruptForwardDistance => interruptForwardDistance;

    private void Awake()
    {
        if (GetComponent<StatusEffectController>() == null)
            gameObject.AddComponent<StatusEffectController>();

        stateController = GetComponent<PlayerStateController>();
        if (stateController == null)
            stateController = gameObject.AddComponent<PlayerStateController>();

        defaultAttack = GetComponent<DefaultAttack>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 내가 Owner인 플레이어가 스폰되면, 카메라 매니저에게 나를 따라오라고 알린다.
        if (IsOwner)
            CameraTargetSwitcher.Active?.FocusOwnerPlayer();

        if (IsServer)
            Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense, maxShield);
    }

    private void Update()
    {
        if (IsNetworkActive &&
            !stateController.ShouldTickForNetwork(IsOwner, HasStateAuthority))
        {
            return;
        }

        stateController.Tick();
    }

    public void EndDefaultAttack()
    {
        defaultAttack.EndCurrentAttack();
    }

    public void HitDefaultAttack()
    {
        defaultAttack.HitCurrentAttack();
    }

    public void EndInterrupt()
    {
        stateController.EndInterrupt();
    }

    public bool SetState(PlayerActionState state)
    {
        return stateController.ChangeState(state);
    }

    public override void Knockback(Vector3 direction, float strength)
    {
        if (!IsServer)
            return;

        //stateController.BeginKnockback(direction, strength);
        ApplyKnockbackClientRpc(direction, strength, CreateOwnerClientRpcParams());
    }

    public bool BeginGrabbedByInstigator(GameObject instigator)
    {
        if (!IsServer)
            return false;

        if (!stateController.BeginGrabbed(instigator))
            return false;

        BeginGrabbedClientRpc(CreateOwnerClientRpcParams());
        return true;
    }

    public bool EndGrabbedByInstigator()
    {
        if (!IsServer)
            return false;

        bool ended = stateController.EndGrabbed();
        EndGrabbedClientRpc(CreateOwnerClientRpcParams());
        return ended;
    }

    [ClientRpc]
    private void BeginGrabbedClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        stateController.ApplyGrabbedFromServer();
    }

    [ClientRpc]
    private void EndGrabbedClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        stateController.EndGrabbed();
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 direction, float strength, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        stateController.ApplyKnockbackFromServer(direction, strength);
    }

    public void SetAnimatorMoving(bool isMoving)
    {
        if (animator != null)
            animator.SetBool(IsMovingHash, isMoving);
    }

    public override void TakeDamage(int damage)
    {
        base.TakeDamage(damage);
    }

    private ClientRpcParams CreateOwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }
}
