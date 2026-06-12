using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    NetworkManager _networkManager;
    NetworkLoadingFlowController _loadingFlowController;
    public GameObject ButtonGroup;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        _loadingFlowController = GetComponent<NetworkLoadingFlowController>();

        if(ButtonGroup == null)
        {
            ButtonGroup = GameObject.Find("TempButtonGroup");
        }
    }

    public void StartHost()
    {
        if (_networkManager.StartHost())
        {
            RegisterLoadingFlowCallbacks();
            SetButtonGroupActive(false);
        }
    }

    public void StartClient()
    {
        if (_networkManager.StartClient())
        {
            RegisterLoadingFlowCallbacks();
            SetButtonGroupActive(false);
        }
    }

    public void StartServer()
    {
        if (_networkManager.StartServer())
        {
            RegisterLoadingFlowCallbacks();
            SetButtonGroupActive(false);
        }
    }

    public void OnSetConnectionData(string ip)
    {
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, 7777);
    }

    public void StartGameLoading()
    {
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
        }

        _loadingFlowController?.RegisterNetworkCallbacks();
    }

    private void SetButtonGroupActive(bool active)
    {
        if (ButtonGroup != null)
        {
            ButtonGroup.SetActive(active);
        }
    }

    private void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }
}
