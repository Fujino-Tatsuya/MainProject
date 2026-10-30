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
/// 서버 판정(넉백 승인·스킬 승인)이 쓰는 사망 기준(할 일 182). FSM 은 사망해도 Dead 로 가지 않으므로
/// 사망 여부는 생명주기로만 본다 — Alive 만 행동 가능, 사망 연출·Soul·영구 사망은 거부.
/// </summary>
public static class PlayerLifeActionRules
{
    public static bool CanAct(PlayerLifeState state) => state == PlayerLifeState.Alive;

    /// <summary>
    /// 강제 이동(넉백·구속) 상태에 들어갈 수 있는가(할 일 183). 서버 승인과 오너 적용이 같은 기준을 쓴다 —
    /// 서버 RPC(넉백·구속)와 생명주기 복제는 도착 순서가 보장되지 않아, 오너가 사망 연출을 먼저 받고 넉백을 나중에 받으면
    /// 시체가 남은 넉백만큼 미끄러졌다. 생명주기가 없는 테스트 씬은 기존 동작(허용)을 유지한다.
    /// </summary>
    public static bool AcceptsForcedMotion(bool hasLifeCycle, PlayerLifeState state) =>
        !hasLifeCycle || CanAct(state);

    /// <summary>
    /// 기본 공격 상태(Attack·AttackReady)에 들어갈 수 있는가(할 일 186). 서버 시작 승인과 오너의 StartRpc 적용이 같은 기준을 쓴다 —
    /// 사망 직전 보낸 시작 요청이 서버 사망 뒤 승인되면 StartRpc 가 이미 Idle 로 끊긴 오너를 다시 Attack 에 넣었다.
    /// 생명주기가 없는 테스트 씬은 기존 동작(허용)을 유지한다.
    /// </summary>
    public static bool AcceptsAttackEntry(bool hasLifeCycle, PlayerLifeState state) =>
        !hasLifeCycle || CanAct(state);
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
