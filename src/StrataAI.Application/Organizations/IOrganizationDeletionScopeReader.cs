namespace StrataAI.Application.Organizations;

// Routing hints only. Execution must still acquire an explicit tenant session
// and the normal queue lease; discovery never authorizes graph mutations.
public interface IOrganizationDeletionScopeReader
{
    Task<IReadOnlyList<Guid>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken);
}
