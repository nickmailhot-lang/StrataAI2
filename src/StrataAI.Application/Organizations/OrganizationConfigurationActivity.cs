using System.Collections.ObjectModel;
using StrataAI.Application.Identity;

namespace StrataAI.Application.Organizations;

// A configuration change is Organization activity. It has no source Board;
// an intake Board is a configured relationship, never its disclosure audience.
public sealed record OrganizationConfigurationActivityItem(Guid EventId, Guid OrganizationId,
    Guid ActorId, long Version, string CorrelationId, DateTimeOffset CreatedAt,
    string ActorLabel, string ActingCapacity)
{
    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    public Guid? BoardId => null;
    public string EventType => "ORGANIZATION_CONFIGURATION_CHANGED";
    public string EntityType => "OrganizationConfiguration";
    public Guid EntityId => OrganizationId;
    public IReadOnlyDictionary<string, object?> Metadata => Empty;
}
public sealed record OrganizationConfigurationActivityPage(Guid OrganizationId,
    IReadOnlyList<OrganizationConfigurationActivityItem> Items, string? NextCursor);
public interface IOrganizationConfigurationActivityCursorCodec
{
    string Encode(OrganizationConfigurationCursorBinding binding, long beforeVersion);
    bool TryDecode(OrganizationConfigurationCursorBinding binding, string token, out long beforeVersion);
}

public sealed class OrganizationConfigurationActivityService(IOrganizationConfigurationStore history,
    IOrganizationConfigurationEventReader admission, IOrganizationConfigurationActivityCursorCodec cursors,
    IOrganizationUnitOfWork unit, ICommandActorAuthorization actors,
    IOrganizationConfigurationAttributionReader attribution)
{
    public Task<OrganizationOperation<OrganizationConfigurationActivityPage>> ReadAsync(Guid organization,
        Guid actor, string? cursor, CancellationToken ct = default)
    {
        if (organization == Guid.Empty || actor == Guid.Empty)
            return Task.FromResult(OrganizationOperation<OrganizationConfigurationActivityPage>.Failure("organization_not_found"));
        return unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            var scope = await Scope(organization, actor, ct);
            if (scope is null) return Denied();
            long? before = null;
            if (cursor is not null)
            {
                if (cursor.Length is 0 or > 4096 || !cursors.TryDecode(scope, cursor, out var position) || position < 1)
                    return OrganizationOperation<OrganizationConfigurationActivityPage>.Failure("invalid_configuration_activity_cursor");
                before = position;
            }
            var head = await admission.GetHeadAsync(organization, ct);
            if (head < 0 || before is { } boundary && boundary - 1 > head) return Unavailable();
            var rows = await history.ReadHistoryAsync(organization, before, ct);
            if (rows.Count > 51 || before is null && head > 0 && rows.Count == 0) return Unavailable();
            List<OrganizationConfigurationActivityItem> items = [];
            var ids = new HashSet<Guid>(); long? previous = before;
            foreach (var source in rows)
            {
                // Never repair a malformed immutable fact with current account,
                // Organization, configuration values or a publication timestamp.
                if (source.OrganizationId != organization || source.ActorId == Guid.Empty || source.EventId == Guid.Empty
                    || source.Version < 1 || previous is { } version && source.Version != version - 1
                    || previous is null && source.Version != head
                    || !ids.Add(source.EventId) || string.IsNullOrWhiteSpace(source.CorrelationId)
                    || source.CorrelationId.Length > 256 || source.CorrelationId.Any(char.IsControl)
                    || source.UpdatedAt.Offset != TimeSpan.Zero || source.CreatedAt.Offset != TimeSpan.Zero
                    || source.UpdatedAt < source.CreatedAt || source.OrganizationVersion < 1)
                    return Unavailable();
                previous = source.Version;
            }
            var captured = await attribution.ReadAttributionAsync(organization, rows.Select(row => row.EventId).ToArray(), ct);
            if (captured.Count != rows.Count || captured.Any(row => !row.Valid || row.OrganizationId != organization)
                || captured.Select(row => row.EventId).Distinct().Count() != captured.Count) return Unavailable();
            var byEvent = captured.ToDictionary(row => row.EventId);
            foreach (var source in rows)
            {
                if (!byEvent.TryGetValue(source.EventId, out var fact) || fact.ActorId != source.ActorId || fact.Version != source.Version)
                    return Unavailable();
            }
            foreach (var source in rows.Take(50))
            {
                var fact = byEvent[source.EventId];
                items.Add(new(source.EventId, organization, source.ActorId, source.Version,
                    source.CorrelationId, source.UpdatedAt, fact.ActorLabel, fact.ActingCapacity));
            }
            // Current private admission is checked after the history/attribution IO. Role,
            // lifecycle or account withdrawal cannot leave a readable old page.
            var current = await Scope(organization, actor, ct);
            if (current is null) return Denied();
            if (current != scope) return Unavailable();
            var next = rows.Count > 50 ? cursors.Encode(current, items[^1].Version) : null;
            return OrganizationOperation<OrganizationConfigurationActivityPage>.Success(
                new(organization, Array.AsReadOnly(items.ToArray()), next));
        }, ct);
    }
    private async Task<OrganizationConfigurationCursorBinding?> Scope(Guid organization, Guid actor, CancellationToken ct)
    {
        if (!await actors.VerifyAsync(actor, ct)) return null;
        var scope = await admission.GetScopeAsync(organization, actor, ct); var binding = scope?.Binding;
        return scope is { OrganizationActive: true, MembershipActive: true } && binding is not null
            && binding.OrganizationId == organization && binding.ActorId == actor
            && binding.MembershipId != Guid.Empty && binding.MembershipVersion > 0
            && binding.Role is OrganizationRole.Owner or OrganizationRole.Admin ? binding : null;
    }
    private static OrganizationOperation<OrganizationConfigurationActivityPage> Denied() =>
        OrganizationOperation<OrganizationConfigurationActivityPage>.Failure("organization_not_found");
    private static OrganizationOperation<OrganizationConfigurationActivityPage> Unavailable() =>
        OrganizationOperation<OrganizationConfigurationActivityPage>.Failure("configuration_source_unavailable");
}
