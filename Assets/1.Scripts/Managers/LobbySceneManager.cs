using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbySceneManager : NemoSceneManager
{
    [Header("Buttons")]
    [SerializeField] private Button startHostButton;
    [SerializeField] private Button startClientButton;
    [SerializeField] private Button jgsButton;
    [SerializeField] private Button ljwButton;
    [SerializeField] private Button lehButton;
    [SerializeField] private Button kthButton;
    [SerializeField] private Button kmkButton;
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button readyButton;

    [Header("Panels")]
    [SerializeField] private GameObject sessionConnectPanel;

    [Header("Connection Data")]
    [SerializeField] private string jgsIp = "172.33.1.8";
    [SerializeField] private string ljwIp = "172.33.1.2";
    [SerializeField] private string lehIp = "172.33.1.22";
    [SerializeField] private string kthIp = "172.33.1.9";
    [SerializeField] private string kmkIp = "172.33.1.19";

    private NetworkManager _networkManager;
    private NetworkSessionLauncher _sessionLauncher;
    private LobbyUIController _lobbyUIController;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] LobbySceneManager.Awake");
        _networkManager = GetNetworkManager();
        _sessionLauncher = _networkManager != null ? _networkManager.GetComponent<NetworkSessionLauncher>() : null;
        _lobbyUIController = LobbyUIController.Active;

        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
        }

        ResolveSceneReferences();
        BindButtons();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] LobbySceneManager.Start");
        PlayEnterFade();

    }

    public void StartHost()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartHost hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            return;
        }

        if (_sessionLauncher.StartHost())
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartHost succeeded, hiding sessionConnectPanel");
            SetSessionConnectPanel(false);
            SetLobbyButtonsInteractable(false);
        }
        else
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartHost failed");
        }
    }

    public void StartGameLoading()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartGameLoading hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            return;
        }

        SetButtonsInteractable(false, gameStartButton);
        _sessionLauncher.StartGameLoading();
    }

    public void ToggleReady()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.ToggleReady hasLobbyUIController={_lobbyUIController != null}");
        if (_lobbyUIController == null)
        {
            _lobbyUIController = LobbyUIController.Active;
        }

        if (_lobbyUIController == null)
        {
            WarnMissingReference(nameof(LobbyUIController));
            return;
        }

        _lobbyUIController.ToggleLocalReady();
    }

    public void StartClient()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartClient hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            return;
        }

        if (_sessionLauncher.StartClient())
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartClient succeeded, hiding sessionConnectPanel");
            SetSessionConnectPanel(false);
            SetLobbyButtonsInteractable(false);
        }
        else
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartClient failed");
        }
    }

    public void SetJgsConnectionData()
    {
        SetConnectionData(jgsIp);
    }

    public void SetLjwConnectionData()
    {
        SetConnectionData(ljwIp);
    }

    public void SetLehConnectionData()
    {
        SetConnectionData(lehIp);
    }

    public void SetKthConnectionData()
    {
        SetConnectionData(kthIp);
    }

    public void SetKmkConnectionData()
    {
        SetConnectionData(kmkIp);
    }

    private void SetConnectionData(string ip)
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.SetConnectionData ip={ip} hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            return;
        }

        if (string.IsNullOrWhiteSpace(ip))
        {
            Debug.LogWarning($"{nameof(LobbySceneManager)} connection ip is empty.");
            return;
        }

        _sessionLauncher.OnSetConnectionData(ip);
    }

    private void ResolveSceneReferences()
    {
        startHostButton ??= FindButton("StartHost");
        startClientButton ??= FindButton("StartClient");
        jgsButton ??= FindButton("JGS");
        ljwButton ??= FindButton("LJW");
        lehButton ??= FindButton("LEH");
        kthButton ??= FindButton("KTH");
        kmkButton ??= FindButton("KMK");
        gameStartButton ??= FindButton("GameStart");
        readyButton ??= FindButton("Ready");

        if (sessionConnectPanel == null)
        {
            sessionConnectPanel = FindInActiveScene("Pannel_SessionConnect");
        }

        _lobbyUIController ??= FindFirstObjectByType<LobbyUIController>();

        WarnMissingButton(startHostButton, nameof(startHostButton));
        WarnMissingButton(startClientButton, nameof(startClientButton));
        WarnMissingButton(jgsButton, nameof(jgsButton));
        WarnMissingButton(ljwButton, nameof(ljwButton));
        WarnMissingButton(lehButton, nameof(lehButton));
        WarnMissingButton(kthButton, nameof(kthButton));
        WarnMissingButton(kmkButton, nameof(kmkButton));
        WarnMissingButton(gameStartButton, nameof(gameStartButton));
        WarnMissingButton(readyButton, nameof(readyButton));

        if (sessionConnectPanel == null)
        {
            WarnMissingReference(nameof(sessionConnectPanel));
        }

        if (_lobbyUIController == null)
        {
            WarnMissingReference(nameof(LobbyUIController));
        }
    }

    private void BindButtons()
    {
        BindButton(startHostButton, StartHost);
        BindButton(startClientButton, StartClient);
        BindButton(jgsButton, SetJgsConnectionData);
        BindButton(ljwButton, SetLjwConnectionData);
        BindButton(lehButton, SetLehConnectionData);
        BindButton(kthButton, SetKthConnectionData);
        BindButton(kmkButton, SetKmkConnectionData);
        BindButton(gameStartButton, StartGameLoading);
        BindButton(readyButton, ToggleReady);
    }

    private void SetLobbyButtonsInteractable(bool interactable)
    {
        SetButtonsInteractable(
            interactable,
            startHostButton,
            startClientButton,
            jgsButton,
            ljwButton,
            lehButton,
            kthButton,
            kmkButton);
    }

    private void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void SetSessionConnectPanel(bool active)
    {
        if (sessionConnectPanel != null)
        {
            sessionConnectPanel.SetActive(active);
        }
        Debug.Log($"[SceneFlow] LobbySceneManager.SetSessionConnectPanel active={active} hasPanel={sessionConnectPanel != null}");
    }

    private void WarnMissingButton(Button button, string referenceName)
    {
        if (button == null)
        {
            WarnMissingReference(referenceName);
        }
    }
}
