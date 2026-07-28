using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    NetworkManager _networkManager;
    NetworkLoadingFlowController _loadingFlowController;
    [SerializeField] private GameObject defaultPlayerPrefab;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        _loadingFlowController = GetComponent<NetworkLoadingFlowController>();
        _loadingFlowController?.SetDefaultPlayerPrefab(defaultPlayerPrefab);
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.Awake hasNetworkManager={_networkManager != null} hasLoadingFlow={_loadingFlowController != null}");
    }

    public bool StartHost()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost before listening={_networkManager.IsListening}");
        if (_networkManager.StartHost())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost success localClientId={_networkManager.LocalClientId}");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartHost failed");
        return false;
    }

    public bool StartClient()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient before listening={_networkManager.IsListening}");
        if (_networkManager.StartClient())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient success localClientId={_networkManager.LocalClientId}");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartClient failed");
        return false;
    }

    public bool StartServer()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartServer before listening={_networkManager.IsListening}");
        if (_networkManager.StartServer())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer success");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer failed");
        return false;
    }

    public void OnSetConnectionData(string ip)
    {
        OnSetConnectionData(ip, 7777);
    }

    public void OnSetConnectionData(string ip, ushort port)
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.OnSetConnectionData ip={ip} port={port}");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, port);
    }

    public void StartGameLoading()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartGameLoading hasFlow={_loadingFlowController != null}");
        RegisterLoadingFlowCallbacks();
        _loadingFlowController?.StartGameLoading();
    }

    private void RegisterLoadingFlowCallbacks()
    {
        if (_loadingFlowController == null)
        {
            _loadingFlowController = GetComponent<NetworkLoadingFlowController>();
        }

        if (_loadingFlowController == null)
        {
            _loadingFlowController = gameObject.AddComponent<NetworkLoadingFlowController>();
            Debug.Log("[SceneFlow] NetworkSessionLauncher.RegisterLoadingFlowCallbacks added NetworkLoadingFlowController");
        }

        _loadingFlowController?.SetDefaultPlayerPrefab(defaultPlayerPrefab);
        _loadingFlowController?.RegisterNetworkCallbacks();
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.RegisterLoadingFlowCallbacks done hasFlow={_loadingFlowController != null}");
    }

    private void OnApplicationQuit()
    {
        Debug.Log("[SceneFlow] NetworkSessionLauncher.OnApplicationQuit");
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }
}
