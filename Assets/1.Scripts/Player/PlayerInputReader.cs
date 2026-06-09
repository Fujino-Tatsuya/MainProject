using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerInputReader : MonoBehaviour
{
    private PlayerInput playerInput;
    private InputAction moveAction;
    private bool inputEnabled = true;

    public Vector2 Direction { get; private set; }
    public bool HasMoveInput => Direction.sqrMagnitude > 0.01f;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();

        moveAction = playerInput.actions["Move"];
    }

    public void SetInputEnabled(bool isEnabled)
    {
        inputEnabled = isEnabled;

        if (playerInput != null)
            playerInput.enabled = isEnabled;

        if (!inputEnabled)
            Direction = Vector2.zero;
    }

    private void Update()
    {
        if (!inputEnabled)
        {
            Direction = Vector2.zero;
            return;
        }

        Direction = moveAction.ReadValue<Vector2>();
    }

    private void OnDisable()
    {
        Direction = Vector2.zero;
    }
}
