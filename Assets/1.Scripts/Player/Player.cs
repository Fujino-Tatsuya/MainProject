using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttackController))]
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
    private DefaultAttackController defaultAttack;

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

        defaultAttack = GetComponent<DefaultAttackController>();

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

    public void HandleDefaultAttackEvent(DefaultAttackAnimationEventType eventType)
    {
        defaultAttack.HandleAnimationEvent(eventType);
    }

    public void EndInterrupt()
    {
        stateController.EndInterrupt();
    }

    public bool SetState(PlayerActionState state)
    {
        return stateController.ChangeState(state);
    }

    protected override void OnKnockback(Vector3 direction, float strength)
    {
        // 서버가 거부(사망/슈퍼아머)하면 오너에게도 전파하지 않는다
        bool accepted = stateController.BeginKnockback(direction, strength);
        if (!accepted)
            return;

        ApplyKnockbackClientRpc(direction, strength, CreateOwnerClientRpcParams());
    }

    public bool BeginGrabbedByInstigator(GameObject instigator)
    {
        if (!IsServer)
            return false;

        NetworkObject instigatorNetworkObject =
            instigator != null ? instigator.GetComponentInParent<NetworkObject>() : null;
        if (instigatorNetworkObject == null)
        {
            Debug.LogError("Grab instigator must belong to a spawned NetworkObject.", this);
            return false;
        }

        if (!stateController.BeginGrabbed(instigator))
            return false;

        BeginGrabbedClientRpc(new NetworkObjectReference(instigatorNetworkObject), CreateOwnerClientRpcParams());
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
    private void BeginGrabbedClientRpc(
        NetworkObjectReference instigatorReference,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        if (!instigatorReference.TryGet(out NetworkObject instigatorNetworkObject))
        {
            Debug.LogError("Grab instigator NetworkObject could not be resolved on the owner.", this);
            return;
        }

        stateController.ApplyGrabbedFromServer(instigatorNetworkObject.gameObject);
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
        // 호스트는 서버 경로의 BeginKnockback으로 이미 상태 진입 — 재진입 시 임펄스가 이중 적용됨
        if (!IsOwner || IsServer)
            return;

        stateController.ApplyKnockbackFromServer(direction, strength);
    }

    /// <summary>이동은 오너 권위(networking.md) — 넉백 물리를 시뮬레이션할 피어인지 여부.</summary>
    public bool IsMovementAuthority => !IsNetworkActive || IsOwner;

    public void NotifyKnockbackEnded()
    {
        if (!IsNetworkActive || IsServer)
            return;

        NotifyKnockbackEndedServerRpc();
    }

    [ServerRpc] // RequireOwnership 기본값 true — 오너만 호출 가능
    private void NotifyKnockbackEndedServerRpc()
    {
        stateController.EndKnockback();
    }

    public void SetAnimatorMoving(bool isMoving)
    {
        if (animator != null)
            animator.SetBool(IsMovingHash, isMoving);
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);
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
