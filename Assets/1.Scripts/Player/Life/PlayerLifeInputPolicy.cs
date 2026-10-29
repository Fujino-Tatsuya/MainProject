using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 복제된 생명주기 GameplayAccess를 로컬 오너 입력에 적용한다.
/// 서버 생명주기 상태나 원격 Player 입력 컴포넌트는 변경하지 않는다.
///
/// 연출 잠금(<see cref="PlayerEncounterLock"/>)도 <b>여기서 함께</b> 적용한다.
/// 두 계통이 각자 <see cref="PlayerInputReader.SetInputEnabled"/>를 호출하면 나중에 부른 쪽이
/// 앞선 차단을 지워버리므로, 오너 입력의 적용 지점은 이 컴포넌트 하나로 유지한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerLifeCycleController))]
[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerLifeInputPolicy : MonoBehaviour
{
    [SerializeField] private Player player;
    [SerializeField] private PlayerLifeCycleController lifeCycle;
    [SerializeField] private PlayerInputReader inputReader;
    [SerializeField] private PlayerStateController stateController;
    [SerializeField] private PlayerSkillTargeting skillTargeting;
    [SerializeField] private PlayerEncounterLock encounterLock;

    // [ElevatorFlow] 진단 — 엘리베이터 입력 잠금이 바뀐 순간만 한 줄 남긴다.
    private bool lastLoggedElevatorLock;

    private static double ServerTimeNow() =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (lifeCycle != null)
            lifeCycle.GameplayAccessChanged += HandleGameplayAccessChanged;

        if (encounterLock != null)
        {
            encounterLock.CinematicLockChanged += HandleCinematicLockChanged;
            encounterLock.CinematicHiddenChanged += HandleCinematicLockChanged;
            encounterLock.CinematicInputLockChanged += HandleCinematicLockChanged;
        }
        VictoryInputHeldChanged += ApplyCurrentAccess;
    }

    private void Start()
    {
        ApplyCurrentAccess();
    }

    private void OnDisable()
    {
        if (lifeCycle != null)
            lifeCycle.GameplayAccessChanged -= HandleGameplayAccessChanged;

        if (encounterLock != null)
        {
            encounterLock.CinematicLockChanged -= HandleCinematicLockChanged;
            encounterLock.CinematicHiddenChanged -= HandleCinematicLockChanged;
            encounterLock.CinematicInputLockChanged -= HandleCinematicLockChanged;
        }
        VictoryInputHeldChanged -= ApplyCurrentAccess;
    }

    private void HandleGameplayAccessChanged(PlayerLifeGameplayAccess access)
    {
        ApplyAccess(access);
    }

    private void HandleCinematicLockChanged(bool isLocked)
    {
        ApplyCurrentAccess();
    }

    /// <summary>
    /// 지금 생명주기 허용값과 연출 잠금을 합쳐 다시 적용한다. 다른 계통이 자기 임시 입력 잠금을 풀 때
    /// <see cref="PlayerInputReader.SetInputEnabled"/>(true) 대신 이걸 부른다 — 그래야 그 사이 걸린 연출 잠금을 지우지 않는다.
    /// </summary>
    public void ApplyCurrentAccess()
    {
        if (lifeCycle == null)
            ResolveReferences();

        if (lifeCycle != null)
            ApplyAccess(lifeCycle.GameplayAccess);
    }

    private void ApplyAccess(PlayerLifeGameplayAccess access)
    {
        if (player == null || !player.IsInputSource || inputReader == null)
            return;

        bool cinematicLocked = encounterLock != null && BlocksForCinematic(
            encounterLock.IsCinematicLocked, encounterLock.IsCinematicInputLocked, encounterLock.IsCinematicHidden);
        bool allowsMovement = access.AllowsMovement && !cinematicLocked;
        bool allowsCombatInput = access.AllowsCombatInput && !cinematicLocked;

        if (encounterLock != null && encounterLock.IsCinematicInputLocked != lastLoggedElevatorLock)
        {
            lastLoggedElevatorLock = encounterLock.IsCinematicInputLocked;
            Edit.Log($"[ElevatorFlow] 오너 입력 적용 — t={ServerTimeNow():0.###} clientId={player.OwnerClientId} " +
                     $"elevatorLock={lastLoggedElevatorLock} allowsMovement={allowsMovement}", this);
        }

        // 🔴 보스 승리 연출 홀드는 취소 판정에 넣지 않는다 — 막타 공격 동작을 살린 채 0.12초에 얼려야 한다(PLAN-boss-victory-cinematic Q1).
        //    스킬 취소는 2.00초 정식 잠금(SetCinematicInputLockServer)이 한다.
        CancelCombatActions(allowsCombatInput);

        // DeadPresentation/PermanentDead는 PlayerInput 자체를 끄고 Direction을 즉시 0으로 만든다.
        // Soul은 PlayerInput을 유지하되 아래 전투 게이트만 닫아 이동 입력을 보존한다.
        inputReader.SetInputEnabled(allowsMovement && !VictoryInputHeld);
        inputReader.SetCombatInputEnabled(allowsCombatInput && !VictoryInputHeld);
    }

    // ── 보스 승리 연출 입력 홀드(로컬, 오너만 의미 있음) ──
    // 처치 순간부터 입력만 막는다(행동 취소 없음). BossVictoryPresentation 이 켜고, 씬이 끝날 때 끈다.
    private static bool victoryInputHeld;
    private static event System.Action VictoryInputHeldChanged;

    // 도메인 리로드 없이 Play 를 다시 켜도 지난 판 홀드가 남지 않게.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetVictoryInputHeld()
    {
        victoryInputHeld = false;
        VictoryInputHeldChanged = null;
    }

    /// <summary>보스 승리 연출 입력 홀드 중인가.</summary>
    public static bool VictoryInputHeld => victoryInputHeld;

    /// <summary>보스 승리 연출 입력 홀드를 켜고 끈다 — 모든 정책 인스턴스가 즉시 다시 적용한다.</summary>
    public static void SetVictoryInputHeld(bool held)
    {
        if (victoryInputHeld == held) return;
        victoryInputHeld = held;
        VictoryInputHeldChanged?.Invoke();
    }

    /// <summary>
    /// 연출 잠금·엘리베이터 입력 잠금·숨김은 생명주기 허용값 위에 덮어쓰는 추가 차단이다(하나라도 막으면 막힌다).
    /// </summary>
    public static bool BlocksForCinematic(bool cinematicLocked, bool elevatorInputLocked, bool hidden) =>
        cinematicLocked || elevatorInputLocked || hidden;

    /// <summary>
    /// 전투 입력이 막히는 순간 지금 행동을 Idle 로 끊는가. Idle 이 아닌 모든 상태가 대상이다 —
    /// <b>대시도 여기서 끊긴다</b>(엘리베이터 입력 잠금 t=0 에 남은 대시로 미끄러지지 않는다. 할 일 46 후속 3).
    /// Idle 전이가 <see cref="PlayerDashState"/>.Exit → Motor.ClearDashMotion 으로 남은 대시 이동을 0 으로 만든다.
    /// </summary>
    public static bool ShouldCancelToIdle(bool allowsCombatInput, PlayerActionState currentState) =>
        !allowsCombatInput && currentState != PlayerActionState.Idle;

    private void CancelCombatActions(bool allowsCombatInput)
    {
        if (!allowsCombatInput)
            skillTargeting?.Cancel();

        if (stateController != null &&
            ShouldCancelToIdle(allowsCombatInput, stateController.CurrentState))
        {
            stateController.ChangeState(PlayerActionState.Idle);
        }
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = GetComponent<Player>();

        if (lifeCycle == null)
            lifeCycle = GetComponent<PlayerLifeCycleController>();

        if (inputReader == null)
            inputReader = GetComponent<PlayerInputReader>();

        if (stateController == null)
            stateController = GetComponent<PlayerStateController>();

        if (skillTargeting == null)
            skillTargeting = GetComponent<PlayerSkillTargeting>();

        if (encounterLock == null)
            encounterLock = GetComponent<PlayerEncounterLock>();
    }
}
