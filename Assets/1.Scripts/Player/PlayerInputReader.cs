using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerInputReader : MonoBehaviour
{
    private PlayerInput Input;
    private InputAction MoveAction;

    public Vector2 Direction { get; private set; }
    public bool HasMoveInput => Direction.sqrMagnitude > 0.01f;

    private void Awake()
    {
        Input = GetComponent<PlayerInput>();

        MoveAction = Input.actions["Move"];
    }

    private void FixedUpdate()
    {
        Direction = MoveAction.ReadValue<Vector2>();
    }
}
