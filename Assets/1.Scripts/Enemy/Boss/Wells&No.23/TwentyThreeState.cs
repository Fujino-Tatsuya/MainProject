using Unity.Behavior;

[BlackboardEnum]
public enum TwentyThreeState
{
    Idle = 0,
    Walk = 1,

    // 근거리 공격
    LeftHookAttack = 2,
    RightHookAttack = 3,
    UpperAttack = 4,
    Grab = 5,
    Hold = 6,
    Throw = 7,

    // 원거리 공격
    JumpAttack = 8,
    DashAttack = 9,

    // 스킬
    Charging = 10,
    Rage = 11,

    Groggy = 12,
    Break = 13,
    Dead = 14
}
