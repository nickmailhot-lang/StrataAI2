using StrataAI.Application.Identity;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationConfigurationCursorBinding(Guid OrganizationId, Guid ActorId,
    Guid MembershipId, long MembershipVersion, OrganizationRole Role);
public sealed record OrganizationConfigurationReadScope(OrganizationConfigurationCursorBinding Binding,
    bool OrganizationActive, bool MembershipActive);
public interface IOrganizationConfigurationCursorCodec
{
    string Encode(OrganizationConfigurationCursorBinding binding, long position);
    bool TryDecode(OrganizationConfigurationCursorBinding binding, string token, out long position);
}
public sealed record OrganizationConfigurationEventBatch(long Head,
    IReadOnlyList<OrganizationConfigurationEventCandidate> Rows);
public interface IOrganizationConfigurationEventReader
{
    // Every operation requires an owning tenant transaction. Scope is current
    // authoritative Organization/membership state, never a client routing hint.
    Task<OrganizationConfigurationReadScope?> GetScopeAsync(Guid organization, Guid actor, CancellationToken ct);
    Task<long> GetHeadAsync(Guid organization, CancellationToken ct);
    Task<OrganizationConfigurationEventBatch> ReadAsync(Guid organization, long since, int limit, CancellationToken ct);
}
public sealed record OrganizationConfigurationSyncPage(string Cursor, bool HasMore, bool Pending,
    bool ResetRequired, IReadOnlyList<OrganizationConfigurationEvent> Events);

public sealed class OrganizationConfigurationSynchronizationService(IOrganizationConfigurationEventReader reader,
    IOrganizationConfigurationCursorCodec cursors, IOrganizationUnitOfWork unit, ICommandActorAuthorization actors)
{
    public Task<OrganizationOperation<OrganizationConfigurationSyncPage>> ReadAsync(Guid organization, Guid actor,
        string? cursor, int limit = 50, CancellationToken ct = default)
    {
        if (organization == Guid.Empty || actor == Guid.Empty)
            return Task.FromResult(OrganizationOperation<OrganizationConfigurationSyncPage>.Failure("organization_not_found"));
        if (limit is < 1 or > 100)
            return Task.FromResult(OrganizationOperation<OrganizationConfigurationSyncPage>.Failure("invalid_sync_limit"));
        if (cursor is { Length: > 4096 })
            return Task.FromResult(OrganizationOperation<OrganizationConfigurationSyncPage>.Failure("invalid_configuration_cursor"));
        return unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            var scope = await Scope(organization, actor, ct);
            if (scope is null) return Denied();
            OrganizationConfigurationEventWindow page;
            if (cursor is null || !cursors.TryDecode(scope, cursor, out var position))
                page = new(await reader.GetHeadAsync(organization, ct), false, false, true, []);
            else
            {
                if (position < 0) return Unavailable();
                var batch = await reader.ReadAsync(organization, position, limit, ct);
                if (batch.Head < 0 || batch.Rows.Count > limit + 1) return Unavailable();
                page = OrganizationConfigurationEventWindow.Build(organization, position, batch.Head, limit, batch.Rows);
                if (page.ResetRequired)
                    page = new(await reader.GetHeadAsync(organization, ct), false, false, true, []);
            }
            // Event/head IO cannot carry an old grant across a session, role,
            // membership epoch or Organization lifecycle change.
            var current = await Scope(organization, actor, ct);
            if (current is null) return Denied();
            if (current != scope || page.Position < 0) return Unavailable();
            return OrganizationOperation<OrganizationConfigurationSyncPage>.Success(new(
                cursors.Encode(current, page.Position), page.HasMore, page.Pending, page.ResetRequired,
                page.Events.Select(row => row.Source).ToArray()));
        }, ct);

        static OrganizationOperation<OrganizationConfigurationSyncPage> Denied() =>
            OrganizationOperation<OrganizationConfigurationSyncPage>.Failure("organization_not_found");
        static OrganizationOperation<OrganizationConfigurationSyncPage> Unavailable() =>
            OrganizationOperation<OrganizationConfigurationSyncPage>.Failure("configuration_sync_unavailable");
    }

    // SignalR must call this after its own session IO, immediately before yield.
    public Task<OrganizationOperation<bool>> IsCursorCurrentAsync(Guid organization, Guid actor, string cursor,
        CancellationToken ct = default)
    {
        if (organization == Guid.Empty || actor == Guid.Empty)
            return Task.FromResult(OrganizationOperation<bool>.Failure("organization_not_found"));
        return unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            var scope = await Scope(organization, actor, ct);
            if (scope is null) return OrganizationOperation<bool>.Failure("organization_not_found");
            var valid = cursors.TryDecode(scope, cursor, out var position) && position >= 0;
            var current = await Scope(organization, actor, ct);
            if (current is null) return OrganizationOperation<bool>.Failure("organization_not_found");
            return OrganizationOperation<bool>.Success(valid && current == scope);
        }, ct);
    }

    private async Task<OrganizationConfigurationCursorBinding?> Scope(Guid organization, Guid actor, CancellationToken ct)
    {
        if (!await actors.VerifyAsync(actor, ct)) return null;
        var scope = await reader.GetScopeAsync(organization, actor, ct);
        var binding = scope?.Binding;
        return scope is { OrganizationActive: true, MembershipActive: true }
            && binding is not null && binding.OrganizationId == organization && binding.ActorId == actor
            && binding.MembershipId != Guid.Empty && binding.MembershipVersion > 0
            && binding.Role is OrganizationRole.Owner or OrganizationRole.Admin ? binding : null;
    }
}
