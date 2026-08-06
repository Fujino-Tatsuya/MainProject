using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fall 쪽에서 사망 원인을 확정한 뒤 호출할 생명주기 진입 seam.
/// FallDeathContext의 구체 타입과 전달 시그니처는 Fall 작업에서 계약이 확정된 뒤 추가한다.
/// </summary>
public interface IPlayerDeathPresentationConsumer
{
    bool TryBeginDeathPresentation();
}

/// <summary>
/// Player 생명주기 상태를 서버 권한으로 전환하고 모든 피어에 복제한다.
/// Visual, Layer, 물리, Camera, Corpse 전환은 후속 소비자가 이벤트/게이트를 통해 처리한다.
/// </summary>
public class PlayerLifeCycleController : NetworkBehaviour, IPlayerDeathPresentationConsumer
{
    [Header("Death Flow")]
    [SerializeField] private Unit deathSource;
    [SerializeField] private Temp_MultiGameRule gameRule;
    [SerializeField, Min(0f)] private float deathPresentationDuration = 1.5f;

    // 사망 시 되돌릴 애니메이터. 비워 두면 Awake 에서 자식에서 찾는다(프리팹 배선 불필요).
    [SerializeField] private Animator lifeAnimator;

    private readonly NetworkVariable<PlayerLifeState> lifeState =
        new NetworkVariable<PlayerLifeState>(
            PlayerLifeState.Alive,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public PlayerLifeState State => lifeState.Value;
    public float DeathPresentationDuration => deathPresentationDuration;
    public PlayerDeathCause LastDeathCause { get; private set; } =
        PlayerDeathCause.Combat;
    public PlayerLifeGameplayAccess GameplayAccess =>
        PlayerLifeGameplayAccess.FromState(lifeState.Value);

    public bool AllowsMovement => GameplayAccess.AllowsMovement;
    public bool AllowsCombatInput => GameplayAccess.AllowsCombatInput;
    public bool ShouldEnableHurtbox => GameplayAccess.ShouldEnableHurtbox;

    public event Action<PlayerLifeState, PlayerLifeState> LifeStateChanged;
    public event Action<PlayerLifeGameplayAccess> GameplayAccessChanged;

    private bool CanWriteLifeState => IsSpawned && IsServer && !IsCinematicLocked;
    private bool deathResolutionPending;
    private double deathResolutionDeadlineServerTime;
    private PlayerEncounterLock encounterLock;
    private bool deferredDeathWhileLocked;

    /// <summary>
    /// 연출 잠금 중에는 생명주기 전이를 동결한다. 피해는 무적 토큰이 이미 막지만,
    /// 추락·부활 타이머 같은 다른 경로가 연출 도중 상태를 바꾸면 참가자 집합이 깨진다.
    /// </summary>
    private bool IsCinematicLocked => encounterLock != null && encounterLock.IsCinematicLocked;

    private void Awake()
    {
        encounterLock = GetComponent<PlayerEncounterLock>();
        ResolveLocalReferences();
        ResolveGameRuleReference();

        if (lifeAnimator == null)
            lifeAnimator = GetComponentInChildren<Animator>(true);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        lifeState.OnValueChanged += HandleLifeStateChanged;

        if (IsServer)
        {
            ResolveLocalReferences();
            ResolveGameRuleReference();
            SubscribeToDeathSource();
            if (gameRule != null)
                gameRule.TryRegisterClient(OwnerClientId);

            if (encounterLock != null)
                encounterLock.CinematicLockChanged += HandleCinematicLockChanged;
        }

        // 초기 복제값도 후속 Visual/Input/Hurtbox 소비자가 한 번 적용할 수 있게 알린다.
        NotifyStateObservers(lifeState.Value, lifeState.Value);
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromDeathSource();

        if (encounterLock != null)
            encounterLock.CinematicLockChanged -= HandleCinematicLockChanged;

        deathResolutionPending = false;
        deferredDeathWhileLocked = false;
        lifeState.OnValueChanged -= HandleLifeStateChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!deathResolutionPending || !CanWriteLifeState)
            return;

        if (lifeState.Value != PlayerLifeState.DeadPresentation)
        {
            deathResolutionPending = false;
            return;
        }

        if (NetworkManager.ServerTime.Time >= deathResolutionDeadlineServerTime)
            ResolveDeathPresentation();
    }

    /// <summary>Alive 사망을 DeadPresentation으로 진입시킨다. 서버에서만 성공한다.</summary>
    public bool TryBeginDeathPresentation()
    {
        return TryBeginDeathPresentation(PlayerDeathCause.Combat);
    }

    /// <summary>
    /// Alive 사망 원인을 기록하고 DeadPresentation으로 진입시킨다.
    /// FallDeathContext가 확정되면 Fall 경로가 Fall 원인으로 이 overload를 호출한다.
    /// </summary>
    public bool TryBeginDeathPresentation(PlayerDeathCause deathCause)
    {
        if (!CanWriteLifeState ||
            !IsValidTransition(lifeState.Value, PlayerLifeState.DeadPresentation))
        {
            return false;
        }

        LastDeathCause = deathCause;
        lifeState.Value = PlayerLifeState.DeadPresentation;
        ScheduleDeathResolution();
        return true;
    }

    /// <summary>DeadPresentation 종료 후 Soul로 전환한다. 서버에서만 성공한다.</summary>
    public bool TryEnterSoul()
    {
        return TryTransition(PlayerLifeState.Soul);
    }

    /// <summary>Soul의 실제 부활 성공을 Alive로 확정한다. 서버에서만 성공한다.</summary>
    public bool TryCompleteRevive()
    {
        if (!CanWriteLifeState ||
            !IsValidTransition(lifeState.Value, PlayerLifeState.Alive))
        {
            return false;
        }

        ResolveLocalReferences();
        if (deathSource == null)
        {
            Debug.LogError(
                "[SoulAlert] 부활할 Unit deathSource가 없어 Alive 전환을 중단합니다.",
                this);
            return false;
        }

        // HP와 Unit.Died 재발행 잠금을 먼저 복구한 뒤 Hurtbox/입력 소비자가
        // Alive 복제 상태에 반응하도록 한다.
        deathSource.Revive();
        lifeState.Value = PlayerLifeState.Alive;
        return true;
    }

    /// <summary>DeadPresentation 종료 후 최종 사망을 확정한다. 서버에서만 성공한다.</summary>
    public bool TryEnterPermanentDead()
    {
        return TryTransition(PlayerLifeState.PermanentDead);
    }

    private bool TryTransition(PlayerLifeState nextState)
    {
        if (!CanWriteLifeState)
            return false;

        PlayerLifeState previousState = lifeState.Value;
        if (!IsValidTransition(previousState, nextState))
            return false;

        lifeState.Value = nextState;
        return true;
    }

    private void HandleUnitDied()
    {
        if (!IsSpawned || !IsServer)
            return;

        // Unit.Died는 _deathNotified로 래치되어 재발행되지 않는다. 연출 중 들어온 사망 신호를
        // 그냥 버리면 HP 0인데 Alive로 남는 좀비가 되므로 잠금 해제 시점까지 보류한다.
        if (IsCinematicLocked)
        {
            deferredDeathWhileLocked = true;
            return;
        }

        TryBeginDeathPresentation(PlayerDeathCause.Combat);
    }

    private void HandleCinematicLockChanged(bool isLocked)
    {
        if (isLocked || !deferredDeathWhileLocked || !IsSpawned || !IsServer)
            return;

        deferredDeathWhileLocked = false;

        if (!TryBeginDeathPresentation(PlayerDeathCause.Combat))
        {
            Debug.LogError(
                "[SoulAlert] 연출 중 보류한 사망을 해제 후에도 처리하지 못했습니다.",
                this);
        }
    }

    private void ScheduleDeathResolution()
    {
        deathResolutionPending = true;
        deathResolutionDeadlineServerTime =
            NetworkManager.ServerTime.Time + Mathf.Max(0f, deathPresentationDuration);

        if (deathPresentationDuration <= 0f)
            ResolveDeathPresentation();
    }

    private void ResolveDeathPresentation()
    {
        if (!deathResolutionPending ||
            !CanWriteLifeState ||
            lifeState.Value != PlayerLifeState.DeadPresentation)
        {
            return;
        }

        deathResolutionPending = false;
        PlayerLifeState destinationState = ResolveDeathDestination();
        bool transitioned = TryEnterResolvedDeathState(destinationState);

        if (!transitioned)
        {
            Debug.LogError(
                $"[SoulAlert] DeadPresentation에서 {destinationState}(으)로 " +
                "전환하지 못했습니다.",
                this);
        }
    }

    private PlayerLifeState ResolveDeathDestination()
    {
        ResolveGameRuleReference();
        if (gameRule != null &&
            gameRule.TryRegisterClient(OwnerClientId) &&
            gameRule.TryResolveDeathState(OwnerClientId, out PlayerLifeState destinationState))
        {
            return destinationState;
        }

        // 임시 Rule 누락이 Player를 영구 사망시키지 않도록 부활 가능한 Soul로 폴백한다.
        Debug.LogWarning(
            "[SoulAlert] Temp_MultiGameRule/LifeCount를 확인할 수 없어 " +
            $"{OwnerClientId}번 Player를 Soul로 전환합니다. " +
            "씬에 Spawn된 Temp_MultiGameRule을 배치하세요.",
            this);
        return PlayerLifeState.Soul;
    }

    private bool TryEnterResolvedDeathState(PlayerLifeState destinationState)
    {
        return destinationState == PlayerLifeState.PermanentDead
            ? TryEnterPermanentDead()
            : TryEnterSoul();
    }

    private void SubscribeToDeathSource()
    {
        if (deathSource != null)
        {
            deathSource.Died += HandleUnitDied;
            return;
        }

        Debug.LogError(
            "[SoulAlert] Unit 사망 신호를 연결할 deathSource가 없습니다.",
            this);
    }

    private void UnsubscribeFromDeathSource()
    {
        if (deathSource != null)
            deathSource.Died -= HandleUnitDied;
    }

    private void ResolveLocalReferences()
    {
        if (deathSource == null)
            deathSource = GetComponent<Unit>();
    }

    private void ResolveGameRuleReference()
    {
        if (gameRule == null)
            gameRule = FindFirstObjectByType<Temp_MultiGameRule>();
    }

    private static bool IsValidTransition(PlayerLifeState from, PlayerLifeState to)
    {
        switch (from)
        {
            case PlayerLifeState.Alive:
                return to == PlayerLifeState.DeadPresentation;

            case PlayerLifeState.DeadPresentation:
                return to == PlayerLifeState.Soul ||
                    to == PlayerLifeState.PermanentDead;

            case PlayerLifeState.Soul:
                return to == PlayerLifeState.Alive;

            case PlayerLifeState.PermanentDead:
            default:
                return false;
        }
    }

    private void HandleLifeStateChanged(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        // ⚠️ 관찰자 통지보다 **먼저** 되돌린다. 사망 연출을 구독하는 쪽이 애니메이터를 세팅한 뒤에
        //    리셋하면 그 연출까지 같이 지워진다.
        if (currentState == PlayerLifeState.DeadPresentation &&
            previousState != PlayerLifeState.DeadPresentation)
        {
            ResetAnimatorForDeath();
        }

        NotifyStateObservers(previousState, currentState);
    }

    /// <summary>
    /// 사망 순간 애니메이터를 초기 상태로 되돌린다.
    ///
    /// 왜 필요한가: Q(FirstMeleeMainSkill) 같은 스킬 도중 죽으면 애니메이터가 그 상태와 트리거를
    /// 그대로 들고 있다. 프로젝트 전체에 플레이어 애니메이션을 되돌리는 경로가 아예 없어
    /// (Rebind/WriteDefaultValues 0건, 스킬 취소 경로 0건, ResetTrigger 는 몬스터 전용)
    /// 부활해도 남은 상태가 그대로 이어진다.
    ///
    /// ⚠️ Unity 6000.3.16f1 에는 "상태만 초기화"하는 전용 API 가 없다.
    ///    ResetAllStates / ResetParameters 는 존재하지 않음을 어셈블리에서 확인했다.
    ///    문서화된 방법은 Rebind() + Update(0) 뿐이다 — Rebind 는 파라미터까지 되돌리므로
    ///    이후 사망 연출은 이 호출 **뒤에** 세팅돼야 한다(호출 위치가 통지보다 앞인 이유).
    ///
    /// 모든 피어에서 같은 복제 전이에 대해 호출되므로 호스트/클라이언트가 어긋나지 않는다.
    /// </summary>
    private void ResetAnimatorForDeath()
    {
        if (lifeAnimator == null || !lifeAnimator.isActiveAndEnabled)
            return;

        lifeAnimator.Rebind();
        lifeAnimator.Update(0f);
    }

    private void NotifyStateObservers(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        PlayerLifeGameplayAccess access =
            PlayerLifeGameplayAccess.FromState(currentState);

        LifeStateChanged?.Invoke(previousState, currentState);
        GameplayAccessChanged?.Invoke(access);
    }
}
