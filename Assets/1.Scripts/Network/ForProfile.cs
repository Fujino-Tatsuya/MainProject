using Unity.Netcode;
using UnityEngine;

public class ForProfile : MonoBehaviour
{
    private NetworkManager _networkManager;
    private bool _serverStartedSubscribed;

    private void Start()
    {
        SubscribeToServerStarted(NetworkManager.Singleton);
    }

    private void OnDisable()
    {
        UnsubscribeFromServerStarted();
    }

    private void OnGUI()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.IsListening)
        {
            return;
        }

        if (GUI.Button(new Rect(20, 20, 200, 60), "Start Host"))
        {
            SubscribeToServerStarted(networkManager);
            networkManager.StartHost();
        }
    }

    private void SubscribeToServerStarted(NetworkManager networkManager)
    {
        if (networkManager == null ||
            (_serverStartedSubscribed && _networkManager == networkManager))
        {
            return;
        }

        UnsubscribeFromServerStarted();
        _networkManager = networkManager;
        _networkManager.OnServerStarted += HandleServerStarted;
        _serverStartedSubscribed = true;
    }

    private void UnsubscribeFromServerStarted()
    {
        if (!_serverStartedSubscribed)
        {
            return;
        }

        if (_networkManager != null)
        {
            _networkManager.OnServerStarted -= HandleServerStarted;
        }

        _networkManager = null;
        _serverStartedSubscribed = false;
    }

    private void HandleServerStarted()
    {
        var gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogWarning(
                "[ForProfile] MainGame ready 이벤트를 발행할 GameManager가 없습니다. " +
                "BootStrap 경로에서 테스트하거나 GameManager가 유지된 상태에서 호스트를 시작하세요.",
                this);
            return;
        }

        if (gameManager.CurrentState == GameManager.GameState.MainGame &&
            !gameManager.IsMainGameReady)
        {
            gameManager.NotifyMainGameReady();
        }
    }
}
