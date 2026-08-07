using System.Threading;
using System.Threading.Tasks;

public interface ISessionConnectionProvider
{
    SessionConnectionMode Mode { get; }

    bool IsAvailable(out string unavailableReason);

    Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken);

    Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken cancellationToken);
}
