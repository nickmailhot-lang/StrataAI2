using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IOrganizationConfigurationIntakeStore
{
    public Task<ConfigurationIntakeListSource?> ReadListsAsync(Guid organization, Guid board,
        string? afterRank, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !transactionScope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("Configuration intake requires an owning Organization transaction.");
        lock (_sync)
        {
            if (!_boards.TryGetValue(board, out var selected) || selected.OrganizationId != organization
                || selected.LifecycleState != BoardLifecycleState.Active || !Enum.IsDefined(selected.Visibility))
                return Task.FromResult<ConfigurationIntakeListSource?>(null);
            var items = _lists.Values.Where(row => row.OrganizationId == organization && row.BoardId == board
                && row.LifecycleState == WorkItemLifecycleState.Active
                && (afterRank is null || string.CompareOrdinal(row.Rank, afterRank) > 0))
                .OrderBy(row => row.Rank, StringComparer.Ordinal).Take(51)
                .Select(row => new ConfigurationIntakeList(row.Id, row.Name, row.Version, row.Rank)).ToArray();
            return Task.FromResult<ConfigurationIntakeListSource?>(new(new(selected.Id, selected.Name, selected.Version), items));
        }
    }
}
