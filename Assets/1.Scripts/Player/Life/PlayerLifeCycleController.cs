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
    private readonly NetworkVariable<PlayerLifeState> lifeState =
        new NetworkVariable<PlayerLifeState>(
            PlayerLifeState.Alive,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public PlayerLifeState State => lifeState.Value;
    public PlayerLifeGameplayAccess GameplayAccess =>
        PlayerLifeGameplayAccess.FromState(lifeState.Value);

    public bool AllowsMovement => GameplayAccess.AllowsMovement;
    public bool AllowsCombatInput => GameplayAccess.AllowsCombatInput;
    public bool ShouldEnableHurtbox => GameplayAccess.ShouldEnableHurtbox;

    public event Action<PlayerLifeState, PlayerLifeState> LifeStateChanged;
    public event Action<PlayerLifeGameplayAccess> GameplayAccessChanged;

    private bool CanWriteLifeState => IsSpawned && IsServer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        lifeState.OnValueChanged += HandleLifeStateChanged;

        // 초기 복제값도 후속 Visual/Input/Hurtbox 소비자가 한 번 적용할 수 있게 알린다.
        ApplyStateHooks(lifeState.Value, lifeState.Value);
    }

    public override void OnNetworkDespawn()
    {
        lifeState.OnValueChanged -= HandleLifeStateChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>Alive 사망을 DeadPresentation으로 진입시킨다. 서버에서만 성공한다.</summary>
    public bool TryBeginDeathPresentation()
    {
        return TryTransition(PlayerLifeState.DeadPresentation);
    }

    /// <summary>DeadPresentation 종료 후 Soul로 전환한다. 서버에서만 성공한다.</summary>
    public bool TryEnterSoul()
    {
        return TryTransition(PlayerLifeState.Soul);
    }

    /// <summary>Soul의 실제 부활 성공을 Alive로 확정한다. 서버에서만 성공한다.</summary>
    public bool TryCompleteRevive()
    {
        return TryTransition(PlayerLifeState.Alive);
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
