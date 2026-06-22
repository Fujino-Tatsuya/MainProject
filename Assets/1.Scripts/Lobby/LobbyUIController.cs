using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIController : MonoBehaviour
{
    private const string ReadyRequestMessageName = "Lobby.ReadyRequest";
    private const string StateMessageName = "Lobby.State";

    public static LobbyUIController Active { get; private set; }

    [SerializeField] private LobbyPlayerSlotView[] slots;
    [SerializeField] private Image startButtonImage;
    [SerializeField] private Color startAvailableColor = new Color(0.25f, 0.85f, 0.45f, 1f);
    [SerializeField] private Color startBlockedColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [SerializeField] private bool autoRegisterWhenNetworkStarts = true;

    private readonly Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();
    private readonly List<ulong> _orderedClients = new List<ulong>();
    private readonly List<ulong> _clientsToRemove = new List<ulong>();

    private NetworkManager _networkManager;
    private Coroutine _registerRoutine;
    private bool _callbacksRegistered;
    private bool _localReady;

    private bool IsServerActive => _networkManager != null && _networkManager.IsListening && _networkManager.IsServer;
    public bool AreAllConnectedClientsReady => HasConnectedClients() && AreAllReady();
    public bool CanStartGame => IsServerActive && AreAllConnectedClientsReady;

    private void Awake()
    {
        Active = this;
    }

    private void OnEnable()
    {
        ApplyUi();

        if (autoRegisterWhenNetworkStarts)
        {
            _registerRoutine = StartCoroutine(RegisterWhenNetworkStarts());
        }
    }

    private void OnDisable()
    {
        if (_registerRoutine != null)
        {
            StopCoroutine(_registerRoutine);
            _registerRoutine = null;
        }

        UnregisterCallbacks();
    }

    private void OnDestroy()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    public void ToggleLocalReady()
    {
        SetLocalReady(!_localReady);
    }

    public void SetLocalReady(bool ready)
    {
        _localReady = ready;
        RegisterCallbacks();

        if (_networkManager == null || !_networkManager.IsListening)
        {
            ApplyUi();
            return;
        }

        if (IsServerActive)
        {
            _readyStates[_networkManager.LocalClientId] = ready;
            BroadcastState();
            ApplyUi();
            return;
        }

        SendReadyRequest(ready);
    }

    private IEnumerator RegisterWhenNetworkStarts()
    {
        while (true)
        {
            _networkManager = NetworkManager.Singleton;
            if (_networkManager != null && _networkManager.IsListening)
            {
                RegisterCallbacks();
                yield break;
            }

            yield return null;
        }
    }

    private void RegisterCallbacks()
    {
        if (_callbacksRegistered)
        {
            return;
        }

        _networkManager = NetworkManager.Singleton;
        if (_networkManager == null || !_networkManager.IsListening)
        {
            return;
        }

        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        if (_networkManager.CustomMessagingManager != null)
        {
            _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ReadyRequestMessageName, HandleReadyRequestMessage);
            _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessageName, HandleStateMessage);
        }

        _callbacksRegistered = true;

        if (IsServerActive)
        {
            SyncConnectedClientsFromServer();
            BroadcastState();
        }
    }

    private void UnregisterCallbacks()
    {
        if (!_callbacksRegistered || _networkManager == null)
        {
            return;
        }

        _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        if (_networkManager.CustomMessagingManager != null)
        {
            _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyRequestMessageName);
            _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessageName);
        }

        _callbacksRegistered = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServerActive)
        {
            return;
        }

        if (!_readyStates.ContainsKey(clientId))
        {
            _readyStates.Add(clientId, false);
        }

        BroadcastState();
        ApplyUi();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServerActive)
        {
            return;
        }

        _readyStates.Remove(clientId);
        BroadcastState();
        ApplyUi();
    }

    private void HandleReadyRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsServerActive)
        {
            return;
        }

        reader.ReadValueSafe(out bool ready);
        _readyStates[senderClientId] = ready;
        BroadcastState();
        ApplyUi();
    }

    private void SendReadyRequest(bool ready)
    {
        var messagingManager = _networkManager.CustomMessagingManager;
        if (messagingManager == null)
        {
            return;
        }

        using (var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp))
        {
            writer.WriteValueSafe(ready);
            messagingManager.SendNamedMessage(ReadyRequestMessageName, NetworkManager.ServerClientId, writer);
        }
    }

    private void BroadcastState()
    {
        if (!IsServerActive || _networkManager.CustomMessagingManager == null)
        {
            return;
        }

        SyncConnectedClientsFromServer();
        ApplyUi();

        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            if (_networkManager.IsHost && clientId == _networkManager.LocalClientId)
            {
                continue;
            }

            SendState(clientId);
        }
    }

    private void SendState(ulong clientId)
    {
        var count = _readyStates.Count;
        var size = sizeof(int) + count * (sizeof(ulong) + sizeof(byte));

        using (var writer = new FastBufferWriter(size, Allocator.Temp))
        {
            writer.WriteValueSafe(count);
            foreach (var pair in _readyStates)
            {
                writer.WriteValueSafe(pair.Key);
                writer.WriteValueSafe(pair.Value);
            }

            _networkManager.CustomMessagingManager.SendNamedMessage(StateMessageName, clientId, writer);
        }
    }

    private void HandleStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        reader.ReadValueSafe(out int count);
        _readyStates.Clear();

        for (var i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out bool ready);
            _readyStates[clientId] = ready;
        }

        if (_networkManager != null && _networkManager.IsListening)
        {
            _localReady = _readyStates.TryGetValue(_networkManager.LocalClientId, out var ready) && ready;
        }

        ApplyUi();
    }

    private void SyncConnectedClientsFromServer()
    {
        if (_networkManager == null)
        {
            return;
        }

        _orderedClients.Clear();
        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            _orderedClients.Add(clientId);
            if (!_readyStates.ContainsKey(clientId))
            {
                _readyStates.Add(clientId, false);
            }
        }

        _clientsToRemove.Clear();
        foreach (var pair in _readyStates)
        {
            if (!_orderedClients.Contains(pair.Key))
            {
                _clientsToRemove.Add(pair.Key);
            }
        }

        foreach (var clientId in _clientsToRemove)
        {
            _readyStates.Remove(clientId);
        }
    }

    private void ApplyUi()
    {
        if (slots == null)
        {
            ApplyStartButtonState();
            return;
        }

        BuildOrderedClientList();

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            if (i >= _orderedClients.Count)
            {
                slot.SetState(false, false);
                continue;
            }

            var clientId = _orderedClients[i];
            var ready = _readyStates.TryGetValue(clientId, out var isReady) && isReady;
            slot.SetState(true, ready);
        }

        ApplyStartButtonState();
    }

    private void BuildOrderedClientList()
    {
        _orderedClients.Clear();

        foreach (var pair in _readyStates)
        {
            _orderedClients.Add(pair.Key);
        }

        _orderedClients.Sort();
    }

    private void ApplyStartButtonState()
    {
        if (startButtonImage == null)
        {
            return;
        }

        startButtonImage.color = AreAllConnectedClientsReady ? startAvailableColor : startBlockedColor;
    }

    private bool HasConnectedClients()
    {
        return _readyStates.Count > 0;
    }

    private bool AreAllReady()
    {
        foreach (var pair in _readyStates)
        {
            if (!pair.Value)
            {
                return false;
            }
        }

        return true;
    }
}
