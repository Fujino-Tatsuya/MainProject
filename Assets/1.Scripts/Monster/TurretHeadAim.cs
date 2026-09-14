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

    [Tooltip("공격이 끝난 뒤 다시 조준을 시작하기까지의 뜸(초). " +
             "0 이면 공격이 끝나는 즉시 머리가 돈다 — 붙어서 돌면 기계적으로 보인다(팀장 피드백).")]
    [SerializeField] private float aimResumeDelay = 0.25f;

    [Header("조준 예고선 (TrackingLaser)")]
    [Tooltip("비우면 이름으로 자동 탐색한다. 아트 프리팹에 딸려온 LineRenderer 로, 원래 비활성이다.")]
    [SerializeField] private LineRenderer aimLaser;
    [SerializeField] private string aimLaserName = "TrackingLaser";
    [Tooltip("예고선을 켤지 여부. 선딜 동안만 켜고 발사 시점에 끈다.")]
    [SerializeField] private bool showAimLaser = true;
    [Tooltip("예고선이 벽에 막히면 거기서 끊는다. 비우면 사거리 끝까지 곧게 그린다.")]
    [SerializeField] private LayerMask laserBlockers;

    [Header("진단")]
    [Tooltip("켜면 0.5초마다 상태·조준각·애니메이터 개입 여부를 콘솔에 찍는다. " +
             "머리가 폭주할 때 원인을 가르는 용도 — 평소에는 끈다.")]
    [SerializeField] private bool logDiagnostics;

    private float _nextLogTime;
    private Quaternion _lastWritten;
    private bool _hasWritten;

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
            return;
        }

        if (aimLaser == null)
        {
            Transform t = FindBone(aimLaserName);
            if (t != null) aimLaser = t.GetComponent<LineRenderer>();
        }
        // 예고선이 없어도 조준은 동작해야 하므로 여기서는 끄지 않는다(경고만).
        if (showAimLaser && aimLaser == null)
            Debug.LogWarning($"[TurretHeadAim] {name}: '{aimLaserName}' LineRenderer 가 없어 예고선을 끈다.", this);

        if (aimLaser != null) aimLaser.enabled = false;
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

        // 🔴 공격 중에는 조준을 갱신하지 않는다 — 「조준 → 영점 고정 → 발사 → 뜸 → 다시 조준」.
        //    갱신만 멈추고 각도는 계속 적용한다(멈추면 애니메이터 포즈로 머리가 튄다).
        //    몸통 쪽 규약과 같다: MonsterBase 는 StartAttack 직전 FaceTarget() 1회로 조준을 확정한다.
        bool attacking = _monster != null && _monster.State == MonsterState.Attack;
        if (attacking) _resumeAimAt = Time.time + aimResumeDelay;

        // 공격이 끝난 직후 바로 돌면 기계적으로 보인다(팀장 피드백) — 짧은 뜸을 둔다.
        bool aimLocked = holdAimWhileAttacking && (attacking || Time.time < _resumeAimAt);

        UpdateAimLaser(attacking);

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

        // 🔴 누적 방지: 이 프레임에 애니메이터가 본을 다시 썼는지 확인한다.
        //    안 썼다면 지금 값은 "내가 지난 프레임에 쓴 것"이므로, 그 위에 또 얹으면
        //    매 프레임 각도가 쌓여 머리가 폭주한다.
        bool animatorWrote = !_hasWritten || headBone.localRotation != _lastWritten;

        if (logDiagnostics && Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + 0.5f;
            Debug.Log($"[TurretHeadAim] {name} state={(_monster != null ? _monster.State.ToString() : "?")} " +
                      $"lock={aimLocked} yaw={_yaw:F1} 애니메이터가씀={animatorWrote} " +
                      $"target={(_monster != null && _monster.CurrentTarget != null ? _monster.CurrentTarget.name : "없음")}", this);
        }

        if (Mathf.Abs(_yaw) < 0.01f) { _hasWritten = false; return; } // 정면이면 포즈를 안 건드린다

        if (!animatorWrote)
        {
            // 애니메이터가 손대지 않았다 → 내가 지난 프레임에 만든 결과를 되돌리고 기준 포즈에서 다시 얹는다.
            headBone.localRotation = _lastAnimPose;
        }

        _lastAnimPose = headBone.localRotation;
        headBone.rotation = Quaternion.AngleAxis(_yaw, Vector3.up) * headBone.rotation;
        _lastWritten = headBone.localRotation;
        _hasWritten = true;
    }

    private Quaternion _lastAnimPose = Quaternion.identity;
    private float _resumeAimAt;
    private float _attackStartedAt = -1f;

    /// <summary>
    /// 조준 예고선. <b>선딜 동안만</b> 켠다 — 발사 시점에 끄면 "지금 여기로 쏜다"는 예고가 된다.
    ///
    /// 🔴 <c>TrackingLaser</c> 는 아트 프리팹에 딸려 있던 <see cref="LineRenderer"/> 인데
    /// <b>비활성이고 참조하는 코드가 하나도 없었다</b>(2026-09-14 전수 확인). 여기서 처음 배선한다.
    /// 애니메이션 클립이 아니다 — 팩 전체에 Lazer/Laser/Beam 클립은 존재하지 않는다.
    ///
    /// 각 피어가 로컬로 그린다. 복제하지 않는다(시각 요소).
    /// </summary>
    private void UpdateAimLaser(bool attacking)
    {
        if (aimLaser == null) return;

        if (!showAimLaser)
        {
            if (aimLaser.enabled) aimLaser.enabled = false;
            return;
        }

        if (!attacking)
        {
            _attackStartedAt = -1f;
            if (aimLaser.enabled) aimLaser.enabled = false;
            return;
        }

        if (_attackStartedAt < 0f) _attackStartedAt = Time.time;

        // 선딜이 지나면(= 탄이 나간 뒤) 끈다. windup 이 0 이면 공격 상태 내내 보여 준다.
        float windup = _monster != null ? _monster.AttackWindupSeconds : 0f;
        if (windup > 0f && Time.time - _attackStartedAt >= windup)
        {
            if (aimLaser.enabled) aimLaser.enabled = false;
            return;
        }

        Vector3 origin = aimLaser.transform.position;
        Vector3 dir = AimDirection;
        float range = _monster != null ? _monster.AttackRangeMeters : 0f;
        if (range <= 0f) range = 10f;

        // 벽에 막히면 거기서 끊는다 — 벽을 뚫고 나간 예고선은 오히려 오해를 만든다.
        if (laserBlockers.value != 0 &&
            Physics.Raycast(origin, dir, out RaycastHit hit, range, laserBlockers, QueryTriggerInteraction.Ignore))
            range = hit.distance;

        aimLaser.useWorldSpace = true;
        aimLaser.positionCount = 2;
        aimLaser.SetPosition(0, origin);
        aimLaser.SetPosition(1, origin + dir * range);
        if (!aimLaser.enabled) aimLaser.enabled = true;
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
