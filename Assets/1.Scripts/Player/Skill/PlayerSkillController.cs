using System;
using System.Collections.Generic;
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
    private const int InvalidSkillIndex = -1;

    [Serializable]
    private sealed class AlternateSkillSet
    {
        [SerializeField] private PlayerSkillBase mainSkill;
        [SerializeField] private PlayerSkillBase subSkill;
        [SerializeField] private PlayerSkillBase interruptSkill;
        [SerializeField] private PlayerSkillBase ultimateSkill;

        public PlayerSkillBase GetSkill(PlayerSkillSlot slot) => slot switch
        {
            PlayerSkillSlot.Main => mainSkill,
            PlayerSkillSlot.Sub => subSkill,
            PlayerSkillSlot.Interrupt => interruptSkill,
            PlayerSkillSlot.Ultimate => ultimateSkill,
            _ => null
        };
    }

    [SerializeField] private Animator animator;
    [SerializeField] private PlayerSkillBase mainSkill;      // Q — 진격의 방패
    [SerializeField] private PlayerSkillBase subSkill;       // E — 수호자의 의지
    [SerializeField] private PlayerSkillBase interruptSkill; // 우클릭 — 단죄의 방패
    [SerializeField] private PlayerSkillBase ultimateSkill;  // R — 최후의 심판
    [SerializeField] private AlternateSkillSet alternateSkills = new AlternateSkillSet();
    [SerializeField] private float endFallbackPadding = 0.1f;

    private readonly NetworkVariable<byte> activeSlotOverrides = new NetworkVariable<byte>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Player player;
    private PlayerStateController stateController;
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private PlayerSkillTargeting targeting;

    // 기본 4슬롯 뒤 대체 4슬롯을 같은 슬롯 순서로 등록한다. null은 건너뛰며 모든 피어에서 순서가 같다.
    private readonly List<PlayerSkillBase> registeredSkills = new List<PlayerSkillBase>(SlotCount * 2);
    private readonly Dictionary<PlayerSkillBase, int> skillIndices = new Dictionary<PlayerSkillBase, int>();
    private PlayerSkillCooldownLedger cooldownLedger;
    private PlayerSkillSlotBindingModel slotBindings;
    private byte locallyAppliedOverrideMask;
    private PlayerSkillBase activeSkill;
    private float activeEndFallbackTime;
    private float nextAimSendTime;
    private bool hasNotifiedRelease;
    private bool isRequestingSkill;
    // 시전 시점에 확정한 홀드 조작 방식 (UserInputConfig). 진행 중 옵션이 바뀌어도 이번 시전은 흔들리지 않는다.
    private bool activeHoldUsesToggle;
    // 토글 종료 입력 수용 여부. 시전한 그 입력이 떨어지기 전까지는 false다.
    private bool isToggleEndArmed;

    public PlayerSkillBase ActiveSkill => activeSkill;
    public bool IsSkillActive => activeSkill != null;
    public event Action<PlayerSkillSlot> SlotBindingChanged;
    // 조준/자동이동/확정 직후 프레임 — FSM이 일반 액션 입력(공격/다른 스킬)을 억제하는 데 쓴다.
    public bool IsChoosingTarget => targeting != null && targeting.IsInterceptingInput;

    // 조준 모드를 공용 대시로 끊을 수 있는가 — 조준 중인 스킬이 정한다(거너 R = 예, 기본 = 아니오).
    public bool CanCancelTargetingByDash
    {
        get
        {
            if (targeting == null || !targeting.IsTargeting)
                return false;
            PlayerSkillBase skill = GetSkill(targeting.CurrentSlot);
            return skill != null && skill.CanCancelAimByDash;
        }
    }

    // [오너] 조준 모드 취소 — 시전하지 않았으므로 쿨타임 없음.
    public void CancelTargeting() => targeting?.Cancel();
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();
        targeting = GetComponent<PlayerSkillTargeting>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        RegisterSkill(mainSkill, PlayerSkillSlot.Main);
        RegisterSkill(subSkill, PlayerSkillSlot.Sub);
        RegisterSkill(interruptSkill, PlayerSkillSlot.Interrupt);
        RegisterSkill(ultimateSkill, PlayerSkillSlot.Ultimate);

        byte alternateMask = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            PlayerSkillSlot slot = (PlayerSkillSlot)i;
            PlayerSkillBase alternate = alternateSkills?.GetSkill(slot);
            if (alternate != null)
                alternateMask |= (byte)(1 << i);
            RegisterSkill(alternate, slot);
        }

        cooldownLedger = new PlayerSkillCooldownLedger(registeredSkills.Count);
        slotBindings = new PlayerSkillSlotBindingModel(alternateMask);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        activeSlotOverrides.OnValueChanged += HandleOverrideMaskChanged;

        if (IsServer && slotBindings != null && activeSlotOverrides.Value != slotBindings.ActiveMask)
            activeSlotOverrides.Value = slotBindings.ActiveMask;

        locallyAppliedOverrideMask = activeSlotOverrides.Value;
    }

    public override void OnNetworkDespawn()
    {
        activeSlotOverrides.OnValueChanged -= HandleOverrideMaskChanged;
        base.OnNetworkDespawn();
    }

    private void RegisterSkill(PlayerSkillBase skill, PlayerSkillSlot slot)
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

        if (skillIndices.ContainsKey(skill))
        {
            Debug.LogError($"[Player] {skill.GetType().Name} 스킬이 둘 이상의 슬롯/세트에 중복 등록됐습니다.", this);
            return;
        }

        skillIndices.Add(skill, registeredSkills.Count);
        registeredSkills.Add(skill);
    }

    // 스킬별 쿨타임 장부가 만료되면 스킬 내부 State도 Cooldown → Ready로 복귀시킨다.
    // 시전 검증은 장부가 담당하므로 State는 표시/조회용 — ResetToReady가 Cooldown일 때만 동작한다.
    private void Update()
    {
        for (int i = 0; i < registeredSkills.Count; i++)
        {
            PlayerSkillBase skill = registeredSkills[i];
            if (skill != null && skill != activeSkill && cooldownLedger.IsReady(i, Time.time))
                skill.ResetToReady();
        }
    }

    public PlayerSkillBase GetSkill(PlayerSkillSlot slot)
    {
        PlayerSkillBase baseSkill = slot switch
        {
            PlayerSkillSlot.Main => mainSkill,
            PlayerSkillSlot.Sub => subSkill,
            PlayerSkillSlot.Interrupt => interruptSkill,
            PlayerSkillSlot.Ultimate => ultimateSkill,
            _ => null
        };

        int slotIndex = (int)slot;
        if (slotIndex < 0 || slotIndex >= SlotCount || (CurrentOverrideMask & (1 << slotIndex)) == 0)
            return baseSkill;

        return alternateSkills?.GetSkill(slot) ?? baseSkill;
    }

    public bool IsCooldownReady(PlayerSkillSlot slot)
    {
        return IsCooldownReady(GetSkill(slot));
    }

    public bool IsCooldownReady(PlayerSkillBase skill)
    {
        int skillIndex = GetSkillIndex(skill);
        return cooldownLedger != null && cooldownLedger.IsReady(skillIndex, Time.time);
    }

    public float GetCooldownRemaining(PlayerSkillSlot slot)
    {
        return GetCooldownRemaining(GetSkill(slot));
    }

    public float GetCooldownRemaining(PlayerSkillBase skill)
    {
        int skillIndex = GetSkillIndex(skill);
        return cooldownLedger != null ? cooldownLedger.GetRemaining(skillIndex, Time.time) : 0f;
    }

    public int GetSkillIndex(PlayerSkillBase skill) =>
        skill != null && skillIndices.TryGetValue(skill, out int index) ? index : InvalidSkillIndex;

    private byte CurrentOverrideMask => IsNetworkActive
        ? activeSlotOverrides.Value
        : slotBindings != null ? slotBindings.ActiveMask : (byte)0;

    // ── 오너 입력 진입점 ──

    // 입력 진입점. 타겟팅 스킬은 즉시 시전 대신 조준 모드로 진입하고, 아니면 바로 시전한다.
    public bool TryUse(PlayerSkillSlot slot)
    {
        PlayerSkillBase skill = GetSkill(slot);
        if (skill == null || skill.Data == null)
            return false;

        if (skill.Data.TargetingMode != SkillTargetingMode.None && CanBeginTargeting())
        {
            // 조준 진입 게이트: 실행 중/요청 중/쿨타임 미완이면 진입 안 함 (서버가 최종 검증하지만 UX상 조기 차단)
            if (IsSkillActive || isRequestingSkill || !IsCooldownReady(slot))
                return false;

            return targeting.Begin(slot);
        }

        return Cast(slot, null, Vector3.zero, false);
    }

    // 명시적 대상 지정 시전 (기존 시그니처 유지 — 조준 모드를 건너뛰고 바로 시전).
    public bool TryUse(PlayerSkillSlot slot, Unit target)
    {
        return Cast(slot, target, Vector3.zero, false);
    }

    // 조준 확정 경로 — PlayerSkillTargeting이 좌클릭 확정 시 호출. 조준 재진입 없이 실제 시전한다.
    public bool ExecuteTargetedSkill(PlayerSkillSlot slot, Unit target, Vector3 aimPoint, bool hasAimPoint)
    {
        return Cast(slot, target, aimPoint, hasAimPoint);
    }

    // 조준 모드는 로컬 조작자(오너/오프라인)에서만, 타겟팅 컴포넌트가 있을 때만 시작할 수 있다.
    private bool CanBeginTargeting()
    {
        return targeting != null && (!IsNetworkActive || IsOwner);
    }

    /// <summary>
    /// [오너] 지점 자동 접근(GroundPoint AutoApproach) 의도를 서버에 알린다 — Unit 대상은 Player.SubmitAutoApproachIntent.
    /// 위치 권위가 서버라 서버도 같은 전진을 만들어야 한다. 슬롯과 지점만 보내고 사거리는 서버가 자기 데이터에서 읽는다.
    /// </summary>
    internal void SubmitPointApproachIntent(PlayerSkillSlot slot, Vector3 point, bool active)
    {
        if (!IsNetworkActive || !IsOwner || IsServer)
            return;

        SubmitPointApproachRpc(slot, point, active);
    }

    // 같은 스킬키 재입력 여부 — 조준 취소 판별용(PlayerSkillTargeting에서 조회).
    public bool WasSkillRePressed(PlayerSkillSlot slot)
    {
        return inputReader != null && inputReader.GetSkillPressed(slot);
    }

    // 실제 시전 공통 경로. aimPoint는 GroundPoint 확정 지점(hasAimPoint일 때만 유효).
    private bool Cast(PlayerSkillSlot slot, Unit target, Vector3 aimPoint, bool hasAimPoint)
    {
        if (IsSkillActive || isRequestingSkill)
            return false;

        PlayerSkillBase skill = GetSkill(slot);
        if (skill == null || skill.Data == null)
            return false;

        int skillIndex = GetSkillIndex(skill);
        if (skillIndex == InvalidSkillIndex)
            return false;

        Vector3 direction = hasAimPoint
            ? ResolveDirection(aimPoint - transform.position)
            : GetCurrentAimDirection();

        if (!IsNetworkActive)
            return StartSkillServer(slot, skillIndex, direction, target, aimPoint, hasAimPoint);

        if (!IsOwner)
            return false;

        NetworkObjectReference targetRef = default;
        if (target != null && target.NetworkObject != null && target.NetworkObject.IsSpawned)
            targetRef = target.NetworkObject;

        isRequestingSkill = true;
        RequestUseSkillRpc(slot, skillIndex, direction, targetRef, aimPoint, hasAimPoint);
        return true;
    }

    // PlayerSkillState.Tick에서 호출 (오너 + 서버만 FSM을 틱한다)
    public void Tick()
    {
        if (!IsNetworkActive || IsOwner)
        {
            TickOwnerSkillInput();
            TickOwnerHoldInput();
        }

        if (HasGameplayAuthority)
            TickServer();
    }

    // PlayerSkillState.FixedTick에서 호출 (오너 + 서버만). 스킬 자체 이동(백스텝 등)용.
    public void FixedTick()
    {
        activeSkill?.OnFixedTick();
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

        SkillEndReason reason = nextState switch
        {
            PlayerActionState.Dead => SkillEndReason.CasterDied,
            PlayerActionState.Dash => SkillEndReason.DashCancelled,
            _ => SkillEndReason.Cancelled,
        };
        skill.OnEnd(reason);

        if (IsNetworkActive && IsServer)
            EndSkillClientRpc(GetSkillIndex(skill), reason);

        ApplyPendingSlotOverrides();
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

        if (stateController != null && stateController.IsInSkillState)
            stateController.EndSkill();

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

        if (IsNetworkActive)
            EndSkillClientRpc(GetSkillIndex(skill), reason);

        ApplyPendingSlotOverrides();
    }

    /// <summary>
    /// [서버] 슬롯을 기본/대체 스킬에 바인딩한다. 스킬 실행 중 요청은 종료 직후까지 보류한다.
    /// 대체 스킬이 비어 있는 슬롯의 활성화 요청은 무시한다.
    /// </summary>
    public void SetSlotOverride(PlayerSkillSlot slot, bool useAlternate)
    {
        if (!HasGameplayAuthority || slotBindings == null)
            return;

        if (slotBindings.Request(slot, useAlternate, activeSkill != null))
            PublishSlotOverrides();
    }

    /// <summary>
    /// [서버] 수동 커밋 스킬(<see cref="PlayerSkillData.CommitCooldownManually"/>)의 쿨타임을 지금부터 시작한다.
    /// 오너 HUD 장부에도 미러한다. 자동 커밋 스킬에서 부르면 무시한다(이미 승인 때 시작됨).
    /// </summary>
    public void CommitCooldownServer(PlayerSkillSlot slot)
    {
        PlayerSkillBase skill = GetSkill(slot);
        CommitCooldownServer(skill);
    }

    /// <summary>[서버] 수동 커밋으로 설정된 특정 스킬 인스턴스의 쿨타임을 시작한다.</summary>
    public void CommitCooldownServer(PlayerSkillBase skill)
    {
        if (skill == null || skill.Data == null || !skill.Data.CommitCooldownManually)
            return;

        StartCooldownServer(skill);
    }

    /// <summary>[서버] 등록된 특정 스킬의 쿨타임을 시작한다. 이미 진행 중이면 남은 시간을 유지한다.</summary>
    public void StartCooldownServer(PlayerSkillBase skill)
    {
        int skillIndex = GetSkillIndex(skill);
        if (!HasGameplayAuthority || skillIndex == InvalidSkillIndex || skill.Data == null ||
            !cooldownLedger.IsReady(skillIndex, Time.time))
        {
            return;
        }

        cooldownLedger.Start(skillIndex, Time.time, skill.Data.CooldownTime);
        Edit.Log($"[Skill] {skill.Slot} 쿨타임 시작 {skill.Data.CooldownTime}s", this);

        MirrorCooldownToOwner(skillIndex);
    }

    /// <summary>[서버] 등록된 특정 스킬의 남은 쿨타임을 감소시킨다. 결과는 0초 아래로 내려가지 않는다.</summary>
    public void ReduceCooldownServer(PlayerSkillBase skill, float seconds)
    {
        int skillIndex = GetSkillIndex(skill);
        if (!HasGameplayAuthority || skillIndex == InvalidSkillIndex || seconds <= 0f)
            return;

        float before = cooldownLedger.GetRemaining(skillIndex, Time.time);
        cooldownLedger.Reduce(skillIndex, Time.time, seconds);
        if (cooldownLedger.GetRemaining(skillIndex, Time.time) >= before)
            return;

        MirrorCooldownToOwner(skillIndex);
    }

    /// <summary>
    /// [시뮬레이션 피어] 실행 중 스킬의 오너 권위 결과(어쌔신 Q 의 벽 조기 종료 거리 등)를 서버에 보고한다.
    /// 서버(호스트·오프라인)는 바로, 원격 오너는 RPC 로 넘긴다. 실행 중인 그 스킬에만 전달하고 값 검증은 스킬이 한다.
    /// </summary>
    public void ReportOwnerSkillResult(PlayerSkillBase skill, float value)
    {
        int skillIndex = GetSkillIndex(skill);
        if (skillIndex == InvalidSkillIndex)
            return;

        if (HasGameplayAuthority)
        {
            if (activeSkill == skill)
                skill.OnOwnerResultReported(value);
            return;
        }

        if (IsOwner)
            NotifySkillOwnerResultRpc(skillIndex, value);
    }

    // 애니메이션 이벤트 (릴레이 경유). 판정은 서버만 처리한다.
    public void HandleAnimationEvent(SkillAnimationEventType eventType)
    {
        if (IsNetworkActive && !IsServer)
            return;

        activeSkill?.OnAnimationEvent(eventType);
    }

    // ── 서버 처리 ──

    private bool StartSkillServer(
        PlayerSkillSlot slot, int expectedSkillIndex, Vector3 direction, Unit target, Vector3 aimPoint, bool hasAimPoint)
    {
        PlayerSkillBase requestedSkill = GetSkill(slot);
        if (hasAimPoint &&
            requestedSkill != null && requestedSkill.Data != null &&
            requestedSkill.Data.TargetingMode == SkillTargetingMode.GroundPoint &&
            requestedSkill.Data.FixedDistance)
        {
            aimPoint = PlayerGroundPointProjection.ReprojectServerFixedDistance(
                transform.position, aimPoint, requestedSkill.Data.CastRange, transform.forward);
            direction = aimPoint - transform.position;
        }

        if (!CanApproveSkill(
                slot, expectedSkillIndex, direction, target, aimPoint, hasAimPoint,
                out PlayerSkillBase skill, out bool isDead))
        {
            return false;
        }

        direction = ResolveDirection(direction);

        // 사망 중 허용 스킬(usableWhileDead)은 FSM 상태를 점유하지 않는다 — Dead 상태 유지
        if (!isDead && !stateController.BeginSkill(skill))
            return false;

        isRequestingSkill = false;
        activeSkill = skill;
        activeEndFallbackTime = Time.time + skill.Data.MaxActiveDuration + Mathf.Max(0f, endFallbackPadding);

        // 쿨타임은 승인 즉시 시작, 환불 없음 — 단 수동 커밋 스킬은 발동 시점에 스스로 시작한다(CommitCooldownServer)
        if (!skill.Data.CommitCooldownManually)
            cooldownLedger.Start(expectedSkillIndex, Time.time, skill.Data.CooldownTime);

        // 상태이상 modifier가 반영된 최종 공격력으로 스냅샷 (그릴 합의: SO 계수 × 최종 스탯)
        // 툴팁 계산식도 같은 계수·고정값을 쓰므로 판정 공식을 바꿀 때 SkillTooltipDamage 계산도 같이 바꿀 것.
        int damageSnapshot = Mathf.Max(0,
            Mathf.RoundToInt(player.FinalAttackDamage * skill.Data.AttackDamageMultiplier) + skill.Data.FlatDamageBonus);
        skill.SetDamageSnapshot(damageSnapshot);
        // 지점(AimPoint)은 CanApproveSkill 이 CanUse 직전에 이미 넣었다.

        Edit.Log($"[Skill] {slot} 시작 — 피해 스냅샷 {damageSnapshot}, 쿨타임 {skill.Data.CooldownTime}s", this);

        skill.OnServerStart(direction, target);
        PlaySkillPresentation(skill, direction);

        if (IsNetworkActive)
            PlaySkillClientRpc(expectedSkillIndex, direction, aimPoint, hasAimPoint);

        return true;
    }

    private bool CanApproveSkill(
        PlayerSkillSlot slot, int expectedSkillIndex, Vector3 direction, Unit target,
        Vector3 aimPoint, bool hasAimPoint,
        out PlayerSkillBase skill, out bool isDead)
    {
        skill = GetSkill(slot);
        isDead = stateController != null && stateController.CurrentState == PlayerActionState.Dead;

        if (skill == null || skill.Data == null || stateController == null)
        {
            Edit.Log($"[Skill] {slot} 거부 — 스킬/데이터 미배정", this);
            return false;
        }

        int serverSkillIndex = GetSkillIndex(skill);
        if (serverSkillIndex != expectedSkillIndex)
        {
            Edit.Log($"[Skill] {slot} 거부 — 슬롯 교체 경합(요청 {expectedSkillIndex}, 서버 {serverSkillIndex})", this);
            return false;
        }

        if (IsSkillActive)
        {
            Edit.Log($"[Skill] {slot} 거부 — {activeSkill.Slot} 실행 중", this);
            return false;
        }

        if (!IsCooldownReady(skill))
        {
            Edit.Log($"[Skill] {slot} 거부 — 쿨타임 {GetCooldownRemaining(skill):F1}s 남음", this);
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

        // 지점 기반 CanUse(거너 R 지점 지정 등)가 이번 요청의 지점을 보도록 CanUse 직전에 넣는다.
        // 실행 중 스킬은 위에서 이미 거부됐으므로 진행 중인 스킬의 지점을 덮어쓰지 않는다.
        // SingleTarget/None 은 hasAimPoint=false 라 HasAimPoint 도 false — 기존 판정 그대로.
        skill.SetAimPoint(aimPoint, hasAimPoint);

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

    // 오너: 실행 중 스킬의 조준 전송·좌클릭 전달·로컬 틱. 홀드 해제 판정은 TickOwnerHoldInput 이 따로 한다.
    private void TickOwnerSkillInput()
    {
        if (activeSkill == null || activeSkill.Data == null)
            return;

        Vector3 aim = GetCurrentAimDirection();
        activeSkill.OnOwnerTick(aim);

        // 🔸 OnOwnerTick 안에서 끝났을 수 있다
        if (activeSkill == null)
            return;

        if (activeSkill.WantsAimUpdates && Time.time >= nextAimSendTime)
        {
            nextAimSendTime = Time.time + AimSendInterval;

            if (!IsNetworkActive)
                activeSkill.OnAimUpdated(aim);
            else
                UpdateSkillAimRpc(aim);
        }

        if (activeSkill.ConsumesPrimaryInput && inputReader != null && inputReader.AttackPressed)
        {
            if (!IsNetworkActive)
                activeSkill.OnPrimaryPressed(aim);
            else
                NotifySkillPrimaryRpc(aim);
        }
    }

    private void TickOwnerHoldInput()
    {
        if (activeSkill == null || activeSkill.Data == null ||
            activeSkill.Data.InputType != PlayerSkillInputType.Hold)
        {
            return;
        }

        if (hasNotifiedRelease || inputReader == null || !ShouldEndHold())
            return;

        hasNotifiedRelease = true;

        if (!IsNetworkActive)
            activeSkill.OnReleased();
        else
            NotifySkillReleasedRpc();
    }

    // 홀드 종료 판정. 기본 조작은 키를 뗀 순간, 토글 조작은 키를 '다시 누른' 순간이다.
    // 지속시간 만료는 두 방식 모두 서버 안전망(EndActiveSkillServer / MaxDurationReached)이 처리한다.
    private bool ShouldEndHold()
    {
        if (!activeHoldUsesToggle)
            return !inputReader.GetSkillHeld(activeSkill.Slot);

        // 시전한 입력이 한 번 떨어지기 전에는 재입력을 받지 않는다 —
        // 안 그러면 시전 프레임의 press가 그대로 종료 입력이 돼 켜자마자 꺼진다.
        if (!isToggleEndArmed)
        {
            isToggleEndArmed = !inputReader.GetSkillHeld(activeSkill.Slot);
            return false;
        }

        return inputReader.GetSkillPressed(activeSkill.Slot);
    }

    // ── RPC (오너 → 서버) ──

    [Rpc(SendTo.Server)]
    private void RequestUseSkillRpc(
        PlayerSkillSlot slot, int expectedSkillIndex, Vector3 direction, NetworkObjectReference targetRef,
        Vector3 aimPoint, bool hasAimPoint, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        Unit target = ResolveTarget(targetRef);
        if (!StartSkillServer(slot, expectedSkillIndex, direction, target, aimPoint, hasAimPoint))
            RejectSkillClientRpc(CreateOwnerClientRpcParams());
    }

    [Rpc(SendTo.Server)]
    private void SubmitPointApproachRpc(PlayerSkillSlot slot, Vector3 point, bool active, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || targeting == null)
            return;

        if (!active)
        {
            targeting.ApplyServerPointApproach(Vector3.zero, 0f, false);
            return;
        }

        PlayerSkillBase skill = GetSkill(slot);
        bool valid = skill != null && skill.Data != null &&
                     skill.Data.TargetingMode == SkillTargetingMode.GroundPoint &&
                     !skill.Data.FixedDistance &&
                     skill.Data.GroundPointOutOfRange == GroundPointOutOfRangeMode.AutoApproach &&
                     IsFinite(point);
        if (!valid)
        {
            // 조용히 넘기면 "자동 접근이 가끔 안 먹는다"가 된다. 사유를 남긴다.
            Edit.LogWarning(
                $"[Skill] 지점 자동 접근 거부 — owner={OwnerClientId}, slot={slot}. 서버가 전진을 만들지 않는다.",
                this);
            targeting.ApplyServerPointApproach(Vector3.zero, 0f, false);
            return;
        }

        targeting.ApplyServerPointApproach(point, skill.Data.CastRange, true);
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

    [Rpc(SendTo.Server)]
    private void NotifySkillPrimaryRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (activeSkill != null && activeSkill.ConsumesPrimaryInput)
            activeSkill.OnPrimaryPressed(ResolveDirection(direction));
    }

    [Rpc(SendTo.Server)]
    private void NotifySkillOwnerResultRpc(int skillIndex, float value, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        PlayerSkillBase skill = GetSkillByIndex(skillIndex);
        if (skill != null && skill == activeSkill)
            skill.OnOwnerResultReported(value);
    }

    // ── RPC (서버 → 클라) ──

    [ClientRpc]
    private void CommitCooldownClientRpc(
        int skillIndex, float remaining, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner && cooldownLedger != null)
            cooldownLedger.SetRemaining(skillIndex, Time.time, remaining);
    }

    [ClientRpc]
    private void PlaySkillClientRpc(int skillIndex, Vector3 direction, Vector3 aimPoint, bool hasAimPoint)
    {
        if (IsServer)
            return;

        isRequestingSkill = false;

        PlayerSkillBase skill = GetSkillByIndex(skillIndex);
        if (skill == null)
            return;

        skill.SetAimPoint(aimPoint, hasAimPoint);

        // 표시용 쿨타임 미러 — 이 RPC 수신 = 서버 승인이므로 오너도 장부를 기록한다.
        // 서버 시점과의 오차(전송 지연)는 HUD 표시용으로 허용, 검증은 여전히 서버 장부가 담당 (그릴 합의)
        // 수동 커밋 스킬은 CommitCooldownClientRpc 가 따로 미러한다.
        if (IsOwner && skill.Data != null && !skill.Data.CommitCooldownManually)
            cooldownLedger.Start(skillIndex, Time.time, skill.Data.CooldownTime);

        if (stateController != null &&
            !stateController.IsInSkillState &&
            !stateController.BeginSkill(skill))
        {
            return;
        }

        activeSkill = skill;
        PlaySkillPresentation(skill, direction);
    }

    [ClientRpc]
    private void EndSkillClientRpc(int skillIndex, SkillEndReason reason)
    {
        if (IsServer)
            return;

        // 로컬(오너)에서 이미 정리된 경우(넉백 선반영 등) 이중 처리 방지
        PlayerSkillBase endedSkill = GetSkillByIndex(skillIndex);
        if (activeSkill != null && activeSkill == endedSkill)
        {
            PlayerSkillBase skill = activeSkill;
            activeSkill = null;
            activeEndFallbackTime = 0f;
            skill.OnEnd(reason);
        }

        if (stateController != null && stateController.IsInSkillState)
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

    private PlayerSkillBase GetSkillByIndex(int skillIndex) =>
        skillIndex >= 0 && skillIndex < registeredSkills.Count ? registeredSkills[skillIndex] : null;

    private void ApplyPendingSlotOverrides()
    {
        if (!HasGameplayAuthority || slotBindings == null || !slotBindings.ApplyPending())
            return;

        PublishSlotOverrides();
    }

    private void PublishSlotOverrides()
    {
        byte current = slotBindings.ActiveMask;
        if (IsNetworkActive)
        {
            activeSlotOverrides.Value = current;
            return;
        }

        byte previous = locallyAppliedOverrideMask;
        locallyAppliedOverrideMask = current;
        HandleOverrideMaskChanged(previous, current);
    }

    private void HandleOverrideMaskChanged(byte previous, byte current)
    {
        locallyAppliedOverrideMask = current;
        byte changed = (byte)(previous ^ current);
        for (int i = 0; i < SlotCount; i++)
        {
            if ((changed & (1 << i)) != 0)
                SlotBindingChanged?.Invoke((PlayerSkillSlot)i);
        }
    }

    private void MirrorCooldownToOwner(int skillIndex)
    {
        if (!IsNetworkActive || IsOwner)
            return;

        float remaining = cooldownLedger.GetRemaining(skillIndex, Time.time);
        CommitCooldownClientRpc(skillIndex, remaining, CreateOwnerClientRpcParams());
    }

    private void PlaySkillPresentation(PlayerSkillBase skill, Vector3 direction)
    {
        hasNotifiedRelease = false;
        nextAimSendTime = 0f;
        // 로컬 조작자의 설정만 의미가 있다 — 이 값을 읽는 TickOwnerHoldInput은 오너/오프라인에서만 돈다.
        activeHoldUsesToggle = skill.Data.InputType == PlayerSkillInputType.Hold &&
            UserInputConfig.HoldSkillAsToggle;
        isToggleEndArmed = false;

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

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
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

/// <summary>
/// 등록된 스킬 인스턴스별 쿨타임 종료 시각을 보관하는 순수 모델.
/// 슬롯 바인딩과 독립적이므로 비활성 스킬도 같은 시간축에서 계속 감소한다.
/// </summary>
public sealed class PlayerSkillCooldownLedger
{
    private readonly float[] nextReadyTimes;

    public int Count => nextReadyTimes.Length;

    public PlayerSkillCooldownLedger(int skillCount)
    {
        if (skillCount < 0)
            throw new ArgumentOutOfRangeException(nameof(skillCount));

        nextReadyTimes = new float[skillCount];
    }

    public bool IsReady(int skillIndex, float now) =>
        IsValid(skillIndex) && now >= nextReadyTimes[skillIndex];

    public float GetRemaining(int skillIndex, float now) =>
        IsValid(skillIndex) ? Math.Max(0f, nextReadyTimes[skillIndex] - now) : 0f;

    public void Start(int skillIndex, float now, float duration)
    {
        if (IsValid(skillIndex))
            nextReadyTimes[skillIndex] = now + Math.Max(0f, duration);
    }

    public void SetRemaining(int skillIndex, float now, float remaining)
    {
        if (IsValid(skillIndex))
            nextReadyTimes[skillIndex] = now + Math.Max(0f, remaining);
    }

    public void Reduce(int skillIndex, float now, float seconds)
    {
        if (IsValid(skillIndex) && seconds > 0f)
            nextReadyTimes[skillIndex] = Math.Max(now, nextReadyTimes[skillIndex] - seconds);
    }

    private bool IsValid(int skillIndex) => skillIndex >= 0 && skillIndex < nextReadyTimes.Length;
}

/// <summary>슬롯 대체 비트마스크와 실행 중 교체 보류를 관리하는 순수 모델.</summary>
public sealed class PlayerSkillSlotBindingModel
{
    private readonly byte alternateAvailableMask;
    private byte activeMask;
    private byte pendingMask;
    private bool hasPending;

    public byte ActiveMask => activeMask;
    public bool HasPending => hasPending;

    public PlayerSkillSlotBindingModel(byte alternateAvailableMask)
    {
        this.alternateAvailableMask = alternateAvailableMask;
    }

    public bool Request(PlayerSkillSlot slot, bool useAlternate, bool defer)
    {
        int slotIndex = (int)slot;
        if (slotIndex < 0 || slotIndex >= 4)
            return false;

        byte bit = (byte)(1 << slotIndex);
        if (useAlternate && (alternateAvailableMask & bit) == 0)
            return false;

        byte sourceMask = hasPending ? pendingMask : activeMask;
        byte desiredMask = useAlternate
            ? (byte)(sourceMask | bit)
            : (byte)(sourceMask & ~bit);

        if (defer)
        {
            pendingMask = desiredMask;
            hasPending = pendingMask != activeMask;
            return false;
        }

        bool changed = activeMask != desiredMask;
        activeMask = desiredMask;
        pendingMask = activeMask;
        hasPending = false;
        return changed;
    }

    public bool ApplyPending()
    {
        if (!hasPending)
            return false;

        bool changed = activeMask != pendingMask;
        activeMask = pendingMask;
        hasPending = false;
        return changed;
    }
}
