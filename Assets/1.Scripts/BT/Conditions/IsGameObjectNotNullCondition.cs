using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "IsGameObjectNotNull", story: "[Target] is not null", category: "Conditions", id: "c4d81f0a6b9e42d7a3f5108c2e7b6904")]
public partial class IsGameObjectNotNullCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    public override bool IsTrue()
    {
        return Target.Value != null;
    }
}
