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

        // [임시 주석처리 2026-07-15] 플레이어 색 구분이 Paladin 텍스처를 단색으로 덮어써 빨갛게 보임 → 비활성화.
        // 텍스처 유지하며 색 구분하려면 sharedMaterial 통째 교체 대신 MaterialPropertyBlock으로 _BaseColor 틴트 방식으로 재작업 필요.
        // if (OwnerClientId < (ulong)colorsByClientId.Length &&
        //     colorsByClientId[OwnerClientId] != null)
        // {
        //     bodyRenderer.sharedMaterial = colorsByClientId[OwnerClientId];
        // }
    }
}
