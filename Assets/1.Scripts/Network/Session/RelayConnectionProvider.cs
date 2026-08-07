using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public sealed class RelayConnectionProvider : ISessionConnectionProvider
{
    private const int MaxClientConnections = 2;
    private const string ConnectionType = "dtls";

    private readonly UnityTransport _transport;

    public RelayConnectionProvider(UnityTransport transport)
    {
        _transport = transport;
    }

    public SessionConnectionMode Mode => SessionConnectionMode.UnityRelay;

    public bool IsAvailable(out string unavailableReason)
    {
        if (_transport == null)
        {
            unavailableReason = "Relay 연결에 필요한 UnityTransport를 찾을 수 없습니다.";
            return false;
        }

        return UnityServicesBootstrap.IsAvailable(out unavailableReason);
    }

    public async Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var allocation = await RelayService.Instance.CreateAllocationAsync(MaxClientConnections);
            cancellationToken.ThrowIfCancellationRequested();

            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            cancellationToken.ThrowIfCancellationRequested();

            _transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));
            return SessionStartResult.Succeeded(joinCode);
        }
        catch (RelayServiceException exception)
        {
            return SessionStartResult.Failed(BuildRelayFailureReason(exception, false));
        }
    }

    public async Task<SessionStartResult> PrepareClientAsync(
        string joinInput,
        CancellationToken cancellationToken)
    {
        var joinCode = joinInput?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(joinCode))
        {
            return SessionStartResult.Failed("Relay 조인코드를 입력하세요.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            cancellationToken.ThrowIfCancellationRequested();

            _transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));
            return SessionStartResult.Succeeded();
        }
        catch (RelayServiceException exception)
        {
            return SessionStartResult.Failed(BuildRelayFailureReason(exception, true));
        }
    }

    private static string BuildRelayFailureReason(RelayServiceException exception, bool joining)
    {
        switch (exception.Reason)
        {
            case RelayExceptionReason.JoinCodeNotFound:
            case RelayExceptionReason.AllocationNotFound:
            case RelayExceptionReason.EntityNotFound:
                return "Relay 조인코드가 잘못되었거나 만료되었습니다.";
            case RelayExceptionReason.InactiveProject:
                return "Unity Dashboard에서 이 프로젝트의 Relay 서비스를 활성화하세요.";
            case RelayExceptionReason.Unauthorized:
            case RelayExceptionReason.Forbidden:
                return "Relay 인증 또는 프로젝트 접근 권한이 없습니다.";
            case RelayExceptionReason.PaymentRequired:
                return "Relay 사용 한도 또는 결제 설정을 확인하세요.";
            case RelayExceptionReason.RateLimited:
                return "Relay 요청 한도를 초과했습니다. 잠시 후 다시 시도하세요.";
            case RelayExceptionReason.NoSuitableRelay:
            case RelayExceptionReason.ServiceUnavailable:
            case RelayExceptionReason.GatewayTimeout:
                return "사용 가능한 Relay 서버가 없습니다. 잠시 후 다시 시도하세요.";
            case RelayExceptionReason.InvalidRequest when joining:
            case RelayExceptionReason.Conflict when joining:
                return $"Relay 방에 참가할 수 없습니다. 조인코드와 방 정원을 확인하세요. ({exception.Message})";
            default:
                var action = joining ? "참가" : "생성";
                return $"Relay 방 {action}에 실패했습니다: {exception.Message}";
        }
    }
}
