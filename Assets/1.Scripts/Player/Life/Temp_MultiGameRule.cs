using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 추후 PlayerGameRuleData 같은 ScriptableObject가 구현할 LifeCount 초기값 공급 계약.
/// </summary>
public interface IPlayerLifeCountInitialValueProvider
{
    int GetInitialLifeCount(ulong clientId);
}

/// <summary>Client별 실제 부활 가능 횟수의 네트워크 복제 항목.</summary>
public struct PlayerLifeCountEntry :
    INetworkSerializable,
    IEquatable<PlayerLifeCountEntry>
{
    public ulong ClientId;
    public int LifeCount;

    public PlayerLifeCountEntry(ulong clientId, int lifeCount)
    {
        ClientId = clientId;
        LifeCount = lifeCount;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref LifeCount);
    }

    public bool Equals(PlayerLifeCountEntry other)
    {
        return ClientId == other.ClientId && LifeCount == other.LifeCount;
    }

    public override bool Equals(object obj)
    {
        return obj is PlayerLifeCountEntry other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)ClientId * 397) ^ LifeCount;
        }
    }
}

/// <summary>
/// 임시 멀티플레이 규칙. 서버만 Client별 LifeCount를 등록/감소/제거하며,
/// 복제된 NetworkList는 클라이언트 Debug UI가 읽기만 할 수 있다.
/// </summary>
public class Temp_MultiGameRule : NetworkBehaviour
{
    [Header("LifeCount")]
    [SerializeField, Min(0)] private int defaultLifeCount = 3;
    [Tooltip("선택 사항. IPlayerLifeCountInitialValueProvider를 구현한 ScriptableObject.")]
    [SerializeField] private ScriptableObject initialValueProvider;

    private readonly NetworkList<PlayerLifeCountEntry> lifeCounts =
        new NetworkList<PlayerLifeCountEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public int DefaultLifeCount => Mathf.Max(0, defaultLifeCount);
    public int RegisteredClientCount => lifeCounts.Count;

    /// <summary>Debug UI 등 읽기 소비자가 목록 변경 뒤 값을 다시 조회하는 훅.</summary>
    public event Action LifeCountsChanged;

    private bool serverCallbacksRegistered;
    private bool CanWriteLifeCounts => IsSpawned && IsServer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        lifeCounts.OnListChanged += HandleLifeCountsChanged;

        if (!IsServer)
            return;

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        serverCallbacksRegistered = true;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            TryRegisterClient(clientId);
    }

    public override void OnNetworkDespawn()
    {
        lifeCounts.OnListChanged -= HandleLifeCountsChanged;

        if (serverCallbacksRegistered && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            serverCallbacksRegistered = false;
        }

        base.OnNetworkDespawn();
    }

    /// <summary>복제된 Client의 현재 LifeCount를 읽는다. 모든 피어에서 호출 가능하다.</summary>
    public bool TryGetLifeCount(ulong clientId, out int lifeCount)
    {
        int index = FindClientIndex(clientId);
        if (index < 0)
        {
            lifeCount = 0;
            return false;
        }

        lifeCount = lifeCounts[index].LifeCount;
        return true;
    }

    /// <summary>표시/사전 게이트용 읽기 API. 사망 상태의 최종 확정에는 TryResolveDeathState를 사용한다.</summary>
    public bool HasReviveAvailable(ulong clientId)
    {
        return TryGetLifeCount(clientId, out int lifeCount) && lifeCount > 0;
    }

    /// <summary>서버가 Client를 장부에 등록한다. 이미 등록된 Client면 성공으로 취급한다.</summary>
    public bool TryRegisterClient(ulong clientId)
    {
        if (!CanWriteLifeCounts)
            return false;

        if (FindClientIndex(clientId) >= 0)
            return true;

        int initialLifeCount = ResolveInitialLifeCount(clientId);
        lifeCounts.Add(new PlayerLifeCountEntry(clientId, initialLifeCount));
        return true;
    }

    /// <summary>서버가 연결 종료 Client의 임시 장부를 제거한다.</summary>
    public bool TryUnregisterClient(ulong clientId)
    {
        if (!CanWriteLifeCounts)
            return false;

        int index = FindClientIndex(clientId);
        if (index < 0)
            return false;

        lifeCounts.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 서버가 사망 시 Soul 또는 PermanentDead 목적지를 판정한다.
    /// LifeCount는 이 시점에 감소하지 않는다.
    /// </summary>
    public bool TryResolveDeathState(
        ulong clientId,
        out PlayerLifeState destinationState)
    {
        destinationState = PlayerLifeState.PermanentDead;

        if (!CanWriteLifeCounts ||
            !TryGetLifeCount(clientId, out int lifeCount))
        {
            return false;
        }

        destinationState = lifeCount > 0
            ? PlayerLifeState.Soul
            : PlayerLifeState.PermanentDead;
        return true;
    }

    /// <summary>
    /// 서버가 Soul에서 Alive로 실제 부활을 성공시킨 직후 LifeCount를 1 감소시킨다.
    /// Soul 진입이나 부활 시도 단계에서는 호출하지 않는다.
    /// </summary>
    public bool TryConsumeLifeAfterAliveRevive(
        ulong clientId,
        out int remainingLifeCount)
    {
        remainingLifeCount = 0;

        if (!CanWriteLifeCounts)
            return false;

        int index = FindClientIndex(clientId);
        if (index < 0)
            return false;

        PlayerLifeCountEntry entry = lifeCounts[index];
        if (entry.LifeCount <= 0)
            return false;

        entry.LifeCount--;
        lifeCounts[index] = entry;
        remainingLifeCount = entry.LifeCount;
        return true;
    }

    private int ResolveInitialLifeCount(ulong clientId)
    {
        if (initialValueProvider is IPlayerLifeCountInitialValueProvider provider)
            return Mathf.Max(0, provider.GetInitialLifeCount(clientId));

        return DefaultLifeCount;
    }

    private int FindClientIndex(ulong clientId)
    {
        for (int i = 0; i < lifeCounts.Count; i++)
        {
            if (lifeCounts[i].ClientId == clientId)
                return i;
        }

        return -1;
    }

    private void HandleClientConnected(ulong clientId)
    {
        TryRegisterClient(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        TryUnregisterClient(clientId);
    }

    private void HandleLifeCountsChanged(
        NetworkListEvent<PlayerLifeCountEntry> changeEvent)
    {
        LifeCountsChanged?.Invoke();
    }
}
