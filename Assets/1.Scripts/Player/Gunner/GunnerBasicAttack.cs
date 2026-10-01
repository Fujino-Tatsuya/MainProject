using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 기본 공격 — 좌클릭 유지 연사 레이저 (character_gunner.md §4, PLAN-gunner.md G3).
///
/// 흐름: 오너가 시작 요청 → 서버 승인(Attack 상태, 준비 동작 1회) → 오너가 발사 간격마다 "한 발 + 조준"을 요청 →
/// 서버가 간격·과열·상태를 검사해 판정(첫 대상 1체)·과열 증가 → 전 피어 연출.
/// 버튼을 놓거나 과열되면 마지막 발의 후속 동작까지 마무리하고 끝난다. 공격 중 이동 불가, 조준은 즉시 회전.
/// 공격 중 다른 스킬 입력은 상태기계가 막는다(Attack 상태 — §3.2, 예약 없음).
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
    // 오너가 떼지도 쏘지도 않고 멈췄을 때(연결 문제 등) 서버가 스스로 끝내는 여유(발사 간격 배수).
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
    private float fireClipLength = -1f;

    // 전 피어 공통 런타임
    private bool active;
    private float startTime;
    private float lastShotTime = -1f;

    // 오너
    private bool isRequesting;
    private bool releaseSent;
    private float nextShotTime;

    // 서버
    private bool released;
    private bool endingGracefully;

    public bool CanStartApprovedAttack => data != null && heat != null && !heat.IsOverheated;

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

        if (releaseSent)
            return;

        if (!input.AttackHeld)
        {
            releaseSent = true;
            if (IsNetworkActive)
                ReleaseRpc();
            else
                released = true;
            return;
        }

        if (Time.time < nextShotTime || heat.IsOverheated)
            return;

        // 한 프레임이 길어도 간격을 누적해 몰아 쏘지 않는다.
        nextShotTime = Mathf.Max(nextShotTime + data.FireInterval, Time.time + data.FireInterval * 0.5f);

        if (IsNetworkActive)
            FireShotRpc(CurrentAim());
        else
            ServerFire(CurrentAim());
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
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerFire(direction);
    }

    [Rpc(SendTo.Server)]
    private void ReleaseRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        released = true;
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
        if (!active || released || data == null)
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

        Vector3 origin = transform.position + Vector3.up * data.MuzzleHeight;
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
        float lastAction = lastShotTime >= 0f ? lastShotTime : startTime + data.WindupDuration;
        float endAt = lastShotTime >= 0f ? lastShotTime + data.ShotRecovery : startTime + data.WindupDuration;

        // 놓았거나 과열됐으면 현재 발의 후속 동작까지 마무리하고 끝낸다(§4.3·§4.4).
        if ((released || heat.IsOverheated) && Time.time >= endAt)
        {
            EndServer();
            return;
        }

        if (!released && Time.time > lastAction + data.FireInterval * StallIntervals)
        {
            Edit.LogWarning("[Gunner] 오너 발사 요청이 끊겼다 — 기본 공격을 서버에서 끝낸다.", this);
            EndServer();
        }
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
        released = false;
        releaseSent = false;
        startTime = Time.time;
        lastShotTime = -1f;
        nextShotTime = startTime + data.WindupDuration;

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
        released = false;
        releaseSent = false;
        lastShotTime = -1f;
    }

    private void PlayShot(Vector3 origin, Vector3 end, bool hit)
    {
        PlayFireAnimation();
        ShowBeam(origin, end);
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

    private void ShowBeam(Vector3 origin, Vector3 end)
    {
        if (beamView != null && data != null)
            beamView.ShowLocal(GunnerBeamView.Kind.BasicAttack, origin, end, data.BeamWidth);
    }

    // 준비 자세 → 연사. 상태기계는 오너·서버만 틱하므로 원격 프록시도 넘어가도록 Update 에서 각 피어가 시간으로 전환한다
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
