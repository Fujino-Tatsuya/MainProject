using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class RunningOnlyOnServer : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent btAgent;
    [SerializeField] NavMeshAgent navMeshAgent;

    public override void OnNetworkSpawn()
    { 
        base.OnNetworkSpawn();

        btAgent.enabled = IsServer;
        navMeshAgent.enabled = IsServer;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    void Update()
    {
        
    }
}
