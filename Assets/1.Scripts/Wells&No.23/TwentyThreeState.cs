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

    // 원거리 공격
    JumpAttack = 6,
    DashAttack = 7,

    // 스킬
    Charging = 8,
    Rage = 9,

    Groggy = 10,
    Break = 11,
    Dead = 12
}
