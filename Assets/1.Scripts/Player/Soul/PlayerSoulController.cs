using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerLifeCycleController))]
public sealed class PlayerSoulController : MonoBehaviour
{
    private const string SoulLayerName = "Soul";

    [Header("Life State")]
    [SerializeField] private PlayerLifeCycleController lifeCycle;
    [SerializeField] private PlayerGroundingSensor groundingSensor;

    [Header("Visual")]
    [SerializeField] private PlayableCharacterVisual characterVisual;
    [SerializeField] private CharacterDefinition characterDefinition;
    [Tooltip("생존 외형 루트. 비어 있으면 PlayableCharacterVisual 또는 Armature에서 찾습니다.")]
    [SerializeField] private GameObject aliveVisual;
    [Tooltip("Soul 외형의 부모. 비어 있으면 Player Root를 사용합니다.")]
    [SerializeField] private Transform soulVisualRoot;
    [Tooltip("SoulVisualPrefab이 없을 때 생존 외형에 임시로 적용할 머티리얼.")]
    [SerializeField] private Material fallbackSoulMaterial;
    [Tooltip("Soul 상태에서만 숨길 장비(무기·방패 등). Soul 진입 시 SetActive(false), 그 외 상태는 SetActive(true).")]
    [SerializeField] private GameObject[] soulHiddenEquipment;

    [Header("Combat Target")]
    [Tooltip("비어 있으면 Player Root 아래의 Hurtbox 컴포넌트에 연결된 Collider를 찾습니다.")]
    [SerializeField] private Collider[] hurtboxColliders;

    [Header("Movement")]
    [Tooltip("Soul 상태의 고정 이동속도. 향후 PlayerGameRuleData.soulSpeed 공급으로 교체합니다.")]
    [SerializeField, Min(0f)] private float soulMoveSpeed = 5f;

    private GameObject soulVisual;
    private Renderer[] aliveRenderers;
    private Material[][] originalAliveMaterials;
    private GameObject cachedMaterialVisual;
    private bool[] initialHurtboxEnabled;
    private int aliveLayer;
    private int soulLayer = -1;
    private bool warnedMissingSoulLayer;
    private bool warnedMissingSoulVisual;
    private bool warnedMissingAliveVisual;

    public GameObject SoulVisual => soulVisual;
    public bool IsSoulVisualReady => soulVisual != null;
    public float SoulMoveSpeed => Mathf.Max(0f, soulMoveSpeed);

    private void Awake()
    {
        ResolveReferences();
        aliveLayer = gameObject.layer;
        soulLayer = LayerMask.NameToLayer(SoulLayerName);
        CacheHurtboxDefaults();
        EnsureSoulVisual();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (lifeCycle != null)
            lifeCycle.LifeStateChanged += HandleLifeStateChanged;

        if (characterVisual != null)
            characterVisual.CharacterApplied += HandleCharacterApplied;
    }

    private void Start()
    {
        if (lifeCycle == null)
        {
            Debug.LogError("[SoulAlert] PlayerLifeCycleController is missing.", this);
            return;
        }

        ApplyLifeState(lifeCycle.State);
    }

    private void OnDisable()
    {
        if (lifeCycle != null)
            lifeCycle.LifeStateChanged -= HandleLifeStateChanged;

        if (characterVisual != null)
            characterVisual.CharacterApplied -= HandleCharacterApplied;
    }

    public void ApplyLifeState(PlayerLifeState state)
    {
        bool isAlive = state == PlayerLifeState.Alive;
        bool isSoul = state == PlayerLifeState.Soul;
        PlayerLifeGameplayAccess gameplayAccess =
            PlayerLifeGameplayAccess.FromState(state);

        EnsureSoulVisual();
        SetVisualState(isAlive, isSoul);
        SetEquipmentActive(!isSoul);
        SetRootLayer(isSoul);
        SetGroundingMode(isSoul);
        SetHurtboxesEnabled(gameplayAccess.ShouldEnableHurtbox);
    }

    public void SetCharacterDefinition(CharacterDefinition definition)
    {
        if (characterDefinition == definition)
            return;

        characterDefinition = definition;
        RecreateSoulVisual();

        if (lifeCycle != null)
            ApplyLifeState(lifeCycle.State);
    }

    /// <summary>
    /// 서버가 복제한 LifeState가 Soul일 때만 상태이상과 무관한 고정 이동속도를 제공한다.
    /// 실제 이동 적용은 기존 오너 이동 경로가 담당한다.
    /// </summary>
    public bool TryGetFixedMoveSpeed(out float moveSpeed)
    {
        if (lifeCycle == null)
            ResolveReferences();

        bool isSoul = lifeCycle != null &&
                      lifeCycle.State == PlayerLifeState.Soul;
        moveSpeed = isSoul ? SoulMoveSpeed : 0f;
        return isSoul;
    }

    private void HandleLifeStateChanged(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        ApplyLifeState(currentState);
    }

    private void HandleCharacterApplied(CharacterDefinition definition)
    {
        InvalidateAliveMaterialCache();
        SetCharacterDefinition(definition);
    }

    private void ResolveReferences()
    {
        if (lifeCycle == null)
            lifeCycle = GetComponent<PlayerLifeCycleController>();

        if (groundingSensor == null)
            groundingSensor = GetComponent<PlayerGroundingSensor>();

        if (characterVisual == null)
            characterVisual = GetComponent<PlayableCharacterVisual>();

        if (characterDefinition == null && characterVisual != null)
            characterDefinition = characterVisual.Definition;

        if (aliveVisual == null && characterVisual != null)
        {
            aliveVisual = characterVisual.CurrentVisual;
            if (aliveVisual == null &&
                characterVisual.VisualRoot != null &&
                characterVisual.VisualRoot != transform)
            {
                aliveVisual = characterVisual.VisualRoot.gameObject;
            }
        }

        if (aliveVisual == null)
        {
            Transform armature = transform.Find("Armature");
            if (armature != null)
                aliveVisual = armature.gameObject;
        }

        if (soulVisualRoot == null)
            soulVisualRoot = transform;
    }

    private void EnsureSoulVisual()
    {
        if (soulVisual != null)
            return;

        ResolveReferences();
        GameObject prefab = characterDefinition != null
            ? characterDefinition.SoulVisualPrefab
            : null;

        if (prefab == null)
        {
            WarnMissingSoulVisual();
            return;
        }

        if (prefab.GetComponentInChildren<Unity.Netcode.NetworkObject>(true) != null)
        {
            Debug.LogWarning(
                "[SoulAlert] SoulVisualPrefab must not contain a NetworkObject. " +
                "Soul state will continue without a Soul visual.",
                prefab);
            return;
        }

        soulVisual = Instantiate(prefab, soulVisualRoot);
        soulVisual.name = $"{prefab.name} (Soul Visual)";
        soulVisual.transform.localPosition = Vector3.zero;
        soulVisual.transform.localRotation = Quaternion.identity;
        soulVisual.transform.localScale = Vector3.one;
        soulVisual.SetActive(false);
    }

    private void RecreateSoulVisual()
    {
        if (soulVisual != null)
            Destroy(soulVisual);

        soulVisual = null;
        warnedMissingSoulVisual = false;
        EnsureSoulVisual();
    }

    private void SetVisualState(bool isAlive, bool isSoul)
    {
        ResolveReferences();
        bool useFallbackAliveVisual = isSoul && soulVisual == null;
        SetFallbackSoulMaterial(useFallbackAliveVisual);

        if (aliveVisual != null)
        {
            aliveVisual.SetActive(isAlive || useFallbackAliveVisual);
        }
        else if (!warnedMissingAliveVisual)
        {
            warnedMissingAliveVisual = true;
            Debug.LogWarning(
                "[SoulAlert] Alive visual reference is missing. " +
                "Life state, layer, grounding, and Hurtbox transitions will continue.",
                this);
        }

        if (soulVisual != null)
            soulVisual.SetActive(isSoul);
    }

    private void SetEquipmentActive(bool active)
    {
        if (soulHiddenEquipment == null)
            return;

        for (int i = 0; i < soulHiddenEquipment.Length; i++)
        {
            GameObject equipment = soulHiddenEquipment[i];
            if (equipment != null && equipment.activeSelf != active)
                equipment.SetActive(active);
        }
    }

    private void SetFallbackSoulMaterial(bool shouldUseFallback)
    {
        CacheAliveVisualMaterials();

        if (aliveRenderers == null || originalAliveMaterials == null)
            return;

        if (shouldUseFallback && fallbackSoulMaterial == null)
        {
            Debug.LogWarning(
                "[SoulAlert] SoulVisualPrefab과 임시 Soul Material이 모두 없습니다. " +
                "Alive 외형을 유지하지만 초록 표시를 적용할 수 없습니다.",
                this);
            return;
        }

        for (int i = 0; i < aliveRenderers.Length; i++)
        {
            Renderer aliveRenderer = aliveRenderers[i];
            if (aliveRenderer == null)
                continue;

            Material[] originalMaterials = originalAliveMaterials[i];
            if (!shouldUseFallback)
            {
                aliveRenderer.sharedMaterials = originalMaterials;
                continue;
            }

            Material[] fallbackMaterials = new Material[originalMaterials.Length];
            for (int materialIndex = 0;
                 materialIndex < fallbackMaterials.Length;
                 materialIndex++)
            {
                fallbackMaterials[materialIndex] = fallbackSoulMaterial;
            }

            aliveRenderer.sharedMaterials = fallbackMaterials;
        }
    }

    private void CacheAliveVisualMaterials()
    {
        if (aliveVisual == null ||
            (cachedMaterialVisual == aliveVisual &&
             aliveRenderers != null &&
             originalAliveMaterials != null))
        {
            return;
        }

        cachedMaterialVisual = aliveVisual;
        aliveRenderers = aliveVisual.GetComponentsInChildren<Renderer>(true);
        originalAliveMaterials = new Material[aliveRenderers.Length][];

        for (int i = 0; i < aliveRenderers.Length; i++)
            originalAliveMaterials[i] = aliveRenderers[i].sharedMaterials;
    }

    private void InvalidateAliveMaterialCache()
    {
        cachedMaterialVisual = null;
        aliveRenderers = null;
        originalAliveMaterials = null;
    }

    private void SetRootLayer(bool isSoul)
    {
        if (!isSoul)
        {
            gameObject.layer = aliveLayer;
            return;
        }

        if (soulLayer < 0)
        {
            if (!warnedMissingSoulLayer)
            {
                warnedMissingSoulLayer = true;
                Debug.LogWarning(
                    "[SoulAlert] Layer 'Soul' is not defined. " +
                    "Life state and other Soul transitions will continue.",
                    this);
            }

            return;
        }

        gameObject.layer = soulLayer;
    }

    private void SetGroundingMode(bool isSoul)
    {
        if (groundingSensor == null)
            return;

        groundingSensor.SetGroundingMode(
            isSoul
                ? PlayerGroundingSensor.GroundingMode.Soul
                : PlayerGroundingSensor.GroundingMode.Alive);
    }

    private void CacheHurtboxDefaults()
    {
        if (hurtboxColliders == null || hurtboxColliders.Length == 0)
        {
            Hurtbox[] hurtboxes = GetComponentsInChildren<Hurtbox>(true);
            hurtboxColliders = new Collider[hurtboxes.Length];

            for (int i = 0; i < hurtboxes.Length; i++)
                hurtboxColliders[i] = hurtboxes[i].GetComponent<Collider>();
        }

        initialHurtboxEnabled = new bool[hurtboxColliders.Length];
        for (int i = 0; i < hurtboxColliders.Length; i++)
        {
            Collider hurtbox = hurtboxColliders[i];
            initialHurtboxEnabled[i] = hurtbox != null && hurtbox.enabled;
        }
    }

    private void SetHurtboxesEnabled(bool shouldEnable)
    {
        if (hurtboxColliders == null || initialHurtboxEnabled == null)
            return;

        for (int i = 0; i < hurtboxColliders.Length; i++)
        {
            Collider hurtbox = hurtboxColliders[i];
            if (hurtbox != null)
                hurtbox.enabled = shouldEnable && initialHurtboxEnabled[i];
        }
    }

    private void WarnMissingSoulVisual()
    {
        if (warnedMissingSoulVisual)
            return;

        warnedMissingSoulVisual = true;
        Debug.LogWarning(
            "[SoulAlert] CharacterDefinition.SoulVisualPrefab is missing. " +
            "Life state, layer, grounding, and Hurtbox transitions will continue.",
            this);
    }

    private void OnValidate()
    {
        soulMoveSpeed = Mathf.Max(0f, soulMoveSpeed);
    }
}
