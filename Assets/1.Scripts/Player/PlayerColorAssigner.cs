using Unity.Netcode;
using UnityEngine;

// OwnerClientId로 바디 머티리얼을 고른다.
// OwnerClientId는 모든 인스턴스(서버/오너/원격)에 복제되므로, 각자 로컬에서 매핑만 하면
// 별도 동기화(NetworkVariable/RPC) 없이 모두 같은 색이 된다.
[DisallowMultipleComponent]
public class PlayerColorAssigner : NetworkBehaviour
{
    [Tooltip("clientId 기준 색 머티리얼. 인덱스 = clientId (0=host).")]
    [SerializeField] private Material[] colorsByClientId;

    [Tooltip("비워두면 자식에서 SkinnedMeshRenderer를 자동 탐색.")]
    [SerializeField] private SkinnedMeshRenderer bodyRenderer;

    public override void OnNetworkSpawn()
    {
        if (bodyRenderer == null)
            bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (bodyRenderer == null || colorsByClientId == null)
            return;

        if (OwnerClientId < (ulong)colorsByClientId.Length &&
            colorsByClientId[OwnerClientId] != null)
        {
            bodyRenderer.sharedMaterial = colorsByClientId[OwnerClientId];
        }
    }
}
