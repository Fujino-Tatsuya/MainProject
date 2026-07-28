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
/// 사망 연출 진입 원인.
/// FallDeathContext가 확정되기 전까지 Fall 쪽이 사용할 최소 placeholder 계약이다.
/// </summary>
public enum PlayerDeathCause
{
    Combat,
    Fall
}

/// <summary>
/// 각 입력/피격 시스템이 생명주기 상태를 소비할 때 사용하는 공용 게이트 값.
/// 로컬 입력과 Hurtbox 정책은 이 값을 소비해 상태별 허용 여부를 적용한다.
/// </summary>
public readonly struct PlayerLifeGameplayAccess
{
    private static readonly PlayerLifeGameplayAccess AliveAccess =
        new PlayerLifeGameplayAccess(
            allowsMovement: true,
            allowsCombatInput: true,
            shouldEnableHurtbox: true);

    private static readonly PlayerLifeGameplayAccess SoulAccess =
        new PlayerLifeGameplayAccess(
            allowsMovement: true,
            allowsCombatInput: false,
            shouldEnableHurtbox: false);

    private static readonly PlayerLifeGameplayAccess BlockedAccess =
        new PlayerLifeGameplayAccess(
            allowsMovement: false,
            allowsCombatInput: false,
            shouldEnableHurtbox: false);

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
                return AliveAccess;

            case PlayerLifeState.Soul:
                return SoulAccess;

            case PlayerLifeState.DeadPresentation:
            case PlayerLifeState.PermanentDead:
            default:
                return BlockedAccess;
        }
    }
}
