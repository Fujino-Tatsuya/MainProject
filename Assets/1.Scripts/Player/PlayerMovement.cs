using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Player player;
    private PlayerSoulController soulController;
    private PlayerMotor motor;

    [SerializeField] private Transform armature;
    [SerializeField] private float rotate_Speed = 10f;
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float midSpeed = 3f;
    [SerializeField] private float acceleration = 80f;
    [SerializeField] private float alignThreshold = 0.98f;
    [SerializeField] private float viewYaw = -45f;

    // 🔴 회전은 물리 틱에서 돈다 — 시각 효과가 아니라 **시뮬레이션 상태**이기 때문이다.
    // Move()의 속도 계산이 armature.forward에 의존한다(dot >= alignThreshold면 즉시 최고속,
    // 아니면 가속). 즉 회전이 이동 속도를 바꾸므로, 회전이 렌더 레이트로 돌면 같은 입력이
    // 프레임레이트에 따라 다른 속도를 낸다 — 4단계의 재생(replay)이 성립하지 않는다.
    // 2026-09-12 A/B 실측으로 감각 영향 없음 확인 후 확정(임시 토글 제거).

    private Vector2 prevDir_for_Rotate = new Vector2(0f, -1f);
    private bool hasRotate = true;
    private float currentSpeed;

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        player = GetComponent<Player>();
        soulController = GetComponent<PlayerSoulController>();
        motor = GetComponent<PlayerMotor>();

        if (armature == null)
            armature = transform.Find("Armature");
    }

    private void Start()
    {
        rotate_Speed = 10f;
    }

    private void FixedUpdate()
    {
        // 회전을 먼저 한다 — Move()의 정렬도 판정(dot(worldDir, armature.forward))이 회전 결과를
        // 읽으므로, 먼저 돌고 그 방향으로 이동하는 인과를 유지한다.
        Rotate(Time.fixedDeltaTime);
        Move();
    }

    private void Move()
    {
        Vector3 inputVelocity = Vector3.zero;

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
                    acceleration * Time.fixedDeltaTime
                );
            }

            inputVelocity = worldDir * ResolveMoveSpeed(currentSpeed);
        }
        else
        {
            currentSpeed = 0f;
        }

        if (motor != null && inputVelocity.sqrMagnitude > 0f)
            motor.AddVelocity(inputVelocity);
    }

    private void Rotate(float deltaTime)
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
            rotate_Speed * deltaTime
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

    /// <summary>
    /// 현재 이동 입력을 뷰(viewYaw) 기준 월드 평면 방향으로 변환한다. 입력이 없으면 zero.
    /// 대시 등 외부 소비자가 이동과 동일한 입력→월드 매핑을 공유하기 위한 진입점.
    /// </summary>
    public Vector3 GetInputWorldDirection()
    {
        if (reader == null || !reader.HasMoveInput)
            return Vector3.zero;

        Vector2 input = reader.Direction;
        Vector3 worldDir = Quaternion.Euler(0f, viewYaw, 0f) * new Vector3(input.x, 0f, input.y);
        worldDir.y = 0f;
        return worldDir.sqrMagnitude > 0.0001f ? worldDir.normalized : Vector3.zero;
    }

    /// <summary>현재 캐릭터(armature)가 바라보는 평면 정면. 대시 무입력 시 기본 방향.</summary>
    public Vector3 CurrentFacing
    {
        get
        {
            Vector3 forward = armature != null ? armature.forward : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>자동 이동도 수동 이동과 같은 상태이상 속도 배율을 사용한다.</summary>
    internal float MaxResolvedMoveSpeed => ResolveMoveSpeed(maxSpeed);

    /// <summary>자동 이동 회전은 일반 이동과 같은 보간 속도를 사용한다.</summary>
    internal float AutoMoveRotationSpeed => rotate_Speed;

    private float ResolveMoveSpeed(float baseSpeed)
    {
        if (soulController != null &&
            soulController.TryGetFixedMoveSpeed(out float soulMoveSpeed))
        {
            return soulMoveSpeed;
        }

        float statusMultiplier = player != null && player.StatusEffects != null
            ? player.StatusEffects.GetStatMultiplier(StatusEffectType.MoveSpeedModifier)
            : 1f;

        return baseSpeed * statusMultiplier;
    }

    public void SetArmature(Transform newArmature)
    {
        if (newArmature == null)
            return;

        armature = newArmature;
    }
}
