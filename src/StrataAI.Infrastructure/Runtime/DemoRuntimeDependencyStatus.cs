using StrataAI.Application.Runtime;

namespace StrataAI.Infrastructure.Runtime;

internal sealed class DemoRuntimeDependencyStatus : IRuntimeDependencyStatus
{
    public RuntimeMode Mode => RuntimeMode.Demo;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
