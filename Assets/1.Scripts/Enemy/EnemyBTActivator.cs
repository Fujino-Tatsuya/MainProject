using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;

public class EnemyBTActivator : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent[] targetBTs;
    [SerializeField] string isOpenVariableName = "IsOpen";

    readonly List<BlackboardVariable<bool>> isOpenVariables = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        CacheIsOpenVariables();
    }

    void CacheIsOpenVariables()
    {
        isOpenVariables.Clear();

        foreach (BehaviorGraphAgent bt in targetBTs)
        {
            if (bt == null)
                continue;

            if (bt.BlackboardReference.GetVariable<bool>(isOpenVariableName, out var isOpen))
            {
                isOpenVariables.Add(isOpen);
            }
            else
            {
                Debug.LogError($"[Enemy] {bt.name} BT에서 {isOpenVariableName} 변수를 찾지 못했습니다.", bt);
            }
        }
    }

    public void OpenBT()
    {
        if (!IsServer) return;

        foreach (var isOpen in isOpenVariables)
            isOpen.Value = true;
    }

    public void CloseBT()
    {
        if (!IsServer) return;

        foreach (var isOpen in isOpenVariables)
            isOpen.Value = false;
    }
}