using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Airborne CC 종료 후 착지 보호(경직 + 무적 + Blink, 동일 지속). (PLAN §12 / W5-b)
///
/// - 서버가 Airborne 상태이상의 종료를 감지해 Grounded면 즉시, 아직 공중이면 마지막 Airborne 종료 후
///   최대 airborneFailsafeSeconds 대기 후 착지 보호를 1회 시작한다. 새 Airborne이 들어오면 Fail-safe 취소.
/// - 보호 = PlayerInvulnerability(LandingProtection 토큰) + Stunned 상태이상(자기 출처) + Blink, 모두 같은 지속.
/// - <see cref="BeginProtection"/>는 부활/추락 복귀 등 다른 원인도 재사용할 수 있는 서버 프리미티브다.
/// - Blink는 renderer 토글로 전 피어에 표시(서버 권한 종료시각 복제). 대시 무적은 Blink를 쓰지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInvulnerability))]
public sealed class PlayerLandingProtection : NetworkBehaviour
{
    [SerializeField] private PlayerInvulnerability invulnerability;
    [SerializeField] private StatusEffectController statusEffects;
    [SerializeField] private PlayerGroundingSensor grounding;

    [Header("Landing Protection")]
    [SerializeField, Min(0f)] private float landingProtectionDuration = 1.0f;
    [SerializeField, Min(0f)] private float airborneFailsafeSeconds = 3.0f;

    [Header("Blink")]
    [Tooltip("Blink 대상 Renderer 루트. 비어 있으면 자식 전체에서 찾는다.")]
    [SerializeField] private GameObject blinkVisualRoot;
    [SerializeField, Min(0.02f)] private float blinkInterval = 0.1f;

    // 서버 권한 Blink 종료시각(GameNow). 전 피어가 이 시각까지 깜빡인다.
    private readonly NetworkVariable<double> _blinkUntil =
        new NetworkVariable<double>(0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 Airborne 추적/Fail-safe.
    private bool _wasAirborne;
    private bool _failsafePending;
    private double _failsafeDeadline;

    // Blink 렌더링(전 피어).
    private Renderer[] _blinkRenderers;
    private bool _blinkOff;
    private double _nextBlinkToggle;

    private double Now() => NetworkClock.Instance != null ? NetworkClock.Instance.GameNow : Time.timeAsDouble;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkDespawn()
    {
        // 사망/Despawn 시 Blink를 정리한다(사망은 모든 임시 Blink 제거).
        SetBlinkVisible(true);
        base.OnNetworkDespawn();
    }

    /// <summary>서버 전용. 보호(무적 + 선택적 경직 + Blink)를 duration 동안 시작한다. 부활/추락 복귀 등 재사용.</summary>
    public void BeginProtection(InvulnerabilityCause cause, float duration, bool applyStun)
    {
        if (!IsSpawned || !IsServer || duration <= 0f)
            return;

        if (invulnerability != null)
            invulnerability.AddServerToken(cause, duration);

        if (applyStun && statusEffects != null)
            statusEffects.Apply(StatusEffectType.Stunned, duration, NetworkObjectId);

        _blinkUntil.Value = Now() + duration;
    }

    private void Update()
    {
        if (IsServer)
            TickAirborneLanding();

        TickBlink();
    }

    // ── 서버: Airborne 종료 → 착지 보호 트리거 + Fail-safe ──
    private void TickAirborneLanding()
    {
        if (statusEffects == null)
            return;

        bool isAirborne = statusEffects.Has(StatusEffectType.Airborne);
        bool grounded = grounding == null || grounding.IsGrounded;
        double now = Now();

        if (isAirborne)
        {
            // 새/진행 중 Airborne이면 Fail-safe를 취소하고 재시작 대기 상태로 둔다.
            _failsafePending = false;
        }
        else if (_wasAirborne)
        {
            // 이번 프레임에 Airborne이 끝났다.
            if (grounded)
                BeginProtection(InvulnerabilityCause.LandingProtection, landingProtectionDuration, applyStun: true);
            else
            {
                _failsafePending = true;
                _failsafeDeadline = now + airborneFailsafeSeconds;
            }
        }

        if (_failsafePending)
        {
            // 착지하면 즉시, 3초 초과면 공중에서라도 1회 보호 시작. (PLAN §12)
            if (grounded || now >= _failsafeDeadline)
            {
                _failsafePending = false;
                BeginProtection(InvulnerabilityCause.LandingProtection, landingProtectionDuration, applyStun: true);
            }
        }

        _wasAirborne = isAirborne;
    }

    // ── 전 피어: Blink 렌더러 토글 ──
    private void TickBlink()
    {
        bool blinking = Now() < _blinkUntil.Value;

        if (!blinking)
        {
            if (_blinkOff)
                SetBlinkVisible(true);
            _nextBlinkToggle = 0.0;
            return;
        }

        double now = Now();
        if (now >= _nextBlinkToggle)
        {
            _nextBlinkToggle = now + Mathf.Max(0.02f, blinkInterval);
            SetBlinkVisible(_blinkOff); // 토글
        }
    }

    private void SetBlinkVisible(bool visible)
    {
        EnsureBlinkRenderers();
        _blinkOff = !visible;
        if (_blinkRenderers == null)
            return;

        for (int i = 0; i < _blinkRenderers.Length; i++)
        {
            if (_blinkRenderers[i] != null)
                _blinkRenderers[i].enabled = visible;
        }
    }

    private void EnsureBlinkRenderers()
    {
        if (_blinkRenderers != null)
            return;

        GameObject root = blinkVisualRoot != null ? blinkVisualRoot : gameObject;
        _blinkRenderers = root.GetComponentsInChildren<Renderer>(true);
    }

    private void ResolveReferences()
    {
        if (invulnerability == null)
            invulnerability = GetComponent<PlayerInvulnerability>();

        if (statusEffects == null)
            statusEffects = GetComponent<StatusEffectController>();

        if (grounding == null)
            grounding = GetComponent<PlayerGroundingSensor>();
    }
}
