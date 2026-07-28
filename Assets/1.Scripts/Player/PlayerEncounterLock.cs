using System;
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
    [SerializeField] private DefaultAttackController defaultAttack;
    [SerializeField] private PlayerSkillController skillController;
    [SerializeField] private Rigidbody body;

    /// <summary>연출 잠금 여부. 서버가 확정하고 전 피어가 읽는다.</summary>
    public bool IsCinematicLocked => IsSpawned ? cinematicLocked.Value : offlineLocked;

    /// <summary>잠금 변화 알림(전 피어). 입력 정책 등 로컬 소비자가 구독한다.</summary>
    public event Action<bool> CinematicLockChanged;

    // 오프라인(비네트워크) 실행에서는 NetworkVariable을 쓸 수 없어 로컬 플래그로 동작한다.
    private bool offlineLocked;
    private bool localLockApplied;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveReferences();
        cinematicLocked.OnValueChanged += HandleLockChanged;

        // 늦게 합류한 피어도 진행 중인 연출 잠금을 그대로 적용한다.
        if (cinematicLocked.Value)
            ApplyLocalLock(true);
    }

    public override void OnNetworkDespawn()
    {
        cinematicLocked.OnValueChanged -= HandleLockChanged;

        // 씬 전환·디스폰으로 사라지는 경우 남은 로컬 잠금을 풀어 둔다(재사용되는 인스턴스 대비).
        // 이때 이벤트는 쏘지 않는다 — 디스폰 중 구독자가 복제 상태를 쓰려 하면 NGO가 거부한다.
        if (localLockApplied)
            ApplyLocalLock(false, notify: false);

        base.OnNetworkDespawn();
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
        statusEffects?.ClearAllServer();

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

        // 이동 권한 피어만 물리 속도를 만든다 — 비권한 피어는 kinematic이라 건드릴 필요가 없다.
        if (body != null && player != null && player.IsMovementAuthority)
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
            defaultAttack = GetComponent<DefaultAttackController>();

        if (skillController == null)
            skillController = GetComponent<PlayerSkillController>();

        if (body == null)
            body = GetComponent<Rigidbody>();
    }
}
