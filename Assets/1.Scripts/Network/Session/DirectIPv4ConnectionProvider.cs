using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;

public sealed class DirectIPv4ConnectionProvider : ISessionConnectionProvider
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

    private readonly UnityTransport _transport;
    private string _address;
    private ushort _port;

    public DirectIPv4ConnectionProvider(UnityTransport transport)
    {
        _transport = transport;
        _address = transport.ConnectionData.Address;
        _port = transport.ConnectionData.Port;
    }

    public SessionConnectionMode Mode => SessionConnectionMode.DirectIPv4;

    public bool IsAvailable(out string unavailableReason)
    {
        unavailableReason = string.Empty;
        return true;
    }

    public Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SessionStartResult.Failed("세션 시작이 취소되었습니다."));
        }

        ApplyConnectionData(_address, _port);
        return Task.FromResult(SessionStartResult.Succeeded(BuildShareCode()));
    }

    public Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SessionStartResult.Failed("세션 시작이 취소되었습니다."));
        }

        if (!TryParseJoinInput(joinInput, out var address, out var port, out var failureReason))
        {
            return Task.FromResult(SessionStartResult.Failed(failureReason));
        }

        ApplyConnectionData(address, port);
        return Task.FromResult(SessionStartResult.Succeeded());
    }

    internal void SetConnectionData(string address, ushort port)
    {
        ApplyConnectionData(address, port);
    }

    private void ApplyConnectionData(string address, ushort port)
    {
        _address = address;
        _port = port;
        _transport.SetConnectionData(address, port, ListenAllInterfaces);
    }

    private bool TryParseJoinInput(
        string joinInput,
        out string address,
        out ushort port,
        out string failureReason)
    {
        address = string.Empty;
        port = _port;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(joinInput))
        {
            failureReason = "IP를 입력하세요.";
            return false;
        }

        var endpoint = joinInput.Trim();
        var separatorIndex = endpoint.LastIndexOf(':');
        if (separatorIndex >= 0)
        {
            address = endpoint.Substring(0, separatorIndex);
            var portText = endpoint.Substring(separatorIndex + 1);
            if (!ushort.TryParse(portText, out port) || port == 0)
            {
                failureReason = $"Port는 1~65535 범위의 숫자여야 합니다: {portText}";
                return false;
            }
        }
        else
        {
            address = endpoint;
        }

        if (!IPAddress.TryParse(address, out var parsedAddress) ||
            parsedAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            failureReason = $"IPv4 형식이 올바르지 않습니다: {address}";
            return false;
        }

        return true;
    }

    private string BuildShareCode()
    {
        return $"{_transport.ConnectionData.Address}:{_transport.ConnectionData.Port}";
    }
}
