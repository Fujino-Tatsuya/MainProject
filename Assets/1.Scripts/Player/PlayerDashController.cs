using UnityEngine;
using BeaverLobby.Player.Dash;

/// <summary>대시 상태가 이동 중 충돌 해결에 쓰는 튜닝값 묶음. (W3)</summary>
public readonly struct DashMotionSettings
{
    public readonly float CollisionSkin;
    public readonly int MaxSweepIterations;
    public readonly float MaxWalkableSlopeAngle;
    public readonly LayerMask ObstacleMask;

    public DashMotionSettings(float collisionSkin, int maxSweepIterations, float maxWalkableSlopeAngle, LayerMask obstacleMask)
    {
        CollisionSkin = Mathf.Max(0f, collisionSkin);
        MaxSweepIterations = Mathf.Max(1, maxSweepIterations);
        MaxWalkableSlopeAngle = Mathf.Clamp(maxWalkableSlopeAngle, 1f, 89f);
        ObstacleMask = obstacleMask;
    }
}

/// <summary>
/// 대시 입력·오너 예측·상태 진입을 담당한다. (PLAN §6, §7 / W2)
///
/// W2 범위: 로컬 오너 예측만. Shift 입력 → 예측 충전 소비 → 대시 상태 진입.
/// 서버 승인(RPC)·권한 충전 장부 동기화·무적은 W4/W5에서 추가한다.
/// 프리팹에 미부착이면 PlayerStateController가 대시를 트리거하지 않는다(안전).
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerStateController))]
public class PlayerDashController : MonoBehaviour
{
    [Tooltip("대시 튜닝 원본(ScriptableObject). 없으면 대시가 비활성화된다.")]
    [SerializeField] private PlayerDashData dashData;
    [Tooltip("공용 이동 규칙(등판각 등). 미할당 시 60도 폴백.")]
    [SerializeField] private PlayerGameRuleData gameRule;
    [Tooltip("지면 판정 센서. 없으면 지면 게이트를 건너뛴다(W2 한정).")]
    [SerializeField] private PlayerGroundingSensor groundingSensor;

    private const float DefaultMaxWalkableSlopeAngle = 60f;

    private Player player;
    private PlayerInputReader input;
    private PlayerStateController stateController;
    private PlayerMovement movement;

    private DashRuntimeConfig config;
    private DashChargeLedger predictedLedger;
    private bool ready;

    /// <summary>설정이 유효해 대시가 활성인지.</summary>
    public bool DashEnabled => ready && config.DashEnabled;

    // HUD(W8)·디버그용 예측 충전 상태.
    public int PredictedCharge => predictedLedger != null ? predictedLedger.Count : 0;
    public int MaxCharge => predictedLedger != null ? predictedLedger.MaxCharge : 0;
    public double PredictedNextReadyTime => predictedLedger != null ? predictedLedger.NextReadyTime : 0.0;

    private void Awake()
    {
        player = GetComponent<Player>();
        input = GetComponent<PlayerInputReader>();
        stateController = GetComponent<PlayerStateController>();
        movement = GetComponent<PlayerMovement>();
        if (groundingSensor == null)
            groundingSensor = GetComponent<PlayerGroundingSensor>();

        BuildRuntime();
    }

    private void BuildRuntime()
    {
        if (dashData == null)
        {
            config = DashRuntimeConfig.Create(0.0, 0.0, 1, 0.0, 1, 0.0); // DashEnabled=false
            predictedLedger = new DashChargeLedger(1, 0.0, 0, Now());
            ready = true;
            Debug.LogWarning("[DashAlert] PlayerDashData가 할당되지 않아 대시를 비활성화합니다.", this);
            return;
        }

        config = dashData.CreateValidatedConfig();
        // 예측 장부는 만충으로 시작(스폰 시 완충). 서버 권한 동기화는 W4.
        predictedLedger = new DashChargeLedger(config.MaxCharge, config.RechargeDuration, config.MaxCharge, Now());
        ready = true;

        if (!config.DashEnabled)
            Debug.LogWarning("[DashAlert] PlayerDashData 값이 비정상이라 대시를 비활성화합니다.", this);
    }

    // W2 예측은 오너 로컬 시간을 쓴다. W4에서 NetworkClock 기반으로 서버와 정합시킨다.
    private static double Now() => Time.timeAsDouble;

    private void Update()
    {
        // 예측 충전 회복은 오너(이동 권한)에서만 진행한다.
        if (ready && predictedLedger != null && player != null && player.IsMovementAuthority)
            predictedLedger.Advance(Now());
    }

    /// <summary>
    /// Idle/Move 상태의 액션 입력 처리에서 대시 우선으로 호출된다.
    /// 모든 게이트 통과 시에만 예측 충전을 소비하고 대시 상태로 진입한다.
    /// </summary>
    public bool TryBeginPredictedDash()
    {
        if (!DashEnabled)
            return false;
        if (player == null || !player.IsMovementAuthority)
            return false;
        if (!player.CanMove) // Idle/Move 상태 + CC(BlocksMovement) 아님
            return false;
        if (groundingSensor != null && !groundingSensor.IsGrounded) // 지면에서만 시작 (불변식 5)
            return false;

        // 게이트를 모두 통과한 뒤에만 예측 충전을 소비한다(실패 시 충전 보존).
        if (!predictedLedger.TryConsume(Now()))
            return false;

        Vector3 direction = ResolveDashDirection();
        return stateController.BeginDash(direction, (float)config.DashSpeed, (float)config.DashDuration, BuildMotionSettings());
    }

    private DashMotionSettings BuildMotionSettings()
    {
        // dashData는 DashEnabled 게이트를 통과한 시점에서 non-null이 보장된다.
        // 등판각은 공용 규칙(GroundingSensor와 단일 소스)에서 읽는다. 미할당 시 폴백.
        float walkableAngle = gameRule != null ? gameRule.MaxWalkableSlopeAngle : DefaultMaxWalkableSlopeAngle;
        return new DashMotionSettings(
            dashData.CollisionSkin,
            dashData.MaxSweepIterations,
            walkableAngle,
            dashData.DashObstacleMask);
    }

    // 이동 입력이 있으면 입력 방향, 없으면 현재 정면. (PLAN §7)
    private Vector3 ResolveDashDirection()
    {
        if (movement != null)
        {
            Vector3 inputDir = movement.GetInputWorldDirection();
            if (inputDir.sqrMagnitude > 0.0001f)
                return inputDir;
            return movement.CurrentFacing;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }
}
