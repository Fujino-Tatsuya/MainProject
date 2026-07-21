using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private string titleSceneName = "TitleScene";
    [SerializeField] private string lobbySceneName = "Temp_LobbyScene";
    [SerializeField] private string resultSceneName = "ResultScene";
    [SerializeField] private string sessionConnectPanelName = "Pannel_SessionConnect";

    private bool _hideSessionConnectPanelOnLobbyLoad;

    private void Awake()
    {
        Debug.Log("[SceneFlow] GameManager.Awake");
        if (Instance != null && Instance != this)
        {
            Debug.Log("[SceneFlow] GameManager.Awake duplicate destroyed");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        Debug.Log($"[SceneFlow] GameManager.Start LoadScene titleSceneName={titleSceneName}");
        SceneManager.LoadScene(titleSceneName);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }
    }

    public void GoToLobby()
    {
        _hideSessionConnectPanelOnLobbyLoad = IsInNetworkSession();
        Debug.Log($"[SceneFlow] GameManager.GoToLobby LoadScene lobbySceneName={lobbySceneName} hideSessionPanel={_hideSessionConnectPanelOnLobbyLoad}");
        SceneManager.LoadScene(lobbySceneName);
    }

    public static void GoToLobbyButton()
    {
        Debug.Log("[SceneFlow] GameManager.GoToLobbyButton");
        var resultSceneManager = FindFirstObjectByType<ResultSceneManager>();
        if (resultSceneManager != null)
        {
            resultSceneManager.GoToLobby();
            return;
        }

        if (Instance == null)
        {
            Debug.LogWarning($"{nameof(GameManager)} is missing from the scene.");
            return;
        }

        Instance.GoToLobby();
    }

    public void GoToResult()
    {
        Debug.Log($"[SceneFlow] GameManager.GoToResult LoadScene resultSceneName={resultSceneName}");
        SceneManager.LoadScene(resultSceneName);
    }

    public static void GoToResultButton()
    {
        Debug.Log("[SceneFlow] GameManager.GoToResultButton");
        var mapSceneManager = FindFirstObjectByType<MapSceneManager>();
        if (mapSceneManager != null)
        {
            mapSceneManager.GoToResult();
            return;
        }

        if (Instance == null)
        {
            Debug.LogWarning($"{nameof(GameManager)} is missing from the scene.");
            return;
        }

        Instance.GoToResult();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[SceneFlow] GameManager.HandleSceneLoaded scene={scene.name} mode={mode} activeScene={SceneManager.GetActiveScene().name}");
        if (scene.name != lobbySceneName)
        {
            return;
        }

        ApplyLobbySessionConnectPanelState();
    }

    private void ApplyLobbySessionConnectPanelState()
    {
        var shouldHide = _hideSessionConnectPanelOnLobbyLoad || IsInNetworkSession();
        _hideSessionConnectPanelOnLobbyLoad = IsInNetworkSession();
        Debug.Log($"[SceneFlow] GameManager.ApplyLobbySessionConnectPanelState shouldHide={shouldHide} inNetwork={_hideSessionConnectPanelOnLobbyLoad}");

        if (!shouldHide)
        {
            return;
        }

        var sessionConnectPanel = FindInActiveScene(sessionConnectPanelName);
        if (sessionConnectPanel != null)
        {
            sessionConnectPanel.SetActive(false);
        }
    }

    private static bool IsInNetworkSession()
    {
        var networkManager = NetworkManager.Singleton;
        return networkManager != null &&
               networkManager.IsListening &&
               (networkManager.IsHost ||
                networkManager.IsServer ||
                networkManager.IsClient ||
                networkManager.IsConnectedClient);
    }

    private static GameObject FindInActiveScene(string objectName)
    {
        var activeScene = SceneManager.GetActiveScene();
        var rootObjects = activeScene.GetRootGameObjects();

        foreach (var rootObject in rootObjects)
        {
            var target = FindInChildren(rootObject.transform, objectName);
            if (target != null)
            {
                return target.gameObject;
            }
        }

        return null;
    }

    private static Transform FindInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (var i = 0; i < parent.childCount; i++)
        {
            var target = FindInChildren(parent.GetChild(i), objectName);
            if (target != null)
            {
                return target;
            }
        }

        return null;
    }
}
