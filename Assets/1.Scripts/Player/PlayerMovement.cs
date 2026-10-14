using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMotor))]
[DataTableSheet("Player", Order = 1)]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Player player;
    private PlayerSoulController soulController;
    private PlayerMotor motor;

    [SerializeField] private Transform armature;
    [DataTableIgnore] [SerializeField] private float rotate_Speed = 10f;
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float midSpeed = 3f;
    [SerializeField] private float acceleration = 80f;
    [DataTableIgnore] [SerializeField] private float alignThreshold = 0.98f;
    [DataTableIgnore] [SerializeField] private float viewYaw = -45f;

    // 🔴 회전은 물리 틱에서 돈다 — 시각 효과가 아니라 **시뮬레이션 상태**이기 때문이다.
    // Move()의 속도 계산이 armature.forward에 의존한다(dot >= alignThreshold면 즉시 최고속,
    // 아니면 가속). 즉 회전이 이동 속도를 바꾸므로, 회전이 렌더 레이트로 돌면 같은 입력이
    // 프레임레이트에 따라 다른 속도를 낸다 — 4단계의 재생(replay)이 성립하지 않는다.
    // 2026-09-12 A/B 실측으로 감각 영향 없음 확인 후 확정(임시 토글 제거).

    private Vector2 prevDir_for_Rotate = new Vector2(0f, -1f);
    private bool hasRotate = true;
    private float currentSpeed;

    // 원인별 회전 잠금(할 일 27). 이동 입력 회전과 RotateToward 를 막는다 — PlayerFacingLock 주석 참조.
    private readonly PlayerFacingLock facingLock = new PlayerFacingLock();

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        player = GetComponent<Player>();
        soulController = GetComponent<PlayerSoulController>();
        motor = GetComponent<PlayerMotor>();

        if (armature == null)
            armature = transform.Find("Armature");

        motor?.SynchronizeArmatureRotation(ArmatureRotation);
    }

    /// <summary>오너 입력 장치에서 서버로 보낼 수 있는 raw 값만 캡처한다.</summary>
    internal PlayerRawSimulationInput CaptureRawSimulationInput()
    {
        return new PlayerRawSimulationInput(
            reader != null ? reader.Direction : Vector2.zero,
            reader != null && reader.HasMoveInput);
    }

    /// <summary>
    /// raw 입력에 이 피어가 보유한 상태 머신/Soul/서버 권위 상태이상을 합쳐 전체 시뮬레이션 입력을 만든다.
    /// 서버 관측 경로도 이 메서드를 사용하므로 raw DTO에 권위 값을 추가하지 않는다.
    /// </summary>
    internal PlayerSimulationInput CaptureSimulationInput(PlayerRawSimulationInput rawInput)
    {
        float fixedMoveSpeed = 0f;
        bool hasFixedMoveSpeed =
            soulController != null &&
            soulController.TryGetFixedMoveSpeed(out fixedMoveSpeed);

        float statusMultiplier = player != null && player.StatusEffects != null
            ? player.StatusEffects.GetStatMultiplier(StatusEffectType.MoveSpeedModifier)
            : 1f;

        return new PlayerSimulationInput
        {
            MoveDirection = rawInput.MoveDirection,
            HasMoveInput = rawInput.HasMoveInput,
            CanMove = player == null || player.CanMove,
            CanRotate = (player == null || player.CanMovementRotate) && !facingLock.IsLocked,
            HasFixedMoveSpeed = hasFixedMoveSpeed,
            FixedMoveSpeed = hasFixedMoveSpeed ? fixedMoveSpeed : 0f,
            MoveSpeedMultiplier = statusMultiplier
        };
    }

    internal PlayerSimulationSettings CaptureSimulationSettings()
    {
        return new PlayerSimulationSettings
        {
            RotateSpeed = rotate_Speed,
            MaxSpeed = maxSpeed,
            MidSpeed = midSpeed,
            Acceleration = acceleration,
            AlignThreshold = alignThreshold,
            ViewYaw = viewYaw
        };
    }

    /// <summary>시각 보간이 오프셋을 걸 대상. 메시 루트다.</summary>
    public Transform ArmatureTransform => armature;

    internal Quaternion ArmatureRotation =>
        armature != null ? armature.rotation : transform.rotation;

    internal Vector2 PreviousRotateDirection => prevDir_for_Rotate;
    internal bool HasRotate => hasRotate;
    internal float CurrentSpeed => currentSpeed;

    internal void CommitSimulationState(PlayerSimulationState state, bool applyArmatureRotation)
    {
        prevDir_for_Rotate = state.PreviousRotateDirection;
        hasRotate = state.HasRotate;
        currentSpeed = state.CurrentSpeed;

        if (applyArmatureRotation && armature != null)
            armature.rotation = state.ArmatureRotation;
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
        motor?.SynchronizeArmatureRotation(armature.rotation, hasRotate);
    }

    /// <summary>
    /// <paramref name="cause"/> 이름으로 회전을 잠그고 <paramref name="direction"/> 을 바라본다.
    /// 같은 원인으로 다시 부르면 방향만 바꾼다. 풀기는 <see cref="ReleaseFacingLock"/> — 원인마다 따로다.
    /// </summary>
    public void AcquireFacingLock(object cause, Vector3 direction)
    {
        facingLock.Acquire(cause);
        RotateImmediately(direction);
    }

    /// <summary>이 원인의 잠금만 푼다. 다른 원인이 남아 있으면 여전히 잠겨 있다.</summary>
    public void ReleaseFacingLock(object cause) => facingLock.Release(cause);

    public bool IsFacingLocked => facingLock.IsLocked;

    public void RotateToward(Vector3 direction, float speed)
    {
        if (armature == null || facingLock.IsLocked)
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
        motor?.SynchronizeArmatureRotation(armature.rotation, hasRotate);
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
    /// <summary>이동 최고속도 원본(버프 배율 적용 전). Unit 의 이동속도 스탯도 이 값이다.</summary>
    internal float BaseMaxSpeed => maxSpeed;
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
        motor?.SynchronizeArmatureRotation(armature.rotation, hasRotate);
    }
}
