using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "IsGameObjectNull", story: "[Target] is null", category: "Conditions", id: "d5e92f1b7c8a43e6b2f4019d3a6c7b15")]
public partial class IsGameObjectNullCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    public override bool IsTrue()
    {
        return Target.Value == null;
    }
}
