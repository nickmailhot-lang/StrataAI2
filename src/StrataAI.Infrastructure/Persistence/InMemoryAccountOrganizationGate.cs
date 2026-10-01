namespace StrataAI.Infrastructure.Persistence;

// One DI-owned gate coordinates Demo account lifecycle with Organization grants
// and departures. Independent API hosts do not share this process-local state.
internal sealed class InMemoryAccountOrganizationGate
{
    internal SemaphoreSlim Commands { get; } = new(1, 1);
}
