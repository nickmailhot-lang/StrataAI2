using StrataAI.Application.Identity;
using System.Text.Json.Serialization;

namespace StrataAI.Application.Onboarding;

// Private routing bindings belong inside protected cursors, never event payloads.
public sealed record InvitationRecipientCursorBinding(Guid ActorId, string EmailNormalized, long AccountVersion,
    long AuthorityRevision = 0);
public interface IInvitationRecipientCursorCodec
{
    string Encode(InvitationRecipientCursorBinding binding, long position);
    bool TryDecode(InvitationRecipientCursorBinding binding, string token, out long position);
    bool TryDecodePriorAuthority(InvitationRecipientCursorBinding binding, string token, out long position);
}
public sealed record InvitationRecipientEvent(Guid EventId, string EventType,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long Sequence, DateTimeOffset CreatedAt);
public sealed record InvitationRecipientEventWindow(long Position, bool HasMore, bool ResetRequired,
    IReadOnlyList<InvitationRecipientEvent> Events)
{
    public static InvitationRecipientEventWindow Build(long since, long head, int limit, IReadOnlyList<InvitationRecipientEvent> rows)
    {
        if (since < 0 || head < 0 || limit is < 1 or > 100 || rows.Count > limit + 1)
            throw new ArgumentOutOfRangeException(nameof(since));
        if (since > head) return new(0, false, true, []);
        long position = since;
        List<InvitationRecipientEvent> events = [];
        HashSet<Guid> identities = [];
        foreach (var row in rows)
        {
            if (position == long.MaxValue || row.Sequence != position + 1 || row.Sequence > head
                || row.EventId == Guid.Empty || !identities.Add(row.EventId)
                || row.EventType is not ("INVITATION_CREATED" or "INVITATION_ACCEPTED" or "INVITATION_REVOKED")
                || row.CreatedAt == default)
                return new(0, false, true, []);
            if (events.Count == limit) return new(position, true, false, events);
            events.Add(row); position = row.Sequence;
        }
        return position < head ? new(0, false, true, []) : new(position, false, false, events);
    }
}
public interface IInvitationRecipientEventReader
{
    Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actorId, CancellationToken cancellationToken);
    Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken cancellationToken);
    Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit,
        CancellationToken cancellationToken);
}
// Only a verified reader binding mismatch uses this marker. Storage failures
// and cancellation keep their original exception paths.
public sealed class InvitationRecipientAdmissionChangedException()
    : InvalidOperationException("Recipient account admission changed.");

public sealed record InvitationRecipientSyncPage(string Cursor, bool HasMore, bool ResetRequired,
    IReadOnlyList<InvitationRecipientEvent> Events);

// Transport must use the owning identity transaction wrapper below.
public sealed class InvitationRecipientSynchronizationService(IInvitationRecipientEventReader reader,
    IInvitationRecipientCursorCodec cursors)
{
    // A transport-owned checkpoint may recover an authority-only change that
    // invalidated a read or its final delivery guard. Bootstrap remains empty;
    // signed actor/email/account binding and older authority are still required.
    public async Task<IdentityOperation<InvitationRecipientSyncPage?>> RecoverLiveReadAsync(Guid actorId,
        string prior, CancellationToken cancellationToken = default)
    {
        var reset = await ReadAsync(actorId, null, cancellationToken: cancellationToken);
        if (!reset.Succeeded || reset.Value is null)
            return IdentityOperation<InvitationRecipientSyncPage?>.Failure(reset.ErrorCode ?? "account_unavailable");
        var recovered = await RecoverLiveCheckpointAsync(actorId, prior, reset.Value.Cursor, cancellationToken);
        if (!recovered.Succeeded)
            return IdentityOperation<InvitationRecipientSyncPage?>.Failure(recovered.ErrorCode ?? "account_unavailable");
        return IdentityOperation<InvitationRecipientSyncPage?>.Success(recovered.Value is { } current
            ? reset.Value with { Cursor = current } : null);
    }
    // Only transport-owned, previously delivered checkpoints may use this
    // continuity path. Ordinary HTTP/bootstrap cursor resets remain at head.
    public async Task<IdentityOperation<string?>> RecoverLiveCheckpointAsync(Guid actorId, string prior,
        string reset, CancellationToken cancellationToken = default)
    {
        try { return await RecoverLiveCheckpointCoreAsync(actorId, prior, reset, cancellationToken); }
        catch (InvitationRecipientAdmissionChangedException)
        { return IdentityOperation<string?>.Failure("account_unavailable"); }
    }
    private async Task<IdentityOperation<string?>> RecoverLiveCheckpointCoreAsync(Guid actorId, string prior,
        string reset, CancellationToken cancellationToken = default)
    {
        var scope = await reader.GetScopeAsync(actorId, cancellationToken);
        if (actorId == Guid.Empty || scope is null || scope.ActorId != actorId)
            return IdentityOperation<string?>.Failure("account_unavailable");
        if (!cursors.TryDecode(scope, reset, out var resetPosition)
            || !cursors.TryDecodePriorAuthority(scope, prior, out var priorPosition)
            || priorPosition > resetPosition)
            return IdentityOperation<string?>.Success(null);
        var head = await reader.GetHeadAsync(scope, cancellationToken);
        if (await reader.GetScopeAsync(actorId, cancellationToken) != scope)
            return IdentityOperation<string?>.Failure("account_unavailable");
        if (resetPosition > head) return IdentityOperation<string?>.Success(null);
        return IdentityOperation<string?>.Success(cursors.Encode(scope, priorPosition));
    }
    public async Task<IdentityOperation<bool>> IsCursorCurrentAsync(Guid actorId, string cursor,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) return IdentityOperation<bool>.Failure("account_unavailable");
        var scope = await reader.GetScopeAsync(actorId, cancellationToken);
        if (scope is null || scope.ActorId != actorId) return IdentityOperation<bool>.Failure("account_unavailable");
        return IdentityOperation<bool>.Success(cursors.TryDecode(scope, cursor, out _));
    }
    public async Task<IdentityOperation<InvitationRecipientSyncPage>> ReadAsync(Guid actorId, string? cursor,
        int limit = 50, CancellationToken cancellationToken = default)
    {
        try { return await ReadCoreAsync(actorId, cursor, limit, cancellationToken); }
        catch (InvitationRecipientAdmissionChangedException)
        { return IdentityOperation<InvitationRecipientSyncPage>.Failure("account_unavailable"); }
    }
    private async Task<IdentityOperation<InvitationRecipientSyncPage>> ReadCoreAsync(Guid actorId, string? cursor,
        int limit = 50, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) return IdentityOperation<InvitationRecipientSyncPage>.Failure("account_unavailable");
        if (limit is < 1 or > 100) return IdentityOperation<InvitationRecipientSyncPage>.Failure("invalid_sync_limit");
        var scope = await reader.GetScopeAsync(actorId, cancellationToken);
        if (scope is null || scope.ActorId != actorId || scope.AccountVersion < 1 || scope.AuthorityRevision < 0 || string.IsNullOrWhiteSpace(scope.EmailNormalized))
            return IdentityOperation<InvitationRecipientSyncPage>.Failure("account_unavailable");
        InvitationRecipientEventWindow page;
        if (cursor is null || !cursors.TryDecode(scope, cursor, out var position))
            page = new(await reader.GetHeadAsync(scope, cancellationToken), false, true, []);
        else
        {
            page = await reader.ReadAsync(scope, position, limit, cancellationToken);
            if (page.ResetRequired) page = new(await reader.GetHeadAsync(scope, cancellationToken), false, true, []);
        }
        var current = await reader.GetScopeAsync(actorId, cancellationToken);
        if (current != scope) return IdentityOperation<InvitationRecipientSyncPage>.Failure("account_unavailable");
        if (page.Position < 0 || page.Events.Count > limit
            || page.ResetRequired && (page.HasMore || page.Events.Count != 0)
            || page.Events.Select(e => e.EventId).Distinct().Count() != page.Events.Count
            || page.Events.Any(e => e.EventId == Guid.Empty || e.Sequence < 1 || e.CreatedAt == default
                || e.EventType is not ("INVITATION_CREATED" or "INVITATION_ACCEPTED" or "INVITATION_REVOKED"))
            || page.Events.Count > 0 && (page.Events[^1].Sequence != page.Position
                || page.Events.Zip(page.Events.Skip(1)).Any(pair => pair.First.Sequence == long.MaxValue
                    || pair.Second.Sequence != pair.First.Sequence + 1)))
            return IdentityOperation<InvitationRecipientSyncPage>.Failure("invitation_sync_unavailable");
        return IdentityOperation<InvitationRecipientSyncPage>.Success(new(cursors.Encode(current, page.Position),
            page.HasMore, page.ResetRequired, page.Events));
    }
}

public sealed class TransactionalInvitationRecipientSynchronization(InvitationRecipientSynchronizationService replay,
    IIdentityUnitOfWork transactions, ICommandActorAuthorization actors)
{
    public Task<IdentityOperation<InvitationRecipientSyncPage?>> RecoverLiveReadAsync(Guid actorId, string prior,
        CancellationToken cancellationToken = default)
        => transactions.ExecuteObservationAsync(actorId, null, async () =>
        {
            if (!await actors.VerifyAsync(actorId, cancellationToken))
                return IdentityOperation<InvitationRecipientSyncPage?>.Failure("session_unavailable");
            var result = await replay.RecoverLiveReadAsync(actorId, prior, cancellationToken);
            return await actors.VerifyAsync(actorId, cancellationToken) ? result
                : IdentityOperation<InvitationRecipientSyncPage?>.Failure("session_unavailable");
        }, cancellationToken);
    public Task<IdentityOperation<string?>> RecoverLiveCheckpointAsync(Guid actorId, string prior, string reset,
        CancellationToken cancellationToken = default)
        => transactions.ExecuteObservationAsync(actorId, null, async () =>
        {
            if (!await actors.VerifyAsync(actorId, cancellationToken)) return IdentityOperation<string?>.Failure("session_unavailable");
            var result = await replay.RecoverLiveCheckpointAsync(actorId, prior, reset, cancellationToken);
            return await actors.VerifyAsync(actorId, cancellationToken) ? result : IdentityOperation<string?>.Failure("session_unavailable");
        }, cancellationToken);
    public Task<IdentityOperation<bool>> IsCursorCurrentAsync(Guid actorId, string cursor, CancellationToken cancellationToken = default)
        => transactions.ExecuteObservationAsync(actorId, null, async () =>
        {
            if (!await actors.VerifyAsync(actorId, cancellationToken)) return IdentityOperation<bool>.Failure("session_unavailable");
            var result = await replay.IsCursorCurrentAsync(actorId, cursor, cancellationToken);
            return await actors.VerifyAsync(actorId, cancellationToken) ? result : IdentityOperation<bool>.Failure("session_unavailable");
        }, cancellationToken);
    public Task<IdentityOperation<InvitationRecipientSyncPage>> ReadAsync(Guid actorId, string? cursor,
        int limit = 50, CancellationToken cancellationToken = default)
        => transactions.ExecuteObservationAsync(actorId, null, async () =>
        {
            if (!await actors.VerifyAsync(actorId, cancellationToken))
                return IdentityOperation<InvitationRecipientSyncPage>.Failure("session_unavailable");
            var result = await replay.ReadAsync(actorId, cursor, limit, cancellationToken);
            if (!await actors.VerifyAsync(actorId, cancellationToken))
                return IdentityOperation<InvitationRecipientSyncPage>.Failure("session_unavailable");
            return result;
        }, cancellationToken);
}
