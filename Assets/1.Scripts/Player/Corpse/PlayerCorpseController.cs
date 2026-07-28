using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 최종 전투 사망 시 Player NetworkObject 아래에 미리 조립된 시체를 활성화한다.
/// 위치 복제는 같은 NetworkObject에 속한 자식 NetworkTransform이 담당한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCorpseController : NetworkBehaviour
{
    private const string CorpseLayerName = "Corpse";
    private const float PlatformProbeOriginOffset = 0.05f;

    [Header("Life State")]
    [SerializeField] private PlayerLifeCycleController lifeCycle;
    [SerializeField] private Transform playerRoot;

    [Header("Prebuilt Corpse")]
    [Tooltip("상태와 무관하게 활성인 시체 물리 Root. Controller와 자식 NetworkTransform이 붙은 현재 Transform을 권장합니다.")]
    [SerializeField] private Transform corpseBody;
    [Tooltip("시체일 때만 표시할 미리 생성된 Visual Root.")]
    [SerializeField] private GameObject corpseVisual;
    [Tooltip("시체 전용 Collider. 서버에서만 활성화합니다.")]
    [SerializeField] private Collider corpseCollider;
    [Tooltip("시체 전용 Rigidbody. 서버만 Dynamic으로 전환합니다.")]
    [SerializeField] private Rigidbody corpseRigidbody;

    [Header("Moving Platform")]
    [SerializeField] private LayerMask platformRiderMask = ~0;
    [SerializeField, Min(0.01f)] private float platformProbeDistance = 0.35f;

    [Header("Fall Boundary Placeholder")]
    [Tooltip("FallBoundary(W10) 연결 전 임시 월드 Y 임계값입니다.")]
    [SerializeField] private float fallBoundaryY = -30f;

    private readonly NetworkVariable<bool> corpseVisible =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private bool permanentlyRemoved;
    private bool warnedMissingCorpseLayer;

    public bool IsCorpseVisible => corpseVisible.Value;
    public bool IsPermanentlyRemoved => permanentlyRemoved;
    public float FallBoundaryY => fallBoundaryY;

    private void Awake()
    {
        ResolveReferences();
        ConfigureLayer();
        ApplyPresentation(false);
        ConfigurePhysics(false);
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (lifeCycle != null)
            lifeCycle.LifeStateChanged += HandleLifeStateChanged;
    }

    private void OnDisable()
    {
        if (lifeCycle != null)
            lifeCycle.LifeStateChanged -= HandleLifeStateChanged;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        permanentlyRemoved = false;
        corpseVisible.OnValueChanged += HandleCorpseVisibilityChanged;
        ApplyPresentation(corpseVisible.Value);
        ConfigurePhysics(IsServer && corpseVisible.Value);

        if (IsServer &&
            lifeCycle != null &&
            lifeCycle.State == PlayerLifeState.PermanentDead)
        {
            ResolvePermanentDeath(lifeCycle.LastDeathCause);
        }
    }

    public override void OnNetworkDespawn()
    {
        corpseVisible.OnValueChanged -= HandleCorpseVisibilityChanged;
        ApplyPresentation(false);
        ConfigurePhysics(false);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || !corpseVisible.Value)
            return;

        if (corpseBody.position.y < fallBoundaryY)
        {
            RemoveCorpsePermanently();
            return;
        }

        ApplyMovingPlatformDelta();
    }

    /// <summary>
    /// 서버가 최종 사망 원인에 따라 시체 유지 여부를 확정한다.
    /// FallDeathContext 통합 뒤에도 이 seam은 그대로 사용할 수 있다.
    /// </summary>
    public bool ResolvePermanentDeath(PlayerDeathCause deathCause)
    {
        if (!IsSpawned || !IsServer || permanentlyRemoved ||
            lifeCycle == null ||
            lifeCycle.State != PlayerLifeState.PermanentDead)
        {
            return false;
        }

        bool shouldShowCorpse = deathCause == PlayerDeathCause.Combat;
        if (shouldShowCorpse)
            PlaceCorpseAtPlayer();

        corpseVisible.Value = shouldShowCorpse;
        ApplyPresentation(shouldShowCorpse);
        ConfigurePhysics(shouldShowCorpse);
        return true;
    }

    /// <summary>FallBoundary 아래로 내려간 시체를 현재 스폰 수명 동안 영구 제거한다.</summary>
    public bool RemoveCorpsePermanently()
    {
        if (!IsSpawned || !IsServer || permanentlyRemoved)
            return false;

        permanentlyRemoved = true;
        corpseVisible.Value = false;
        ApplyPresentation(false);
        ConfigurePhysics(false);
        return true;
    }

    private void HandleLifeStateChanged(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        if (!IsSpawned || !IsServer ||
            currentState != PlayerLifeState.PermanentDead)
        {
            return;
        }

        ResolvePermanentDeath(lifeCycle.LastDeathCause);
    }

    private void HandleCorpseVisibilityChanged(bool previous, bool current)
    {
        ApplyPresentation(current);
        ConfigurePhysics(IsServer && current);
    }

    private void ResolveReferences()
    {
        if (lifeCycle == null)
            lifeCycle = GetComponentInParent<PlayerLifeCycleController>(true);

        if (playerRoot == null && lifeCycle != null)
            playerRoot = lifeCycle.transform;

        if (corpseBody == null)
            corpseBody = transform;

        if (corpseRigidbody == null)
            corpseRigidbody = GetComponent<Rigidbody>();

        if (corpseCollider == null)
            corpseCollider = GetComponent<Collider>();
    }

    private void ConfigureLayer()
    {
        int corpseLayer = LayerMask.NameToLayer(CorpseLayerName);
        if (corpseLayer >= 0)
        {
            SetLayerRecursively(corpseBody.gameObject, corpseLayer);
            return;
        }

        if (warnedMissingCorpseLayer)
            return;

        warnedMissingCorpseLayer = true;
        Debug.LogWarning(
            "[CorpseAlert] Corpse Layer가 없습니다. " +
            "Project Settings > Tags and Layers와 Collision Matrix를 설정하세요.",
            this);
    }

    private void PlaceCorpseAtPlayer()
    {
        if (corpseBody == null || playerRoot == null)
            return;

        corpseBody.SetPositionAndRotation(
            playerRoot.position,
            playerRoot.rotation);

        if (corpseRigidbody != null)
        {
            corpseRigidbody.linearVelocity = Vector3.zero;
            corpseRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void ApplyPresentation(bool visible)
    {
        if (corpseVisual != null)
            corpseVisual.SetActive(visible);
    }

    private void ConfigurePhysics(bool simulateOnServer)
    {
        if (corpseCollider != null)
            corpseCollider.enabled = simulateOnServer;

        if (corpseRigidbody == null)
            return;

        corpseRigidbody.linearVelocity = Vector3.zero;
        corpseRigidbody.angularVelocity = Vector3.zero;
        corpseRigidbody.detectCollisions = simulateOnServer;
        corpseRigidbody.useGravity = simulateOnServer;
        corpseRigidbody.isKinematic = !simulateOnServer;
        corpseRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void ApplyMovingPlatformDelta()
    {
        if (corpseRigidbody == null || corpseCollider == null)
            return;

        Bounds bounds = corpseCollider.bounds;
        Vector3 origin =
            bounds.center + Vector3.up * PlatformProbeOriginOffset;
        float distance = bounds.extents.y + platformProbeDistance;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            distance,
            platformRiderMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            MovingPlatform platform =
                hits[i].collider.GetComponentInParent<MovingPlatform>();
            if (platform == null)
                continue;

            corpseRigidbody.position += platform.CurrentDelta;
            return;
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;

        Transform rootTransform = root.transform;
        for (int i = 0; i < rootTransform.childCount; i++)
            SetLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
    }
}
