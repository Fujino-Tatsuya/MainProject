using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInputReader))]
public class PlayerMovement : MonoBehaviour
{
    PlayerInputReader reader;
    Rigidbody rb;

    private Vector2 prevDir_for_Rotate;
    [SerializeField] private float rotate_Speed;
    private bool hasRotate;

    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float midSpeed = 3f;
    [SerializeField] private float acceleration = 80f;
    [SerializeField] private float alignThreshold = 0.98f;
    [SerializeField] private float viewYaw = -45f;

    private float currentSpeed;

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        rotate_Speed = 10.0f;
        prevDir_for_Rotate = new Vector2(0,-1);
        hasRotate = true;
    }

    private void FixedUpdate()
    {
        Move();
        Rotate();
    }

    /// <summary>
    /// 기존의 정면과 같은 방향으로 이동한다면 즉시 최고속도.
    /// 정면과 다른 방향으로 이동한다면 중간 -> 최고 속도로 가속.
    /// forward 와 input direction 을 내적하여 감별.
    /// </summary>
    void Move()
    {
        if (!reader.HasMoveInput)
        {
            currentSpeed = 0f;
            return;
        }

        Vector2 input = reader.Direction;

        Vector3 localDir = new Vector3(input.x, 0f, input.y);
        Vector3 worldDir = Quaternion.Euler(0f, viewYaw, 0f) * localDir;
        worldDir.Normalize();

        float dot = Vector3.Dot(worldDir, transform.forward);

        if (dot >= alignThreshold)
        {
            currentSpeed = maxSpeed;
        }
        else
        {
            if (currentSpeed > midSpeed)
                currentSpeed = midSpeed;

            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                maxSpeed,
                acceleration * Time.fixedDeltaTime
            );
        }

        rb.MovePosition(
            rb.position + worldDir * currentSpeed * Time.fixedDeltaTime
        );
    }

    /// <summary>
    /// 마지막 입력 방향에 도달 할 때 까지 반복하는 회전 함수
    /// </summary>
    void Rotate()
    {
        if (reader.HasMoveInput)
        {
            prevDir_for_Rotate = reader.Direction;
            hasRotate = true;
        }

        if (!hasRotate)
            return;

        Vector3 dir = new Vector3(prevDir_for_Rotate.x, 0, prevDir_for_Rotate.y);
        dir = Quaternion.Euler(0f, viewYaw, 0f) * dir;

        Quaternion targetRotation = Quaternion.LookRotation(dir);

        if (Vector3.Dot(dir, transform.forward) > 0.999f)
        {
            rb.MoveRotation(targetRotation);
            hasRotate = false;
            return;
        }

        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotate_Speed * Time.fixedDeltaTime));
    }

    /// <summary>
    /// Rotate like a robot, If you use this function, you need to increase the rotation speed.
    /// </summary>
    void RoatateToward()
    {
        if (reader.HasMoveInput)
            prevDir_for_Rotate = reader.Direction;

        Vector3 dir = new Vector3(prevDir_for_Rotate.x, 0, prevDir_for_Rotate.y);
        dir = Quaternion.Euler(0f, viewYaw, 0f) * dir;

        Quaternion targetRotation = Quaternion.LookRotation(dir);

        if (Vector3.Dot(dir, transform.forward) > 0.999f)
        {
            rb.rotation = targetRotation;
            return;
        }

        rb.rotation = Quaternion.RotateTowards(rb.rotation, targetRotation, rotate_Speed * Time.deltaTime);
    }
}
