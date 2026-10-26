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
public class PlayerSkillController : BaseNetworkBehaviour, IPlayerAttackSpeedAnimatorUser
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

    [Tooltip("스킬을 쿨타임 중에 눌러 거부됐을 때 오너 로컬에서 재생할 사운드 키(Sound 시트 key). 슬롯 공용. 비우면 무음 — Docs/tech/skill-reject-feedback.md")]
    [SerializeField] private string cooldownRejectSoundKey = "";

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
    // 쿨 장부의 시간축. 피어마다 자기 시계 — 서버↔오너는 남은 초만 주고받는다(할 일 91 · PLAN-91 §3.3).
    private PlayerCooldownClock cooldownClock;
    private PlayerSkillSlotBindingModel slotBindings;
    private byte locallyAppliedOverrideMask;
    private PlayerSkillBase activeSkill;
    private float activeEndFallbackTime;
    // [서버] 이벤트 방식 스킬 — 안전망을 상태 클립 길이로 맞춘다(ResolveClipFallback). hasClipFallback = 한 번이라도 클립 길이로 정했다.
    private bool activeEndsOnClipEvent;
    private bool hasClipFallback;
    private int activeStartFrame;
    // 상태가 다시 시작됐다고 볼 안전망 연장 폭. 같은 재생 안에서는 매 틱 계산값이 거의 같아 이보다 작다.
    private const float ClipRestartSlack = 0.05f;
    // 클립 길이를 못 구해 타이머 안전망으로 대체한 경고는 스킬당 1회.
    private readonly HashSet<PlayerSkillBase> clipFallbackWarned = new HashSet<PlayerSkillBase>();
    private float nextAimSendTime;
    private bool hasNotifiedRelease;
    private bool isRequestingSkill;
    // 시전 시점에 확정한 홀드 조작 방식 (UserInputConfig). 진행 중 옵션이 바뀌어도 이번 시전은 흔들리지 않는다.
    private bool activeHoldUsesToggle;
    // 토글 종료 입력 수용 여부. 시전한 그 입력이 떨어지기 전까지는 false다.
    private bool isToggleEndArmed;
    // [오너] ReportsHeldInput 스킬에 마지막으로 보낸 누름 상태. 시전마다 첫 틱에 한 번은 보낸다(hasReportedHeld).
    private bool hasReportedHeld;
    private bool lastReportedHeld;

    public PlayerSkillBase ActiveSkill => activeSkill;
    public bool IsSkillActive => activeSkill != null;
    // 전 피어 — 실행 중(관찰자는 PlaySkillClientRpc~EndSkillClientRpc) 스킬이 공격속도를 타면 Animator.speed = 배율(할 일 91 T5).
    public bool ScalesAnimatorWithAttackSpeed => activeSkill != null && activeSkill.ScalesWithAttackSpeed;
    /// <summary>[오너] 스킬 실행 중이거나 시전 요청 응답을 기다리는 중 — 이때 못 쓴 입력은 선입력 버퍼에 남긴다(할 일 44).</summary>
    public bool IsBusy => IsSkillActive || isRequestingSkill;
    public event Action<PlayerSkillSlot> SlotBindingChanged;

    /// <summary>
    /// [오너 로컬] 스킬을 쿨타임 중에 입력해 사용이 거부됐다. 인자 = 거부된 슬롯. 입력 1회당 1회(선입력 버퍼 재시도 없음).
    /// 서버·다른 피어에서는 나지 않는다 — 효과음·UI 같은 로컬 피드백용. 문서: Docs/tech/skill-reject-feedback.md
    /// </summary>
    public event Action<PlayerSkillSlot> CooldownRejected;

    /// <summary>
    /// [서버] 스킬 시전을 승인해 시작했다(Q·E·R·우클릭 공통, 쿨타임 시작 지점). 인자 = 시전자, 슬롯.
    /// 수동 커밋 스킬(거너 Q 등 <see cref="PlayerSkillData.CommitCooldownManually"/>)도 쿨이 아니라 이 승인 시점에 난다.
    /// 오프라인(비네트워크)에서는 권한을 가진 로컬에서 난다. 튜토리얼 판정 훅.
    /// </summary>
    public static event Action<Player, PlayerSkillSlot> ServerSkillUsed;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents() => ServerSkillUsed = null;
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
    // 쿨 장부 전용 시각. 시계가 없으면(Awake 전) 실시간으로 대신한다.
    private float CooldownNow => cooldownClock != null ? cooldownClock.SkillNow : Time.time;

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
        cooldownClock = PlayerCooldownClock.For(this);
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
            if (skill != null && skill != activeSkill && cooldownLedger.IsReady(i, CooldownNow))
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
        return cooldownLedger != null && cooldownLedger.IsReady(skillIndex, CooldownNow);
    }

    public float GetCooldownRemaining(PlayerSkillSlot slot)
    {
        return GetCooldownRemaining(GetSkill(slot));
    }

    public float GetCooldownRemaining(PlayerSkillBase skill)
    {
        int skillIndex = GetSkillIndex(skill);
        return cooldownLedger != null ? cooldownLedger.GetRemaining(skillIndex, CooldownNow) : 0f;
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

    /// <summary>
    /// [오너] 이 슬롯이 쿨타임 때문에만 못 쓰는가 — 실행·요청 중(바쁨)이면 false.
    /// FSM 이 <see cref="TryUse(PlayerSkillSlot)"/> 전에 묻고, 참이면 시전 요청을 보내지 않고 입력을 버린다(할 일 44).
    /// 판정은 이 피어의 쿨타임 장부(원격 오너 = 서버가 미러한 HUD 장부)로 한다 — 조준 진입 게이트와 같은 조기 차단이다.
    /// </summary>
    public bool IsBlockedByCooldown(PlayerSkillSlot slot)
    {
        if (IsBusy)
            return false;

        PlayerSkillBase skill = GetSkill(slot);
        return skill != null && skill.Data != null && GetSkillIndex(skill) != InvalidSkillIndex &&
            !IsCooldownReady(skill);
    }

    /// <summary>[오너] 쿨타임 거부 피드백을 낸다 — <see cref="CooldownRejected"/> + 사운드 키. 입력 1회당 한 번만 부를 것.</summary>
    public void NotifyCooldownRejected(PlayerSkillSlot slot)
    {
        if (IsNetworkActive && !IsOwner)
            return;

        Edit.Log($"[Skill] {slot} 쿨타임 거부 — {GetCooldownRemaining(slot):F1}s 남음(입력 버림)", this);
        CooldownRejected?.Invoke(slot);

        if (!string.IsNullOrEmpty(cooldownRejectSoundKey) && AudioManager.Instance != null)
            AudioManager.Instance.Play(cooldownRejectSoundKey, transform.position);
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
    // 참이면 조준 취소에 쓰이므로 선입력 버퍼에서도 지운다 — 취소 뒤 같은 스킬 재시전으로 이어지지 않게(할 일 44).
    public bool WasSkillRePressed(PlayerSkillSlot slot)
    {
        if (inputReader == null || !inputReader.GetSkillPressed(slot))
            return false;

        inputReader.ConsumeBufferedSkillInput(slot);
        return true;
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
        Unit sentTarget = null; // 서버가 받는 대상 — 스폰 안 된 대상은 null 로 간다
        if (target != null && target.NetworkObject != null && target.NetworkObject.IsSpawned)
        {
            targetRef = target.NetworkObject;
            sentTarget = target;
        }

        // 원격 오너만 — 호스트는 같은 프레임에 서버 승인이 바로 돈다(왕복 없음).
        if (!IsServer && !PassesOwnerPrecheck(slot, skillIndex, direction, sentTarget, aimPoint, hasAimPoint))
            return false;

        isRequestingSkill = true;
        // 누른 순간의 클라 추정 서버 시각 — 간파 지연 측정 전용(판정에 안 쓴다, 다른 슬롯은 서버가 무시).
        RequestUseSkillRpc(slot, skillIndex, direction, targetRef, aimPoint, hasAimPoint, player.CreateStatReport(),
            NetworkManager.ServerTime.Time);
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
            !cooldownLedger.IsReady(skillIndex, CooldownNow))
        {
            return;
        }

        cooldownLedger.Start(skillIndex, CooldownNow, skill.Data.CooldownTime);
        Edit.Log($"[Skill] {skill.Slot} 쿨타임 시작 {skill.Data.CooldownTime}s", this);

        MirrorCooldownToOwner(skillIndex);
    }

    /// <summary>[서버] 등록된 특정 스킬의 남은 쿨타임을 감소시킨다. 결과는 0초 아래로 내려가지 않는다.</summary>
    public void ReduceCooldownServer(PlayerSkillBase skill, float seconds)
    {
        int skillIndex = GetSkillIndex(skill);
        if (!HasGameplayAuthority || skillIndex == InvalidSkillIndex || seconds <= 0f)
            return;

        float now = CooldownNow;
        float before = cooldownLedger.GetRemaining(skillIndex, now);
        cooldownLedger.Reduce(skillIndex, now, seconds);
        if (cooldownLedger.GetRemaining(skillIndex, now) >= before)
            return;

        MirrorCooldownToOwner(skillIndex);
    }

    /// <summary>[서버] 슬롯에 지금 바인딩된 스킬(대체 슬롯 포함)의 남은 쿨타임을 감소시킨다.</summary>
    public void ReduceCooldownServer(PlayerSkillSlot slot, float seconds) =>
        ReduceCooldownServer(GetSkill(slot), seconds);

    /// <summary>[서버] 등록된 특정 스킬의 쿨타임을 즉시 끝낸다(남은 0초). 오너 HUD 장부에도 미러한다.</summary>
    public void ResetCooldownServer(PlayerSkillBase skill)
    {
        int skillIndex = GetSkillIndex(skill);
        if (!HasGameplayAuthority || skillIndex == InvalidSkillIndex ||
            cooldownLedger.IsReady(skillIndex, CooldownNow))
        {
            return;
        }

        cooldownLedger.SetRemaining(skillIndex, CooldownNow, 0f);
        MirrorCooldownToOwner(skillIndex);
    }

    /// <summary>[서버] 슬롯에 지금 바인딩된 스킬(대체 슬롯 포함)의 쿨타임을 즉시 끝낸다.</summary>
    public void ResetCooldownServer(PlayerSkillSlot slot) => ResetCooldownServer(GetSkill(slot));

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

    // 애니메이션 이벤트 (릴레이 경유). 각 피어는 자기 애니메이터의 이벤트를 받는다 —
    // 먼저 이 피어 로컬(오너 이동·연출)로 넘기고, 판정 경로(OnAnimationEvent)는 서버만 처리한다.
    public void HandleAnimationEvent(SkillAnimationEventType eventType)
    {
        activeSkill?.OnLocalAnimationEvent(eventType);

        if (IsNetworkActive && !IsServer)
            return;

        activeSkill?.OnAnimationEvent(eventType);
    }

    // ── 서버 처리 ──

    private bool StartSkillServer(
        PlayerSkillSlot slot, int expectedSkillIndex, Vector3 direction, Unit target, Vector3 aimPoint, bool hasAimPoint)
    {
        ReprojectFixedDistanceAim(slot, hasAimPoint, ref direction, ref aimPoint);

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
        // 타이머 방식 안전망. 이벤트 방식은 상태에 들어간 뒤 클립 길이로 바꾼다(ResolveClipFallback) — 그 전까지 이 값이 임시 그물이다.
        // 공격속도를 타는 스킬은 이 그물도 배율로 나눈다 — 감속만 늘리고 가속은 줄이지 않는다(할 일 91 T5, SafetyNetDuration).
        // 클립 길이 그물은 Animator.speed 를 이미 포함한다(Probe).
        float maxActive = skill.ScalesSafetyNetWithAttackSpeed
            ? PlayerAttackSpeedRules.SafetyNetDuration(skill.Data.MaxActiveDuration, player.AttackSpeedRate)
            : skill.Data.MaxActiveDuration;
        maxActive += Mathf.Max(0f, skill.SafetyNetExtraSeconds);
        activeEndFallbackTime = Time.time + PlayerSkillSafetyNet.TimerSeconds(maxActive, endFallbackPadding);
        activeEndsOnClipEvent = skill.EndsOnClipEvent;
        hasClipFallback = false;
        activeStartFrame = Time.frameCount;

        // 쿨타임은 승인 즉시 시작, 환불 없음 — 단 수동 커밋 스킬은 발동 시점에 스스로 시작한다(CommitCooldownServer)
        if (!skill.Data.CommitCooldownManually)
            cooldownLedger.Start(expectedSkillIndex, CooldownNow, skill.Data.CooldownTime);

        // 상태이상 modifier가 반영된 최종 공격력으로 스냅샷 (그릴 합의: SO 계수 × 최종 스탯)
        // 툴팁 계산식도 같은 계수·고정값을 쓰므로 판정 공식을 바꿀 때 SkillTooltipDamage 계산도 같이 바꿀 것.
        int damageSnapshot = player.ResolveDamage(skill.Data.AttackDamageMultiplier, skill.Data.FlatDamageBonus);
        skill.SetDamageSnapshot(damageSnapshot);
        // 지점(AimPoint)은 CanApproveSkill 이 CanUse 직전에 이미 넣었다.

        Edit.Log($"[Skill] {slot} 시작 — 피해 스냅샷 {damageSnapshot}, 쿨타임 {skill.Data.CooldownTime}s", this);

        skill.OnServerStart(direction, target);
        PlaySkillPresentation(skill, direction);

        if (IsNetworkActive)
            PlaySkillClientRpc(expectedSkillIndex, direction, aimPoint, hasAimPoint);

        ServerSkillUsed?.Invoke(player, slot);
        return true;
    }

    // 고정 거리 지점 스킬은 서버가 자기 위치·전방 기준으로 지점을 다시 투영한 뒤 승인한다. 원격 오너의 1차 검증도 같은 투영을 쓴다.
    private void ReprojectFixedDistanceAim(PlayerSkillSlot slot, bool hasAimPoint, ref Vector3 direction, ref Vector3 aimPoint)
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
    }

    /// <summary>
    /// [원격 오너] 시전 요청 RPC 를 보내기 전 1차 검증(할 일 75) — 서버 승인과 같은 <see cref="CanApproveSkill"/> 를 이 피어의 상태로 돌린다.
    /// 쿨타임 = 서버가 미러한 HUD 장부, 상태·차단 효과 = 자기 FSM·복제된 상태이상, CanUse = 복제된 상태. 걸리면 요청을 보내지 않는다.
    /// 최종 판정은 그대로 서버다 — 통과해도 서버가 거부할 수 있다(그때는 기존처럼 RejectSkillClientRpc).
    /// </summary>
    private bool PassesOwnerPrecheck(
        PlayerSkillSlot slot, int skillIndex, Vector3 direction, Unit target, Vector3 aimPoint, bool hasAimPoint)
    {
        ReprojectFixedDistanceAim(slot, hasAimPoint, ref direction, ref aimPoint);
        return CanApproveSkill(
            slot, skillIndex, direction, target, aimPoint, hasAimPoint, out _, out _);
    }

    private bool CanApproveSkill(
        PlayerSkillSlot slot, int expectedSkillIndex, Vector3 direction, Unit target,
        Vector3 aimPoint, bool hasAimPoint,
        out PlayerSkillBase skill, out bool isDead)
    {
        skill = GetSkill(slot);
        isDead = stateController != null && stateController.CurrentState == PlayerActionState.Dead;
        bool assigned = skill != null && skill.Data != null && stateController != null;
        int skillIndex = assigned ? GetSkillIndex(skill) : InvalidSkillIndex;

        PlayerSkillApproval approval = PlayerSkillApprovalRules.EvaluatePreconditions(
            assigned,
            skillIndex == expectedSkillIndex,
            IsSkillActive,
            assigned && IsCooldownReady(skill),
            isDead,
            assigned && skill.Data.UsableWhileDead,
            stateController != null && stateController.CanUseSkill);

        if (approval == PlayerSkillApproval.Approved)
        {
            // 지점 기반 CanUse(거너 R 지점 지정 등)가 이번 요청의 지점을 보도록 CanUse 직전에 넣는다.
            // 실행 중 스킬은 위에서 이미 거부됐으므로 진행 중인 스킬의 지점을 덮어쓰지 않는다.
            // SingleTarget/None 은 hasAimPoint=false 라 HasAimPoint 도 false — 기존 판정 그대로.
            skill.SetAimPoint(aimPoint, hasAimPoint);

            if (!skill.CanUse(direction, target))
                approval = PlayerSkillApproval.CanUseFailed;
        }

        switch (approval)
        {
            case PlayerSkillApproval.Approved:
                return true;
            case PlayerSkillApproval.Unassigned:
                Edit.Log($"[Skill] {slot} 거부 — 스킬/데이터 미배정", this);
                break;
            case PlayerSkillApproval.SlotRace:
                Edit.Log($"[Skill] {slot} 거부 — 슬롯 교체 경합(요청 {expectedSkillIndex}, 현재 {skillIndex})", this);
                break;
            case PlayerSkillApproval.SkillActive:
                Edit.Log($"[Skill] {slot} 거부 — {activeSkill.Slot} 실행 중", this);
                break;
            case PlayerSkillApproval.Cooldown:
                Edit.Log($"[Skill] {slot} 거부 — 쿨타임 {GetCooldownRemaining(skill):F1}s 남음", this);
                break;
            case PlayerSkillApproval.Dead:
                Edit.Log($"[Skill] {slot} 거부 — 사망 상태", this);
                break;
            case PlayerSkillApproval.StateOrEffect:
                Edit.Log($"[Skill] {slot} 거부 — 상태 {stateController.CurrentState} 또는 차단 효과", this);
                break;
            case PlayerSkillApproval.CanUseFailed:
                Edit.Log($"[Skill] {slot} 거부 — 스킬 자체 조건(CanUse) 불충족", this);
                break;
        }

        return false;
    }

    private void TickServer()
    {
        if (activeSkill == null)
            return;

        activeSkill.OnTick();

        // OnTick 안에서 스킬이 스스로 종료했을 수 있다
        if (activeSkill != null && activeEndsOnClipEvent)
            ResolveClipFallback();

        if (activeSkill != null && activeEndFallbackTime > 0f && Time.time >= activeEndFallbackTime)
        {
            if (activeEndsOnClipEvent && !hasClipFallback)
                WarnClipFallback(activeSkill, "상태에 들어가지 않았다");
            EndActiveSkillServer(SkillEndReason.MaxDurationReached);
        }
    }

    // [서버] 이벤트 방식 안전망 = 지금부터 상태 클립 끝까지의 실제 시간 + padding.
    // 시작 프레임은 건너뛴다 — CrossFade 는 그 프레임 애니메이터 갱신 때 반영되므로 직전 상태(같은 상태 재시전 포함)를 읽을 수 있다.
    // 클립 재생 동안은 계속 본다 — 원격 오너면 NetworkAnimator 가 오너의 CrossFade 를 늦게 받아 서버 상태를 다시 시작시키고
    // (End 도 그만큼 늦게 온다), 그때 안전망을 늦춘다. 클립 끝(normalizedTime ≥ 1)을 넘기면 더 늘리지 않는다 —
    // 한 번 재생 클립은 끝 프레임에 머문 채 normalizedTime 이 계속 커져 남은 시간이 0 이 되므로, 막지 않으면 매 틱 '지금 + padding' 으로 밀려 안 끊긴다.
    private void ResolveClipFallback()
    {
        if (Time.frameCount <= activeStartFrame)
            return;

        int stateHash = Animator.StringToHash(activeSkill.Data.AnimatorStateName ?? string.Empty);
        switch (PlayerSkillSafetyNet.Probe(animator, stateHash, out float playbackSeconds, out float normalizedTime))
        {
            case PlayerSkillSafetyNet.ProbeResult.NotInState:
                return; // 다음 틱에 다시 본다. 끝내 못 찾으면 임시(타이머) 안전망이 끊는다

            case PlayerSkillSafetyNet.ProbeResult.Invalid:
                if (!hasClipFallback)
                {
                    activeEndsOnClipEvent = false; // 이번 시전은 타이머 안전망 그대로
                    WarnClipFallback(activeSkill, "클립이 없거나 재생 속도가 0");
                }
                return;
        }

        if (hasClipFallback && normalizedTime >= 1f)
            return;

        float remaining = PlayerSkillSafetyNet.RemainingSeconds(playbackSeconds, normalizedTime);
        float fallbackTime = Time.time + remaining + Mathf.Max(0f, endFallbackPadding);
        if (hasClipFallback && fallbackTime <= activeEndFallbackTime + ClipRestartSlack)
            return;

        hasClipFallback = true;
        activeEndFallbackTime = fallbackTime;
        activeSkill.OnClipPlaybackResolved(remaining);
    }

    private void WarnClipFallback(PlayerSkillBase skill, string why)
    {
        if (!clipFallbackWarned.Add(skill))
            return;

        Edit.LogWarning(
            $"[Skill] {skill.GetType().Name} — 상태 '{skill.Data.AnimatorStateName}' 클립 길이를 못 구했다({why}). " +
            $"안전망을 maxActiveDuration({skill.Data.MaxActiveDuration:0.##}s) + padding 으로 대체한다.", this);
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
            // 스킬이 받은 좌클릭이 스킬이 끝난 뒤 평타로 한 번 더 나가지 않게 한다(할 일 44).
            inputReader.ConsumeBufferedInput(PlayerBufferedInput.Attack);
            if (!IsNetworkActive)
                activeSkill.OnPrimaryPressed(aim);
            else
                NotifySkillPrimaryRpc(aim);
        }

        // 같은 슬롯 재입력(팔라딘 E 방어중 해제, 할 일 94). 받을지(방어중인가)는 서버가 정한다 — 오너는 상태를 먼저 바꾸지 않는다.
        // 단계와 무관하게 버퍼에서 지운다 — 준비·해제·반격 중 누른 E 가 E 가 끝난 뒤 새 시전(또는 쿨타임 거부)으로 이어지지 않게.
        // 쿨타임 거부(UseSkillSlot)는 Idle/Move 후보 경로에서만 나므로, 스킬 상태에서 도는 이 경로가 E 실행 중 입력을 먼저 가져간다.
        if (activeSkill.ConsumesSkillRePress && inputReader != null && inputReader.GetSkillPressed(activeSkill.Slot))
        {
            inputReader.ConsumeBufferedSkillInput(activeSkill.Slot);
            if (!IsNetworkActive)
                activeSkill.OnSkillRePressed();
            else
                NotifySkillRePressedRpc(GetSkillIndex(activeSkill));
        }

        // 같은 슬롯 키 누름/뗌 보고(팔라딘 E 홀드 방식, 할 일 95). 첫 틱에 지금 상태를 한 번 보내고(준비 중에 이미 뗐으면 false),
        // 그 뒤 바뀔 때만 보낸다. 해제·최대 시간 판정과 전이는 서버가 한다 — 오너는 상태를 먼저 바꾸지 않는다.
        if (activeSkill != null && activeSkill.ReportsHeldInput && inputReader != null)
        {
            bool held = inputReader.GetSkillHeld(activeSkill.Slot);
            if (!hasReportedHeld || held != lastReportedHeld)
            {
                hasReportedHeld = true;
                lastReportedHeld = held;
                if (!IsNetworkActive)
                    activeSkill.OnSkillHeldChanged(held);
                else
                    NotifySkillHeldRpc(GetSkillIndex(activeSkill), held);
            }
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

        // 종료에 쓴 재입력이 스킬이 끝난 뒤 같은 스킬 재시전으로 이어지지 않게 선입력 버퍼에서 지운다(할 일 44).
        if (!inputReader.GetSkillPressed(activeSkill.Slot))
            return false;

        inputReader.ConsumeBufferedSkillInput(activeSkill.Slot);
        return true;
    }

    // ── RPC (오너 → 서버) ──

    [Rpc(SendTo.Server)]
    private void RequestUseSkillRpc(
        PlayerSkillSlot slot, int expectedSkillIndex, Vector3 direction, NetworkObjectReference targetRef,
        Vector3 aimPoint, bool hasAimPoint, PlayerStatReport statReport, double pressedServerTime,
        RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, $"스킬 {slot}");

        // 간파 지연 측정(측정만) — 승인되면 OnServerStart 가 가져가고, 거부되면 아래에서 버린다.
        PlayerInterruptSkillBase interrupt = GetSkillByIndex(expectedSkillIndex) as PlayerInterruptSkillBase;
        if (interrupt != null)
            interrupt.ServerSetLagPress(rpcParams.Receive.SenderClientId, pressedServerTime);

        Unit target = ResolveTarget(targetRef);
        bool started = StartSkillServer(slot, expectedSkillIndex, direction, target, aimPoint, hasAimPoint);
        if (interrupt != null)
            interrupt.ServerClearLagPress();

        if (!started)
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

    // 인덱스를 같이 보낸다 — 늦게 도착한 재입력이 그사이 바뀐 다른 스킬에 닿지 않게 한다.
    [Rpc(SendTo.Server)]
    private void NotifySkillRePressedRpc(int skillIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        PlayerSkillBase skill = GetSkillByIndex(skillIndex);
        if (skill != null && skill == activeSkill && skill.ConsumesSkillRePress)
            skill.OnSkillRePressed();
    }

    // 인덱스를 같이 보낸다 — 늦게 도착한 뗌이 그사이 바뀐 다른 스킬에 닿지 않게 한다. ReportsHeldInput 은 오너 설정이라 서버에서 다시 보지 않는다.
    [Rpc(SendTo.Server)]
    private void NotifySkillHeldRpc(int skillIndex, bool held, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        PlayerSkillBase skill = GetSkillByIndex(skillIndex);
        if (skill != null && skill == activeSkill)
            skill.OnSkillHeldChanged(held);
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
            cooldownLedger.SetRemaining(skillIndex, CooldownNow, remaining);
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
            cooldownLedger.Start(skillIndex, CooldownNow, skill.Data.CooldownTime);

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

        float remaining = cooldownLedger.GetRemaining(skillIndex, CooldownNow);
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
        hasReportedHeld = false;

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

/// <summary>스킬 시전 승인 판정 결과. 위에서 아래 순서로 먼저 걸린 사유 하나.</summary>
public enum PlayerSkillApproval
{
    Approved,
    Unassigned,     // 스킬·데이터 미배정
    SlotRace,       // 요청한 스킬 인덱스와 지금 슬롯 바인딩이 다르다
    SkillActive,    // 다른 스킬 실행 중
    Cooldown,
    Dead,           // 사망 중 + 사망 중 허용 스킬 아님
    StateOrEffect,  // Idle/Move 아님 또는 스킬 차단 효과
    CanUseFailed    // 스킬 자체 조건(CanUse)
}

/// <summary>
/// 스킬 시전 승인의 사전 조건 순서 — 서버 최종 승인과 원격 오너 1차 검증(할 일 75)이 같은 이 판정을 쓴다.
/// 스킬 자체 조건(CanUse)은 사전 조건이 모두 통과했을 때만 호출자가 본다.
/// </summary>
public static class PlayerSkillApprovalRules
{
    public static PlayerSkillApproval EvaluatePreconditions(
        bool assigned, bool skillIndexMatches, bool skillActive, bool cooldownReady,
        bool isDead, bool usableWhileDead, bool canUseSkillState)
    {
        if (!assigned)
            return PlayerSkillApproval.Unassigned;
        if (!skillIndexMatches)
            return PlayerSkillApproval.SlotRace;
        if (skillActive)
            return PlayerSkillApproval.SkillActive;
        if (!cooldownReady)
            return PlayerSkillApproval.Cooldown;
        if (isDead)
            return usableWhileDead ? PlayerSkillApproval.Approved : PlayerSkillApproval.Dead;
        return canUseSkillState ? PlayerSkillApproval.Approved : PlayerSkillApproval.StateOrEffect;
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
