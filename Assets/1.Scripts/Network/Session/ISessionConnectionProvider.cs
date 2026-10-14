using System.Threading;
using System.Threading.Tasks;

public interface ISessionConnectionProvider
{
    SessionConnectionMode Mode { get; }

    /// <summary>쓸 수 없으면 false + 이유(오류 코드). 쓸 수 있으면 <paramref name="unavailable"/> 는 default.</summary>
    bool IsAvailable(out SessionError unavailable);

    Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken);

    Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken cancellationToken);
}
