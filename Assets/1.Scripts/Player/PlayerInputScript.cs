using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputScript : MonoBehaviour
{
    private CharacterController controller;
    private PlayerInput playerInput;
    private InputAction moveAction;

    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private float viewYaw = -45f;

    private void Awake()
    {
        Init();
    }

    private void Update()
    {
        Move();
    }


    private void Init()
    {
        playerInput = GetComponent<PlayerInput>();

        if (playerInput != null)
            moveAction = playerInput.actions["Move"];

        if (controller == null)
            controller = GetComponent<CharacterController>();
    }

    private void Move()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 inputDir = new Vector3(input.x, 0f, input.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);

        Vector3 moveDir = Quaternion.Euler(0f, viewYaw, 0f) * inputDir;

        controller.Move(moveDir * moveSpeed * Time.deltaTime);
    }
}
