using UnityEngine;

/// <summary>
/// 전역 UI 입력 차단 상태를 이 클라이언트의 로컬 Player 입력에만 적용한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerUiInputPolicy : MonoBehaviour
{
    [SerializeField] private Player player;
    [SerializeField] private PlayerInputReader inputReader;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        UiInputGateManager.BlockedChanged += HandleBlockedChanged;
        ApplyCurrentState();
    }

    private void Start()
    {
        ApplyCurrentState();
    }

    private void OnDisable()
    {
        UiInputGateManager.BlockedChanged -= HandleBlockedChanged;
    }

    private void HandleBlockedChanged(bool blocked)
    {
        ApplyBlockedState(blocked);
    }

    private void ApplyCurrentState()
    {
        ApplyBlockedState(UiInputGateManager.IsInputBlocked);
    }

    private void ApplyBlockedState(bool blocked)
    {
        if (player == null || inputReader == null)
            ResolveReferences();

        if (player == null || !player.IsMovementAuthority || inputReader == null)
            return;

        inputReader.SetUiInputSuppressed(blocked);
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = GetComponent<Player>();

        if (inputReader == null)
            inputReader = GetComponent<PlayerInputReader>();
    }
}
