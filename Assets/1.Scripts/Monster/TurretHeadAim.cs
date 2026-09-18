using Unity.Netcode;
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
/// 🔴 <b>네트워크</b>(2026-09-14 정정): <b>복제한다</b>. 예전 주석은 "각 피어가 자기 쪽 타깃으로
/// 같은 계산을 하면 같은 그림이 나온다"고 적었는데 <b>사실이 아니었다</b> — 타깃 선정은
/// <c>MonsterBase</c> 의 <c>IsServer</c> 게이트 안에 있어서 <b>클라에는 <c>CurrentTarget</c> 이
/// 항상 <c>null</c></b> 이다. 그래서 클라에서는 머리가 돌지도, 예고선이 켜지지도 않았다
/// (호스트 화면에서만 보였다). 예고선은 <b>피하라고 보여 주는 것</b>이라 안 보이면 기능이 없다.
///
/// 복제하는 것은 <b>결과값 두 개</b>뿐이다 — 요(yaw) 하나와 예고 중 여부 하나.
/// 타깃 참조도, <c>NetworkTransform</c> 도 태우지 않는다(본 하나 때문에 대역폭을 계속 쓴다).
/// 판정은 그대로 서버가 한다 — 이 회전은 판정에 관여하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterBase))]
public class TurretHeadAim : NetworkBehaviour, ITurretAimGate
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
    [Tooltip("예고선 머티리얼. 비우면 아트 프리팹에 있던 것을 그대로 쓴다 — " +
             "그 기본값은 내장 RP 의 Default-Line 이라 URP 에서 안 보인다.")]
    [SerializeField] private Material laserMaterial;
    [Tooltip("예고선 두께(m).")]
    [SerializeField] private float laserWidth = 0.05f;
    [Tooltip("예고선이 나가는 지점. 비우면 Muzzle_Socket → Head → HeadRotator 순으로 자동 탐색한다. " +
             "예고선 오브젝트의 위치를 쓰지 않는 이유는, 그 오브젝트가 머리에 붙어 있지 않을 수도 있어서다.")]
    [SerializeField] private Transform laserOrigin;

    [Header("조준 예고 시간")]
    [Tooltip("사거리 안에 들어온 뒤 조준선을 보이며 타깃을 따라가는 시간(초). " +
             "이 시간이 지나면 조준이 그 방향에 고정된다(팀장 확정: 0.5~1.0초).")]
    [SerializeField] private float telegraphSeconds = 0.7f;

    [Tooltip("추적이 끝난 뒤 최종 방향에 조준선을 '고정한 채 유지'하는 시간(초). " +
             "이 시간이 지나야 발사한다 — 플레이어가 피할 여지를 주는 구간이다.")]
    [SerializeField] private float aimHoldSeconds = 0.5f;

    [Header("네트워크")]
    [Tooltip("서버가 조준각을 다시 보내는 최소 변화량(도). 이보다 작게 움직이면 보내지 않는다. " +
             "0 이면 추적하는 내내 매 틱 보낸다.")]
    [SerializeField] private float yawSendThreshold = 0.5f;

    [Tooltip("클라가 받은 조준각을 따라잡는 시간(초). 복제가 틱 단위(기본 30Hz)로 오므로 " +
             "그 간격을 메우는 값이다. 크면 머리가 늘어지고, 0 이면 계단처럼 튄다.")]
    [SerializeField] private float replicationSmoothing = 0.08f;

    [Header("진단")]
    [Tooltip("켜면 0.5초마다 상태·조준각·애니메이터 개입 여부를 콘솔에 찍는다. " +
             "머리가 폭주할 때 원인을 가르는 용도 — 평소에는 끈다.")]
    [SerializeField] private bool logDiagnostics;

    // ── 복제되는 값 ─────────────────────────────────────────────────────────
    // 🔴 이 두 개가 전부다. 타깃 참조도 본 트랜스폼도 보내지 않는다 — 클라가 같은 그림을 그리는 데
    //    필요한 최소값만 보낸다. 권한 규약은 MonsterBase._state 와 같다(서버 쓰기 / 전원 읽기).
    private readonly NetworkVariable<float> _netYaw = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _netTelegraphing = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

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

    private bool _telegraphing;
    private float _telegraphStartedAt;

    /// 예고선을 지금 그려야 하는가. 서버는 <see cref="_telegraphing"/> 에서, 클라는 복제값에서 온다.
    private bool _laserVisible;

    /// 예고가 시작된 뒤 지난 시간(초). 예고 중이 아니면 0.
    private float TelegraphElapsed => _telegraphing ? Time.time - _telegraphStartedAt : 0f;

    /// <summary>
    /// 추적이 끝나 <b>조준선이 최종 방향에 고정된</b> 구간인가.
    /// 「추적(<c>telegraphSeconds</c>) → 고정 유지(<c>aimHoldSeconds</c>) → 발사」의 가운데 구간이다.
    /// </summary>
    private bool AimHolding => _telegraphing && TelegraphElapsed >= telegraphSeconds;

    /// <summary>
    /// 조준을 <b>내가 계산하는가</b>. 서버이거나, 아직 스폰되지 않은(= 네트워크 밖) 상태면 그렇다.
    /// 스폰 전까지 권한을 인정하는 이유는 프리뷰·단독 씬에서도 컴포넌트가 동작해야 하기 때문이다.
    /// 스폰 전에는 <c>NetworkVariable</c> 에 쓰면 예외가 나므로 <see cref="PublishAimState"/> 가 막는다.
    /// </summary>
    private bool HasAimAuthority => !IsSpawned || IsServer;

    // ── ITurretAimGate ──────────────────────────────────────────────────────
    // 🔴 아래 셋은 MonsterBase.SeekTurret 이 서버에서만 부른다. 그래서 _telegraphing /
    //    _telegraphStartedAt 은 서버 전용 상태로 둔다 — 클라는 이 값들을 만들지 않고
    //    복제된 _netTelegraphing 만 본다.
    /// <summary>
    /// 발사해도 되는가. <b>추적 + 고정 유지</b> 가 모두 끝나야 <c>true</c> 다.
    ///
    /// 🔴 고정 유지 구간이 따로 있는 이유(팀장 확정 2026-09-14): 조준선이 멈추는 순간 바로 쏘면
    /// 플레이어가 피할 틈이 없다. 최종 방향에 선을 <b>박아 둔 채 잠깐 기다렸다가</b> 쏴야
    /// 예고로서 기능한다.
    /// </summary>
    public bool IsAimReady =>
        !_telegraphing || TelegraphElapsed >= telegraphSeconds + aimHoldSeconds;

    /// <summary>
    /// 조준선과 <b>같은</b> 방향. 예고선도 이 방향으로 그리므로 선과 탄이 정확히 일치한다.
    /// 고정 구간에는 <c>_yaw</c> 갱신이 멈춰 있어 값이 그대로 유지된다.
    /// </summary>
    public Vector3 LockedAimDirection => AimDirection;

    public void BeginAiming()
    {
        if (_telegraphing) return;
        _telegraphing = true;
        _telegraphStartedAt = Time.time;
    }

    public void CancelAiming() => _telegraphing = false;

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

        // 예고선이 나가는 지점 — 총구가 있으면 총구, 없으면 머리.
        if (laserOrigin == null)
            laserOrigin = FindBone("Muzzle_Socket") ?? FindBone("Head") ?? headBone;

        // 🔴 막는 레이어를 안 정하면 예고선이 벽을 뚫고 나가 "저기까지 공격이 온다"로 오해된다.
        //    지정이 없으면 바닥·구조물 레이어로 기본값을 잡는다(MapContentSpawner 와 같은 집합).
        if (laserBlockers.value == 0)
            laserBlockers = LayerMask.GetMask("Default", "Ground");

        if (aimLaser != null)
        {
            // 🔴 아트 프리팹의 기본 머티리얼은 내장 RP 의 Default-Line 이라 URP 에서 안 보인다.
            //    프리팹 오버라이드로 갈지 않고 런타임에 넣는다 — 이 오브젝트는 2단 중첩 프리팹
            //    안에 있고(우리 프리팹 → 아트 프리팹), 중첩 오버라이드가 조용히 실패한 전례가 있다.
            if (laserMaterial != null) aimLaser.sharedMaterial = laserMaterial;
            if (laserWidth > 0f) aimLaser.widthMultiplier = laserWidth;

            aimLaser.useWorldSpace = true;
            aimLaser.positionCount = 2;
            // 🔴 GameObject 자체가 비활성이다(m_IsActive: 0). Renderer.enabled 만 켜면
            //    아무것도 그려지지 않는다 — 켜고 끄는 것은 GameObject 쪽이어야 한다.
            aimLaser.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 늦게 들어온 클라도 <b>진행 중인 예고</b>를 제대로 본다 — <c>NetworkVariable</c> 의 현재 값이
    /// 스폰 시점에 그대로 전달되므로, 받은 각을 <b>보간 없이</b> 초기값으로 깔아 둔다.
    /// 안 깔면 정면(0도)에서 조준 각도까지 한 번 휙 돌아가는 게 보인다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer) return;
        _yaw = _netYaw.Value;
        _laserVisible = _netTelegraphing.Value;
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

        if (HasAimAuthority) TickAimAuthoritative();
        else                 TickAimReplicated();

        UpdateAimLaser();
        ApplyHeadYaw();
    }

    /// <summary>서버(또는 네트워크 밖) — 조준각을 직접 계산하고 그 결과를 복제한다.</summary>
    private void TickAimAuthoritative()
    {
        // 🔴 공격 중에는 조준을 갱신하지 않는다 — 「조준 → 영점 고정 → 발사 → 뜸 → 다시 조준」.
        //    갱신만 멈추고 각도는 계속 적용한다(멈추면 애니메이터 포즈로 머리가 튄다).
        //    몸통 쪽 규약과 같다: MonsterBase 는 StartAttack 직전 FaceTarget() 1회로 조준을 확정한다.
        bool attacking = _monster != null && _monster.State == MonsterState.Attack;
        if (attacking)
        {
            _resumeAimAt = Time.time + aimResumeDelay;
            _telegraphing = false;   // 발사에 들어갔다 — 다음 사이클에 예고를 처음부터 다시 한다
        }

        // 🔴 추적이 끝나는 순간부터 고정한다 — IsAimReady(발사 가능)보다 aimHoldSeconds 만큼 이르다.
        //    이 차이가 "최종 방향에 선을 박아 둔 채 기다리는" 구간을 만든다.
        //    여기서 IsAimReady 를 쓰면 고정 구간이 사라져 멈추자마자 쏘게 된다.
        bool telegraphLocked = AimHolding;

        // 공격이 끝난 직후 바로 돌면 기계적으로 보인다(팀장 피드백) — 짧은 뜸을 둔다.
        bool aimLocked = holdAimWhileAttacking
                         && (attacking || telegraphLocked || Time.time < _resumeAimAt);

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

        _laserVisible = _telegraphing;
        PublishAimState(telegraphLocked);
    }

    /// <summary>
    /// 복제 상태를 갱신한다. <b>바뀔 때만</b> 쓴다 — 고정 유지 구간과 타깃이 없는 동안에는
    /// 한 바이트도 나가지 않는다(<c>NetworkVariable</c> 은 더티일 때만 보낸다).
    /// </summary>
    /// <param name="telegraphLocked">이번 프레임이 조준 고정 구간인가(<see cref="AimHolding"/>).</param>
    private void PublishAimState(bool telegraphLocked)
    {
        if (!IsSpawned) return;   // 네트워크 밖(프리뷰·단독 씬) — 여기에 쓰면 예외가 난다

        if (_netTelegraphing.Value != _telegraphing)
            _netTelegraphing.Value = _telegraphing;

        // 🔴 고정으로 넘어가는 첫 프레임은 임계값을 무시하고 무조건 보낸다.
        //    이 각이 곧 탄이 날아갈 방향(LockedAimDirection)이라, 임계값만큼 어긋난 채로 굳으면
        //    클라의 예고선과 실제 탄이 그만큼 다른 곳을 가리킨다 — 예고가 거짓말이 된다.
        bool lockEdge = telegraphLocked && !_wasTelegraphLocked;
        _wasTelegraphLocked = telegraphLocked;

        if (lockEdge || Mathf.Abs(Mathf.DeltaAngle(_netYaw.Value, _yaw)) >= yawSendThreshold)
            _netYaw.Value = _yaw;
    }

    /// <summary>
    /// 클라 — 계산하지 않고 받은 각을 따라간다.
    /// 그대로 대입하지 않는 이유는 복제가 <b>틱 단위</b>(기본 30Hz)로 오기 때문이다. 프레임마다
    /// 남은 각을 <c>replicationSmoothing</c> 안에 메우도록 속도를 잡으면, 추적 구간은 부드럽고
    /// 고정 구간에서는 목표값에 정확히 수렴한다 — 즉 <b>정작 중요한 순간에는 서버와 같은 각</b>이다.
    ///
    /// 🔴 여기서 <c>turnDegreesPerSecond</c> 를 그대로 쓰면 안 된다. 서버가 그 속도로 돌고 클라도
    ///    같은 속도로 쫓으면 <b>지연이 영원히 안 줄어든다</b>(계속 그만큼 뒤처진 각을 그린다).
    /// </summary>
    private void TickAimReplicated()
    {
        _laserVisible = _netTelegraphing.Value;

        float target = _netYaw.Value;
        float delta = Mathf.Abs(Mathf.DeltaAngle(_yaw, target));
        if (delta <= 0.01f || replicationSmoothing <= 0f)
        {
            _yaw = target;
            return;
        }

        _yaw = Mathf.MoveTowardsAngle(
            _yaw, target, delta / Mathf.Max(0.02f, replicationSmoothing) * Time.deltaTime);
    }

    /// <summary>애니메이터가 쓴 포즈 위에 요(yaw)를 얹는다. 서버·클라 공통 경로다.</summary>
    private void ApplyHeadYaw()
    {
        // 🔴 누적 방지: 이 프레임에 애니메이터가 본을 다시 썼는지 확인한다.
        //    안 썼다면 지금 값은 "내가 지난 프레임에 쓴 것"이므로, 그 위에 또 얹으면
        //    매 프레임 각도가 쌓여 머리가 폭주한다.
        bool animatorWrote = !_hasWritten || headBone.localRotation != _lastWritten;

        if (logDiagnostics && Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + 0.5f;
            Debug.Log($"[TurretHeadAim] {name} state={(_monster != null ? _monster.State.ToString() : "?")} " +
                      $"권한={HasAimAuthority} yaw={_yaw:F1} " +
                      $"복제yaw={(IsSpawned ? _netYaw.Value.ToString("F1") : "-")} 예고={_laserVisible} " +
                      $"애니메이터가씀={animatorWrote} " +
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
    private bool _wasTelegraphLocked;

    /// <summary>
    /// 조준 예고선. <b>선딜 동안만</b> 켠다 — 발사 시점에 끄면 "지금 여기로 쏜다"는 예고가 된다.
    ///
    /// 🔴 <c>TrackingLaser</c> 는 아트 프리팹에 딸려 있던 <see cref="LineRenderer"/> 인데
    /// <b>비활성이고 참조하는 코드가 하나도 없었다</b>(2026-09-14 전수 확인). 여기서 처음 배선한다.
    /// 애니메이션 클립이 아니다 — 팩 전체에 Lazer/Laser/Beam 클립은 존재하지 않는다.
    ///
    /// 선 자체는 각 피어가 로컬로 그린다. 오가는 것은 <b>켜짐 여부와 각도</b>뿐이다. 벽에 막히는
    /// 계산도 각자 한다 — 같은 콜라이더를 보므로 결과가 같고, 그만큼 보낼 것이 없다.
    /// </summary>
    private void UpdateAimLaser()
    {
        if (aimLaser == null) return;

        // 예고 구간에만 보인다: 사거리 안 + 쿨 참 → 조준선 ON → telegraphSeconds 동안 추적 →
        // 멈추는 순간 발사(그때 _telegraphing 이 꺼지므로 선도 사라진다).
        if (!showAimLaser || !_laserVisible)
        {
            if (aimLaser.gameObject.activeSelf) aimLaser.gameObject.SetActive(false);
            return;
        }

        Vector3 origin = laserOrigin != null ? laserOrigin.position : headBone.position;
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
        if (!aimLaser.gameObject.activeSelf) aimLaser.gameObject.SetActive(true);
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
