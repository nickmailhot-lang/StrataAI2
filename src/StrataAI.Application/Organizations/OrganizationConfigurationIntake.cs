namespace StrataAI.Application.Organizations;

public sealed record ConfigurationIntakeBoard(Guid Id, string Name, long Version);
public sealed record ConfigurationIntakeList(Guid Id, string Name, long Version, string Rank);
public sealed record ConfigurationIntakeListSource(ConfigurationIntakeBoard Board,
    IReadOnlyList<ConfigurationIntakeList> Items);
public sealed record ConfigurationIntakeListPage(Guid OrganizationId, ConfigurationIntakeBoard Board,
    IReadOnlyList<ConfigurationIntakeList> Items, string? NextAfterRank);

public interface IOrganizationConfigurationIntakeStore
{
    // Caller owns the tenant command and current configuration-administrator
    // admission. Return at most 51 active Lists from the actual owning Board.
    Task<ConfigurationIntakeListSource?> ReadListsAsync(Guid organization, Guid board,
        string? afterRank, CancellationToken ct);
}
