using Unity.Netcode;
using UnityEngine;

public class KMKScene : NetworkBehaviour
{
    [SerializeField] GameObject bossPrefab;

    public override void OnNetworkSpawn()
    {
        if(!IsServer) return;

        NetworkObject boss = Instantiate(bossPrefab).GetComponent<NetworkObject>();
        if (boss == null)
        {
            Debug.LogError("해당 프리펩에 NetworkObject 컴포넌트가 없습니다. 추가해주세요.");
            return;
        }
        boss.Spawn();
    }
}
