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

    private bool CanWriteLifeState => IsSpawned && IsServer;
    private bool deathResolutionPending;
    private double deathResolutionServerTime;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        lifeState.OnValueChanged += HandleLifeStateChanged;

        if (IsServer)
        {
            ResolveReferences();

            if (deathSource != null)
                deathSource.Died += HandleUnitDied;
            else
                Debug.LogError(
                    "[SoulAlert] Unit 사망 신호를 연결할 deathSource가 없습니다.",
                    this);

            if (gameRule != null)
                gameRule.TryRegisterClient(OwnerClientId);
        }

        // 초기 복제값도 후속 Visual/Input/Hurtbox 소비자가 한 번 적용할 수 있게 알린다.
        ApplyStateHooks(lifeState.Value, lifeState.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (deathSource != null)
            deathSource.Died -= HandleUnitDied;

        deathResolutionPending = false;
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

        if (NetworkManager.ServerTime.Time >= deathResolutionServerTime)
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

        ResolveReferences();
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
        if (!CanWriteLifeState)
            return;

        TryBeginDeathPresentation(PlayerDeathCause.Combat);
    }

    private void ScheduleDeathResolution()
    {
        deathResolutionPending = true;
        deathResolutionServerTime =
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
        PlayerLifeState destinationState = PlayerLifeState.Soul;

        ResolveGameRuleReference();
        bool resolvedByRule =
            gameRule != null &&
            gameRule.TryRegisterClient(OwnerClientId) &&
            gameRule.TryResolveDeathState(OwnerClientId, out destinationState);

        if (!resolvedByRule)
        {
            // 임시 Rule 누락이 Player를 영구 사망시키지 않도록 부활 가능한 Soul로 폴백한다.
            destinationState = PlayerLifeState.Soul;
            Debug.LogWarning(
                "[SoulAlert] Temp_MultiGameRule/LifeCount를 확인할 수 없어 " +
                $"{OwnerClientId}번 Player를 Soul로 전환합니다. " +
                "씬에 Spawn된 Temp_MultiGameRule을 배치하세요.",
                this);
        }

        bool transitioned = destinationState == PlayerLifeState.PermanentDead
            ? TryEnterPermanentDead()
            : TryEnterSoul();

        if (!transitioned)
        {
            Debug.LogError(
                $"[SoulAlert] DeadPresentation에서 {destinationState}(으)로 " +
                "전환하지 못했습니다.",
                this);
        }
    }

    private void ResolveReferences()
    {
        if (deathSource == null)
            deathSource = GetComponent<Unit>();

        ResolveGameRuleReference();
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
        ApplyStateHooks(previousState, currentState);
    }

    private void ApplyStateHooks(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        PlayerLifeGameplayAccess access =
            PlayerLifeGameplayAccess.FromState(currentState);

        LifeStateChanged?.Invoke(previousState, currentState);
        GameplayAccessChanged?.Invoke(access);
    }
}
