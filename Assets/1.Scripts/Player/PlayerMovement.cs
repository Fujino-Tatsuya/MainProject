using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Player player;
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

    // 이동 플랫폼 캐리: 이번 프레임 외부 이동량(플랫폼). Move()에서 입력 이동과 합산 후 리셋.
    private Vector3 _carryDelta;

    /// <summary>이동 플랫폼 등 외부 이동량을 이번 프레임 이동에 가산한다(소유자측에서 호출).</summary>
    public void AddCarryDelta(Vector3 delta)
    {
        _carryDelta += delta;
    }

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        player = GetComponent<Player>();
        rb = GetComponent<Rigidbody>();

        if (armature == null)
            armature = transform.Find("Armature");
    }

    private void Start()
    {
        rotate_Speed = 10f;
    }

    private void Update()
    {
        Move();
        Rotate();
    }

    private void Move()
    {
        Vector3 inputMove = Vector3.zero;

        bool canMove = player == null || player.CanMove;
        if (canMove && reader.HasMoveInput)
        {
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

            // 상태이상 이속 modifier 반영 (버프 > 1, 둔화 < 1)
            float statusMultiplier = player != null && player.StatusEffects != null
                ? player.StatusEffects.GetStatMultiplier(StatusEffectType.MoveSpeedModifier)
                : 1f;

            inputMove = worldDir * currentSpeed * statusMultiplier * Time.deltaTime;
        }
        else
        {
            currentSpeed = 0f;
        }

        // 입력 이동 + 플랫폼 캐리를 단일 MovePosition으로 적용.
        // (MovePosition을 프레임당 두 번 호출하면 뒤엣것이 덮어쓰므로 반드시 합산.)
        // 캐리는 CanMove/입력과 무관하게 적용 → 스턴/사망 중에도 플랫폼에 실려 이동(시체 잔류).
        Vector3 total = inputMove + _carryDelta;
        _carryDelta = Vector3.zero;

        if (total.sqrMagnitude > 0f)
        {
            rb.MovePosition(rb.position + total);
        }
    }

    private void Rotate()
    {
        if (player != null && !player.CanMovementRotate)
            return;

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

    public void RotateImmediately(Vector3 direction)
    {
        if (armature == null)
            return;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        armature.rotation = Quaternion.LookRotation(direction.normalized);
        hasRotate = false;
    }

    public void RotateToward(Vector3 direction, float speed)
    {
        if (armature == null)
            return;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        armature.rotation = Quaternion.Slerp(
            armature.rotation,
            targetRotation,
            speed * Time.deltaTime
        );
    }

    public void MoveRoot(Vector3 deltaPosition)
    {
        rb.MovePosition(rb.position + deltaPosition);
    }

    public void SetArmature(Transform newArmature)
    {
        if (newArmature == null)
            return;

        armature = newArmature;
    }
}
