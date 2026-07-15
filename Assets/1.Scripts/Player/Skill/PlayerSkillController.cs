using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 스킬 시스템의 단일 네트워크 창구. 입력 라우팅 → 서버 승인 → 쿨타임 장부 → 실행/종료를 담당한다.
/// RPC는 전부 여기에만 둔다 — 스킬(PlayerSkillBase)은 RPC를 직접 갖지 않는다.
/// 흐름은 DefaultAttackController의 승인 패턴을 따른다: 오너 요청 → 서버 검증 → 전 클라 재생.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerSkillController : BaseNetworkBehaviour
{
    private static readonly int IdleHash = Animator.StringToHash("Idle");

    // 홀드 조향 오너→서버 전송 주기 (~10Hz). 서버는 마지막 값만 유지한다.
    private const float AimSendInterval = 0.1f;
    private const int SlotCount = 4;

    [SerializeField] private Animator animator;
    [SerializeField] private PlayerSkillBase mainSkill;      // Q — 진격의 방패
    [SerializeField] private PlayerSkillBase subSkill;       // E — 수호자의 의지
    [SerializeField] private PlayerSkillBase interruptSkill; // 우클릭 — 단죄의 방패
    [SerializeField] private PlayerSkillBase ultimateSkill;  // R — 최후의 심판
    [SerializeField] private float endFallbackPadding = 0.1f;

    private Player player;
    private PlayerStateController stateController;
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;

    // 서버 권위 쿨타임 장부. 승인 즉시 기록하며 환불하지 않는다 (사망 포함).
    private readonly float[] nextReadyTime = new float[SlotCount];
    private PlayerSkillBase activeSkill;
    private float activeEndFallbackTime;
    private float nextAimSendTime;
    private bool hasNotifiedRelease;
    private bool isRequestingSkill;

    public PlayerSkillBase ActiveSkill => activeSkill;
    public bool IsSkillActive => activeSkill != null;
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        InitializeSkill(mainSkill, PlayerSkillSlot.Main);
        InitializeSkill(subSkill, PlayerSkillSlot.Sub);
        InitializeSkill(interruptSkill, PlayerSkillSlot.Interrupt);
        InitializeSkill(ultimateSkill, PlayerSkillSlot.Ultimate);
    }

    private void InitializeSkill(PlayerSkillBase skill, PlayerSkillSlot slot)
    {
        if (skill == null)
            return;

        if (skill.Slot != slot)
        {
            Debug.LogError(
                $"[Player] {skill.GetType().Name}의 Slot({skill.Slot})이 배정된 슬롯({slot})과 다릅니다.",
                this);
        }

        skill.Initialize(player, this);
    }

    public PlayerSkillBase GetSkill(PlayerSkillSlot slot)
    {
        return slot switch
        {
            PlayerSkillSlot.Main => mainSkill,
            PlayerSkillSlot.Sub => subSkill,
            PlayerSkillSlot.Interrupt => interruptSkill,
            PlayerSkillSlot.Ultimate => ultimateSkill,
            _ => null
        };
    }

    public bool IsCooldownReady(PlayerSkillSlot slot)
    {
        return Time.time >= nextReadyTime[(int)slot];
    }

    public float GetCooldownRemaining(PlayerSkillSlot slot)
    {
        return Mathf.Max(0f, nextReadyTime[(int)slot] - Time.time);
    }

    // ── 오너 입력 진입점 ──

    public bool TryUse(PlayerSkillSlot slot)
    {
        return TryUse(slot, null);
    }

    public bool TryUse(PlayerSkillSlot slot, Unit target)
    {
        if (IsSkillActive || isRequestingSkill)
            return false;

        PlayerSkillBase skill = GetSkill(slot);
        if (skill == null || skill.Data == null)
            return false;

        Vector3 direction = GetCurrentAimDirection();

        if (!IsNetworkActive)
            return StartSkillServer(slot, direction, target);

        if (!IsOwner)
            return false;

        NetworkObjectReference targetRef = default;
        if (target != null && target.NetworkObject != null && target.NetworkObject.IsSpawned)
            targetRef = target.NetworkObject;

        isRequestingSkill = true;
        RequestUseSkillRpc(slot, direction, targetRef);
        return true;
    }

    // PlayerSkillState.Tick에서 호출 (오너 + 서버만 FSM을 틱한다)
    public void Tick()
    {
        if (!IsNetworkActive || IsOwner)
            TickOwnerHoldInput();

        if (HasGameplayAuthority)
            TickServer();
    }

    // FSM이 Skill 상태를 떠날 때 호출 — 정상 종료(EndActiveSkillServer)는 activeSkill을 먼저 비우므로
    // 여기 도달했는데 activeSkill이 남아 있으면 외부 요인(넉백/그랩/사망) 강제 이탈이다.
    public void HandleSkillStateExit(PlayerActionState nextState)
    {
        isRequestingSkill = false;

        if (activeSkill == null)
            return;

        PlayerSkillBase skill = activeSkill;
        activeSkill = null;
        activeEndFallbackTime = 0f;

        SkillEndReason reason = nextState == PlayerActionState.Dead
            ? SkillEndReason.CasterDied
            : SkillEndReason.Cancelled;
        skill.OnEnd(reason);

        if (IsNetworkActive && IsServer)
            EndSkillClientRpc(skill.Slot, reason);
    }

    // 서버에서 실행 중인 스킬을 종료한다. 스킬 스스로(EndSelf) 또는 안전망이 호출.
    public void EndActiveSkillServer(SkillEndReason reason)
    {
        if (!HasGameplayAuthority || activeSkill == null)
            return;

        PlayerSkillBase skill = activeSkill;
        activeSkill = null;
        activeEndFallbackTime = 0f;

        Edit.Log($"[Skill] {skill.Slot} 종료 ({reason})", this);

        skill.OnEnd(reason);

        if (stateController != null && stateController.CurrentState == PlayerActionState.Skill)
            stateController.EndSkill();

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

        if (IsNetworkActive)
            EndSkillClientRpc(skill.Slot, reason);
    }

    // 애니메이션 이벤트 (릴레이 경유). 판정은 서버만 처리한다.
    public void HandleAnimationEvent(SkillAnimationEventType eventType)
    {
        if (IsNetworkActive && !IsServer)
            return;

        activeSkill?.OnAnimationEvent(eventType);
    }

    // ── 서버 처리 ──

    private bool StartSkillServer(PlayerSkillSlot slot, Vector3 direction, Unit target)
    {
        if (!CanApproveSkill(slot, direction, target, out PlayerSkillBase skill, out bool isDead))
            return false;

        direction = ResolveDirection(direction);

        // 사망 중 허용 스킬(usableWhileDead)은 FSM 상태를 점유하지 않는다 — Dead 상태 유지
        if (!isDead && !stateController.BeginSkill(skill))
            return false;

        isRequestingSkill = false;
        activeSkill = skill;
        activeEndFallbackTime = Time.time + skill.Data.MaxActiveDuration + Mathf.Max(0f, endFallbackPadding);

        // 쿨타임은 승인 즉시 시작, 환불 없음
        nextReadyTime[(int)slot] = Time.time + skill.Data.CooldownTime;

        // 상태이상 modifier가 반영된 최종 공격력으로 스냅샷 (그릴 합의: SO 계수 × 최종 스탯)
        int damageSnapshot = Mathf.Max(0,
            Mathf.RoundToInt(player.FinalAttackDamage * skill.Data.AttackDamageMultiplier) + skill.Data.FlatDamageBonus);
        skill.SetDamageSnapshot(damageSnapshot);

        Edit.Log($"[Skill] {slot} 시작 — 피해 스냅샷 {damageSnapshot}, 쿨타임 {skill.Data.CooldownTime}s", this);

        skill.OnServerStart(direction, target);
        PlaySkillPresentation(skill, direction);

        if (IsNetworkActive)
            PlaySkillClientRpc(slot, direction);

        return true;
    }

    private bool CanApproveSkill(
        PlayerSkillSlot slot, Vector3 direction, Unit target, out PlayerSkillBase skill, out bool isDead)
    {
        skill = GetSkill(slot);
        isDead = stateController != null && stateController.CurrentState == PlayerActionState.Dead;

        if (skill == null || skill.Data == null || stateController == null)
        {
            Edit.Log($"[Skill] {slot} 거부 — 스킬/데이터 미배정", this);
            return false;
        }

        if (IsSkillActive)
        {
            Edit.Log($"[Skill] {slot} 거부 — {activeSkill.Slot} 실행 중", this);
            return false;
        }

        if (!IsCooldownReady(slot))
        {
            Edit.Log($"[Skill] {slot} 거부 — 쿨타임 {GetCooldownRemaining(slot):F1}s 남음", this);
            return false;
        }

        if (isDead)
        {
            if (!skill.Data.UsableWhileDead)
            {
                Edit.Log($"[Skill] {slot} 거부 — 사망 상태", this);
                return false;
            }
        }
        else if (!stateController.CanUseSkill)
        {
            Edit.Log($"[Skill] {slot} 거부 — 상태 {stateController.CurrentState} 또는 차단 효과", this);
            return false;
        }

        if (!skill.CanUse(direction, target))
        {
            Edit.Log($"[Skill] {slot} 거부 — 스킬 자체 조건(CanUse) 불충족", this);
            return false;
        }

        return true;
    }

    private void TickServer()
    {
        if (activeSkill == null)
            return;

        activeSkill.OnTick();

        // OnTick 안에서 스킬이 스스로 종료했을 수 있다
        if (activeSkill != null && activeEndFallbackTime > 0f && Time.time >= activeEndFallbackTime)
            EndActiveSkillServer(SkillEndReason.MaxDurationReached);
    }

    private void TickOwnerHoldInput()
    {
        if (activeSkill == null || activeSkill.Data == null ||
            activeSkill.Data.InputType != PlayerSkillInputType.Hold)
        {
            return;
        }

        if (Time.time >= nextAimSendTime)
        {
            nextAimSendTime = Time.time + AimSendInterval;
            Vector3 direction = GetCurrentAimDirection();

            if (!IsNetworkActive)
                activeSkill.OnAimUpdated(direction);
            else
                UpdateSkillAimRpc(direction);
        }

        if (!hasNotifiedRelease && inputReader != null && !inputReader.GetSkillHeld(activeSkill.Slot))
        {
            hasNotifiedRelease = true;

            if (!IsNetworkActive)
                activeSkill.OnReleased();
            else
                NotifySkillReleasedRpc();
        }
    }

    // ── RPC (오너 → 서버) ──

    [Rpc(SendTo.Server)]
    private void RequestUseSkillRpc(
        PlayerSkillSlot slot, Vector3 direction, NetworkObjectReference targetRef, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        Unit target = ResolveTarget(targetRef);
        if (!StartSkillServer(slot, direction, target))
            RejectSkillClientRpc(CreateOwnerClientRpcParams());
    }

    [Rpc(SendTo.Server)]
    private void UpdateSkillAimRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        activeSkill?.OnAimUpdated(ResolveDirection(direction));
    }

    [Rpc(SendTo.Server)]
    private void NotifySkillReleasedRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        activeSkill?.OnReleased();
    }

    // ── RPC (서버 → 클라) ──

    [ClientRpc]
    private void PlaySkillClientRpc(PlayerSkillSlot slot, Vector3 direction)
    {
        if (IsServer)
            return;

        isRequestingSkill = false;

        PlayerSkillBase skill = GetSkill(slot);
        if (skill == null)
            return;

        // 표시용 쿨타임 미러 — 이 RPC 수신 = 서버 승인이므로 오너도 장부를 기록한다.
        // 서버 시점과의 오차(전송 지연)는 HUD 표시용으로 허용, 검증은 여전히 서버 장부가 담당 (그릴 합의)
        if (IsOwner && skill.Data != null)
            nextReadyTime[(int)slot] = Time.time + skill.Data.CooldownTime;

        if (stateController != null &&
            stateController.CurrentState != PlayerActionState.Skill &&
            !stateController.BeginSkill(skill))
        {
            return;
        }

        activeSkill = skill;
        PlaySkillPresentation(skill, direction);
    }

    [ClientRpc]
    private void EndSkillClientRpc(PlayerSkillSlot slot, SkillEndReason reason)
    {
        if (IsServer)
            return;

        // 로컬(오너)에서 이미 정리된 경우(넉백 선반영 등) 이중 처리 방지
        if (activeSkill != null)
        {
            PlayerSkillBase skill = activeSkill;
            activeSkill = null;
            activeEndFallbackTime = 0f;
            skill.OnEnd(reason);
        }

        if (stateController != null && stateController.CurrentState == PlayerActionState.Skill)
            stateController.EndSkill();

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    [ClientRpc]
    private void RejectSkillClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner)
            isRequestingSkill = false;
    }

    // ── 내부 유틸 ──

    private void PlaySkillPresentation(PlayerSkillBase skill, Vector3 direction)
    {
        hasNotifiedRelease = false;
        nextAimSendTime = 0f;

        if (skill.Data.SnapRotationOnStart && movement != null)
            movement.RotateImmediately(direction);

        player.SetAnimatorMoving(false);

        if (animator != null && !string.IsNullOrEmpty(skill.Data.AnimatorStateName))
            animator.CrossFadeInFixedTime(Animator.StringToHash(skill.Data.AnimatorStateName), 0.05f);

        skill.OnClientPlay(direction);
    }

    private static Unit ResolveTarget(NetworkObjectReference targetRef)
    {
        return targetRef.TryGet(out NetworkObject networkObject) && networkObject != null
            ? networkObject.GetComponent<Unit>()
            : null;
    }

    private Vector3 GetCurrentAimDirection()
    {
        if (aimIndicator != null)
            return ResolveDirection(aimIndicator.AimDirection);

        return ResolveDirection(transform.forward);
    }

    private Vector3 ResolveDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude >= 0.001f)
            return direction.normalized;

        return transform.forward;
    }

    private ClientRpcParams CreateOwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }
}
