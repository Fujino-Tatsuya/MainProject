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

    // ⚠️ 임시 실험 토글 (2026-09-12, 4단계 선결 판단용). 감각 확정 후 한쪽으로 고정하고 이 필드를 지운다.
    //
    // 왜 필요한가: Move()의 속도 계산이 armature.forward에 의존한다
    // (dot >= alignThreshold면 즉시 최고속, 아니면 가속). 즉 **회전이 이동 속도를 바꾸므로
    // 회전은 사실상 시뮬레이션 상태의 일부**인데, 지금은 Update에서 렌더 레이트로 돈다.
    // 4단계의 재생(replay)은 같은 입력에 같은 결과가 나와야 하므로 회전도 물리 틱이어야 한다.
    //
    // 대가: 회전 갱신이 50Hz로 떨어져 고프레임에서 덜 부드러워 보일 수 있다. 그 체감을 재려고 둔다.
    // Play 중 인스펙터에서 토글해 A/B로 비교할 것.
    [Header("실험 — 4단계 선결 판단용 (임시)")]
    [Tooltip("켜면 회전을 물리 틱(FixedUpdate)에서 처리한다. 끄면 기존대로 Update.")]
    [SerializeField] private bool rotateOnPhysicsTick = true;

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

    private void Update()
    {
        if (!rotateOnPhysicsTick)
            Rotate(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        // 회전을 먼저 한다 — Move()의 정렬도 판정(dot(worldDir, armature.forward))이 회전 결과를
        // 읽으므로, 기존 Update 회전과 같은 인과(먼저 돌고 그 방향으로 이동)를 유지한다.
        if (rotateOnPhysicsTick)
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
