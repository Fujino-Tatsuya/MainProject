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
    private LineRenderer beamView;
    private float beamViewHideTime;

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

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

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
            animator = newAnimator;
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
        if (!player.BeginAttackState())
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
        if (!player.BeginAttackState())
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
        CrossFadeIfExists(data != null ? data.FireStateName : null);
        ShowBeam(origin, end);
    }

    private void CrossFadeIfExists(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName) || animator.runtimeAnimatorController == null)
            return;

        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, 0.03f);
    }

    // 🔸 임시 연출 — 민경 VFX 가 들어오면 교체한다. 판정 확인용으로 발사선을 잠깐 그린다.
    private void ShowBeam(Vector3 origin, Vector3 end)
    {
        if (data == null || data.BeamViewDuration <= 0f)
            return;

        if (beamView == null)
        {
            var go = new GameObject("GunnerBeamView(임시)");
            go.transform.SetParent(transform, false);
            beamView = go.AddComponent<LineRenderer>();
            beamView.useWorldSpace = true;
            beamView.positionCount = 2;
            beamView.material = new Material(Shader.Find("Sprites/Default"));
            beamView.startColor = beamView.endColor = new Color(0.6f, 0.9f, 1f, 0.9f);
        }

        beamView.startWidth = beamView.endWidth = data.BeamWidth;
        beamView.SetPosition(0, origin);
        beamView.SetPosition(1, end);
        beamView.enabled = true;
        beamViewHideTime = Time.time + data.BeamViewDuration;
    }

    private void LateUpdate()
    {
        if (beamView != null && beamView.enabled && Time.time >= beamViewHideTime)
            beamView.enabled = false;
    }

    private Vector3 CurrentAim() => Flatten(aim != null ? aim.AimDirection : transform.forward);

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
    }
}
