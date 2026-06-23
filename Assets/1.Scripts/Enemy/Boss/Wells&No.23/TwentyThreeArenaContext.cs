using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TwentyThreeArenaContext : NetworkBehaviour
{
    [SerializeField] GameObject bossPrefab;
    [SerializeField] List<ChargingObject> ChargingObjects;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        NetworkObject boss = Instantiate(bossPrefab).GetComponent<NetworkObject>();
        if (boss == null)
        {
            Debug.LogError("해당 프리펩에 NetworkObject 컴포넌트가 없습니다. 추가해주세요.");
            return;
        }
        boss.Spawn();

        ChargeController controller = boss.GetComponentInChildren<ChargeController>();
        if (controller == null)
        {
            Debug.LogError("해당 보스에 ChargeController 컴포넌트가 없습니다. 추가해주세요.");
            return;
        }
        controller.SetList(ChargingObjects);
    }
}
