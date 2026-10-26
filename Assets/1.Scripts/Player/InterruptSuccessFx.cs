using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 간파 성공 이펙트 칸 — <see cref="GlobalTimeScale.Started"/> 가 간파 성공이고 발동자가 이 플레이어면
/// <see cref="successEffect"/> 를 원샷으로 재생한다. <c>Player.prefab</c>(base) 루트에 붙고 칸은 비워 둔다 —
/// 캐릭터 Variant 가 자기 소켓을 만들어 칸을 덮어쓴다. 할 일 87 C4, 문서 Docs/tech/interrupt-presentation.md.
///
/// <list type="bullet">
/// <item>새 RPC 없음 — 슬로우 이벤트의 목적 칸이 전 피어 공용 성공 신호다. 각 피어가 자기 화면에 각자 재생한다.</item>
/// <item>칸이 비어 있으면 아무것도 안 한다(경고도 없음).</item>
/// <item>NetworkManager 의 <see cref="GlobalTimeScale"/> 가 이 플레이어보다 늦게 생겨도 Update 에서 다시 찾아 구독한다.</item>
/// <item>캐릭터별 기존 성공 연출(가붕이 방패 충격파·어쌔신 성공 소켓·거너)과는 별개다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptSuccessFx : MonoBehaviour
{
    [Tooltip("간파 성공 시 이 플레이어 쪽에 재생할 원샷 이펙트. base 는 비워 두고 캐릭터 Variant 가 소켓을 연결한다")]
    [SerializeField] private EffectSocketPlayer successEffect;

    private NetworkObject _networkObject;
    private GlobalTimeScale _subscribed;

    /// <summary>목적·발동자·이 플레이어의 OwnerClientId 로 재생 여부를 정한다.</summary>
    public static bool ShouldPlay(SlowMotionPurpose purpose, ulong triggerClientId, bool isSpawned, ulong ownerClientId) =>
        purpose == SlowMotionPurpose.Success && isSpawned && triggerClientId == ownerClientId;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
    }

    private void OnEnable() => TrySubscribe();

    private void Update()
    {
        if (_subscribed == null)
            TrySubscribe();
    }

    private void OnDisable() => Unsubscribe();

    private void TrySubscribe()
    {
        GlobalTimeScale timeScale = GlobalTimeScale.Instance;
        if (timeScale == null || timeScale == _subscribed)
            return;

        Unsubscribe();
        timeScale.Started += HandleStarted;
        _subscribed = timeScale;
    }

    private void Unsubscribe()
    {
        // 파괴된 GlobalTimeScale 도 C# 객체는 남아 있어 그대로 뗄 수 있다. Unity null 비교라 ReferenceEquals 로 본다.
        if (ReferenceEquals(_subscribed, null))
            return;

        _subscribed.Started -= HandleStarted;
        _subscribed = null;
    }

    private void HandleStarted(uint triggerId, ulong triggerClientId, SlowMotionPurpose purpose)
    {
        if (successEffect == null || _networkObject == null)
            return;

        if (!ShouldPlay(purpose, triggerClientId, _networkObject.IsSpawned, _networkObject.OwnerClientId))
            return;

        successEffect.PlayOnce();
    }
}
