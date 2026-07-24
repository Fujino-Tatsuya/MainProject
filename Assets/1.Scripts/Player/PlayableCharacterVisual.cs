using System;
using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(DefaultAttackController))]
public class PlayableCharacterVisual : MonoBehaviour
{
    [SerializeField] private CharacterDefinition initialCharacter;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private bool applyInitialCharacterOnAwake = true;

    private GameObject currentVisual;
    private PlayerMovement movement;
    private DefaultAttackController defaultAttack;

    public CharacterDefinition CurrentDefinition { get; private set; }
    public CharacterDefinition Definition =>
        CurrentDefinition != null ? CurrentDefinition : initialCharacter;
    public GameObject CurrentVisual => currentVisual;
    public Transform VisualRoot => visualRoot;

    public event Action<CharacterDefinition> CharacterApplied;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        defaultAttack = GetComponent<DefaultAttackController>();

        if (visualRoot == null)
            visualRoot = transform.Find("Armature") ?? transform;

        if (applyInitialCharacterOnAwake && initialCharacter != null)
            ApplyCharacter(initialCharacter);
        else
            BindExistingVisual();
    }

    public void ApplyCharacter(CharacterDefinition definition)
    {
        if (definition == null)
            return;

        CurrentDefinition = definition;

        if (definition.VisualPrefab != null)
            ReplaceVisual(definition.VisualPrefab);

        Animator animator = ResolveAnimator();
        if (animator != null && definition.AnimatorController != null)
            animator.runtimeAnimatorController = definition.AnimatorController;

        BindVisual(animator);

        if (definition.DefaultAttackData != null)
            defaultAttack.ApplyData(definition.DefaultAttackData);

        CharacterApplied?.Invoke(definition);
    }

    public void ReplaceVisual(GameObject visualPrefab)
    {
        if (visualPrefab == null)
            return;

        ClearVisualRoot();
        currentVisual = Instantiate(visualPrefab, visualRoot);
        currentVisual.transform.localPosition = Vector3.zero;
        currentVisual.transform.localRotation = Quaternion.identity;
        currentVisual.transform.localScale = Vector3.one;
    }

    private void BindExistingVisual()
    {
        BindVisual(ResolveAnimator());
    }

    private void BindVisual(Animator animator)
    {
        if (animator == null)
            return;

        if (!animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (!animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();

        defaultAttack.SetAnimator(animator);
        movement.SetArmature(animator.transform);
    }

    private Animator ResolveAnimator()
    {
        if (currentVisual != null)
        {
            Animator visualAnimator = currentVisual.GetComponentInChildren<Animator>();
            if (visualAnimator != null)
                return visualAnimator;
        }

        return visualRoot != null
            ? visualRoot.GetComponentInChildren<Animator>()
            : GetComponentInChildren<Animator>();
    }

    private void ClearVisualRoot()
    {
        if (visualRoot == null)
            return;

        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualRoot.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}
