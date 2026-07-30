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
            encounterLock.CinematicLockChanged += HandleCinematicLockChanged;
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
            encounterLock.CinematicLockChanged -= HandleCinematicLockChanged;
    }

    private void HandleGameplayAccessChanged(PlayerLifeGameplayAccess access)
    {
        ApplyAccess(access);
    }

    private void HandleCinematicLockChanged(bool isLocked)
    {
        ApplyCurrentAccess();
    }

    private void ApplyCurrentAccess()
    {
        if (lifeCycle == null)
            ResolveReferences();

        if (lifeCycle != null)
            ApplyAccess(lifeCycle.GameplayAccess);
    }

    private void ApplyAccess(PlayerLifeGameplayAccess access)
    {
        if (player == null || !player.IsMovementAuthority || inputReader == null)
            return;

        // 연출 잠금은 생명주기 허용값 위에 덮어쓰는 추가 차단이다(둘 중 하나라도 막으면 막힌다).
        bool cinematicLocked = encounterLock != null && encounterLock.IsCinematicLocked;
        bool allowsMovement = access.AllowsMovement && !cinematicLocked;
        bool allowsCombatInput = access.AllowsCombatInput && !cinematicLocked;

        if (!allowsCombatInput)
            CancelCombatActions();

        // DeadPresentation/PermanentDead는 PlayerInput 자체를 끄고 Direction을 즉시 0으로 만든다.
        // Soul은 PlayerInput을 유지하되 아래 전투 게이트만 닫아 이동 입력을 보존한다.
        inputReader.SetInputEnabled(allowsMovement);
        inputReader.SetCombatInputEnabled(allowsCombatInput);
    }

    private void CancelCombatActions()
    {
        skillTargeting?.Cancel();

        if (stateController != null &&
            stateController.CurrentState != PlayerActionState.Idle)
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
