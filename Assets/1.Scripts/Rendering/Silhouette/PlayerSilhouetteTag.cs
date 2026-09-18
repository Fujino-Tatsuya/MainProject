using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 이 플레이어를 <b>벽 뒤 실루엣 대상</b>으로 표시한다. 렌더링은
/// <c>PlayerSilhouetteFeature</c> 가 하고, 여기서는 <b>누가 내 캐릭터인지</b>만 정한다.
///
/// 🔴 <b>복제하지 않는다.</b> "내 캐릭터 = 초록"은 <b>보는 사람마다 다른 오브젝트</b>를 가리키므로
/// 복제하면 오히려 전원이 같은 색이 된다. 각 피어가 자기 <see cref="NetworkBehaviour.IsOwner"/> 로
/// 판단하는 순수 로컬 시각 값이다.
///
/// 🔴 <b>왜 <see cref="NetworkBehaviour"/> 인가</b>: 소유권은 스폰이 끝나야 확정된다.
/// <c>Awake</c>/<c>Start</c> 에서 읽으면 아직 안 정해져 있어 전원이 원격으로 찍힌다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerSilhouetteTag : NetworkBehaviour
{
    [Tooltip("표시를 시작할 루트. 비우면 이 오브젝트 아래 전부. " +
             "모델이 따로 있으면 그것만 지정해 VFX·시체 렌더러가 윤곽선에 섞이지 않게 한다.")]
    [SerializeField] private GameObject modelRoot;

    [Tooltip("켜면 표시한 렌더러 수를 한 번 찍는다. 0 이면 배선이 잘못된 것이다.")]
    [SerializeField] private bool logOnSpawn;

    private GameObject Target => modelRoot != null ? modelRoot : gameObject;

    public override void OnNetworkSpawn() => Retag();

    public override void OnNetworkDespawn() => PlayerSilhouetteLayers.Untag(Target);

    /// <summary>
    /// 소유권이 바뀌면 색이 바뀌어야 한다 — 내 캐릭터였다가 남의 것이 되면 초록에서 파랑으로.
    /// </summary>
    protected override void OnOwnershipChanged(ulong previous, ulong current) => Retag();

    /// <summary>
    /// 모델을 런타임에 교체하거나 리스폰으로 렌더러가 새로 생겼을 때 호출한다.
    /// 표시는 <b>렌더러에</b> 붙는 것이라 새 렌더러는 표시가 없다.
    /// </summary>
    public void Retag()
    {
        if (!IsSpawned) return;

        int count = PlayerSilhouetteLayers.Tag(Target, IsOwner);

        if (count == 0)
        {
            Debug.LogWarning(
                $"[PlayerSilhouette] {name}: 표시할 Renderer 가 없다. " +
                "Model Root 가 잘못 지정됐거나 모델이 아직 안 붙었다.", this);
            return;
        }

        if (logOnSpawn)
            Debug.Log($"[PlayerSilhouette] {name}: 렌더러 {count}개 표시 " +
                      $"({(IsOwner ? "내 캐릭터=초록" : "다른 플레이어=파랑")})", this);
    }
}
