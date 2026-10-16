using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 등장 연출 동안 이 Player의 gameplay를 서버 권한으로 잠근다.
/// (승인 계획 <c>Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md</c> Task 2)
///
/// 잠금은 새 차단 계통을 만들지 않고 이미 있는 게이트 위에 올린다:
/// <list type="bullet">
/// <item>행동 차단 = <see cref="PlayerActionState.Cinematic"/> FSM 상태.
///   <see cref="PlayerStateController"/>의 CanMove/CanAttack/CanUseSkill이 Idle·Move만 허용하므로
///   이동·기본공격·스킬의 <b>서버 승인 경로가 함께 닫힌다</b>.</item>
/// <item>피해 무시 = <see cref="PlayerInvulnerability"/>의 <see cref="InvulnerabilityCause.Cinematic"/> 토큰.
///   실제 차단은 <c>Player.CanApplyHealthDamage</c> 게이트가 수행한다.</item>
/// <item>오너 입력 = <see cref="PlayerLifeInputPolicy"/>가 생명주기 GameplayAccess와 이 잠금을
///   <b>한 곳에서</b> 합쳐 적용한다(두 계통이 서로의 입력 설정을 덮어쓰지 않게).</item>
/// </list>
///
/// FSM은 복제되지 않으므로 잠금 여부만 NetworkVariable로 복제하고, 각 피어가
/// <see cref="ApplyLocalLock"/>에서 자기 FSM·애니메이터·입력에 같은 결과를 적용한다.
/// 연출 중 추락 판정과 생명주기 전이 차단은 각 소유 컴포넌트가 이 값을 읽어 처리한다
/// (<see cref="PlayerFallController"/>, <see cref="PlayerLifeCycleController"/>).
///
/// 보스 엘리베이터 연출의 <b>입력 잠금</b>(<see cref="SetCinematicInputLockServer"/> — 카메라 포커스부터, 보이는 채로 걸어 들어간다)과
/// <b>숨김</b>(<see cref="SetCinematicHiddenServer"/> — 문이 닫힌 뒤 상승 동안)도 여기 한 곳이다. 둘 다 보스 등장 잠금과 독립된 복제 값이다.
/// 숨김은 모델·실루엣(렌더러 forceRenderingOff)·머리 위 바를 감춘다. 입력은 둘 다 <see cref="PlayerLifeInputPolicy"/> 가 막고,
/// 무적은 둘 중 하나라도 켜져 있는 동안 <see cref="InvulnerabilityCause.CinematicHidden"/> 토큰 하나가 맡는다.
/// FSM 은 Cinematic 으로 바꾸지 않는다 — 걷는 동안 오너가 Motor 포즈·IsMoving 애니를 직접 쓰고(<see cref="BossTeleportManager"/>),
/// 보스 등장 잠금(<see cref="BeginCinematicServer"/>)이 도착 뒤 겹쳐 걸려도 서로 풀지 않게.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerStateController))]
public sealed class PlayerEncounterLock : NetworkBehaviour
{
    private readonly NetworkVariable<bool> cinematicLocked =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    [SerializeField] private Player player;
    [SerializeField] private PlayerStateController stateController;
    [SerializeField] private PlayerInvulnerability invulnerability;
    [SerializeField] private StatusEffectController statusEffects;
    private IPlayerBasicAttack defaultAttack; // 인터페이스라 직렬화 불가 — ResolveReferences 에서 찾는다
    [SerializeField] private PlayerSkillController skillController;
    [SerializeField] private Rigidbody body;

    // 연출 숨김(보스 엘리베이터 '뿅'). 잠금과 별개 — 숨김이 끝나기 전에 보스 등장 잠금이 겹쳐 걸린다.
    private readonly NetworkVariable<bool> cinematicHidden =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // 보스 엘리베이터 입력 잠금(보이는 채로). 숨김·보스 등장 잠금과 별개.
    private readonly NetworkVariable<bool> cinematicInputLocked =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// <summary>연출 잠금 여부. 서버가 확정하고 전 피어가 읽는다.</summary>
    public bool IsCinematicLocked => IsSpawned ? cinematicLocked.Value : offlineLocked;

    /// <summary>
    /// 연출 숨김 여부(전 피어). 숨김 = 모델·머리 위 UI·실루엣 안 보임 + 오너 입력 잠금(<see cref="PlayerLifeInputPolicy"/>) +
    /// 서버 무적(<see cref="InvulnerabilityCause.CinematicHidden"/>).
    /// </summary>
    public bool IsCinematicHidden => IsSpawned ? cinematicHidden.Value : offlineHidden;

    /// <summary>
    /// 보스 엘리베이터 입력 잠금 여부(전 피어). 보이는 채로 오너 입력 잠금(<see cref="PlayerLifeInputPolicy"/>) +
    /// 서버 무적(<see cref="InvulnerabilityCause.CinematicHidden"/>). 이동은 연출이 오너에게 시킨다.
    /// </summary>
    public bool IsCinematicInputLocked => IsSpawned ? cinematicInputLocked.Value : offlineInputLocked;

    /// <summary>잠금 변화 알림(전 피어). 입력 정책 등 로컬 소비자가 구독한다.</summary>
    public event Action<bool> CinematicLockChanged;

    /// <summary>숨김 변화 알림(전 피어) — 입력 정책과 사라짐/나타남 VFX 의 훅.</summary>
    public event Action<bool> CinematicHiddenChanged;

    /// <summary>엘리베이터 입력 잠금 변화 알림(전 피어) — 입력 정책이 구독한다.</summary>
    public event Action<bool> CinematicInputLockChanged;

    // 오프라인(비네트워크) 실행에서는 NetworkVariable을 쓸 수 없어 로컬 플래그로 동작한다.
    private bool offlineLocked;
    private bool localLockApplied;
    private bool offlineHidden;
    private bool localHiddenApplied;
    private bool offlineInputLocked;
    // 서버: 숨김·입력 잠금이 함께 쓰는 무적 토큰을 들고 있나(둘 중 하나라도 켜져 있으면 든다).
    private bool elevatorInvulnerabilityHeld;
    // 숨길 때 끈 렌더러만 되켠다 — 원래 꺼져 있던 것(forceRenderingOff)은 그대로 둔다.
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveReferences();
        cinematicLocked.OnValueChanged += HandleLockChanged;
        cinematicHidden.OnValueChanged += HandleHiddenChanged;
        cinematicInputLocked.OnValueChanged += HandleInputLockChanged;

        // 늦게 합류한 피어도 진행 중인 연출 잠금·숨김을 그대로 적용한다(입력 잠금은 오너 입력 정책이 값을 직접 읽는다).
        if (cinematicLocked.Value)
            ApplyLocalLock(true);
        if (cinematicHidden.Value)
            ApplyLocalHidden(true);
    }

    public override void OnNetworkDespawn()
    {
        cinematicLocked.OnValueChanged -= HandleLockChanged;
        cinematicHidden.OnValueChanged -= HandleHiddenChanged;
        cinematicInputLocked.OnValueChanged -= HandleInputLockChanged;

        // 씬 전환·디스폰으로 사라지는 경우 남은 로컬 잠금을 풀어 둔다(재사용되는 인스턴스 대비).
        // 이때 이벤트는 쏘지 않는다 — 디스폰 중 구독자가 복제 상태를 쓰려 하면 NGO가 거부한다.
        if (localLockApplied)
            ApplyLocalLock(false, notify: false);
        if (localHiddenApplied)
            ApplyLocalHidden(false, notify: false);

        base.OnNetworkDespawn();
    }

    /// <summary>
    /// 서버 전용·멱등. 연출 숨김을 켜고 끈다(보스 엘리베이터 — <see cref="BossTeleportManager"/>).
    /// 무적 토큰(<see cref="InvulnerabilityCause.CinematicHidden"/>)을 함께 걸고 푼다. 보스 등장 잠금(<see cref="BeginCinematicServer"/>)과는 독립이다.
    /// </summary>
    public bool SetCinematicHiddenServer(bool hidden)
    {
        if (!IsServerAuthority())
            return false;

        if (IsCinematicHidden == hidden)
            return true;

        if (hidden)
        {
            // 잠금과 같은 순서 — 진행 중인 행동을 정상 경로로 끊은 뒤 숨긴다.
            skillController?.EndActiveSkillServer(SkillEndReason.Cancelled);
            defaultAttack?.CancelCurrentAttack();
        }
        SetElevatorInvulnerabilityServer(hidden || IsCinematicInputLocked);

        if (!IsSpawned)
        {
            offlineHidden = hidden;
            ApplyLocalHidden(hidden);
        }
        else
        {
            cinematicHidden.Value = hidden; // 쓰기 피어에서도 OnValueChanged → 핸들러 한 곳에서 적용
        }
        return true;
    }

    private void HandleHiddenChanged(bool previous, bool current)
    {
        ApplyLocalHidden(current);
    }

    /// <summary>
    /// 서버 전용·멱등. 보스 엘리베이터 입력 잠금을 켜고 끈다(<see cref="BossTeleportManager"/> — 카메라 포커스 ~ 연출 끝).
    /// 보이는 채로 오너 입력만 막고 무적을 건다. 켤 때 진행 중인 스킬·공격을 정상 경로로 끊는다. 숨김·보스 등장 잠금과는 독립이다.
    /// </summary>
    public bool SetCinematicInputLockServer(bool locked)
    {
        if (!IsServerAuthority())
            return false;

        if (IsCinematicInputLocked == locked)
            return true;

        if (locked)
        {
            skillController?.EndActiveSkillServer(SkillEndReason.Cancelled);
            defaultAttack?.CancelCurrentAttack();
        }
        SetElevatorInvulnerabilityServer(locked || IsCinematicHidden);

        if (!IsSpawned)
        {
            offlineInputLocked = locked;
            CinematicInputLockChanged?.Invoke(locked);
        }
        else
        {
            cinematicInputLocked.Value = locked; // 쓰기 피어에서도 OnValueChanged → 핸들러 한 곳에서 알린다
        }
        return true;
    }

    private void HandleInputLockChanged(bool previous, bool current)
    {
        CinematicInputLockChanged?.Invoke(current);
    }

    // 숨김·입력 잠금이 같은 토큰 하나를 나눠 쓴다 — 한쪽을 풀어도 다른 쪽이 켜져 있으면 무적이 유지된다.
    private void SetElevatorInvulnerabilityServer(bool held)
    {
        if (elevatorInvulnerabilityHeld == held)
            return;

        elevatorInvulnerabilityHeld = held;
        if (held)
            invulnerability?.AddServerToken(InvulnerabilityCause.CinematicHidden, 0.0);
        else
            invulnerability?.RemoveServerToken(InvulnerabilityCause.CinematicHidden);
    }

    // 렌더러는 끄지(enabled) 않고 forceRenderingOff 로 감춘다 — Soul·장비 전환이 enabled/SetActive 를 따로 쓰므로 서로 덮지 않는다.
    // 실루엣(PlayerSilhouetteFeature)도 컬링 결과로 그리므로 함께 사라진다. 머리 위 바는 PlayerOverheadHealthBarRule 이 이 값을 읽는다.
    private void ApplyLocalHidden(bool hidden, bool notify = true)
    {
        if (localHiddenApplied == hidden)
            return;

        localHiddenApplied = hidden;

        if (hidden)
        {
            hiddenRenderers.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                hiddenRenderers.Add(r);
            }
        }
        else
        {
            foreach (Renderer r in hiddenRenderers)
            {
                if (r != null) r.forceRenderingOff = false;
            }
            hiddenRenderers.Clear();
        }

        if (notify)
            CinematicHiddenChanged?.Invoke(hidden);
    }

    /// <summary>
    /// 서버 전용·멱등. 진행 중인 행동을 정상 종료 경로로 정리한 뒤 잠금을 올린다.
    /// 정리를 먼저 하는 이유: FSM이 Cinematic으로 바뀐 뒤에는 공격·스킬 상태의 Exit 정리가
    /// "외부 요인 강제 이탈" 경로로 빠져 쿨타임·클라 애니 정리가 덜 돌 수 있다.
    /// </summary>
    public bool BeginCinematicServer()
    {
        if (!IsServerAuthority())
            return false;

        if (IsCinematicLocked)
            return true;

        skillController?.EndActiveSkillServer(SkillEndReason.Cancelled);
        defaultAttack?.CancelCurrentAttack();
        // 디버프만 — 버프(플레이어 패시브 등)는 연출을 지나도 남아야 한다(팀장 2026-09-29).
        statusEffects?.ClearDebuffsServer();

        // 무기한 토큰 — EndCinematicServer/AbortEncounter가 반드시 해제해야 한다.
        invulnerability?.AddServerToken(InvulnerabilityCause.Cinematic, 0.0);

        SetLockValue(true);
        return true;
    }

    /// <summary>서버 전용·멱등. 잠금을 풀고 무적 토큰을 회수한다.</summary>
    public bool EndCinematicServer()
    {
        if (!IsServerAuthority())
            return false;

        if (!IsCinematicLocked)
            return true;

        SetLockValue(false);
        invulnerability?.RemoveServerToken(InvulnerabilityCause.Cinematic);
        return true;
    }

    private bool IsServerAuthority()
    {
        // 오프라인 테스트 씬(네트워크 미시작)에서도 연출 검증이 가능하도록 허용한다.
        if (!IsNetworkListening)
            return true;

        return IsSpawned && IsServer;
    }

    private static bool IsNetworkListening =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private void SetLockValue(bool locked)
    {
        if (!IsSpawned)
        {
            offlineLocked = locked;
            ApplyLocalLock(locked);
            return;
        }

        // 쓰기 피어에서도 OnValueChanged가 불리므로 로컬 적용은 핸들러 한 곳에서만 한다.
        cinematicLocked.Value = locked;
    }

    private void HandleLockChanged(bool previous, bool current)
    {
        ApplyLocalLock(current);
    }

    private void ApplyLocalLock(bool locked, bool notify = true)
    {
        if (localLockApplied == locked)
            return;

        localLockApplied = locked;

        if (stateController != null)
        {
            if (locked)
                stateController.BeginCinematic();
            else
                stateController.EndCinematic();
        }

        // 구속에 따른 물리 정지는 권위 결과여야 한다. 입력 오너나 원격 프록시가 따로 적용하지 않는다.
        if (body != null && player != null && player.IsMotionAuthority)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (notify)
            CinematicLockChanged?.Invoke(locked);
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = GetComponent<Player>();

        if (stateController == null)
            stateController = GetComponent<PlayerStateController>();

        if (invulnerability == null)
            invulnerability = GetComponent<PlayerInvulnerability>();

        if (statusEffects == null)
            statusEffects = GetComponent<StatusEffectController>();

        if (defaultAttack == null)
            defaultAttack = GetComponent<IPlayerBasicAttack>();

        if (skillController == null)
            skillController = GetComponent<PlayerSkillController>();

        if (body == null)
            body = GetComponent<Rigidbody>();
    }
}
