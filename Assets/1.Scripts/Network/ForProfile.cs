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
        // 디버그 호스트 시작 경로: 정식 4.MapScene 로딩(GameManager가 MarkMainGameStart 호출)을
        // 거치지 않는 테스트 씬에서도 MainGame 시계를 켜준다. 이게 있어야 NetworkClock.MainGameElapsed가
        // 흐르고, 그에 의존하는 결정론 모션(MovingPlatform, Vent 등)이 실제로 동작한다.
        // 이미 시작된 시계는 재스탬프하지 않아 정식 흐름에 영향 없음.
        var clock = NetworkClock.Instance;
        if (clock != null && !clock.HasMainGameStarted)
        {
            clock.MarkMainGameStart();
        }

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
