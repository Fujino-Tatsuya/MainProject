using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    /// <summary>
    /// 서버가 들을 주소. 모든 NIC(0.0.0.0)에서 듣는다.
    /// UnityTransport.SetConnectionData 는 3번째 인자를 생략하면 <c>ServerListenAddress = ip</c> 로 채운다
    /// (NGO 2.12.0 UnityTransport.cs). 그래서 2인자로 호출하면 호스트가 입력칸의 IP 를 그대로
    /// 바인딩 주소로 쓰고, 기본값 127.0.0.1 이면 루프백에만 바인딩되어 다른 PC 에서 접속이 불가능하다.
    /// 접속 대상(Address)만 입력값을 쓰고 바인딩은 항상 전체 인터페이스로 고정한다.
    /// 클라이언트는 ServerListenAddress 를 쓰지 않으므로 영향이 없다.
    /// </summary>
    private const string ListenAllInterfaces = "0.0.0.0";

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
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.OnSetConnectionData ip={ip} port={port} listen={ListenAllInterfaces}");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, port, ListenAllInterfaces);
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
