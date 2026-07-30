using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;

public class EnemyBTActivator : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent[] targetBTs;
    [SerializeField] string isOpenVariableName = "IsOpen";
    [SerializeField] private ReStart restartChannel;

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


    public void RaiseRestart()
    {
        // 이 채널을 구독(On Start 등)하는 모든 BehaviorGraph의 노드가 트리거됨
        restartChannel.SendEventMessage();
        OpenBT();
    }
}
