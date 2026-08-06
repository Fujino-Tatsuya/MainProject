using UnityEngine;
using Unity.Netcode;
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
/// 대시 입력·오너 예측·서버 승인·충전 정합을 담당한다. (PLAN §6, §7, §9, §10 / W2·W4)
///
/// - 오너: Shift 입력 → 게이트 → 예측 충전 소비 → 대시 상태 진입 → 서버로 요청 RPC.
/// - 서버: 매 물리 tick 스냅샷 저장, 요청을 DashValidationPolicy로 검증(멱등)하고 권한 충전 소비 후 응답.
/// - 오너: 응답으로 예측 충전을 권한값에 정합. 거부/중단/이미종료면 대시를 멈춘다(위치 롤백은 v1 없음).
/// 오프라인(비네트워크)에서는 예측만 수행한다.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerStateController))]
public class PlayerDashController : NetworkBehaviour
{
    [Tooltip("대시 튜닝 원본(ScriptableObject). 없으면 대시가 비활성화된다.")]
    [SerializeField] private PlayerDashData dashData;
    [Tooltip("공용 이동 규칙(등판각 등). 미할당 시 60도 폴백.")]
    [SerializeField] private PlayerGameRuleData gameRule;
    [Tooltip("지면 판정 센서. 없으면 지면 게이트를 건너뛴다.")]
    [SerializeField] private PlayerGroundingSensor groundingSensor;

    private const float DefaultMaxWalkableSlopeAngle = 60f;

    private Player player;
    private PlayerInputReader input;
    private PlayerStateController stateController;
    private PlayerMovement movement;
    private StatusEffectController statusEffects;
    private PlayerInvulnerability invulnerability;
    private PlayerEncounterLock encounterLock;
    private PlayerLifeCycleController lifeCycle;

    private DashRuntimeConfig config;
    private DashChargeLedger predictedLedger;
    private bool ready;

    // 오너 요청/멱등 상태.
    private uint _nextRequestId = 1u;
    private uint _pendingRequestId;
    private bool _hasPending;
    private bool _serverSnapshotWarned;

    public bool DashEnabled => ready && config.DashEnabled;

    // HUD(W8)·디버그용 예측 충전 상태.
    public int PredictedCharge => predictedLedger != null ? predictedLedger.Count : 0;
    public int MaxCharge => predictedLedger != null ? predictedLedger.MaxCharge : 0;
    public double PredictedNextReadyTime => predictedLedger != null ? predictedLedger.NextReadyTime : 0.0;

    /// <summary>재충전 1회에 걸리는 시간(초). HUD 진행도의 분모.</summary>
    public float RechargeDuration => (float)config.RechargeDuration;

    /// <summary>충전이 1개 이상 남아 있는지(예측 기준). 다른 게이트(접지·CC·사망)는 보지 않는다.</summary>
    public bool IsReady => PredictedCharge > 0;

    /// <summary>
    /// 다음 충전 완료까지 남은 시간(초). 만충이면 0.
    ///
    /// ⚠️ HUD가 <see cref="PredictedNextReadyTime"/>에서 직접 빼면 안 되기 때문에 존재한다 —
    /// 그 값은 절대시각이고, 시간 원점이 NetworkClock인지 <see cref="Time.timeAsDouble"/>인지는
    /// <see cref="OwnerNow"/>만 안다. 도메인을 밖에서 다시 고르면 두 시계가 어긋나 남은 시간이 엉킨다.
    /// </summary>
    public float RemainingRecharge
    {
        get
        {
            if (predictedLedger == null)
                return 0f;

            double next = predictedLedger.NextReadyTime;
            if (double.IsInfinity(next))
                return 0f; // 만충 — 회복 타이머가 없다.

            return Mathf.Max(0f, (float)(next - OwnerNow()));
        }
    }

    private void Awake()
    {
        player = GetComponent<Player>();
        input = GetComponent<PlayerInputReader>();
        stateController = GetComponent<PlayerStateController>();
        movement = GetComponent<PlayerMovement>();
        statusEffects = GetComponent<StatusEffectController>();
        invulnerability = GetComponent<PlayerInvulnerability>();
        encounterLock = GetComponent<PlayerEncounterLock>();
        lifeCycle = GetComponent<PlayerLifeCycleController>();
        if (groundingSensor == null)
            groundingSensor = GetComponent<PlayerGroundingSensor>();

        BuildRuntime();
    }

    private void BuildRuntime()
    {
        if (dashData == null)
        {
            config = DashRuntimeConfig.Create(0.0, 0.0, 1, 0.0, 1, 0.0); // DashEnabled=false
            predictedLedger = new DashChargeLedger(1, 0.0, 0, OwnerNow());
            ready = true;
            Debug.LogWarning("[DashAlert] PlayerDashData가 할당되지 않아 대시를 비활성화합니다.", this);
            return;
        }

        config = dashData.CreateValidatedConfig();
        predictedLedger = new DashChargeLedger(config.MaxCharge, config.RechargeDuration, config.MaxCharge, OwnerNow());
        ready = true;

        if (!config.DashEnabled)
            Debug.LogWarning("[DashAlert] PlayerDashData 값이 비정상이라 대시를 비활성화합니다.", this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            if (PlayerDashValidationManager.Instance != null)
            {
                PlayerDashValidationManager.Instance.RegisterPlayer(NetworkObjectId, OwnerClientId, config, ServerNow());
            }
            else if (!_serverSnapshotWarned)
            {
                _serverSnapshotWarned = true;
                Debug.LogWarning("[DashAlert] PlayerDashValidationManager가 씬에 없어 대시 서버 검증이 비활성화됩니다.", this);
            }

            // 사망/영혼 게이트는 이 컴포넌트에서만 읽는다. 없으면 죽은 플레이어의 대시를 서버가 막지 못한다.
            if (lifeCycle == null)
            {
                Debug.LogWarning("[DashAlert] PlayerLifeCycleController가 없어 사망/영혼 상태를 대시 서버 검증에 " +
                                 "반영하지 못합니다(항상 살아있음으로 간주). Player 프리팹 배선을 확인하세요.", this);
            }
        }

        if (IsOwner)
        {
            // 예측 장부를 네트워크 시간 기준으로 재시작(만충).
            predictedLedger = new DashChargeLedger(config.MaxCharge, config.RechargeDuration, config.MaxCharge, OwnerNow());

            Edit.Log(
                $"[Dash] 오너 초기화 — 활성={DashEnabled} 속도={config.DashSpeed:F1} 지속={config.DashDuration:F3}s " +
                $"충전={config.MaxCharge} 재충전={config.RechargeDuration:F2}s " +
                $"시계={(ClockRunning ? "NetworkClock" : "Time.timeAsDouble")} " +
                $"접지센서={(groundingSensor != null ? "있음" : "없음(접지 게이트 생략)")}", this);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && PlayerDashValidationManager.Instance != null)
        {
            PlayerDashValidationManager.Instance.DeregisterPlayer(NetworkObjectId);
        }

        base.OnNetworkDespawn();
    }

    // ⚠️ Instance 유무가 아니라 IsRunning(세션 가동)으로 폴백을 판단한다.
    // NetworkClock은 세션이 안 돌면 0을 돌려주므로, 예전처럼 Instance만 보면 오프라인 Play에서
    // "멈춘 시계"로 Advance()가 돌아 충전이 영구히 회복되지 않았다(대시 1회만 되는 증상).
    private static bool ClockRunning => NetworkClock.Instance != null && NetworkClock.Instance.IsRunning;

    private double OwnerNow() => ClockRunning ? NetworkClock.Instance.GameLocalNow : Time.timeAsDouble;
    private double ServerNow() => ClockRunning ? NetworkClock.Instance.GameNow : Time.timeAsDouble;
    private double LocalTimeForRequest() => ClockRunning ? NetworkClock.Instance.LocalNow : Time.timeAsDouble;

    // 생명주기 상태를 대시 검증 입력(dead/soul)으로 변환한다. 서버에서만 읽는다.
    // ⚠️ player.CanMove로 대체할 수 없다 — Soul은 "이동은 되지만 대시는 금지"이기 때문이다.
    //    (PlayerLifeGameplayAccess.SoulAccess는 AllowsMovement=true,
    //     DashValidationPolicy는 Dead||Soul을 DeadOrSoul로 거부한다.)
    // lifeCycle이 없으면(대시 전용 테스트 씬 등) 살아있는 것으로 간주해 기존 동작을 유지한다.
    private bool IsDeadForDash =>
        lifeCycle != null &&
        (lifeCycle.State == PlayerLifeState.DeadPresentation ||
         lifeCycle.State == PlayerLifeState.PermanentDead);

    private bool IsSoulForDash => lifeCycle != null && lifeCycle.State == PlayerLifeState.Soul;

    private void Update()
    {
        if (player == null || !player.IsMovementAuthority)
            return;

        // 오너 예측 충전 회복.
        if (ready && predictedLedger != null)
            predictedLedger.Advance(OwnerNow());

        // 예측 무적을 대시 상태에 맞춘다(오너 로컬 Hurtbox 즉시 반영). 서버 무적은 승인 토큰이 확정.
        if (invulnerability != null && stateController != null)
            invulnerability.SetOwnerPredicted(stateController.CurrentState == PlayerActionState.Dash);
    }

    private void FixedUpdate()
    {
        // 서버는 매 물리 tick Player 상태 스냅샷을 저장한다.
        if (!IsSpawned || !IsServer || PlayerDashValidationManager.Instance == null)
            return;

        // 연출 잠금은 "행동 불가"로 스냅샷에 남긴다 — 요청 시점 스냅샷으로 검증하는 구조라
        // 잠금 구간에 찍힌 요청은 뒤늦게 도착해도 CrowdControlled로 거부된다.
        bool cinematicLocked = encounterLock != null && encounterLock.IsCinematicLocked;

        PlayerDashValidationManager.Instance.CaptureSnapshot(
            NetworkObjectId,
            ServerNow(),
            grounded: groundingSensor == null || groundingSensor.IsGrounded,
            dead: IsDeadForDash,
            soul: IsSoulForDash,
            crowdControlled: cinematicLocked || (statusEffects != null && statusEffects.BlocksMovement),
            landingProtected: false); // TODO: W5 착지 보호
    }

    /// <summary>Idle/Move 액션 입력에서 대시 우선으로 호출된다. 게이트 통과 시 예측 소비 + 대시 진입(+온라인이면 서버 요청).</summary>
    public bool TryBeginPredictedDash()
    {
        // ⚠️ 아래 5개 게이트는 전부 조용히 false를 돌려줬다. 그래서 "대시가 안 되는데 로그도 없다"가
        // 됐고, 원인을 서버 거부 쪽에서 찾다가 시간을 썼다. 실제로는 대시가 **시작조차 안 되는**
        // 경우가 여기 숨는다. 입력 1회당 한 줄이므로 스팸이 되지 않는다.
        if (!DashEnabled)
        {
            Edit.LogWarning($"[Dash] 시작 불가: 대시가 비활성입니다(dashData 미할당 또는 값 비정상). " +
                            $"ready={ready} config.DashEnabled={config.DashEnabled}", this);
            return false;
        }

        if (player == null || !player.IsMovementAuthority)
        {
            Edit.LogWarning("[Dash] 시작 불가: 이동 권한이 없습니다(오너가 아님).", this);
            return false;
        }

        if (!player.CanMove)
        {
            Edit.LogWarning($"[Dash] 시작 불가: CanMove=false (상태 {stateController?.CurrentState}). " +
                            "연출 잠금·CC·사망 게이트를 확인하세요.", this);
            return false;
        }

        if (groundingSensor != null && !groundingSensor.IsGrounded)
        {
            Edit.LogWarning("[Dash] 시작 불가: 접지 상태가 아닙니다(공중). PlayerGroundingSensor 판정 확인.", this);
            return false;
        }

        double now = OwnerNow();
        if (!predictedLedger.TryConsume(now))
        {
            // 가장 흔한 정상 거절 — 충전 1개/재충전 2초 설계라 연속 입력은 여기서 막힌다.
            Edit.Log($"[Dash] 시작 불가: 충전 없음 {predictedLedger.Count}/{predictedLedger.MaxCharge}, " +
                     $"다음 충전까지 {Mathf.Max(0f, (float)(predictedLedger.NextReadyTime - now)):F2}초.", this);
            return false;
        }

        Vector3 direction = ResolveDashDirection();
        bool started = stateController.BeginDash(direction, (float)config.DashSpeed, (float)config.DashDuration, BuildMotionSettings());
        if (!started)
        {
            // ⚠️ 여기까지 왔다면 예측 충전은 이미 소비됐다(위 TryConsume). 상태 진입만 거부되면
            // 대시는 안 나가고 재충전 2초만 흐르는 "됐다 말았다"가 된다. 거부 사유는 BeginDash가 남긴다.
            Edit.LogWarning(
                $"[Dash] 시작 불가: 상태 진입 거부(현재상태={stateController.CurrentState}, " +
                $"연출잠금={stateController.IsCinematicLocked}). 예측 충전은 이미 소비돼 " +
                $"{predictedLedger.Count}/{predictedLedger.MaxCharge}입니다.", this);
            return false;
        }

        // 성공 경로도 한 줄 남긴다 — 거부 로그만 있으면 "쿨타임이 안 돈다"를 진단할 때
        // 실제 간격과 시계 출처를 알 수 없어 원인을 거꾸로 짚게 된다. 입력 1회당 한 줄.
        Edit.Log($"[Dash] 시작 — 남은충전 {predictedLedger.Count}/{predictedLedger.MaxCharge}, " +
                 $"재충전 {config.RechargeDuration:F2}s, now={now:F3}, " +
                 $"시계={(ClockRunning ? "NetworkClock" : "Time.timeAsDouble")}", this);

        // 온라인이면 서버 승인을 요청한다. 오프라인(테스트)에서는 예측만.
        if (IsSpawned && IsOwner)
        {
            uint requestId = _nextRequestId++;

            // 이전 요청의 응답이 아직 안 왔는데 새 요청을 보내면, 뒤늦게 도착한 이전 응답은
            // RespondDashClientRpc에서 "pending 아님"으로 버려진다(취소·환불이 유실될 수 있다).
            if (_hasPending)
            {
                Edit.LogWarning(
                    $"[Dash] 이전 요청 응답 대기 중 새 요청 전송 — pending {_pendingRequestId} → {requestId}. " +
                    "이전 응답은 도착해도 무시되므로 그 요청의 취소/환불이 유실될 수 있습니다.", this);
            }

            _pendingRequestId = requestId;
            _hasPending = true;

            double requestLocalTime = LocalTimeForRequest();
            Edit.Log(
                $"[Dash] 서버 요청 전송 — requestId={requestId} localTime={requestLocalTime:F3} " +
                $"방향=({direction.x:F2}, {direction.z:F2}) 예측Revision={predictedLedger.Revision}", this);

            SubmitDashRequestServerRpc(requestId, requestLocalTime, direction.x, direction.z, predictedLedger.Revision);
        }
        else
        {
            Edit.Log("[Dash] 오프라인/비스폰 — 서버 검증 없이 예측만 수행합니다(서버 취소 경로 없음).", this);
        }

        return true;
    }

    [ServerRpc]
    private void SubmitDashRequestServerRpc(uint requestId, double clientLocalTime, float directionX, float directionZ, uint knownChargeRevision, ServerRpcParams rpcParams = default)
    {
        if (PlayerDashValidationManager.Instance == null)
        {
            Edit.LogWarning(
                $"[Dash/서버] 거부: PlayerDashValidationManager가 없습니다(requestId={requestId}). " +
                "MainGame 기준 씬에 매니저가 배치돼 있는지 확인하세요. 사유는 ConfigDisabled로 나갑니다.", this);
            RespondDashClientRpc(requestId, false, (int)DashRejectReason.ConfigDisabled, 0.0, false, 0, 0.0, 0u, 0u, OwnerClientRpcParams());
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        double rttSeconds = GetSenderRttSeconds(senderClientId, out bool rttAvailable);

        if (!rttAvailable)
        {
            Edit.LogWarning(
                $"[Dash/서버] RTT를 읽을 수 없습니다(sender={senderClientId}, requestId={requestId}) — " +
                "NetworkTransport가 null입니다. RttUnavailable로 거부됩니다.", this);
        }

        DashServerResponse response = PlayerDashValidationManager.Instance.ValidateRequest(
            NetworkObjectId, senderClientId, requestId,
            clientLocalTime, directionX, directionZ,
            ServerNow(), rttSeconds, rttAvailable,
            currentDead: IsDeadForDash,
            currentSoul: IsSoulForDash,
            currentCrowdControlled:
                (encounterLock != null && encounterLock.IsCinematicLocked) ||
                (statusEffects != null && statusEffects.BlocksMovement));

        // 승인되고 실제 대시가 진행될 때만 서버 권한 무적을 남은 대시 시간만큼 부여한다. (PLAN §11)
        if (invulnerability != null &&
            response.IsApproved &&
            !response.WasInterruptedByServerState &&
            response.RemainingServerDuration > 0.0)
        {
            invulnerability.AddServerToken(InvulnerabilityCause.Dash, response.RemainingServerDuration);
        }

        RespondDashClientRpc(
            requestId, response.IsApproved, (int)response.Reason, response.RemainingServerDuration,
            response.WasInterruptedByServerState, response.AuthoritativeChargeCount, response.NextChargeReadyServerTime,
            response.ChargeEpoch, response.ChargeRevision, OwnerClientRpcParams());
    }

    [ClientRpc]
    private void RespondDashClientRpc(
        uint requestId, bool approved, int reason, double remainingServerDuration, bool interrupted,
        int authoritativeChargeCount, double nextChargeReadyServerTime, uint chargeEpoch, uint chargeRevision,
        ClientRpcParams rpcParams = default)
    {
        if (!IsOwner || predictedLedger == null)
            return;

        bool isPendingResponse = _hasPending && requestId == _pendingRequestId;

        // 대기 중이 아닌 응답은 충전 정합만 하고 대시 종료 판단에서 제외된다 —
        // 이 줄이 없으면 "서버가 거부했는데 대시가 계속됐다"를 설명할 수 없다.
        if (!isPendingResponse)
        {
            Edit.Log(
                $"[Dash] 대기 중이 아닌 응답(무시): requestId={requestId}, pending={(_hasPending ? _pendingRequestId.ToString() : "없음")} — " +
                $"승인={approved} 사유={(DashRejectReason)reason}. 충전 정합만 반영하고 대시 종료 판단은 건너뜁니다.", this);
        }

        if (isPendingResponse && !approved)
        {
            // 거부 = 우리 예측 소비가 무효였다는 뜻. Revision 가드를 우회해 권한값으로 되돌린다.
            // ⚠️ SyncToAuthoritative로는 이 경로가 항상 무시된다(오너 Revision이 더 높음) →
            // 오너만 충전 1개와 재충전 시간을 잃어 "쿨타임이 안 도는" 것처럼 보였다.
            predictedLedger.ForceAdoptAuthoritative(
                authoritativeChargeCount, chargeEpoch, chargeRevision, OwnerNow(),
                RemainingToReadyInOwnerDomain(nextChargeReadyServerTime));
        }
        else
        {
            // 권한 충전으로 예측 정합(더 최신 Epoch/Revision만 채택).
            predictedLedger.SyncToAuthoritative(authoritativeChargeCount, chargeEpoch, chargeRevision, OwnerNow());
        }

        if (isPendingResponse)
        {
            _hasPending = false;

            // 거부/중단/이미 종료면 진행 중인 예측 대시를 멈춘다(위치 롤백은 v1 없음).
            if (!approved || interrupted || remainingServerDuration <= 0.0)
            {
                // 어느 조건에 걸렸는지 명시한다 — 세 원인의 대처가 완전히 다르다.
                string cancelCause = !approved
                    ? $"서버 거부({(DashRejectReason)reason})"
                    : interrupted
                        ? "승인 후 현재상태 중단(사망/소울/CC)"
                        : "승인했지만 남은시간 0(RTT가 대시 지속시간보다 큼 / 시각 추정 어긋남)";

                Edit.LogWarning($"[Dash] 취소 원인 분류: {cancelCause} (requestId={requestId})", this);
                // ⚠️ 조용히 끝내면 안 된다. 호스트에서는 ServerRpc→ClientRpc 왕복이 사실상 같은 프레임이라
                // PlayerDashState.Tick이 변위를 한 번도 적용하기 전에 상태가 끝난다. 증상은
                // "대시 애니메이션은 뜨는데 이동이 없다"로만 나타나고, 로그가 없으면 거부 사유를
                // 추적할 방법이 없다(첫 대시만 되고 이후 안 되는 현상의 원인이 여기 숨는다).
                Edit.LogWarning(
                    $"[Dash] 서버가 대시를 취소했습니다 — approved={approved} / " +
                    $"reason={(DashRejectReason)reason} / interrupted={interrupted} / " +
                    $"남은시간={remainingServerDuration:F3}s / 권한충전={authoritativeChargeCount} / " +
                    $"환불후 예측충전={predictedLedger.Count}/{predictedLedger.MaxCharge}", this);

                // 이미 대시가 아니면(정상 종료 후 응답 도착) EndDash가 "무효" 로그를 남긴다.
                stateController.EndDash();
            }
            else
            {
                Edit.Log(
                    $"[Dash] 서버 승인 — requestId={requestId} 남은시간={remainingServerDuration:F3}s " +
                    $"권한충전={authoritativeChargeCount} 예측충전={predictedLedger.Count}/{predictedLedger.MaxCharge} " +
                    $"(Epoch={chargeEpoch} Revision={chargeRevision})", this);
            }
        }
    }

    /// <summary>
    /// 서버 도메인 "다음 충전 완료시각"을 오너 도메인 잔여시간으로 환산한다.
    /// 두 시계(GameNow / GameLocalNow)는 도메인만 다르고 진행 속도는 같으므로 차이만 옮긴다.
    /// 만충(+Inf)/비정상 값은 0.
    /// </summary>
    private double RemainingToReadyInOwnerDomain(double nextChargeReadyServerTime)
    {
        if (double.IsNaN(nextChargeReadyServerTime) || double.IsInfinity(nextChargeReadyServerTime))
            return 0.0;

        return System.Math.Max(0.0, nextChargeReadyServerTime - ServerNow());
    }

    private ClientRpcParams OwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
    }

    // 원격 Client는 Transport RTT(ms) 사용. Host 자신(로컬)은 0이 정상. 원격 RTT가 0/비정상이면 안전 거부. (PLAN §9)
    private double GetSenderRttSeconds(ulong senderClientId, out bool rttAvailable)
    {
        if (senderClientId == NetworkManager.ServerClientId || senderClientId == NetworkManager.LocalClientId)
        {
            rttAvailable = true;
            return 0.0;
        }

        var transport = NetworkManager != null && NetworkManager.NetworkConfig != null
            ? NetworkManager.NetworkConfig.NetworkTransport
            : null;
        if (transport == null)
        {
            rttAvailable = false;
            return 0.0;
        }

        // localhost/MPPM에서는 원격 RTT도 0으로 보고될 수 있다. 0은 "지연 0"으로 유효 처리하고,
        // Transport가 있으면 사용 가능으로 본다(음수만 0으로 보정). 이전엔 0을 거부해 클라 대시가 전부 중단됐다.
        double rttSeconds = transport.GetCurrentRtt(senderClientId) / 1000.0;
        if (rttSeconds < 0.0)
            rttSeconds = 0.0;
        rttAvailable = true;
        return rttSeconds;
    }

    /// <summary>서버 권한 충전을 1개로 강제 초기화(생존 복귀/부활 시). (PLAN §10)</summary>
    public void ServerResetChargeToOne()
    {
        if (IsServer && PlayerDashValidationManager.Instance != null)
            PlayerDashValidationManager.Instance.ForceReset(NetworkObjectId, 1, ServerNow());
    }

    /// <summary>오너 예측 충전을 1개로 강제 초기화. 다음 충전 진행도는 0부터.</summary>
    public void OwnerResetChargeToOne()
    {
        if (player != null && player.IsMovementAuthority && predictedLedger != null)
            predictedLedger.ForceReset(1, OwnerNow());
    }

    private DashMotionSettings BuildMotionSettings()
    {
        float walkableAngle = gameRule != null ? gameRule.MaxWalkableSlopeAngle : DefaultMaxWalkableSlopeAngle;
        return new DashMotionSettings(
            dashData.CollisionSkin,
            dashData.MaxSweepIterations,
            walkableAngle,
            dashData.DashObstacleMask);
    }

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
