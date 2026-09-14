using UnityEngine;

/// <summary>
/// 고정 터렛(<c>RangedTurret</c>)의 <b>머리 본만</b> 타깃 쪽으로 돌린다.
///
/// 🔴 <b>왜 코드인가</b>(2026-09-14 Play 실측): 예전에는 <c>MonsterBase</c> 가 <c>turnSpeed: 10</c> 으로
/// <b>몸통 전체</b>를 돌렸다. 고정 포탑이 몸을 트는 것이 설계와 어긋나고, 플레이어가 뒤에 있어도
/// 몸이 늦게 따라와 "안 보고 쏘는" 상태가 됐다. 이제 <c>MonsterBase.BodyRotationLocked</c> 가
/// 몸통 회전을 막고, 조준은 여기서 머리만 한다.
///
/// 🔴 <b>왜 LateUpdate 인가</b>: Animator 는 <c>Update</c> 와 <c>LateUpdate</c> <b>사이</b>에 본을 쓴다.
/// <c>Update</c> 에서 돌리면 그 프레임에 애니메이터가 그대로 덮어써서 <b>아무 일도 안 일어난다</b>.
///
/// 🔴 <b>왜 델타 회전인가</b>: 본의 로컬 축이 어디를 향하는지 알 수 없다(Armature 가 100배 스케일 +
/// 270° 축변환이고 본마다 방향이 제각각이다). 그래서 <c>LookRotation</c> 으로 <b>덮어쓰지 않고</b>,
/// 애니메이터가 쓴 회전 <b>위에</b> 월드 업축 기준 요(yaw)만 얹는다. 축 방향을 몰라도 안전하고
/// 발사 애니메이션의 포즈도 보존된다.
///
/// 🔴 <b>네트워크</b>: 복제하지 않는다. 본 회전은 <b>시각 요소</b>이고 각 피어가 자기 쪽 타깃으로
/// 같은 계산을 하면 같은 그림이 나온다. 여기서 <c>NetworkTransform</c> 을 태우면 본 하나 때문에
/// 대역폭을 계속 쓴다. 데미지·발사 판정은 서버가 하므로 이 회전은 판정에 관여하지 않는다
/// (<c>MonsterRangedAttack</c> 은 <c>targetPoint - origin</c> 으로 쏜다 — 머리 각도와 무관).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterBase))]
public class TurretHeadAim : MonoBehaviour
{
    [Header("머리 본")]
    [Tooltip("비우면 이름으로 자동 탐색한다. 터렛 리그는 Root→Column01~03→HeadRotator 순서다.")]
    [SerializeField] private Transform headBone;
    [SerializeField] private string headBoneName = "HeadRotator";

    [Header("회전")]
    [Tooltip("초당 회전 각도(도). 0 이면 즉시 스냅.")]
    [SerializeField] private float turnDegreesPerSecond = 360f;
    [Tooltip("몸통 정면 기준 좌우 최대 각(도). 180 이면 제한 없음.")]
    [Range(0f, 180f)]
    [SerializeField] private float maxYaw = 180f;

    [Header("복귀")]
    [Tooltip("타깃이 없을 때 정면(0도)으로 돌아오는 속도(도/초). 0 이면 그 자리에 멈춘다.")]
    [SerializeField] private float returnDegreesPerSecond = 120f;

    [Header("조준 고정")]
    [Tooltip("공격 중에는 조준을 고정한다(영점 고정 → 발사 → 다시 조준). " +
             "끄면 공격 중에도 계속 타깃을 따라 돌아간다.")]
    [SerializeField] private bool holdAimWhileAttacking = true;

    private MonsterBase _monster;

    /// 몸통 정면 대비 현재 머리 요(도). 프레임마다 애니메이터 포즈 위에 다시 얹는다.
    private float _yaw;

    /// <summary>현재 머리 요(도). 레이저 표시 등 외부 시각 요소가 읽는다.</summary>
    public float CurrentYaw => _yaw;

    /// <summary>조준이 향하는 월드 방향(수평). 타깃이 없으면 몸통 정면.</summary>
    public Vector3 AimDirection => Quaternion.AngleAxis(_yaw, Vector3.up) * FlatForward();

    private void Awake()
    {
        _monster = GetComponent<MonsterBase>();
        if (headBone == null) headBone = FindBone(headBoneName);

        if (headBone == null)
        {
            // 조용히 죽지 않게 한 번만 알린다 — 본 이름이 바뀌면 조준이 통째로 사라진다.
            Debug.LogWarning(
                $"[TurretHeadAim] {name}: '{headBoneName}' 본을 찾지 못해 머리 조준을 끈다. " +
                "리그가 바뀌었으면 Head Bone 을 직접 지정할 것.", this);
            enabled = false;
        }
    }

    private Transform FindBone(string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
    }

    private void LateUpdate()
    {
        if (headBone == null) return;

        // 🔴 공격 중에는 조준을 갱신하지 않는다 — 「조준 → 영점 고정 → 발사 → 다시 조준」.
        //    갱신만 멈추고 각도는 계속 적용한다(멈추면 애니메이터 포즈로 머리가 튄다).
        //    몸통 쪽 규약과 같다: MonsterBase 는 StartAttack 직전 FaceTarget() 1회로 조준을 확정한다.
        bool aimLocked = holdAimWhileAttacking
                         && _monster != null
                         && _monster.State == MonsterState.Attack;

        if (!aimLocked)
        {
            float desired = 0f;                       // 타깃이 없으면 정면으로 복귀
            float speed = returnDegreesPerSecond;

            Transform target = _monster != null ? _monster.CurrentTarget : null;
            if (target != null)
            {
                Vector3 toTarget = target.position - headBone.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    // 몸통 정면과 타깃 방향의 부호 있는 각차. 몸통이 고정이므로 이 값이 곧 머리 각이다.
                    desired = Vector3.SignedAngle(FlatForward(), toTarget.normalized, Vector3.up);
                    desired = Mathf.Clamp(desired, -maxYaw, maxYaw);
                    speed = turnDegreesPerSecond;
                }
            }

            _yaw = speed <= 0f
                ? desired
                : Mathf.MoveTowardsAngle(_yaw, desired, speed * Time.deltaTime);
        }

        if (Mathf.Abs(_yaw) < 0.01f) return;      // 정면이면 애니메이터 포즈를 건드리지 않는다

        // 🔴 덮어쓰지 않고 얹는다. 애니메이터가 방금 쓴 회전이 피연산자다.
        headBone.rotation = Quaternion.AngleAxis(_yaw, Vector3.up) * headBone.rotation;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (headBone == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(headBone.position, AimDirection * 2f);
    }
#endif
}
