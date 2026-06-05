using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    NetworkManager _networkManager;
    public GameObject ButtonGroup;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();

        if(ButtonGroup == null)
        {
            ButtonGroup = GameObject.Find("TempButtonGroup");
        }
    }

    public void StartHost()
    {
        _networkManager.StartHost();
        ButtonGroup.SetActive(false);
    }

    public void StartClient()
    {
        _networkManager.StartClient();
        ButtonGroup.SetActive(false);
    }

    public void StartServer()
    {
        _networkManager.StartServer();
        ButtonGroup.SetActive(false);
    }

    public void OnSetConnectionData(string ip)
    {
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, 7777);
    }

    private void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }
}
