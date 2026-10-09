using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 분노 게이지·일반 E 강화·변신 상태(PLAN-assassin A7, §4.2 게이지 2026-10-09). 서버만 쓰고 전 피어에 하나의 스냅샷으로 복제한다 —
/// 변신 중 줄어드는 게이지·남은 변신 시간은 각 피어가 스냅샷과 네트워크 시계로 계산하므로 매 프레임 복제하지 않는다.
/// 충전(적중 판정)·감소·초기화는 서버, 오너는 HUD 와 R 입력 가능 판정에 읽기만 한다. R 시전 허가는 서버 스킬 승인이 최종 판정한다.
/// 규칙은 <see cref="AssassinStateModel"/>, 수치는 <see cref="AssassinStateData"/>.
///
/// 변신 종료(만료·수동 해제)는 "현재 공격 완료 후"다 — 서버 FSM 이 공격·스킬·간파 상태를 벗어난 첫 프레임에 끝내고
/// 슬롯을 원복한 뒤 R 쿨타임을 그 시점부터 시작한다(§10.2·§10.3). 쓰러짐·사망은 대기 없이 즉시 정리한다(§12.2).
/// </summary>
[RequireComponent(typeof(Player))]
public sealed class AssassinState : BaseNetworkBehaviour, IPassiveTooltipProvider
{
    [SerializeField] private AssassinStateData data;

    private readonly NetworkVariable<AssassinStateSnapshot> state = new NetworkVariable<AssassinStateSnapshot>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 오프라인(네트워크 없이 실행) 경로용 로컬 사본
    private AssassinStateSnapshot offlineState;

    private Player player;
    private PlayerStateController stateController;
    private PlayerSkillController skills;
    private PlayerInputReader input;
    private PlayerLifeCycleController lifeCycle;
    private AssassinEnhanceSkill enhanceSkill;
    private AssassinTransformSkill transformSkill;

    /// <summary>[전 피어] 스냅샷이 바뀌었다(이전, 현재). 변신 중 게이지 감소는 스냅샷을 바꾸지 않는다 — 표시는 <see cref="Rage"/> 를 매 프레임 읽는다.</summary>
    public event Action<AssassinStateSnapshot, AssassinStateSnapshot> StateChanged;

    /// <summary>[전 피어] 변신 시작(true)·종료(false). VFX 훅.</summary>
    public event Action<bool> TransformChanged;

    /// <summary>[전 피어] 일반 E 강화 준비(true)·소모/제거(false). VFX 훅.</summary>
    public event Action<bool> EnhancedReadyChanged;

    public AssassinStateData Data => data;

    /// <summary>P 칸 툴팁 출처(백어택 + 분노 게이지 설명) — PassiveHUD 가 캐릭터 타입을 모르고 찾는다.</summary>
    public ISkillTooltipSource PassiveTooltip => data;
    public AssassinStateSnapshot Snapshot => State;

    /// <summary>지금 분노 게이지(0~최대). 변신 중이면 감소를 반영한 값.</summary>
    public float Rage => AssassinStateModel.Rage(State, Now(), Rules);
    public float MaxRage => Rules.MaxRage;
    public float MinTransformRage => Rules.MinTransformRage;
    public bool IsEnhancedReady => State.enhancedReady;
    public bool IsTransformed => State.transformed;
    public bool IsReleaseRequested => State.releaseRequested;
    public float ReleaseLockSeconds => Rules.ReleaseLockSeconds;

    /// <summary>변신 종료 예정 시각(네트워크 시계). 일반 상태면 0.</summary>
    public double TransformEndServerTime => State.transformed ? State.transformEndTime : 0.0;

    public float TransformDuration => State.transformed
        ? (float)Math.Max(0.0, State.transformEndTime - State.transformStartTime)
        : 0f;

    public float TransformElapsed => AssassinStateModel.Elapsed(State, Now());
    public float TransformRemaining => AssassinStateModel.Remaining(State, Now());

    /// <summary>지금 R 을 다시 누르면 해제 요청이 받아들여지는가(2초 제한 경과·미요청).</summary>
    public bool CanRequestRelease => AssassinStateModel.CanRequestRelease(State, Now(), Rules);

    /// <summary>만료·수동 해제로 종료를 기다리는 중 — 새 공격·다음 묶음을 시작하지 않는다.</summary>
    public bool IsTransformEndPending => AssassinStateModel.IsEndPending(State, Now());

    /// <summary>백어택 배율(A11). 데이터가 없으면 1(백어택 없음).</summary>
    public float BackAttackMultiplier => data != null ? data.BackAttackMultiplier : 1f;

    /// <summary>[서버] 어쌔신 타격에 백어택 배율과 변신 중 강제 여부를 싣는다(A11). Q·변신 E·간파가 쓴다(평타는 BaseAttack.SetBackAttack 에 같은 두 값).</summary>
    public AttackInfo WithBackAttack(AttackInfo attackInfo)
    {
        attackInfo.backAttackMultiplier = BackAttackMultiplier;
        attackInfo.forceBackAttack = IsTransformed;
        return attackInfo;
    }

    /// <summary>일반 상태 + 게이지 ≥ 최소 변신량. 오너는 입력·HUD 판정에, 서버는 R 승인(<see cref="AssassinTransformSkill.CanUse"/>)에 쓴다.</summary>
    public bool CanBeginTransform => AssassinStateModel.CanBeginTransform(State, Rules);
    public bool CanPrepareEnhancement => AssassinStateModel.CanPrepareEnhancement(State);

    private AssassinStateSnapshot State => IsNetworkActive ? state.Value : offlineState;
    private AssassinStateRules Rules => data != null ? data.Rules : AssassinStateRules.Default;
    private bool IsInputSource => !IsNetworkActive || IsOwner;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
        skills = GetComponent<PlayerSkillController>();
        input = GetComponent<PlayerInputReader>();
        lifeCycle = GetComponent<PlayerLifeCycleController>();
        enhanceSkill = GetComponent<AssassinEnhanceSkill>();
        transformSkill = GetComponent<AssassinTransformSkill>();

        if (lifeCycle != null)
            lifeCycle.LifeStateChanged += HandleLifeStateChanged;
    }

    public override void OnDestroy()
    {
        if (lifeCycle != null)
            lifeCycle.LifeStateChanged -= HandleLifeStateChanged;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        state.OnValueChanged += HandleStateValueChanged;

        // 플레이어는 씬마다 새로 스폰된다 — 스폰 시점이 곧 초기화 시점이다.
        // 🔸 게이지는 스테이지 이동에 유지해야 하는데(§4.2) 지금은 스폰마다 0 이 된다 — 스테이지 간 이월 수단이 생기면 여기서 복원한다.
        if (IsServer)
            state.Value = default;
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= HandleStateValueChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsInputSource)
            TickOwnerReleaseInput();

        if (HasStateAuthority)
            TickServerTransformEnd();
    }

    // ── 서버 API (스킬·평타가 호출) ──

    /// <summary>
    /// [서버] 유효 대상 적중 공격 1회의 분노 게이지 충전(평타 1타·Q 1회·강타 1회). 여러 대상이어도 호출자가 1번만 부른다.
    /// 변신 중·상한이면 무시.
    /// </summary>
    public bool ServerTryGainRage(AssassinRageSource source)
    {
        if (!HasStateAuthority)
            return false;

        AssassinStateSnapshot next = State;
        if (!AssassinStateModel.TryGainRage(ref next, source, Rules))
            return false;

        Write(next);
        Edit.Log($"[Assassin] 분노 게이지 +{Rules.GainFor(source):0.#} ({source}) → {next.rage:0.#}/{Rules.MaxRage:0.#}", this);
        return true;
    }

    /// <summary>[서버] 일반 E — 강화 준비. 쿨타임은 시작하지 않는다(§8.1).</summary>
    public bool ServerTryPrepareEnhancement()
    {
        if (!HasStateAuthority)
            return false;

        AssassinStateSnapshot next = State;
        if (!AssassinStateModel.TryPrepareEnhancement(ref next))
            return false;

        Write(next);
        Edit.Log("[Assassin] 일반 E 강화 준비", this);
        return true;
    }

    /// <summary>[서버] 강타가 실제로 시작됐다 — 강화 소모 + 일반 E 쿨타임 시작(§8.2).</summary>
    public bool ServerConsumeEnhancement()
    {
        if (!HasStateAuthority)
            return false;

        AssassinStateSnapshot next = State;
        if (!AssassinStateModel.TryConsumeEnhancement(ref next))
            return false;

        Write(next);
        StartEnhanceCooldown();
        Edit.Log("[Assassin] 강타 시작 — 강화 소모, 일반 E 쿨타임 시작", this);
        return true;
    }

    /// <summary>
    /// [서버] Parry_R 시작 — 게이지 전부가 연료, 감소 시작, 강화 준비 제거(+일반 E 쿨), Q·E 대체 세트 요청(§10.1).
    /// 대체 스킬이 비어 있는 슬롯은 컨트롤러가 무시하고, R 실행 중 요청은 R 종료 직후 적용된다(A2).
    /// </summary>
    public bool ServerBeginTransform()
    {
        if (!HasStateAuthority)
            return false;

        AssassinStateSnapshot next = State;
        if (!AssassinStateModel.TryBeginTransform(ref next, Now(), Rules, out bool removedEnhancement))
            return false;

        Write(next);
        if (removedEnhancement)
            StartEnhanceCooldown();

        if (skills != null)
        {
            skills.SetSlotOverride(PlayerSkillSlot.Main, true);
            skills.SetSlotOverride(PlayerSkillSlot.Sub, true);
        }

        Edit.Log($"[Assassin] 변신 시작 — 게이지 {next.rage:0.#} 연료, 지속 {Rules.DurationFor(next.rage):F1}s" +
                 (removedEnhancement ? ", 강화 제거(일반 E 쿨)" : ""), this);
        return true;
    }

    // ── 수동 해제 입력 ──

    // R 재입력은 공격·스킬 중에도 받는다(종료 대기 예외 — §3.1). FSM 은 Idle/Move 에서만 스킬 입력을 읽으므로 여기서 따로 본다.
    // 변신 중 FSM 경로의 R 시전 요청은 AssassinTransformSkill.CanUse 가 거부한다.
    private void TickOwnerReleaseInput()
    {
        if (input == null || !State.transformed || !input.GetSkillPressed(PlayerSkillSlot.Ultimate))
            return;

        // 2초 전 입력은 무시하고 예약하지 않는다. 서버가 같은 규칙으로 다시 검증한다.
        if (!CanRequestRelease)
            return;

        if (!IsNetworkActive)
            ServerTryRequestRelease();
        else
            RequestReleaseRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestReleaseRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ServerTryRequestRelease();
    }

    private void ServerTryRequestRelease()
    {
        AssassinStateSnapshot next = State;
        if (!AssassinStateModel.TryRequestRelease(ref next, Now(), Rules))
            return;

        Write(next);
        Edit.Log($"[Assassin] 변신 수동 해제 요청 (경과 {AssassinStateModel.Elapsed(next, Now()):F2}s)", this);
    }

    // ── 종료 ──

    private void TickServerTransformEnd()
    {
        AssassinStateSnapshot current = State;
        if (!current.transformed)
            return;

        if (!AssassinStateModel.ShouldFinishTransform(current, Now(), IsActionInProgress()))
            return;

        FinishTransformServer(current.releaseRequested ? "수동 해제" : "만료");
    }

    // 현재 공격 완료 기준(§10.3): 평타(묶음)·스킬(변신 Q/E 포함)·간파가 끝날 때까지 기다린다.
    // 변신 E 조준은 서버 FSM 에서 Idle/Move 라 대기 대상이 아니다 — 종료 시 오너가 조준을 취소한다.
    private bool IsActionInProgress()
    {
        if (stateController == null)
            return false;

        switch (stateController.CurrentState)
        {
            case PlayerActionState.Attack:
            case PlayerActionState.AttackReady:
            case PlayerActionState.Skill:
            case PlayerActionState.Focus:
            case PlayerActionState.Interrupt:
                return true;
            default:
                return false;
        }
    }

    private void FinishTransformServer(string reason)
    {
        AssassinStateSnapshot next = State;
        if (!AssassinStateModel.TryFinishTransform(ref next, Now(), Rules))
            return;

        Write(next);
        RestoreSlotsAndStartTransformCooldown();
        Edit.Log($"[Assassin] 변신 종료({reason}) — 남은 게이지 {next.rage:0.#} 보존, 슬롯 원복, R 쿨타임 시작", this);
    }

    private void RestoreSlotsAndStartTransformCooldown()
    {
        if (skills == null)
            return;

        skills.SetSlotOverride(PlayerSkillSlot.Main, false);
        skills.SetSlotOverride(PlayerSkillSlot.Sub, false);
        if (transformSkill != null)
            skills.StartCooldownServer(transformSkill);
    }

    private void StartEnhanceCooldown()
    {
        if (skills != null && enhanceSkill != null)
            skills.StartCooldownServer(enhanceSkill);
    }

    // ── 쓰러짐·사망 (§12.2) ──

    private void HandleLifeStateChanged(PlayerLifeState previous, PlayerLifeState current)
    {
        if (previous != PlayerLifeState.Alive || current == PlayerLifeState.Alive)
            return;

        // 오너: 진행 중 조준 취소(쿨타임 없음).
        if (IsInputSource && skills != null && skills.IsChoosingTarget)
            skills.CancelTargeting();

        if (HasStateAuthority)
            ResetForDownServer();
    }

    private void ResetForDownServer()
    {
        // 3. 진행 중 공격·스킬을 즉시 취소한다 — 아직 나지 않은 타격은 사라지고 이미 준 피해는 그대로다.
        //    (생명주기는 FSM 을 Dead 로 보내지 않으므로 여기서 직접 끊는다.)
        if (stateController != null)
        {
            PlayerActionState action = stateController.CurrentState;
            if (action == PlayerActionState.Attack || action == PlayerActionState.AttackReady)
                player.EndAttackState(); // Attack.Exit → AssassinBasicAttack.CancelCurrentAttack (+클라 CancelRpc)
            else if (skills != null && skills.IsSkillActive)
                skills.EndActiveSkillServer(SkillEndReason.CasterDied);
        }

        // 1·2·5. 게이지 0, 강화 제거(+일반 E 쿨), 변신 즉시 종료(+슬롯 원복·R 쿨). 6. 다른 쿨은 그대로.
        AssassinStateSnapshot next = State;
        AssassinDownResult result = AssassinStateModel.ResetForDown(ref next);
        Write(next);

        if (result.HadEnhancement)
            StartEnhanceCooldown();
        if (result.WasTransformed)
            RestoreSlotsAndStartTransformCooldown();

        Edit.Log($"[Assassin] 쓰러짐·사망 정리 — 강화 제거={result.HadEnhancement}, 변신 종료={result.WasTransformed}", this);
    }

    // ── 복제·통지 ──

    private void Write(AssassinStateSnapshot next)
    {
        if (IsNetworkActive)
        {
            state.Value = next;
            return;
        }

        AssassinStateSnapshot previous = offlineState;
        offlineState = next;
        RaiseChanged(previous, next);
    }

    private void HandleStateValueChanged(AssassinStateSnapshot previous, AssassinStateSnapshot current)
    {
        RaiseChanged(previous, current);
    }

    private void RaiseChanged(AssassinStateSnapshot previous, AssassinStateSnapshot current)
    {
        if (previous.Equals(current))
            return;

        StateChanged?.Invoke(previous, current);

        if (previous.enhancedReady != current.enhancedReady)
            EnhancedReadyChanged?.Invoke(current.enhancedReady);

        if (previous.transformed != current.transformed)
        {
            // 변신 E 조준 중 종료 → 조준 취소, 쿨타임 없음(§9.1). 슬롯이 원복되면 같은 키가 일반 E 를 가리키게 된다.
            if (!current.transformed && IsInputSource && skills != null && skills.IsChoosingTarget)
                skills.CancelTargeting();

            TransformChanged?.Invoke(current.transformed);
        }
    }

    // 상태이상·보호막·과열과 같은 시간 도메인.
    private double Now()
        => NetworkClock.Instance != null
            ? NetworkClock.Instance.GameNow
            : (NetworkManager != null && IsNetworkActive ? NetworkManager.ServerTime.Time : Time.timeAsDouble);
}
