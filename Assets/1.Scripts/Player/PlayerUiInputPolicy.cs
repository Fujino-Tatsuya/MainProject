using UnityEngine;

/// <summary>
/// 전역 UI 입력 차단 상태(<see cref="UiInputGateManager"/> — 결과 패널 등 <see cref="UiModalBlocker"/>)를
/// 이 클라이언트의 로컬 Player 입력에만 차단 토큰으로 적용한다.
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

        if (player == null || !player.IsInputSource || inputReader == null)
            return;

        // 입력 차단 토큰(할 일 48 C9) — 이 정책 자신이 토큰이다. 다른 요청자의 토큰·생명주기 정책과 AND 로 합쳐진다.
        if (blocked)
            inputReader.AcquireInputBlock(this);
        else
            inputReader.ReleaseInputBlock(this);
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = GetComponent<Player>();

        if (inputReader == null)
            inputReader = GetComponent<PlayerInputReader>();
    }
}
