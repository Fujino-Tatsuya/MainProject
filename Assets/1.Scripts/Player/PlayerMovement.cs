using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Player player;
    private PlayerSoulController soulController;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private PlayerGroundingSensor grounding;
    private PlayerMotor motor;
    private LayerMask rootMoveBlockingMask;

    [SerializeField] private Transform armature;
    [SerializeField] private float rotate_Speed = 10f;
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float midSpeed = 3f;
    [SerializeField] private float acceleration = 80f;
    [SerializeField] private float alignThreshold = 0.98f;
    [SerializeField] private float viewYaw = -45f;

    // 평지 판정 기준. QA의 P9-PlayerWallClimb 디텍터와 같은 값이라 디텍터가 이상으로 보는 구간이
    // 그대로 Y 잠금 구간이 된다(경사·램프는 이 값을 못 넘어 잠기지 않는다).
    private const float FlatGroundNormalY = 0.999f;
    private const float VerticalIntentEpsilon = 0.00005f;

    private Vector2 prevDir_for_Rotate = new Vector2(0f, -1f);
    private bool hasRotate = true;
    private float currentSpeed;

    // Y 잠금 판정용(ApplyFlatGroundYLock 참조). 스크립트가 스스로 넣은 수직 이동이 있으면 잠그지
    // 않아야 하므로 이번 물리 틱의 최종 의도값을 남긴다. initialConstraints는 저작된 제약(회전 고정)이다.
    private float lastVerticalIntentY;
    private RigidbodyConstraints initialConstraints;

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        player = GetComponent<Player>();
        soulController = GetComponent<PlayerSoulController>();
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        grounding = GetComponent<PlayerGroundingSensor>();
        motor = GetComponent<PlayerMotor>();
        initialConstraints = rb.constraints;

        // MoveRoot(평타 러시 스텝/스킬 전진) 관통 방지 스윕 대상 — 정적 지오메트리만.
        // 유닛(Enemy/Player)은 제외해 러시가 몹 사이를 지나는 기존 감각을 유지한다.
        rootMoveBlockingMask = LayerMask.GetMask("Default", "Ground", "Wall", "Env");

        if (armature == null)
            armature = transform.Find("Armature");
    }

    private void Start()
    {
        rotate_Speed = 10f;
    }

    private void Update()
    {
        Rotate();
    }

    private void FixedUpdate()
    {
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

    /// <summary>Motor가 최종 이동을 적용한 뒤 같은 물리 틱의 Y 잠금 판정을 마무리한다.</summary>
    internal void ApplyPostMotorGroundLock(float verticalIntentY)
    {
        lastVerticalIntentY = verticalIntentY;
        ApplyFlatGroundYLock();
    }

    /// <summary>
    /// 평지 접지 중에는 Rigidbody의 Y축을 잠근다(그 외에는 저작된 제약으로 되돌린다).
    ///
    /// 루트 Rigidbody는 저작상 non-kinematic이라(Paladin 프리팹) <c>rb.MovePosition</c>이 텔레포트가
    /// 아니라 "목표까지 가는 속도 + 솔버의 충돌 해석"으로 동작한다. 그래서 캡슐이 벽 모서리에 눌리면
    /// PhysX가 접촉 법선 방향으로 관통을 밀어내는데, 모서리·베벨 면의 법선에 섞인 미세한 +Y가 매
    /// 스텝 쌓여 벽을 타고 오른다(QA P9-PlayerWallClimb). <see cref="PlayerMotionSweep"/>은
    /// MovePosition <b>전에</b> 끝나 이걸 막을 수 없고 사후 보정은 한 프레임 늦으므로, 솔버가 Y를
    /// 아예 못 건드리게 제약으로 막는다. 잠긴 축의 접촉 해석은 수평 성분만 남아 벽을 따라 미끄러지는
    /// 동작은 그대로다. 판정은 <see cref="PlayerMotor"/>가 최종 이동을 제출한 뒤 물리 스텝마다 갱신한다.
    ///
    /// 다음 중 하나라도 어긋나면 즉시 잠금을 푼다(정상적인 수직 이동 보호):
    /// - 평지 접지가 아님(경사·램프는 법선이 <see cref="FlatGroundNormalY"/> 미만 → 등판·낙하 정상)
    /// - 이동 플랫폼 위(플랫폼이 수직으로 움직인다)
    /// - 스크립트가 직접 수직 이동을 넣었음(플랫폼 캐리 등)
    /// - 넉백 중(위로 띄우는 넉백이라면 Y가 잠긴 채로는 떠오르지 못해 접지도 안 풀린다)
    ///
    /// 대시는 제외하지 않는다 — 설계상 평면 이동이고(<c>planar.y = 0f</c>), 절벽에서 떨어지는
    /// 수직 이동은 접지가 풀리는 순간 이 잠금도 같이 풀리므로 평지 접지 게이트만으로 충분하다.
    /// </summary>
    private void ApplyFlatGroundYLock()
    {
        bool lockY =
            grounding != null &&
            grounding.IsGrounded &&
            !grounding.IsMovingPlatform &&
            grounding.GroundNormal.y >= FlatGroundNormalY &&
            Mathf.Abs(lastVerticalIntentY) <= VerticalIntentEpsilon &&
            (player == null || player.CurrentState != PlayerActionState.Knockback);

        RigidbodyConstraints desired = lockY
            ? initialConstraints | RigidbodyConstraints.FreezePositionY
            : initialConstraints;

        if (rb.constraints != desired)
            rb.constraints = desired;
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
        rb.MovePosition(rb.position + ClampByStaticGeometry(deltaPosition));
    }

    // MovePosition은 스윕 없이 목표 지점으로 이동해, 평타 러시 스텝처럼 한 프레임 대이동이
    // 논컨벡스 벽 MeshCollider를 그대로 관통한다 — 벽에 막히면 그 앞까지로 이동량을 클램프.
    private Vector3 ClampByStaticGeometry(Vector3 delta)
    {
        float dist = delta.magnitude;
        if (dist < 0.0001f || capsule == null)
            return delta;

        Vector3 dir = delta / dist;
        Vector3 center = rb.position + capsule.center;
        float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
        // 반경/정지거리에 스킨 여유 — 바닥 등 기존 접촉면 스침으로 제자리 클램프되는 것 방지.
        float radius = Mathf.Max(0.01f, capsule.radius - 0.02f);

        if (Physics.CapsuleCast(
                center + Vector3.up * half, center - Vector3.up * half, radius,
                dir, out RaycastHit hit, dist, rootMoveBlockingMask, QueryTriggerInteraction.Ignore))
        {
            float allowed = Mathf.Max(0f, hit.distance - 0.02f);

            // ⚠️ 대시는 PlayerMotionSweep으로 이미 충돌을 해결한 뒤 여기로 온다. 이 클램프는 마스크
            // (Default/Ground/Wall/Env)와 등판각 판정이 달라, 대시 스윕이 통과시킨 경사·지면을
            // 여기서 다시 막을 수 있다 — "대시가 시작은 됐는데 안 나간다"의 마지막 후보다.
            if (player != null && player.CurrentState == PlayerActionState.Dash && allowed < dist * 0.9f)
            {
                Edit.LogWarning(
                    $"[Dash] MoveRoot 클램프: 요청 {dist:F3}m → 허용 {allowed:F3}m, " +
                    $"막은 콜라이더='{hit.collider.name}' (레이어 {LayerMask.LayerToName(hit.collider.gameObject.layer)}), " +
                    $"법선각={Vector3.Angle(hit.normal, Vector3.up):F0}°. " +
                    "대시 스윕(PlayerMotionSweep)과 마스크·등판각 판정이 다른 2차 클램프입니다.", this);
            }

            return dir * allowed;
        }

        return delta;
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

    /// <summary>
    /// 오너 자동 이동(스킬 사거리 확보용). worldTarget 방향으로 최대 이속(상태이상 배율 반영)으로 이동하며
    /// armature를 진행 방향으로 회전시킨다. CanMove가 막히면(CC 등) 그 프레임은 정지한다.
    /// 수동 입력이 없을 때만 호출되므로 Move()의 입력 이동과 충돌하지 않는다.
    /// </summary>
    public void MoveTowardsPoint(Vector3 worldTarget)
    {
        if (rb == null)
            return;

        if (player != null && !player.CanMove)
            return;

        Vector3 dir = worldTarget - rb.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return;

        dir.Normalize();

        rb.MovePosition(
            rb.position + dir * (ResolveMoveSpeed(maxSpeed) * Time.deltaTime));
        RotateToward(dir, rotate_Speed);
    }

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
