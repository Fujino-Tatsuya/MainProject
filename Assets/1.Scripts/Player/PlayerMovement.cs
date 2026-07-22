using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    private PlayerInputReader reader;
    private Player player;
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

    private void Awake()
    {
        reader = GetComponent<PlayerInputReader>();
        player = GetComponent<Player>();
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
        if (player != null && !player.CanMove)
        {
            currentSpeed = 0f;
            return;
        }

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

        // 상태이상 이속 modifier 반영 (버프 > 1, 둔화 < 1)
        float statusMultiplier = player != null && player.StatusEffects != null
            ? player.StatusEffects.GetStatMultiplier(StatusEffectType.MoveSpeedModifier)
            : 1f;

        rb.MovePosition(
            rb.position + worldDir * currentSpeed * statusMultiplier * Time.deltaTime
        );
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

    public void SetArmature(Transform newArmature)
    {
        if (newArmature == null)
            return;

        armature = newArmature;
    }
}
