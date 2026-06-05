using Unity.Behavior;

[BlackboardEnum]
public enum TwentyThreeState
{
    Idle,
    Walk,

    HookAttack,
    UpperAttack,
    Grab,

    JumpAttack,
    DashAttack,

    Charging,
    Groggy,
    Break,
    Dead
}
