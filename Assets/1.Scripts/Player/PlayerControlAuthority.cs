using BaseNetCode;
using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerControlAuthority : BaseNetworkBehaviour
{
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private bool controlEnabled = true;

    private bool CanUseLocalControl =>
        !IsNetworkActive || IsOwner;

    private void Awake()
    {
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
    }

    private void Start()
    {
        RefreshControlState();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RefreshControlState();
    }

    public override void OnNetworkDespawn()
    {
        SetLocalControl(false);
        base.OnNetworkDespawn();
    }

    private void RefreshControlState()
    {
        SetLocalControl(CanUseLocalControl);
    }

    private void SetLocalControl(bool isEnabled)
    {
        if (controlEnabled == isEnabled)
            return;

        controlEnabled = isEnabled;
        inputReader.SetInputEnabled(isEnabled);
        movement.enabled = isEnabled;
    }
}
