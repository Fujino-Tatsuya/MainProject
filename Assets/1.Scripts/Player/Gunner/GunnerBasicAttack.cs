using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 기본 공격 — 준비 동작 → 레이저 1발. 버튼을 누르고 있거나(홀드) 입력 창 안에서 클릭하면 다음 발을 잇는다
/// (character_gunner.md §4, PLAN-gunner.md G3).
///
/// 흐름: 오너가 시작 요청(조준 포함) → 서버 승인(준비 동작 1회) → 준비가 끝나면 오너가 첫 발을 요청 →
/// 서버가 간격·과열·상태를 검사해 판정(첫 대상 1체)·과열 증가 → 전 피어 연출.
/// 이어지는 발의 조건은 둘 중 하나다(2026-10-09). 허락은 서버가, 발사 요청 시점과 방향은 오너가 정한다(2026-10-12).
///  · 홀드: 오너가 버튼 눌림/뗌을 서버에 알리고, 눌림 동안 직전 발사 + 발사 간격마다 오너가 다음 발을 요청한다.
///  · 입력 창: 팔라딘 콤보(DefaultAttackController 의 ComboWindowOpen~Close)와 같은 구조 — 오너가 클릭을 보내면
///    서버가 자기 시각으로 창(직전 발사 + ComboWindowOpen~Close) 안인지 보고 예약한다. 오너도 같은 창으로 자기 예약을 들고
///    발사 간격이 차면 요청한다. 서버는 눌림·예약이 없는 요청을 버린다(창 우회 방지).
/// 어느 쪽이든 준비 동작은 첫 발만이다. 둘 다 아니거나 과열되면 후속 동작(ShotRecovery)까지 마무리하고 끝난다.
/// 창은 애니 이벤트가 아니라 데이터의 시간 값이다(판정도 시간 기반이라 한 곳에서 맞춘다).
/// 공격 중 이동 불가. 스킬 입력(Q·E·R·우클릭)은 공격을 끊지 않는다 — 상태기계가 '공격 뒤 스킬'로 예약하고
/// <see cref="FinishWithoutChaining"/> 을 부르면 현재 발은 끝까지 가고 다음 발만 잇지 않아(뗀 것처럼) 정상 종료한다.
/// 끝나면 Idle/Move 첫 틱에 예약 스킬이 기존 경로로 요청된다(<see cref="ReservesSkillAfterAttack"/>, 할 일 44).
/// 홀드 시작(<see cref="StartsOnHold"/>): 스킬 등으로 공격이 끝난 뒤에도 좌클릭을 누르고 있으면 Idle/Move 에서 다시 시작한다.
/// 방향(할 일 27, <see cref="GunnerAttackFacingModel"/>): 발 잠금 중에는 몸이 돌지 않고, 그 밖에는 커서 쪽으로
/// <see cref="GunnerBasicAttackData.BetweenShotTurnSpeed"/> 로 돈다. 발 잠금 = 첫 발은 공격 시작 ~ 발사 + ShotLockTail,
/// 이어지는 발은 발사 − ShotLockLead ~ 발사 + ShotLockTail(잠근 순간의 몸 방향). 🔴 잠금은 오너 시계만으로 돈다 —
/// 오너가 방향을 발사 요청에 실어 보내고 서버는 그대로 쏘기만 한다(서버는 몸을 돌리거나 잠금을 검증하지 않는다, 2026-10-13).
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(GunnerHeat))]
[RequireComponent(typeof(GunnerBeamAttack))]
public class GunnerBasicAttack : BaseNetworkBehaviour, IPlayerBasicAttack, IPlayerAttackSpeedAnimatorUser
{
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int EmptyHash = Animator.StringToHash("Empty");
    private static readonly int FireSpeedHash = Animator.StringToHash("FireSpeed");

    // 오너 요청 간격이 네트워크 지터로 조금 당겨져 와도 받아 준다.
    private const float IntervalTolerance = 0.75f;
    // 오너 신호가 끊겼을 때(연결 문제 등) 서버가 스스로 끝내는 여유(발사 간격 배수).
    // 첫 발 요청이 안 오면 공격을 끝내고, 홀드 중 조준 갱신이 끊기면 뗀 것으로 본다.
    private const float StallIntervals = 3f;
    // 홀드 중 오너가 "아직 누르고 있다"를 보내는 주기(발사 간격 배수). 방향은 발사 요청에 싣는다.
    private const float HoldSignalIntervals = 0.5f;

    [SerializeField] private GunnerBasicAttackData data;
    [SerializeField] private Animator animator;

    private Player player;
    private PlayerStateController stateController;
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
    private float lastShotTime = -1f;
    private readonly GunnerAttackFacingModel facing = new GunnerAttackFacingModel();

    // 오너
    private bool isRequesting;
    private bool firstShotSent;
    private bool holdSent;         // 서버가 "누르고 있다"로 알고 있는가
    private float nextHoldSignalTime;
    private float ownerLastShotTime = -1f; // 오너가 마지막 발을 요청한 시각(오너 시계)
    private bool ownerQueued;              // 오너 쪽 입력 창 예약 — 서버 예약과 같은 규칙
    private bool ownerPreLocked;           // 다음 발을 위해 발사 전에 미리 잠갔는가(오너 시계)
    private bool ownerFinishing;           // 스킬 예약 — 지금 발로 마무리(누름·창 예약 무시, 할 일 44)

    // 서버
    private bool hasQueuedShot;
    private bool held;
    private float lastHoldSignalTime;
    private bool endingGracefully;

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
    /// 서버 발사(<see cref="ServerFire"/>)와 특성 랩 공격 범위 기즈모(할 일 152)가 같이 쓴다. data 가 있을 때만 부른다.
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

    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;
    private bool IsInputSource => !IsNetworkActive || IsOwner;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
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
    }

    // 사망(살아 있음 → 그 밖) — 예약한 추가 레이저 취소. 발수 카운터는 공용 장부(PlayerTraitEffects.ClearTransientServer)가 0 으로 돌린다.
    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (player != null && clientId == player.OwnerClientId && previous == PlayerLifeState.Alive && current != PlayerLifeState.Alive)
            extraShots.Clear();
    }

    // ── IPlayerBasicAttack ───────────────────────────────────────────────

    public bool IsOutOfResource => heat != null && heat.IsOverheated;

    // ── 공격속도(할 일 91 T5) ────────────────────────────────────────────
    // 거너 평타 타이밍은 클립 이벤트가 아니라 데이터 시간 값이라 그 값들을 배율로 나눈다(준비·발사 간격·후속 동작·입력 창·발 잠금).
    // 서버·오너가 같은 배율(복제된 상태이상) — 배율이 바뀌는 순간만 복제 지연만큼 어긋나고, 서버 간격 검사(IntervalTolerance)가 받아 준다.
    // 재생 속도는 공격 중(전 피어 StartRpc~EndRpc) Animator.speed 로 같이 탄다 — FireSpeed 는 배율 전 발사 간격 기준이라 두 번 곱하지 않는다.
    public bool ScalesAnimatorWithAttackSpeed => active;

    private float AttackSpeedRate => player != null ? player.AttackSpeedRate : 1f;
    private float WindupDuration => PlayerAttackSpeedRules.Duration(data.WindupDuration, AttackSpeedRate);
    private float FireInterval => PlayerAttackSpeedRules.Duration(data.FireInterval, AttackSpeedRate);
    private float ShotRecovery => PlayerAttackSpeedRules.Duration(data.ShotRecovery, AttackSpeedRate);
    private float ComboWindowOpen => PlayerAttackSpeedRules.Duration(data.ComboWindowOpen, AttackSpeedRate);
    private float ComboWindowClose => PlayerAttackSpeedRules.Duration(data.ComboWindowClose, AttackSpeedRate);
    private float ShotLockLead => PlayerAttackSpeedRules.Duration(data.ShotLockLead, AttackSpeedRate);
    private float ShotLockTail => PlayerAttackSpeedRules.Duration(data.ShotLockTail, AttackSpeedRate);

    public bool TryStart()
    {
        if (active || isRequesting || data == null || player == null || heat.IsOverheated)
            return false;

        Vector3 direction = CurrentAim();

        if (!IsNetworkActive)
        {
            StartServer(direction);
            return true;
        }

        if (!IsOwner)
            return false;

        isRequesting = true;
        RequestStartRpc(direction, player.CreateStatReport());
        return true;
    }

    public void BeginFromState() { }

    public void Tick()
    {
        if (!active)
            return;

        if (IsInputSource)
            TickOwner();

        if (HasGameplayAuthority)
            TickServer();
    }

    public void FixedTickMovement() { } // 공격 중 이동 없음(§4.1)

    public void CancelCurrentAttack()
    {
        // EndServer/EndRpc 가 부른 EndAttackState → PlayerAttackState.Exit 의 되돌이 호출이다 — 정상 종료라 끊지 않는다.
        if (endingGracefully)
            return;

        bool hadActive = active || isRequesting;
        ResetRuntime();

        if (!hadActive)
            return;

        ReturnToIdlePose();

        if (IsNetworkActive && IsServer)
            EndRpc();
    }

    // 스킬 예약(할 일 44) — 끊지 않고 뗀 것처럼 마무리한다. 서버도 눌림·창 예약을 지워 이번 발(첫 발이면 준비 뒤 첫 발)의
    // ShotRecovery 뒤 기존 정상 종료(EndServer → EndRpc)로 끝난다 — 원격 피어의 마무리·애니도 평소 종료와 같다.
    public void FinishWithoutChaining()
    {
        if (!active || !IsInputSource || ownerFinishing)
            return;

        ownerFinishing = true;
        ownerQueued = false;
        holdSent = false;
        if (IsNetworkActive)
            FinishRpc();
        else
            ServerFinish();
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

    // ── 오너 ─────────────────────────────────────────────────────────────

    private void TickOwner()
    {
        // 발 잠금은 오너 시계만으로 돈다(2026-10-13, 서버 왕복·검증 없음): 발사 요청 뒤 ShotLockTail 이 지나면 풀고,
        // 다음 발을 이어 쏠 거면 발사 ShotLockLead 전에 그 순간 몸 방향으로 다시 잠근다. 그 사이는 커서 쪽으로 제한 속도 회전.
        if (ownerPreLocked && !OwnerChainsNextShot)
            ownerPreLocked = false; // 미리 잠근 뒤 손을 뗐다(또는 스킬 예약) — 다음 발이 없으니 잠금도 푼다
        if (facing.IsShotLocked && firstShotSent && !ownerPreLocked && Time.time >= ownerLastShotTime + ShotLockTail)
            facing.EndShot();
        if (firstShotSent && !ownerPreLocked && OwnerChainsNextShot && !heat.IsOverheated
            && Time.time >= ownerLastShotTime + FireInterval - ShotLockLead)
        {
            facing.BeginShot(movement.CurrentFacing);
            ownerPreLocked = true;
        }
        if (!facing.IsShotLocked)
            movement.RotateImmediately(facing.TurnBetweenShots(
                movement.CurrentFacing, CurrentAim(), data.BetweenShotTurnSpeed, Time.deltaTime));

        // 홀드 상태는 바뀔 때만 알린다 — 뗌은 ReleaseRpc, 다시 누름은 아래 클릭 RPC 가 겸한다.
        if (holdSent && !input.AttackHeld)
        {
            holdSent = false;
            if (IsNetworkActive)
                ReleaseRpc();
            else
                held = false;
        }

        // 클릭 — 누른 프레임만 보낸다. 창 안인지는 서버가 자기 시각으로 판단한다(팔라딘 RequestQueueNextAttackRpc 와 같음).
        // 창 밖이어도 서버는 "누르고 있음"으로 받아 홀드가 이어진다. 스킬 예약 뒤 클릭은 보내지 않는다(마무리 중).
        if (input.AttackPressed && !ownerFinishing)
        {
            // 발사 중 클릭은 여기서 홀드/예약으로 쓴다 — 공격이 끝난 뒤 새 평타로 한 번 더 나가지 않게 선입력 버퍼에서 지운다(할 일 44).
            input.ConsumeBufferedInput(PlayerBufferedInput.Attack);
            holdSent = true;
            nextHoldSignalTime = Time.time + FireInterval * HoldSignalIntervals;
            // 오너도 같은 창으로 자기 예약을 든다 — 서버 예약과 기준(각자의 직전 발사)이 같아 지터만큼만 어긋난다.
            if (firstShotSent && !ownerQueued)
            {
                float sinceShot = Time.time - ownerLastShotTime;
                ownerQueued = sinceShot >= ComboWindowOpen && sinceShot <= ComboWindowClose;
            }
            if (IsNetworkActive)
                RequestQueueShotRpc();
            else
                ServerQueueShot();
        }

        // 첫 발 — 준비가 끝나면 한 번만 요청한다. 방향은 공격 시작 때 잠근 조준이다.
        if (!firstShotSent)
        {
            if (Time.time < startTime + WindupDuration || heat.IsOverheated)
                return;

            firstShotSent = true;
            RequestShot(facing.Direction);
            return;
        }

        // 이어지는 발 — 누르고 있거나 창 안에서 예약했으면 발사 간격이 찰 때 요청한다.
        // 방향 = 발사 ShotLockLead 전에 잠근 몸 방향(덜 돌았으면 덜 돈 방향). 그 사이에 눌러 미리 못 잠갔으면 지금 몸 방향.
        if (OwnerChainsNextShot && !heat.IsOverheated
            && Time.time >= ownerLastShotTime + FireInterval)
        {
            ownerQueued = false;
            Vector3 shotDirection = ownerPreLocked ? facing.Direction : facing.BeginShot(movement.CurrentFacing);
            ownerPreLocked = false;
            RequestShot(shotDirection);
            return;
        }

        // 홀드 신호 — 끊기면 서버가 뗀 것으로 본다.
        if (!holdSent || Time.time < nextHoldSignalTime)
            return;

        nextHoldSignalTime = Time.time + FireInterval * HoldSignalIntervals;
        if (IsNetworkActive)
            HoldSignalRpc();
        else
            ServerHoldSignal();
    }

    private void RequestShot(Vector3 direction)
    {
        ownerLastShotTime = Time.time;
        nextHoldSignalTime = Time.time + FireInterval * HoldSignalIntervals;
        if (IsNetworkActive)
            FireShotRpc(direction);
        else
            ServerRequestShot(direction);
    }

    // ── 서버 ─────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void RequestStartRpc(Vector3 direction, PlayerStatReport statReport, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, "거너 기본 공격 시작");

        if (active || stateController == null || !stateController.CanAttack || heat.IsOverheated)
        {
            RejectStartRpc();
            return;
        }

        StartServer(direction);
    }

    [Rpc(SendTo.Server)]
    private void FireShotRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerRequestShot(direction);
    }

    // 첫 발은 준비가 끝났으면, 이어지는 발은 서버가 아는 눌림·창 예약이 있을 때만 쏜다(창 우회 방지).
    // 방향은 오너가 잠근 몸 방향 그대로다 — 몸 회전은 오너 권한이라 서버가 따로 정하지 않는다.
    private void ServerRequestShot(Vector3 direction)
    {
        if (!active || (lastShotTime >= 0f && !held && !hasQueuedShot))
            return;

        lastHoldSignalTime = Time.time;
        if (ServerFire(direction))
            hasQueuedShot = false;
    }

    [Rpc(SendTo.Server)]
    private void RequestQueueShotRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerQueueShot();
    }

    [Rpc(SendTo.Server)]
    private void ReleaseRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        held = false;
    }

    [Rpc(SendTo.Server)]
    private void FinishRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerFinish();
    }

    // 스킬 예약 마무리 — 눌림·창 예약을 지운다. 이미 온 발 요청은 그대로 쏘고(첫 발 포함), 그 뒤 TickServer 가 ShotRecovery 로 끝낸다.
    private void ServerFinish()
    {
        held = false;
        hasQueuedShot = false;
    }

    [Rpc(SendTo.Server)]
    private void HoldSignalRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerHoldSignal();
    }

    // 클릭 = 버튼이 눌렸다 — 홀드를 (다시) 켠다. 다음 발 예약은 창(직전 발사 + ComboWindowOpen~Close) 안의 클릭만이다.
    // 창 밖 클릭은 예약을 버린다 — 이월 없음. 그 뒤 계속 누르고 있으면 홀드로 이어진다.
    private void ServerQueueShot()
    {
        if (!active || data == null)
            return;

        held = true;
        lastHoldSignalTime = Time.time;

        if (lastShotTime < 0f || hasQueuedShot || heat.IsOverheated)
            return;

        float sinceShot = Time.time - lastShotTime;
        if (sinceShot < ComboWindowOpen || sinceShot > ComboWindowClose)
            return;

        hasQueuedShot = true;
    }

    // 신호 시각만 갱신한다 — 홀드를 켜지는 않는다(뗀 뒤 늦게 온 신호가 홀드를 되살리지 않게).
    private void ServerHoldSignal()
    {
        if (!active || !held)
            return;

        lastHoldSignalTime = Time.time;
    }

    private void StartServer(Vector3 direction)
    {
        if (!player.BeginAttackReadyState())
        {
            isRequesting = false;
            return;
        }

        BeginRuntime(direction);

        if (IsNetworkActive)
            StartRpc(direction);
    }

    /// <returns>실제로 쐈으면 true.</returns>
    private bool ServerFire(Vector3 requested)
    {
        if (!active || data == null)
            return false;

        float now = Time.time;
        if (now < startTime + WindupDuration * IntervalTolerance)
            return false;
        if (lastShotTime >= 0f && now - lastShotTime < FireInterval * IntervalTolerance)
            return false;
        if (heat.IsOverheated)
            return false;

        // 오너가 잠근 방향으로 쏘기만 한다(할 일 27). 몸 회전·잠금은 오너 권한 — 서버는 돌리거나 검증하지 않는다(2026-10-13).
        Vector3 direction = GunnerAttackFacingModel.Flatten(requested, movement.CurrentFacing);

        // 단계 배율은 이번 발의 증가 전 단계로 계산한다. 최대치에 닿는 발도 피해는 정상 적용(§4.4).
        // 2차 과열 모드(할 일 127 3/4): 한계 돌파 = 단계 배율표 교체·과열 증가 ×1.35, 정온 운용 = 0단계 × (1 − 감소율)·과열 없음.
        GunnerHeatTraits heatTraits = heat.Traits;
        int stage = heat.CurrentStage;
        int baseDamage = player.ResolveDamage(data.AttackDamageMultiplier, data.FlatDamageBonus, new TraitAttackKey(AttackType.Default));
        int damage = Mathf.Max(0, Mathf.RoundToInt(baseDamage * heatTraits.BasicShotScale(stage, heat.Data)));

        Vector3 origin = ShotOrigin;
        bool hit = FireBeam(origin, direction, damage, data.TriggersOnHit, TraitHitFlags.Original, out Vector3 end);

        heat.ServerAddShot(heatTraits.HeatPerShot(data.HeatPerShot)); // 허공이어도 증가(§4.2). 최대치 제한은 모델, 정온 운용이면 증가 없음
        lastShotTime = now;
        player.RaiseServerBasicAttackStarted();   // 튜토리얼 훅 — 한 발 발사(적중 무관)
        CommitShotForTraits(origin, direction, damage);
        if (IsNetworkActive)
            ShotRpc(origin, end, hit);
        else
            PlayShot(origin, end, hit);
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
    private void CommitShotForTraits(Vector3 origin, Vector3 direction, int shotDamage)
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
            extraShots.Schedule(Time.timeAsDouble, delay, new GunnerDerivedShot(origin, direction, data.Range, data.BeamWidth, extraDamage));
    }

    // [서버] 예약된 추가 레이저 — 판정만(VFX 없음, 할 일 127 범위 밖). 과열·패시브·쿨·발수·특성 사건 없음, 행동 잠금 없음.
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
            TraitSkillFxNetwork.ServerSend(player, TraitFxTrait.GunnerDualOutput, TraitFxKind.Activated, shot.Origin, shot.Direction, end);
            if (hit)
                TraitSkillFxNetwork.ServerSend(player, TraitFxTrait.GunnerDualOutput, TraitFxKind.Hit, end, shot.Direction, end);
        }
    }

    private void TickServer()
    {
        float now = Time.time;

        // 첫 발 대기 — 준비 끝에 과열이면 쏘지 않고 끝내고, 오너 요청이 끊기면 서버가 스스로 끝낸다.
        if (lastShotTime < 0f)
        {
            float fireAt = startTime + WindupDuration;
            if (heat.IsOverheated && now >= fireAt)
            {
                EndServer();
                return;
            }

            if (now > fireAt + FireInterval * StallIntervals)
            {
                Edit.LogWarning("[Gunner] 오너 첫 발 요청이 끊겼다 — 기본 공격을 서버에서 끝낸다.", this);
                EndServer();
            }
            return;
        }

        // 홀드 중 오너 신호가 끊기면 뗀 것으로 본다 — 아래 마무리로 끝난다.
        if (held && now > lastHoldSignalTime + FireInterval * StallIntervals)
        {
            Edit.LogWarning("[Gunner] 오너 홀드 신호가 끊겼다 — 뗀 것으로 보고 기본 공격을 마무리한다.", this);
            held = false;
        }

        // 창 예약을 했는데 오너 발사 요청이 끊기면(창 경계에서 오너는 창 밖으로 본 경우 포함) 버린다 — 아래 마무리로 끝난다.
        if (hasQueuedShot && now > lastShotTime + FireInterval * StallIntervals)
        {
            Edit.LogWarning("[Gunner] 창 예약 뒤 오너 발사 요청이 끊겼다 — 예약을 버리고 기본 공격을 마무리한다.", this);
            hasQueuedShot = false;
        }

        // 다음 발 — 누르고 있거나 창 안에서 예약됐으면 오너의 발사 요청(그 발의 몸 방향)을 기다린다(ServerRequestShot).
        // 간격은 실제 직전 발사 기준으로 ServerFire 가 검사하므로 몰아 쏘지 않는다.
        // 과열이면 기다리지 않고 이번 발의 후속 동작까지 마무리한다(§4.4).
        if ((held || hasQueuedShot) && !heat.IsOverheated)
            return;

        // 홀드도 예약도 없으면(창 밖·과열 포함) 이번 발의 후속 동작까지 마무리하고 끝낸다(§4.3·§4.4).
        // 🔴 여기가 공격 끝이다 — 공격 잠금(이동 입력 회전 차단)도 여기서(EndLocal → ResetRuntime) 풀린다(할 일 27).
        if (now >= lastShotTime + ShotRecovery)
            EndServer();
    }

    private void EndServer()
    {
        EndLocal();
        if (IsNetworkActive)
            EndRpc();
    }

    // ── 전 피어 ──────────────────────────────────────────────────────────

    [Rpc(SendTo.NotServer)]
    private void StartRpc(Vector3 direction)
    {
        isRequesting = false;
        if (!player.BeginAttackReadyState())
        {
            ResetRuntime();
            return;
        }
        BeginRuntime(direction);
    }

    [Rpc(SendTo.Owner)]
    private void RejectStartRpc()
    {
        isRequesting = false;
    }

    [Rpc(SendTo.NotServer)]
    private void EndRpc() => EndLocal();

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void ShotRpc(Vector3 origin, Vector3 end, bool hit) => PlayShot(origin, end, hit);

    private void BeginRuntime(Vector3 direction)
    {
        active = true;
        isRequesting = false;
        firstShotSent = false;
        hasQueuedShot = false;
        ownerQueued = false;
        ownerPreLocked = false;
        ownerFinishing = false;
        startTime = Time.time;
        lastShotTime = -1f;
        ownerLastShotTime = -1f;
        // 시작 클릭 = 눌림. 오너가 이미 뗐으면 다음 틱에 ReleaseRpc 로 바로잡는다.
        holdSent = true;
        held = true;
        lastHoldSignalTime = startTime;

        // 공격 잠금 + 첫 발 잠금 — 시작 RPC 의 조준을 바라보고 이동 입력 회전을 막는다. 풀기는 ResetRuntime(정상 종료·끊김 공통).
        // 발 사이의 커서 회전은 TickOwner 가 RotateImmediately 로 직접 돌린다(토큰이 막지 않는 행동 회전).
        movement.AcquireFacingLock(this, facing.Begin(direction, movement.CurrentFacing));
        player.SetAnimatorMoving(false);
        CrossFadeIfExists(data.WindupStateName);
    }

    private void EndLocal()
    {
        endingGracefully = true;
        try
        {
            ResetRuntime();
            player.EndAttackState();
        }
        finally
        {
            endingGracefully = false;
        }

        ReturnToIdlePose();
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
        isRequesting = false;
        firstShotSent = false;
        holdSent = false;
        hasQueuedShot = false;
        ownerQueued = false;
        ownerPreLocked = false;
        ownerFinishing = false;
        held = false;
        lastShotTime = -1f;
        ownerLastShotTime = -1f;

        // 잠금 해제 — 정상 종료(EndLocal)·대시/상태 전이 끊김(CancelCurrentAttack)·시작 거절이 모두 여기를 지난다.
        facing.End();
        movement?.ReleaseFacingLock(this);
    }

    private void PlayShot(Vector3 origin, Vector3 end, bool hit)
    {
        // 회전 잠금 해제는 여기서 하지 않는다 — 오너 시계(ShotLockTail)로만 푼다(서버 왕복 없음, 2026-10-13).
        PlayFireAnimation();
        ShowBeam(origin, end, hit);
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
    private void ShowBeam(Vector3 origin, Vector3 end, bool hit)
    {
        if (beamView == null || data == null)
            return;

        bool stopped = hit || Vector3.Distance(origin, end) < data.Range - 0.01f;
        beamView.ShowBasicShot(origin, end, stopped, hit, data.BeamWidth);
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
