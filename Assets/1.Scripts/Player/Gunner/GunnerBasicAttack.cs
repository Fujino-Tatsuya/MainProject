using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 기본 공격 — 클릭 1회 = 준비 동작 → 레이저 1발, 입력 창 안 클릭으로 다음 발을 잇는다
/// (character_gunner.md §4, PLAN-gunner.md G3). 홀드해도 연사하지 않는다.
///
/// 흐름: 오너가 시작 요청 → 서버 승인(준비 동작 1회) → 준비가 끝나면 오너가 "첫 발 + 조준"을 요청 →
/// 서버가 간격·과열·상태를 검사해 판정(첫 대상 1체)·과열 증가 → 전 피어 연출.
/// 이어 쏘기는 팔라딘 콤보(DefaultAttackController 의 ComboWindowOpen~Close)와 같은 구조다 — 오너가 클릭을
/// 보내면 서버가 자기 시각으로 창(직전 발사 + ComboWindowOpen~Close) 안인지 보고 예약, 발사 간격이 차면
/// 준비 동작 없이 다음 발을 쏜다. 예약이 없거나 과열되면 후속 동작(ShotRecovery)까지 마무리하고 끝난다.
/// 창은 애니 이벤트가 아니라 데이터의 시간 값이다(판정도 시간 기반이라 한 곳에서 맞춘다).
/// 공격 중 이동 불가, 조준은 즉시 회전. 다른 스킬 입력은 상태기계가 막는다(Attack 상태 — §3.2, 예약 없음).
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(GunnerHeat))]
[RequireComponent(typeof(GunnerBeamAttack))]
public class GunnerBasicAttack : BaseNetworkBehaviour, IPlayerBasicAttack
{
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int EmptyHash = Animator.StringToHash("Empty");
    private static readonly int FireSpeedHash = Animator.StringToHash("FireSpeed");

    // 오너 요청 간격이 네트워크 지터로 조금 당겨져 와도 받아 준다.
    private const float IntervalTolerance = 0.75f;
    // 준비가 끝났는데 오너의 첫 발 요청이 안 올 때(연결 문제 등) 서버가 스스로 끝내는 여유(발사 간격 배수).
    // 이어지는 발은 서버가 예약으로 직접 쏘므로 첫 발만 기다린다.
    private const float StallIntervals = 3f;

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
    private float fireClipLength = -1f;

    // 전 피어 공통 런타임
    private bool active;
    private float startTime;
    private float lastShotTime = -1f;

    // 오너
    private bool isRequesting;
    private bool firstShotSent;

    // 서버
    private bool hasQueuedShot;
    private Vector3 queuedDirection;
    private bool endingGracefully;

    public bool CanStartApprovedAttack => data != null && heat != null && !heat.IsOverheated;
    public GunnerBasicAttackData Data => data;

    // 공용 대시 우선(D1·D2) — 준비 중 끊기면 발사·과열 없음, 발사 후면 이미 쏜 발은 그대로이고 후속 동작만 끊긴다.
    public bool CanBeCanceledByDash => true;

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
    }

    // ── IPlayerBasicAttack ───────────────────────────────────────────────

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
        RequestStartRpc(direction);
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
        // 조준 방향으로 즉시 회전(§2.1 — 회전 속도 없음)
        movement.RotateImmediately(CurrentAim());

        // 첫 발 — 준비가 끝나면 그 순간의 조준으로 한 번만 요청한다(홀드해도 더 쏘지 않는다).
        if (!firstShotSent)
        {
            if (Time.time < startTime + data.WindupDuration || heat.IsOverheated)
                return;

            firstShotSent = true;
            if (IsNetworkActive)
                FireShotRpc(CurrentAim());
            else
                ServerFire(CurrentAim());
            return;
        }

        // 이어 쏘기 — 누른 프레임만 보낸다. 창 안인지는 서버가 자기 시각으로 판단한다(팔라딘 RequestQueueNextAttackRpc 와 같음).
        if (!input.AttackPressed)
            return;

        if (IsNetworkActive)
            RequestQueueShotRpc(CurrentAim());
        else
            ServerQueueShot(CurrentAim());
    }

    // ── 서버 ─────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void RequestStartRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

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
        // 오너가 직접 요청하는 건 첫 발뿐이다 — 이어지는 발은 창 예약으로만 나간다(창 우회 방지).
        if (rpcParams.Receive.SenderClientId != OwnerClientId || lastShotTime >= 0f)
            return;
        ServerFire(direction);
    }

    [Rpc(SendTo.Server)]
    private void RequestQueueShotRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerQueueShot(direction);
    }

    // 창(직전 발사 + ComboWindowOpen~Close) 안의 클릭만 다음 발로 예약한다. 창 밖 클릭은 버린다 — 이월 없음.
    private void ServerQueueShot(Vector3 direction)
    {
        if (!active || data == null || lastShotTime < 0f || hasQueuedShot || heat.IsOverheated)
            return;

        float sinceShot = Time.time - lastShotTime;
        if (sinceShot < data.ComboWindowOpen || sinceShot > data.ComboWindowClose)
            return;

        hasQueuedShot = true;
        queuedDirection = direction;
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

    private void ServerFire(Vector3 requested)
    {
        if (!active || data == null)
            return;

        float now = Time.time;
        if (now < startTime + data.WindupDuration * IntervalTolerance)
            return;
        if (lastShotTime >= 0f && now - lastShotTime < data.FireInterval * IntervalTolerance)
            return;
        if (heat.IsOverheated)
            return;

        Vector3 direction = Flatten(requested);
        movement.RotateImmediately(direction);

        // 단계 배율은 이번 발의 증가 전 단계로 계산한다. 최대치에 닿는 발도 피해는 정상 적용(§4.4).
        int stage = heat.CurrentStage;
        int baseDamage = Mathf.RoundToInt(player.FinalAttackDamage * data.AttackDamageMultiplier) + data.FlatDamageBonus;
        int damage = Mathf.Max(0, Mathf.RoundToInt(baseDamage * heat.StageDamageMultiplier(stage)));

        Vector3 origin = muzzle != null
            ? muzzle.position
            : transform.position + Vector3.up * data.MuzzleHeight;
        bool hit = beam.Fire(origin, direction, data.Range, data.BeamWidth * 0.5f,
                             data.HittableLayers, data.BlockingLayers, damage, data.TriggersOnHit, out Vector3 end);

        heat.ServerAddShot(data.HeatPerShot); // 허공이어도 증가(§4.2)
        lastShotTime = now;
        if (IsNetworkActive)
            ShotRpc(origin, end, hit);
        else
            PlayShot(origin, end, hit);
    }

    private void TickServer()
    {
        float now = Time.time;

        // 첫 발 대기 — 준비 끝에 과열이면 쏘지 않고 끝내고, 오너 요청이 끊기면 서버가 스스로 끝낸다.
        if (lastShotTime < 0f)
        {
            float fireAt = startTime + data.WindupDuration;
            if (heat.IsOverheated && now >= fireAt)
            {
                EndServer();
                return;
            }

            if (now > fireAt + data.FireInterval * StallIntervals)
            {
                Edit.LogWarning("[Gunner] 오너 첫 발 요청이 끊겼다 — 기본 공격을 서버에서 끝낸다.", this);
                EndServer();
            }
            return;
        }

        // 창 안에서 예약된 다음 발 — 발사 간격이 차면 준비 동작 없이 쏜다. 과열 등으로 거절되면 아래 마무리로 간다.
        if (hasQueuedShot)
        {
            if (now < lastShotTime + data.FireInterval)
                return;

            hasQueuedShot = false;
            ServerFire(queuedDirection);
            if (lastShotTime >= now)
                return;
        }

        // 예약이 없으면(창 밖·과열 포함) 이번 발의 후속 동작까지 마무리하고 끝낸다(§4.3·§4.4).
        if (now >= lastShotTime + data.ShotRecovery)
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
        startTime = Time.time;
        lastShotTime = -1f;

        movement.RotateImmediately(Flatten(direction));
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
        hasQueuedShot = false;
        lastShotTime = -1f;
    }

    private void PlayShot(Vector3 origin, Vector3 end, bool hit)
    {
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

    private float FireSpeed()
    {
        if (fireClipLength < 0f)
        {
            fireClipLength = 0f;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name == data.FireClipName)
                {
                    fireClipLength = clip.length;
                    break;
                }
            }
        }

        return fireClipLength > 0f ? Mathf.Max(1f, fireClipLength / data.FireInterval) : 1f;
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
        if (!active || data == null || player.CurrentState != PlayerActionState.AttackReady)
            return;

        if (Time.time >= startTime + data.WindupDuration)
            player.BeginAttackState();
    }

    private Vector3 CurrentAim() => Flatten(aim != null ? aim.AimDirection : transform.forward);

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
    }
}
