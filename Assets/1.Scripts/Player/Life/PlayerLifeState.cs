/// <summary>
/// 서버가 확정하는 Player 생명주기 상태.
/// 시각 상태는 이 값을 표현할 뿐 게임플레이 판정의 원본이 아니다.
/// </summary>
public enum PlayerLifeState
{
    Alive,
    DeadPresentation,
    Soul,
    PermanentDead
}

/// <summary>
/// 각 입력/피격 시스템이 생명주기 상태를 소비할 때 사용하는 공용 게이트 값.
/// 실제 입력 컴포넌트와 Hurtbox 배선은 후속 통합에서 수행한다.
/// </summary>
public readonly struct PlayerLifeGameplayAccess
{
    public bool AllowsMovement { get; }
    public bool AllowsCombatInput { get; }
    public bool ShouldEnableHurtbox { get; }

    public PlayerLifeGameplayAccess(
        bool allowsMovement,
        bool allowsCombatInput,
        bool shouldEnableHurtbox)
    {
        AllowsMovement = allowsMovement;
        AllowsCombatInput = allowsCombatInput;
        ShouldEnableHurtbox = shouldEnableHurtbox;
    }

    public static PlayerLifeGameplayAccess FromState(PlayerLifeState state)
    {
        switch (state)
        {
            case PlayerLifeState.Alive:
                return new PlayerLifeGameplayAccess(
                    allowsMovement: true,
                    allowsCombatInput: true,
                    shouldEnableHurtbox: true);

            case PlayerLifeState.Soul:
                return new PlayerLifeGameplayAccess(
                    allowsMovement: true,
                    allowsCombatInput: false,
                    shouldEnableHurtbox: false);

            case PlayerLifeState.DeadPresentation:
            case PlayerLifeState.PermanentDead:
            default:
                return new PlayerLifeGameplayAccess(
                    allowsMovement: false,
                    allowsCombatInput: false,
                    shouldEnableHurtbox: false);
        }
    }
}
