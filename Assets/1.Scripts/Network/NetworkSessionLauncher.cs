using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    // 서버는 항상 모든 NIC(0.0.0.0)에서 수신해야 LAN 클라가 붙을 수 있다.
    // UnityTransport.SetConnectionData(ip, port)는 listenAddress를 생략하면 ServerListenAddress = ip 로 덮어써서
    // 127.0.0.1을 입력한 호스트가 루프백에만 바인딩되는 문제가 생긴다.
    private const string ServerListenAny = "0.0.0.0";

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
        DumpNetworkDiagnostics("StartHost/before");
        if (_networkManager.StartHost())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost success localClientId={_networkManager.LocalClientId}");
            DumpNetworkDiagnostics("StartHost/after");
            return true;
        }

        Debug.LogError("[SceneFlow] NetworkSessionLauncher.StartHost failed");
        DumpNetworkDiagnostics("StartHost/failed");
        return false;
    }

    public bool StartClient()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient before listening={_networkManager.IsListening}");
        DumpNetworkDiagnostics("StartClient/before");
        if (_networkManager.StartClient())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient success localClientId={_networkManager.LocalClientId}");
            return true;
        }

        Debug.LogError("[SceneFlow] NetworkSessionLauncher.StartClient failed");
        DumpNetworkDiagnostics("StartClient/failed");
        return false;
    }

    public bool StartServer()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartServer before listening={_networkManager.IsListening}");
        DumpNetworkDiagnostics("StartServer/before");
        if (_networkManager.StartServer())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer success");
            DumpNetworkDiagnostics("StartServer/after");
            return true;
        }

        Debug.LogError("[SceneFlow] NetworkSessionLauncher.StartServer failed");
        DumpNetworkDiagnostics("StartServer/failed");
        return false;
    }

    public void OnSetConnectionData(string ip)
    {
        OnSetConnectionData(ip, 7777);
    }

    public void OnSetConnectionData(string ip, ushort port)
    {
        var transport = ResolveTransport();
        if (transport == null)
        {
            Debug.LogError($"[SceneFlow] NetworkSessionLauncher.OnSetConnectionData UnityTransport 없음 ip={ip} port={port}");
            return;
        }

        // listenAddress를 명시하지 않으면 ServerListenAddress가 ip로 덮어써진다 → 호스트가 루프백에만 바인딩됨.
        transport.SetConnectionData(ip, port, ServerListenAny);
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.OnSetConnectionData ip={ip} port={port} listen={ServerListenAny}");
        DumpNetworkDiagnostics("OnSetConnectionData");
    }

    private UnityTransport ResolveTransport()
    {
        if (_networkManager == null)
        {
            _networkManager = NetworkManager.Singleton;
        }

        return _networkManager != null ? _networkManager.GetComponent<UnityTransport>() : null;
    }

    // LAN 연결 실패 원인 추적용 덤프. 호스트/클라 양쪽 Editor.log에 그대로 남는다.
    public void DumpNetworkDiagnostics(string phase)
    {
        var nm = _networkManager != null ? _networkManager : NetworkManager.Singleton;
        var transport = nm != null ? nm.GetComponent<UnityTransport>() : null;

        var sb = new StringBuilder();
        sb.AppendLine($"[NetDiag] ===== {phase} =====");

        if (nm == null)
        {
            sb.AppendLine("[NetDiag] NetworkManager.Singleton == null");
            Debug.LogWarning(sb.ToString());
            return;
        }

        sb.AppendLine($"[NetDiag] NM listening={nm.IsListening} host={nm.IsHost} server={nm.IsServer} client={nm.IsClient} connected={nm.IsConnectedClient} localClientId={nm.LocalClientId} logLevel={nm.LogLevel}");
        sb.AppendLine($"[NetDiag] NM shutdownInProgress={nm.ShutdownInProgress} connectedClients={(nm.IsServer ? nm.ConnectedClientsIds.Count : -1)} disconnectReason='{nm.DisconnectReason}'");

        if (transport == null)
        {
            sb.AppendLine("[NetDiag] UnityTransport 컴포넌트를 찾지 못함 (NetworkManager 프리팹 확인)");
        }
        else
        {
            var cd = transport.ConnectionData;
            sb.AppendLine($"[NetDiag] Transport protocol={transport.Protocol} useWebSockets={transport.UseWebSockets}");
            sb.AppendLine($"[NetDiag] ConnectionData Address='{cd.Address}' Port={cd.Port} ServerListenAddress='{cd.ServerListenAddress}' ClientBindPort={cd.ClientBindPort} IsIpv6={cd.IsIpv6}");
            sb.AppendLine($"[NetDiag] ServerEndPoint={cd.ServerEndPoint} (valid={cd.ServerEndPoint.IsValid})");
            sb.AppendLine($"[NetDiag] ListenEndPoint={cd.ListenEndPoint} (valid={cd.ListenEndPoint.IsValid})");
            sb.AppendLine($"[NetDiag] Timeouts connect={transport.ConnectTimeoutMS}ms attempts={transport.MaxConnectAttempts} disconnect={transport.DisconnectTimeoutMS}ms heartbeat={transport.HeartbeatTimeoutMS}ms");

            if (string.Equals(cd.ServerListenAddress, "127.0.0.1") || string.Equals(cd.ServerListenAddress, "localhost"))
            {
                sb.AppendLine("[NetDiag] ** 경고: ServerListenAddress가 루프백이라 LAN 클라이언트는 절대 접속할 수 없음 → 0.0.0.0 필요 **");
            }
        }

        sb.AppendLine($"[NetDiag] 이 PC의 LAN IPv4: {string.Join(", ", GetLocalIPv4Addresses())}");
        sb.Append($"[NetDiag] 상대에게 알려줄 주소 = 위 IPv4 중 하나 : {(transport != null ? transport.ConnectionData.Port : (ushort)0)} / UDP 인바운드 방화벽 허용 필요");
        Debug.Log(sb.ToString());
    }

    private static string[] GetLocalIPv4Addresses()
    {
        var result = new System.Collections.Generic.List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        result.Add($"{ua.Address}({nic.Name}/{nic.NetworkInterfaceType})");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            result.Add($"조회 실패: {e.Message}");
        }

        if (result.Count == 0)
        {
            result.Add("없음");
        }

        return result.ToArray();
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
