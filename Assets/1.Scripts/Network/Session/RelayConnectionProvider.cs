using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public sealed class RelayConnectionProvider : ISessionConnectionProvider
{
    private const int MaxClientConnections = 2;
    private const string ConnectionType = "dtls";
    private const string DiagTag = "RelayConnectionProvider";

    private readonly UnityTransport _transport;

    public RelayConnectionProvider(UnityTransport transport)
    {
        _transport = transport;
    }

    public SessionConnectionMode Mode => SessionConnectionMode.UnityRelay;

    public bool IsAvailable(out SessionError unavailable)
    {
        if (_transport == null)
        {
            unavailable = new SessionError(ErrorCatalog.RelayTransportMissing);
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.IsAvailable", unavailable.LogText);
            return false;
        }

        var available = UnityServicesBootstrap.IsAvailable(out unavailable);
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.IsAvailable", $"available={available} error='{unavailable.LogText}'");
        return available;
    }

    public async Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.PrepareHostAsync",
            $"할당 요청 maxClients={MaxClientConnections} connectionType={ConnectionType}");

        try
        {
            var allocation = await RelayService.Instance.CreateAllocationAsync(MaxClientConnections);
            cancellationToken.ThrowIfCancellationRequested();
            LogAllocation("PrepareHostAsync", allocation.AllocationId.ToString(), allocation.Region, allocation.ServerEndpoints);

            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            cancellationToken.ThrowIfCancellationRequested();
            NetworkDiagnosticsLog.Log($"{DiagTag}.PrepareHostAsync", $"joinCode='{joinCode}'");

            _transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));
            NetworkDiagnosticsLog.Log(
                $"{DiagTag}.PrepareHostAsync", $"SetRelayServerData 적용 protocol={_transport.Protocol}");
            return SessionStartResult.Succeeded(joinCode);
        }
        catch (RelayServiceException exception)
        {
            NetworkDiagnosticsLog.LogError(
                $"{DiagTag}.PrepareHostAsync",
                $"RelayServiceException reason={exception.Reason} status={exception.ErrorCode} {exception.Message}");
            return SessionStartResult.Failed(BuildRelayFailure(exception, false));
        }
    }

    public async Task<SessionStartResult> PrepareClientAsync(
        string joinInput,
        CancellationToken cancellationToken)
    {
        var joinCode = joinInput?.Trim().ToUpperInvariant();
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.PrepareClientAsync", $"입력='{joinInput}' 정규화='{joinCode}'");

        if (string.IsNullOrEmpty(joinCode))
        {
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.PrepareClientAsync", "조인코드가 비어 있습니다.");
            return SessionStartResult.Failed(ErrorCatalog.RelayJoinCodeEmpty);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            cancellationToken.ThrowIfCancellationRequested();
            LogAllocation("PrepareClientAsync", allocation.AllocationId.ToString(), allocation.Region, allocation.ServerEndpoints);

            _transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));
            NetworkDiagnosticsLog.Log(
                $"{DiagTag}.PrepareClientAsync", $"SetRelayServerData 적용 protocol={_transport.Protocol}");
            return SessionStartResult.Succeeded();
        }
        catch (RelayServiceException exception)
        {
            NetworkDiagnosticsLog.LogError(
                $"{DiagTag}.PrepareClientAsync",
                $"RelayServiceException reason={exception.Reason} status={exception.ErrorCode} {exception.Message}");
            return SessionStartResult.Failed(BuildRelayFailure(exception, true));
        }
    }

    /// <summary>
    /// 어느 리전의 어떤 엔드포인트로 붙었는지 남긴다. 호스트와 클라의 리전이 다르면
    /// 조인코드가 옛 세션의 것이라는 뜻이라 여기서 바로 갈린다.
    /// </summary>
    private static void LogAllocation(
        string context,
        string allocationId,
        string region,
        IEnumerable<RelayServerEndpoint> endpoints)
    {
        var lines = new List<string>
        {
            $"allocationId = {allocationId}",
            $"region       = {region}",
        };

        if (endpoints != null)
        {
            foreach (var endpoint in endpoints)
            {
                lines.Add(
                    $"endpoint     = {endpoint.ConnectionType} {endpoint.Host}:{endpoint.Port} " +
                    $"secure={endpoint.Secure} network={endpoint.Network}");
            }
        }

        NetworkDiagnosticsLog.LogBlock($"{DiagTag}.{context} Allocation", lines);
    }

    /// <summary>예외 사유 → 오류 코드. 예외 메시지는 화면 문구가 아니라 상세(로그)로만 남긴다.</summary>
    private static SessionError BuildRelayFailure(RelayServiceException exception, bool joining)
    {
        var detail = $"reason={exception.Reason} status={exception.ErrorCode} {exception.Message}";
        var code = exception.Reason switch
        {
            RelayExceptionReason.JoinCodeNotFound or
            RelayExceptionReason.AllocationNotFound or
            RelayExceptionReason.EntityNotFound => ErrorCatalog.RelayJoinCodeInvalid,
            RelayExceptionReason.InactiveProject => ErrorCatalog.RelayInactiveProject,
            RelayExceptionReason.Unauthorized or
            RelayExceptionReason.Forbidden => ErrorCatalog.RelayUnauthorized,
            RelayExceptionReason.PaymentRequired => ErrorCatalog.RelayPaymentRequired,
            RelayExceptionReason.RateLimited => ErrorCatalog.RelayRateLimited,
            RelayExceptionReason.NoSuitableRelay or
            RelayExceptionReason.ServiceUnavailable or
            RelayExceptionReason.GatewayTimeout => ErrorCatalog.RelayUnavailable,
            RelayExceptionReason.InvalidRequest or
            RelayExceptionReason.Conflict when joining => ErrorCatalog.RelayJoinRejected,
            _ => joining ? ErrorCatalog.RelayJoinFailed : ErrorCatalog.RelayHostFailed,
        };
        return new SessionError(code, detail: detail);
    }
}
