using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Rigidbody rb;

    [SerializeField] private Transform armature;
    [SerializeField] private float rotate_Speed = 10f;
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float midSpeed = 3f;
    [SerializeField] private float acceleration = 80f;
    [SerializeField] private float alignThreshold = 0.98f;
    [SerializeField] private float viewYaw = -45f;

    private Vector2 prevDir_for_Rotate = new Vector2(0f, -1f);
    private bool hasRotate = true;
    private float currentSpeed;

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        rb = GetComponent<Rigidbody>();

        if (armature == null)
            armature = transform.Find("Armature");
    }

    private void Update()
    {
        Move();
        Rotate();
    }

    private void Move()
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

        Vector3 forward = armature != null ? armature.forward : transform.forward;
        float dot = Vector3.Dot(worldDir, forward);

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
                acceleration * Time.deltaTime
            );
        }

        rb.MovePosition(
            rb.position + worldDir * currentSpeed * Time.deltaTime
        );
    }

    private void Rotate()
    {
        if (armature == null)
            return;

        if (reader.HasMoveInput)
        {
            prevDir_for_Rotate = reader.Direction;
            hasRotate = true;
        }

        if (!hasRotate)
            return;

        Vector3 dir = new Vector3(prevDir_for_Rotate.x, 0f, prevDir_for_Rotate.y);
        dir = Quaternion.Euler(0f, viewYaw, 0f) * dir;

        Quaternion targetRotation = Quaternion.LookRotation(dir);

        if (Vector3.Dot(dir, armature.forward) > 0.999f)
        {
            armature.rotation = targetRotation;
            hasRotate = false;
            return;
        }

        armature.rotation = Quaternion.Slerp(
            armature.rotation,
            targetRotation,
            rotate_Speed * Time.deltaTime
        );
    }
}
