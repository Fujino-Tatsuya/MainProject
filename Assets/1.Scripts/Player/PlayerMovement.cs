using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Player player;
    private PlayerSoulController soulController;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private LayerMask rootMoveBlockingMask;

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
        soulController = GetComponent<PlayerSoulController>();
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

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

            inputMove = worldDir * ResolveMoveSpeed(currentSpeed) * Time.deltaTime;
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
            return dir * Mathf.Max(0f, hit.distance - 0.02f);

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
