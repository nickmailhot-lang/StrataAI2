namespace StrataAI.Application.Runtime;

public interface IRuntimeDependencyStatus
{
    RuntimeMode Mode { get; }

    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
