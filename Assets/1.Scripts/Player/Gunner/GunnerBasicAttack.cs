using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 기본 공격 — 준비 동작 → 레이저 1발. 버튼을 누르고 있거나(홀드) 입력 창 안에서 클릭하면 다음 발을 잇는다
/// (character_gunner.md §4, PLAN-gunner.md G3).
///
/// 오너 예측(할 일 256): 오너가 시작·발사·종료를 주도하고 누르는 즉시 준비 동작, 발사 순간 즉시 발사 연출(상체 Fire·레이저 — 끝점은
/// 피해 없는 예측 직선)을 낸다. 서버는 시작 통지(NotifyStartRpc)로 공격 상태를 따라가고, 발마다 발사 요청(FireShotRpc — 총구 위치·방향)을
/// 공용 2차 검증(<see cref="BasicAttackServerVerifier.VerifyFixedInterval"/> — 첫 발은 준비 동작, 그 뒤는 발사 간격 ÷ 최근 최대 공속 + 여유)과
/// 과열(<see cref="GunnerHeat"/> 원본)로 거른 뒤 판정(첫 대상 1체·특성)·과열 증가 → 오너 뺀 피어 연출(ShotRpc). 걸린 발은 레이저만, 데미지 없음.
/// 과열은 오너가 1차 예측한다(복제 기준점 + 서버 미반영 자기 발, <see cref="GunnerHeatPrediction"/>) — 예측상 과열이면 쏘지 않는다.
/// 이어지는 발의 조건(오너 판단): 누르고 있거나, 입력 창(직전 발사 + ComboWindowOpen~Close) 안에서 클릭해 예약했을 때.
/// 어느 쪽이든 준비 동작은 첫 발만이다. 둘 다 아니거나 과열되면 후속 동작(ShotRecovery)까지 마무리하고 끝난다.
/// 창은 애니 이벤트가 아니라 데이터의 시간 값이다. 공격 중 이동 불가. 스킬 입력(Q·E·R·우클릭)은 공격을 끊지 않는다 — 상태기계가 '공격 뒤 스킬'로
/// 예약하고 <see cref="FinishWithoutChaining"/> 을 부르면 현재 발은 끝까지 가고 다음 발만 잇지 않아(뗀 것처럼) 정상 종료한다.
/// 끝나면 Idle/Move 첫 틱에 예약 스킬이 기존 경로로 요청된다(<see cref="ReservesSkillAfterAttack"/>, 할 일 44).
/// 홀드 시작(<see cref="StartsOnHold"/>): 스킬 등으로 공격이 끝난 뒤에도 좌클릭을 누르고 있으면 Idle/Move 에서 다시 시작한다.
/// 방향(할 일 27, <see cref="GunnerAttackFacingModel"/>): 발 잠금 중에는 몸이 돌지 않고, 그 밖에는 커서 쪽으로
/// <see cref="GunnerBasicAttackData.BetweenShotTurnSpeed"/> 로 돈다. 발 잠금 = 첫 발은 공격 시작 ~ 발사 + ShotLockTail,
/// 이어지는 발은 발사 − ShotLockLead ~ 발사 + ShotLockTail(잠근 순간의 몸 방향). 🔴 잠금은 오너 시계만으로 돈다 —
/// 오너가 방향을 발사 요청에 실어 보내고 서버는 그대로 쏘기만 한다(서버는 몸을 돌리거나 잠금을 검증하지 않는다, 2026-10-13).
/// 문서: Docs/tech/owner-predicted-basic-attack.md.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(GunnerHeat))]
[RequireComponent(typeof(GunnerBeamAttack))]
public class GunnerBasicAttack : BaseNetworkBehaviour, IPlayerBasicAttack, IPlayerAttackSpeedAnimatorUser
{
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int EmptyHash = Animator.StringToHash("Empty");
    private static readonly int FireSpeedHash = Animator.StringToHash("FireSpeed");

    // 오너 발사 요청이 끊겼을 때(연결 문제 등) 서버가 스스로 끝내는 여유(발사 간격 배수) — 첫 발 대기.
    private const float StallIntervals = 3f;
    // 서버 안전망 — 마지막 발 + 후속 동작 + 이만큼 지나도 종료 통지가 없으면 서버가 혼자 끝낸다(오너 끊김·패킷 유실).
    private const float ServerFollowGraceSeconds = 1f;
    // 발사 요청의 총구 위치가 서버가 보는 총구와 이만큼(m) 넘게 다르면 경고만 남긴다.
    private const float OwnerPositionWarnDistance = 2f;

    [SerializeField] private GunnerBasicAttackData data;
    [SerializeField] private Animator animator;

    private Player player;
    private PlayerInputReader input;
    private PlayerMovement movement;
    private PlayerAimIndicator aim;
    private GunnerHeat heat;
    private GunnerBeamAttack beam;
    private GunnerBeamView beamView;

    // 🔴 판정선의 출발점이다 — 연출용이 아니다(2026-10-04).
    //    비워 두면 예전대로 루트 + 위로 MuzzleHeight 를 쓴다. 그 자리는 **총이 아니라 몸통**이라
    //    빔이 배에서 나가는 것처럼 보였고, 벽 뒤에서 쏠 때 판정 시작점도 실제 총구와 어긋났다.
    [Tooltip("레이가 출발할 총구. 비우면 루트 + 위로 MuzzleHeight(예전 동작)")]
    [SerializeField] private Transform muzzle;
    // 발사 상태가 실제로 트는 클립 길이. -1 = 아직 모름(상체가 발사 상태에 들어간 뒤 LateUpdate 가 읽는다), 0 = 클립 없음.
    private float fireClipLength = -1f;

    // 전 피어 공통 런타임
    private bool active;
    private float startTime;
    private readonly GunnerAttackFacingModel facing = new GunnerAttackFacingModel();
    private bool endingGracefully;

    // 오너(체인 주도)
    private bool firstShotSent;
    private float ownerLastShotTime = -1f; // 오너가 마지막 발을 쏜 시각(오너 시계)
    private bool ownerQueued;              // 오너 입력 창 예약
    private bool ownerPreLocked;           // 다음 발을 위해 발사 전에 미리 잠갔는가(오너 시계)
    private bool ownerFinishing;           // 스킬 예약 — 지금 발로 마무리(누름·창 예약 무시, 할 일 44)
    private byte comboSerial;              // 공격(시작~종료) 일련번호 — 서버 끊기·종료 통지가 옛 공격을 건드리지 않게
    private readonly GunnerHeatPrediction heatPrediction = new GunnerHeatPrediction();

    // 서버(원격 오너를 따라가는 쪽)
    private byte serverComboSerial;
    private double serverStartUnscaled;
    private float serverLastShotTime = -1f;
    private float serverFollowFallbackTime;
    private readonly BasicAttackServerVerifier verifier = new BasicAttackServerVerifier();

    // 특성(할 일 127) — 관통 탄도·집중 포화·이중 출력. 서버에서만 쓴다.
    private PlayerTraitEffects traitEffects;
    private GunnerBeamAttack.TargetHitHook focusFireHook;
    private readonly List<TraitDerivedAttack> derivedBuffer = new List<TraitDerivedAttack>();
    private readonly GunnerDelayedShots extraShots = new GunnerDelayedShots();
    private readonly List<GunnerDerivedShot> dueExtraShots = new List<GunnerDerivedShot>();
    private bool lifeSubscribed;

    public bool CanStartApprovedAttack => data != null && heat != null && !heat.IsOverheated;
    public GunnerBasicAttackData Data => data;

    /// <summary>
    /// 한 발의 출발점 — 총구, 비었으면 루트 + 위로 MuzzleHeight. 판정은 여기서 Range·반경 BeamWidth/2 로 훑는다.
    /// 오너 발사(원격 오너는 발사 요청에 실음)와 특성 랩 공격 범위 기즈모(할 일 152)가 같이 쓴다. data 가 있을 때만 부른다.
    /// </summary>
    public Vector3 ShotOrigin => muzzle != null
        ? muzzle.position
        : transform.position + Vector3.up * data.MuzzleHeight;

    // 공용 대시 우선(D1·D2) — 준비 중 끊기면 발사·과열 없음, 발사 후면 이미 쏜 발은 그대로이고 후속 동작만 끊긴다.
    // 홀드 연사 중에도 언제든 끊는다(할 일 9).
    public bool CanBeCanceledByDash => true;

    // 홀드 연사 캐릭터 — 누르고 있으면 시작하고, 공격 중 스킬 입력은 지금 발 마무리 뒤로 예약된다(할 일 44).
    public bool StartsOnHold => true;
    public bool ReservesSkillAfterAttack => true;

    // 이어지는 발 조건 — 누르고 있거나 창 예약. 스킬이 예약됐으면 무시한다(오너 시계).
    private bool OwnerChainsNextShot =>
        PlayerBasicAttackInputRules.ShouldChainNextShot(input.AttackHeld, ownerQueued, ownerFinishing);

    // 서버 처리(판정·과열 증가·특성)를 이 인스턴스가 직접 하는가 — 호스트·오프라인.
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;
    // 시작·발사·종료를 주도하는 쪽 — 오너(호스트 오너 포함)·오프라인.
    private bool IsChainAuthority => !IsNetworkActive || IsOwner;

    // 오너가 보는 과열 — 호스트·오프라인은 원본 그대로, 원격 오너는 1차 예측(복제 기준점 + 미반영 자기 발).
    private bool OwnerOverheated =>
        heat != null && (HasGameplayAuthority ? heat.IsOverheated : heat.PredictOverheated(heatPrediction));

    private void Awake()
    {
        player = GetComponent<Player>();
        input = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aim = GetComponent<PlayerAimIndicator>();
        heat = GetComponent<GunnerHeat>();
        beam = GetComponent<GunnerBeamAttack>();
        beamView = GetComponent<GunnerBeamView>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        focusFireHook = ApplyFocusFire;
    }

    private void OnEnable()
    {
        if (!lifeSubscribed)
        {
            PlayerLifeCycleController.ServerLifeStateChanged += HandleLifeStateChanged;
            lifeSubscribed = true;
        }
    }

    // 씬 종료·비활성 — 예약한 추가 레이저를 버린다(명세 §9).
    private void OnDisable()
    {
        if (lifeSubscribed)
        {
            PlayerLifeCycleController.ServerLifeStateChanged -= HandleLifeStateChanged;
            lifeSubscribed = false;
        }

        extraShots.Clear();
        heatPrediction.Clear();
    }

    // 사망(살아 있음 → 그 밖) — 예약한 추가 레이저 취소. 발수 카운터는 공용 장부(PlayerTraitEffects.ClearTransientServer)가 0 으로 돌린다.
    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (player != null && clientId == player.OwnerClientId && previous == PlayerLifeState.Alive && current != PlayerLifeState.Alive)
            extraShots.Clear();
    }

    // ── IPlayerBasicAttack ───────────────────────────────────────────────

    // 오너는 예측 과열로 거부 입력을 버린다(할 일 44·256).
    public bool IsOutOfResource => heat != null && (IsChainAuthority ? OwnerOverheated : heat.IsOverheated);

    // ── 공격속도(할 일 91 T5) ────────────────────────────────────────────
    // 거너 평타 타이밍은 클립 이벤트가 아니라 데이터 시간 값이라 그 값들을 배율로 나눈다(준비·발사 간격·후속 동작·입력 창·발 잠금).
    // 서버·오너가 같은 배율(복제된 상태이상) — 배율이 바뀌는 순간만 복제 지연만큼 어긋나고, 서버 2차 검증(최근 0.5초 최대 공속 + 여유)이 받아 준다.
    // 재생 속도는 공격 중(전 피어 시작~종료) Animator.speed 로 같이 탄다 — FireSpeed 는 배율 전 발사 간격 기준이라 두 번 곱하지 않는다.
    public bool ScalesAnimatorWithAttackSpeed => active;

    private float AttackSpeedRate => player != null ? player.AttackSpeedRate : 1f;
    private float WindupDuration => PlayerAttackSpeedRules.Duration(data.WindupDuration, AttackSpeedRate);
    private float FireInterval => PlayerAttackSpeedRules.Duration(data.FireInterval, AttackSpeedRate);
    private float ShotRecovery => PlayerAttackSpeedRules.Duration(data.ShotRecovery, AttackSpeedRate);
    private float ComboWindowOpen => PlayerAttackSpeedRules.Duration(data.ComboWindowOpen, AttackSpeedRate);
    private float ComboWindowClose => PlayerAttackSpeedRules.Duration(data.ComboWindowClose, AttackSpeedRate);
    private float ShotLockLead => PlayerAttackSpeedRules.Duration(data.ShotLockLead, AttackSpeedRate);
    private float ShotLockTail => PlayerAttackSpeedRules.Duration(data.ShotLockTail, AttackSpeedRate);

    // 오너 1차 판단 후 즉시 준비 동작(할 일 256) — 서버 승인을 기다리지 않는다. 서버는 시작 통지를 받고 따라간다.
    public bool TryStart()
    {
        if (active || data == null || player == null || heat == null || !IsChainAuthority || OwnerOverheated)
            return false;

        Vector3 direction = CurrentAim();
        if (!player.BeginAttackReadyState())
            return false;

        comboSerial++;
        BeginRuntime(direction);

        if (HasGameplayAuthority)
            ServerOnStart(direction);
        else
            NotifyStartRpc(comboSerial, direction, player.CreateStatReport());

        return true;
    }

    public void BeginFromState() { }

    public void Tick()
    {
        if (!active)
            return;

        if (IsChainAuthority)
        {
            TickOwner();
            return;
        }

        if (IsNetworkActive && IsServer && serverFollowFallbackTime > 0f && Time.time >= serverFollowFallbackTime)
        {
            Edit.LogWarning($"[Gunner] {name} 오너 발사·종료 통지가 끊겼다 — 서버가 기본 공격을 끝낸다(안전망).", this);
            EndFollowServer();
        }
    }

    public void FixedTickMovement() { } // 공격 중 이동 없음(§4.1)

    public void CancelCurrentAttack()
    {
        // EndLocal·CancelRpc 가 부른 EndAttackState → PlayerAttackState.Exit 의 되돌이 호출이다 — 이미 처리했다.
        if (endingGracefully)
            return;

        bool hadActive = active;
        byte ownerSerial = comboSerial;
        byte followedSerial = serverComboSerial;
        ResetRuntime();

        if (!hadActive)
            return;

        ReturnToIdlePose();
        if (!IsNetworkActive)
            return;

        if (IsServer)
        {
            // 서버가 끊는 경로(넉백·구속·연출 잠금·숨김·대시) — 오너(원격이면)·관전자 모두 즉시 멈춘다.
            verifier.EndCombo();
            CancelRpc(IsOwner ? ownerSerial : followedSerial);
        }
        else if (IsOwner)
        {
            // 원격 오너가 스스로 끊었다(대시 등) — 서버가 따라 끝낸다(이미 끊었으면 무시).
            NotifyAttackEndRpc(ownerSerial);
        }
    }

    // 스킬 예약(할 일 44) — 끊지 않고 뗀 것처럼 마무리한다. 이번 발(첫 발이면 준비 뒤 첫 발)의 ShotRecovery 뒤 기존 정상 종료로 끝난다.
    // 체인은 오너가 주도하므로(할 일 256) 오너 래치만 세운다.
    public void FinishWithoutChaining()
    {
        if (!active || !IsChainAuthority || ownerFinishing)
            return;

        ownerFinishing = true;
        ownerQueued = false;
    }

    public void CancelFinishingTailForMovement() { }
    public void EndCurrentAttack() { }
    public void HitCurrentAttack() { }
    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType) { } // 판정은 시간 기반(애니 이벤트 없음)
    public void HandleAnimatorMove(Vector3 deltaPosition, Vector3 animatorForward) { }

    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator != null)
        {
            animator = newAnimator;
            fireClipLength = -1f;
        }
    }

    // ── 오너(체인 주도) ──────────────────────────────────────────────────

    private void TickOwner()
    {
        bool overheated = OwnerOverheated;

        // 발 잠금은 오너 시계만으로 돈다(2026-10-13, 서버 왕복·검증 없음): 발사 뒤 ShotLockTail 이 지나면 풀고,
        // 다음 발을 이어 쏠 거면 발사 ShotLockLead 전에 그 순간 몸 방향으로 다시 잠근다. 그 사이는 커서 쪽으로 제한 속도 회전.
        if (ownerPreLocked && !OwnerChainsNextShot)
            ownerPreLocked = false; // 미리 잠근 뒤 손을 뗐다(또는 스킬 예약) — 다음 발이 없으니 잠금도 푼다
        if (facing.IsShotLocked && firstShotSent && !ownerPreLocked && Time.time >= ownerLastShotTime + ShotLockTail)
            facing.EndShot();
        if (firstShotSent && !ownerPreLocked && OwnerChainsNextShot && !overheated
            && Time.time >= ownerLastShotTime + FireInterval - ShotLockLead)
        {
            facing.BeginShot(movement.CurrentFacing);
            ownerPreLocked = true;
        }
        if (!facing.IsShotLocked)
            movement.RotateImmediately(facing.TurnBetweenShots(
                movement.CurrentFacing, CurrentAim(), data.BetweenShotTurnSpeed, Time.deltaTime));

        // 클릭 — 발사 중 클릭은 여기서 예약으로 쓴다(창 = 직전 발사 + ComboWindowOpen~Close, 오너 시계). 스킬 예약 뒤 클릭은 무시(마무리 중).
        if (input.AttackPressed && !ownerFinishing)
        {
            // 공격이 끝난 뒤 새 평타로 한 번 더 나가지 않게 선입력 버퍼에서 지운다(할 일 44).
            input.ConsumeBufferedInput(PlayerBufferedInput.Attack);
            if (firstShotSent && !ownerQueued)
            {
                float sinceShot = Time.time - ownerLastShotTime;
                ownerQueued = sinceShot >= ComboWindowOpen && sinceShot <= ComboWindowClose;
            }
        }

        // 첫 발 — 준비가 끝나면 한 번만 쏜다. 방향은 공격 시작 때 잠근 조준이다. 준비 끝에 (예측) 과열이면 쏘지 않고 끝낸다.
        if (!firstShotSent)
        {
            if (Time.time < startTime + WindupDuration)
                return;

            if (overheated)
            {
                EndAsChainAuthority();
                return;
            }

            firstShotSent = true;
            FireAsOwner(facing.Direction);
            return;
        }

        // 이어지는 발 — 누르고 있거나 창 안에서 예약했으면 발사 간격이 찰 때 쏜다.
        // 방향 = 발사 ShotLockLead 전에 잠근 몸 방향(덜 돌았으면 덜 돈 방향). 그 사이에 눌러 미리 못 잠갔으면 지금 몸 방향.
        if (OwnerChainsNextShot && !overheated)
        {
            if (Time.time >= ownerLastShotTime + FireInterval)
            {
                ownerQueued = false;
                Vector3 shotDirection = ownerPreLocked ? facing.Direction : facing.BeginShot(movement.CurrentFacing);
                ownerPreLocked = false;
                FireAsOwner(shotDirection);
            }
            return;
        }

        // 홀드도 예약도 없으면(창 밖·과열 포함) 이번 발의 후속 동작까지 마무리하고 끝낸다(§4.3·§4.4).
        // 🔴 여기가 공격 끝이다 — 공격 잠금(이동 입력 회전 차단)도 여기서(EndLocal → ResetRuntime) 풀린다(할 일 27).
        if (Time.time >= ownerLastShotTime + ShotRecovery)
            EndAsChainAuthority();
    }

    /// <summary>
    /// [체인 주도] 한 발 — 호스트·오프라인은 바로 서버 발사(판정 결과로 연출), 원격 오너는 예측 직선으로 즉시 연출 + 과열 예측 기록 + 발사 요청.
    /// </summary>
    private void FireAsOwner(Vector3 direction)
    {
        ownerLastShotTime = Time.time;
        Vector3 origin = ShotOrigin;

        if (HasGameplayAuthority)
        {
            if (ServerFire(origin, direction, out Vector3 end, out bool hit, out byte stage))
                PlayShot(origin, end, hit, stage);
            return;
        }

        GunnerHeatTraits heatTraits = heat.Traits;
        byte overdriveStage = heatTraits.Mode == GunnerHeatMode.Overdrive
            ? (byte)Mathf.Clamp(heat.PredictStage(heatPrediction), 0, 255)
            : (byte)0;
        bool pierce = GunnerTraitRules.PierceSecondaryRatioFor(player.TraitModifiers) >= 0f;
        Vector3 predictedEnd = beam.PredictEnd(origin, direction, data.Range, data.BeamWidth * 0.5f, data.HittableLayers,
            data.BlockingLayers, pierce, out bool predictedHit);

        heatPrediction.Record(heat.ClockNow, heatTraits.HeatPerShot(data.HeatPerShot));
        PlayShot(origin, predictedEnd, predictedHit, overdriveStage);
        FireShotRpc(comboSerial, origin, direction);
    }

    private void EndAsChainAuthority()
    {
        byte serial = comboSerial;
        EndLocal();
        if (HasGameplayAuthority)
            verifier.EndCombo();

        if (!IsNetworkActive)
            return;

        if (IsServer)
            EndRpc();
        else
            NotifyAttackEndRpc(serial);
    }

    // ── 서버 ─────────────────────────────────────────────────────────────

    // [서버] 공격이 시작됐다(호스트 오너는 직접, 원격 오너는 시작 통지로) — 2차 검증 상태를 새로 열고 오너 뺀 피어에 준비 동작 전달.
    private void ServerOnStart(Vector3 direction)
    {
        verifier.EndCombo();
        verifier.RecordAttackSpeed(Time.unscaledTimeAsDouble, AttackSpeedRate);
        if (IsNetworkActive)
            StartRpc(direction);
    }

    // [서버] 원격 오너의 시작 통지 — 거부하지 않고 따라간다. 서버가 준비 상태에 못 들어가면(넉백·구속·과열 등) 끊기 경로로 돌려보낸다.
    [Rpc(SendTo.Server)]
    private void NotifyStartRpc(byte serial, Vector3 direction, PlayerStatReport statReport, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, "거너 기본 공격 시작");

        // 이전 공격을 아직 따라가는 중이면(종료 통지보다 시작이 먼저 처리될 일은 없지만 안전망 전) 조용히 정리한다.
        if (active)
        {
            ResetRuntime();
            EndAttackStateGracefully();
        }

        if (!player.BeginAttackReadyState())
        {
            verifier.EndCombo();
            CancelRpc(serial);
            return;
        }

        serverComboSerial = serial;
        BeginRuntime(direction);
        ServerOnStart(direction);
    }

    // [서버] 원격 오너의 발사 요청(Reliable) — 오너 총구 위치·방향. 공용 2차 검증·과열에 걸리면 레이저만(데미지·과열 증가 없음).
    [Rpc(SendTo.Server)]
    private void FireShotRpc(byte serial, Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!active || serial != serverComboSerial || data == null)
            return;

        double now = Time.unscaledTimeAsDouble;
        BasicAttackVerifySettings settings = BasicAttackVerifySettings.Current;
        verifier.RecordAttackSpeed(now, AttackSpeedRate);

        // 첫 발은 준비 동작, 그 뒤는 발사 간격(공속 1 기준 ÷ 최근 최대 공속) + 여유. 간격 기록은 검증기가 든다.
        bool windupOk = serverLastShotTime >= 0f ||
            BasicAttackVerifyRules.PassesInterval((float)(now - serverStartUnscaled), data.WindupDuration, AttackSpeedRate, settings.IntervalSlack);
        BasicAttackHitVerdict verdict = verifier.VerifyFixedInterval(now, data.FireInterval, AttackSpeedRate, settings);
        Vector3 shotDirection = GunnerAttackFacingModel.Flatten(direction, movement.CurrentFacing);

        string rejected = heat.IsOverheated ? "과열"
            : !windupOk ? "준비 동작보다 빠름"
            : verdict != BasicAttackHitVerdict.Accepted ? verdict.ToString()
            : null;
        if (rejected != null)
        {
            Edit.LogWarning($"[평타 검증] {name} 거너 발사 데미지 없음 — {rejected}", this);
            NoteServerShot();
            // 레이저만 — 오너는 이미 자기 연출을 냈다. 다른 피어에는 벽까지 빗나간 레이저.
            Vector3 missEnd = origin + shotDirection * beam.CastLength(origin, shotDirection, data.Range, data.BeamWidth * 0.5f, data.BlockingLayers);
            ShotRpc(origin, missEnd, false, 0);
            return;
        }

        Vector3 offset = origin - ShotOrigin;
        if (offset.sqrMagnitude > OwnerPositionWarnDistance * OwnerPositionWarnDistance)
            Edit.LogWarning($"[평타 검증] {name} 발사 위치가 서버가 보는 총구와 {offset.magnitude:0.00}m 다르다(경고만, 오너 위치로 판정).", this);

        ServerFire(origin, shotDirection, out _, out _, out _);
    }

    // [서버] 원격 오너의 공격 종료(정상 종료·스스로 끊음). 지금 따라가는 공격이 아니면 무시(이미 끊었다).
    [Rpc(SendTo.Server)]
    private void NotifyAttackEndRpc(byte serial, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!active || serial != serverComboSerial)
            return;

        EndFollowServer();
    }

    private void EndFollowServer()
    {
        verifier.EndCombo();
        EndLocal();
        if (IsNetworkActive)
            EndRpc();
    }

    // 서버가 발 하나를 처리했다(수락·거부) — 따라가기 안전망을 이 발 기준으로 미룬다.
    private void NoteServerShot()
    {
        serverLastShotTime = Time.time;
        serverFollowFallbackTime = Time.time + Mathf.Max(FireInterval, ShotRecovery) + ServerFollowGraceSeconds;
    }

    /// <summary>
    /// [서버] 한 발 판정 — 오너가 정한 위치·방향으로 쏘기만 한다(할 일 27·256, 몸 회전·잠금은 오너 권한). 과열 증가·특성·오너 뺀 피어 연출.
    /// 호스트·오프라인 오너는 반환값(<paramref name="end"/>·<paramref name="hit"/>·<paramref name="overdriveStage"/>)으로 자기 연출을 낸다.
    /// </summary>
    private bool ServerFire(Vector3 origin, Vector3 requested, out Vector3 end, out bool hit, out byte overdriveStage)
    {
        end = origin;
        hit = false;
        overdriveStage = 0;
        if (!active || data == null)
            return false;

        Vector3 direction = GunnerAttackFacingModel.Flatten(requested, movement.CurrentFacing);

        // 단계 배율은 이번 발의 증가 전 단계로 계산한다. 최대치에 닿는 발도 피해는 정상 적용(§4.4).
        // 2차 과열 모드(할 일 127 3/4): 한계 돌파 = 단계 배율표 교체·과열 증가 ×1.35, 정온 운용 = 0단계 × (1 − 감소율)·과열 없음.
        GunnerHeatTraits heatTraits = heat.Traits;
        int stage = heat.CurrentStage;
        int baseDamage = player.ResolveDamage(data.AttackDamageMultiplier, data.FlatDamageBonus, new TraitAttackKey(AttackType.Default));
        int damage = Mathf.Max(0, Mathf.RoundToInt(baseDamage * heatTraits.BasicShotScale(stage, heat.Data)));

        hit = FireBeam(origin, direction, damage, data.TriggersOnHit, TraitHitFlags.Original, out end);

        heat.ServerAddShot(heatTraits.HeatPerShot(data.HeatPerShot)); // 허공이어도 증가(§4.2). 최대치 제한은 모델, 정온 운용이면 증가 없음
        NoteServerShot();
        player.RaiseServerBasicAttackStarted();   // 튜토리얼 훅 — 한 발 발사(적중 무관)
        // 한계 돌파 과열 색(2026-11-03 민경) — 이 발 피해에 쓴 단계(증가 전)를 연출에 같이 싣는다. 한계 돌파가 아니면 0(원래 색). 판정 무관.
        overdriveStage = heatTraits.Mode == GunnerHeatMode.Overdrive ? (byte)Mathf.Clamp(stage, 0, 255) : (byte)0;
        CommitShotForTraits(origin, direction, damage, overdriveStage);
        if (IsNetworkActive)
            ShotRpc(origin, end, hit, overdriveStage);
        return true;
    }

    // ── 특성(할 일 127) ──────────────────────────────────────────────────

    // 한 발 판정 — 관통 탄도면 적을 꿰뚫고(첫 적 100%·이후 각각 비율, 벽에서 멈춤), 아니면 첫 대상 1체(원래 동작).
    // 집중 포화는 대상마다 피해 순간 위치로(hook). 원래 발·추가 발이 같은 정책을 쓴다 — 추가 발은 Derived(적중 사건·패시브 없음).
    private bool FireBeam(Vector3 origin, Vector3 direction, int damage, bool triggersOnHit, TraitHitFlags flags, out Vector3 end)
    {
        float pierceRatio = GunnerTraitRules.PierceSecondaryRatioFor(player.TraitModifiers);
        return pierceRatio >= 0f
            ? beam.FireThrough(origin, direction, data.Range, data.BeamWidth * 0.5f, data.HittableLayers, data.BlockingLayers,
                               damage, pierceRatio, triggersOnHit, out end, flags, focusFireHook)
            : beam.Fire(origin, direction, data.Range, data.BeamWidth * 0.5f, data.HittableLayers, data.BlockingLayers,
                        damage, triggersOnHit, out end, flags, focusFireHook);
    }

    private void ApplyFocusFire(Unit target, ref AttackInfo info) => GunnerTrackingLaser.ApplyFocusFire(player, target, ref info);

    // 원래 기본 공격 발사 커밋(빗나가도) — 특성 사건 BasicAttackFired + 이중 출력 발수. 취소된 공격·Q·추가 발은 여기를 지나지 않는다.
    private void CommitShotForTraits(Vector3 origin, Vector3 direction, int shotDamage, int overdriveStage)
    {
        if (traitEffects == null)
            traitEffects = PlayerTraitEffects.For(player);
        if (traitEffects == null)
            return;

        traitEffects.RaiseServer(new TraitTriggerEvent(TraitTrigger.BasicAttackFired, AttackType.Default, TraitAttackForm.Normal, 0));

        // 이중 출력 — N 번째 원래 발이면 그 발 피해 × 비율의 추가 레이저를 공격 가속 반영 지연 뒤로 예약(같은 방향·사거리·폭·관통 정책).
        TraitDerivedAttack extra = GunnerTraitRules.FindDerived(traitEffects, GunnerTraitRules.BasicAttackKey,
            GunnerTraitRules.BasicExtraDerivedKey, derivedBuffer);
        if (GunnerDualOutputRules.CountOriginalShot(extra, shotDamage, AttackSpeedRate, out int extraDamage, out float delay))
            extraShots.Schedule(Time.timeAsDouble, delay, new GunnerDerivedShot(origin, direction, data.Range, data.BeamWidth, extraDamage, overdriveStage));
    }

    // [서버] 예약된 추가 레이저 — 판정 + 특성 연출 사건(연출은 GunnerBeamView 가 사건을 받아 낸다, 2026-11-03 민경). 과열·패시브·쿨·발수 없음, 행동 잠금 없음.
    // 사건 Variant = 한계 돌파 색 단계(원래 세 번째 발의 피해 단계, 아니면 0).
    private void TickExtraShotsServer()
    {
        if (extraShots.Count == 0)
            return;

        extraShots.CollectDue(Time.timeAsDouble, dueExtraShots);
        if (data == null || beam == null || player == null || player.CurrentHealth <= 0)
            return;

        for (int i = 0; i < dueExtraShots.Count; i++)
        {
            GunnerDerivedShot shot = dueExtraShots[i];
            bool hit = FireBeam(shot.Origin, shot.Direction, shot.Damage, false, TraitHitFlags.Derived, out Vector3 end);
            Edit.Log($"[Gunner] 이중 출력 추가 레이저 — 피해 {shot.Damage}, 적중 {hit}", this);
            // 특성 연출 사건(할 일 145) — 추가 레이저 1발(원점 → 끝점: 원래 발 ShotRpc 와 같은 의미). 맞혔으면 끝점에 Hit(대상 없음 — 위치만).
            TraitSkillFxNetwork.ServerSend(player, TraitFxTrait.GunnerDualOutput, TraitFxKind.Activated, shot.Origin, shot.Direction, end,
                variant: shot.OverdriveStage);
            if (hit)
                TraitSkillFxNetwork.ServerSend(player, TraitFxTrait.GunnerDualOutput, TraitFxKind.Hit, end, shot.Direction, end,
                    variant: shot.OverdriveStage);
        }
    }

    // ── 다른 피어 ────────────────────────────────────────────────────────

    // [오너 뺀 피어] 준비 동작. 오너는 이미 자기 화면에서 틀었다. 서버(원격 오너)는 통지 처리에서 직접 튼다.
    [Rpc(SendTo.NotOwner)]
    private void StartRpc(Vector3 direction)
    {
        if (IsServer)
            return;

        if (!player.BeginAttackReadyState())
        {
            ResetRuntime();
            return;
        }
        BeginRuntime(direction);
    }

    // [오너 뺀 피어] 정상 종료.
    [Rpc(SendTo.NotOwner)]
    private void EndRpc()
    {
        if (IsServer)
            return;

        EndLocal();
    }

    // [서버 뺀 피어] 서버가 끊었다 — 오너(원격)·관전자 즉시 멈춤. 오너는 지금 공격이 아니면(옛 공격에 대한 끊기) 무시한다.
    [Rpc(SendTo.NotServer)]
    private void CancelRpc(byte serial)
    {
        if (!active)
            return;

        if (IsOwner && serial != comboSerial)
            return;

        ResetRuntime();
        EndAttackStateGracefully();
        ReturnToIdlePose();
    }

    // [오너 뺀 피어] 발사 연출 — 오너는 자기 발사 순간에 이미 냈다(원격 오너 = 예측 직선, 호스트 오너 = 판정 결과).
    [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
    private void ShotRpc(Vector3 origin, Vector3 end, bool hit, byte overdriveStage) => PlayShot(origin, end, hit, overdriveStage);

    private void BeginRuntime(Vector3 direction)
    {
        active = true;
        firstShotSent = false;
        ownerQueued = false;
        ownerPreLocked = false;
        ownerFinishing = false;
        startTime = Time.time;
        ownerLastShotTime = -1f;
        serverStartUnscaled = Time.unscaledTimeAsDouble;
        serverLastShotTime = -1f;
        // 첫 발 요청이 안 오면 서버가 스스로 끝낸다(따라가는 서버만 본다).
        serverFollowFallbackTime = Time.time + WindupDuration + FireInterval * StallIntervals;

        // 공격 잠금 + 첫 발 잠금 — 시작 조준을 바라보고 이동 입력 회전을 막는다. 풀기는 ResetRuntime(정상 종료·끊김 공통).
        // 발 사이의 커서 회전은 TickOwner 가 RotateImmediately 로 직접 돌린다(토큰이 막지 않는 행동 회전).
        movement.AcquireFacingLock(this, facing.Begin(direction, movement.CurrentFacing));
        player.SetAnimatorMoving(false);
        CrossFadeIfExists(data.WindupStateName);
    }

    private void EndLocal()
    {
        ResetRuntime();
        EndAttackStateGracefully();
        ReturnToIdlePose();
    }

    // 정상 종료·끊기 처리를 이미 했다 — EndAttackState → PlayerAttackState.Exit → CancelCurrentAttack 되돌이를 막는다.
    private void EndAttackStateGracefully()
    {
        endingGracefully = true;
        try
        {
            player.EndAttackState();
        }
        finally
        {
            endingGracefully = false;
        }
    }

    // 발사 자세(Q_charge_loop)는 루프라 스스로 빠져나오지 않는다 — Idle 로 돌려야 Idle↔Walk 전환이 다시 산다.
    private void ReturnToIdlePose()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        animator.CrossFadeInFixedTime(IdleHash, 0.1f, 0);

        int upper = UpperLayer();
        if (upper >= 0 && animator.HasState(upper, EmptyHash))
            animator.CrossFadeInFixedTime(EmptyHash, 0.1f, upper);
    }

    private void ResetRuntime()
    {
        active = false;
        firstShotSent = false;
        ownerQueued = false;
        ownerPreLocked = false;
        ownerFinishing = false;
        ownerLastShotTime = -1f;
        serverLastShotTime = -1f;
        serverFollowFallbackTime = 0f;

        // 잠금 해제 — 정상 종료(EndLocal)·대시/상태 전이 끊김(CancelCurrentAttack)·서버 끊기가 모두 여기를 지난다.
        facing.End();
        movement?.ReleaseFacingLock(this);
    }

    private void PlayShot(Vector3 origin, Vector3 end, bool hit, byte overdriveStage)
    {
        // 회전 잠금 해제는 여기서 하지 않는다 — 오너 시계(ShotLockTail)로만 푼다(서버 왕복 없음, 2026-10-13).
        PlayFireAnimation();
        ShowBeam(origin, end, hit, overdriveStage);
    }

    // 상체 레이어에서 발사 클립을 매 발 처음부터. 클립이 발사 간격보다 길면 그만큼 빨리 재생해 다음 발 전에 끝나게 한다.
    private void PlayFireAnimation()
    {
        if (data == null || animator == null || animator.runtimeAnimatorController == null)
            return;

        int upper = UpperLayer();
        int hash = Animator.StringToHash(data.FireStateName);
        if (upper < 0 || !animator.HasState(upper, hash))
            return;

        animator.SetFloat(FireSpeedHash, FireSpeed());
        animator.Play(hash, upper, 0f);
    }

    // 🔴 클립을 이름으로 찾지 않는다(2026-10-09). 상태의 모션만 바꾸면(8940e020: gunner_attack → gunner_attack01)
    //    이름이 어긋나 길이를 못 찾고 속도가 조용히 1 로 떨어졌다 — 1.17초 클립의 앞 30%만 매 발 되풀이됐다.
    private float FireSpeed() =>
        fireClipLength > 0f ? Mathf.Max(1f, fireClipLength / data.FireInterval) : 1f;

    // Play() 는 다음 애니메이터 갱신에서 반영돼 같은 프레임엔 클립 정보를 못 읽는다.
    // 애니메이터 갱신(Update 뒤) 이후인 LateUpdate 에서 한 번 읽고 곧바로 속도를 고친다 — 첫 발만 한 프레임 속도 1.
    private void LateUpdate()
    {
        if (fireClipLength >= 0f || data == null || animator == null || animator.runtimeAnimatorController == null)
            return;

        int upper = UpperLayer();
        if (upper < 0 || animator.GetCurrentAnimatorStateInfo(upper).shortNameHash != Animator.StringToHash(data.FireStateName))
            return;

        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(upper);
        fireClipLength = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.length : 0f;
        animator.SetFloat(FireSpeedHash, FireSpeed());
    }

    private int UpperLayer() =>
        animator != null && data != null ? animator.GetLayerIndex(data.UpperBodyLayerName) : -1;

    private void CrossFadeIfExists(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName) || animator.runtimeAnimatorController == null)
            return;

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, 0.1f, 0);
    }

    /// <summary>
    /// 🔴 <b><c>hit</c> 은 "유닛에 피해를 줬나" 지 "뭔가에 부딪혔나"가 아니다.</b>
    /// <c>GunnerBeamAttack.Fire</c> 는 벽에 막히면 <c>end</c> 만 당기고 <b>false</b> 를 돌려준다.
    /// 그래서 착탄 연출을 <c>hit</c> 으로 묶으면 <b>벽에서는 영원히 안 뜬다.</b>
    ///
    /// 착탄 판단은 <b>빔이 사거리 끝까지 못 갔는가</b>로 한다 — 유닛이든 벽이든 똑같이 잡힌다.
    /// 아무것도 안 맞으면 <c>Fire</c> 가 <c>end = origin + dir * range</c> 를 그대로 두므로 거리가 딱 사거리다.
    /// </summary>
    private void ShowBeam(Vector3 origin, Vector3 end, bool hit, byte overdriveStage)
    {
        if (beamView == null || data == null)
            return;

        bool stopped = hit || Vector3.Distance(origin, end) < data.Range - 0.01f;
        beamView.ShowBasicShot(origin, end, stopped, hit, data.BeamWidth, overdriveStage);
    }

    // 준비 자세 → 공격. 상태기계는 오너·서버만 틱하므로 원격 프록시도 넘어가도록 Update 에서 각 피어가 시간으로 전환한다
    // (상태는 복제하지 않는다 — 시작 RPC 기준 시각). 판정은 여전히 서버 시각으로 검증한다.
    private void Update()
    {
        if (HasGameplayAuthority)
            TickExtraShotsServer(); // 공격이 끝난 뒤에도 돈다(추가 발은 행동 잠금 없음)

        if (!active || data == null || player.CurrentState != PlayerActionState.AttackReady)
            return;

        if (Time.time >= startTime + WindupDuration)
            player.BeginAttackState();
    }

    private Vector3 CurrentAim() => Flatten(aim != null ? aim.AimDirection : transform.forward);

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
    }
}
