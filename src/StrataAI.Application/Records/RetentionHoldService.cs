using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

public sealed record RetentionHoldReceipt(Guid ActorId, Guid Key, string Fingerprint,
    RetentionHold Result, DateTimeOffset ExpiresAt);
public sealed record RetentionHoldWrite(RetentionHold Hold, Guid ActorId, Guid MembershipId,
    long MembershipVersion, OrganizationRole ActingCapacity, Guid EventId, string CorrelationId,
    RetentionHoldReceipt Receipt);
public enum RetentionHoldWriteResult { Written, VersionConflict, KeyConflict, SourceUnavailable }
public interface IRetentionHoldStore
{
    Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct);
    Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
    // All current/history/receipt/audit writes belong to the existing transaction.
    Task<RetentionHoldWriteResult> WriteAsync(long expectedVersion, RetentionHoldWrite write, CancellationToken ct);
}
public interface IRetentionHoldScopeAuthorization
{
    // Resolve typed owning record references and confidentiality under the existing
    // Organization transaction. Elevated Organization role alone is insufficient.
    Task<bool> VerifyAsync(RetentionHoldScope scope, Guid actor, CancellationToken ct);
}

// Not registered or exposed until owning scope authorization and both atomic
// storage adapters exist. This contract never substitutes a permissive resolver.
public sealed class RetentionHoldService(IOrganizationStore organizations, IRetentionHoldStore holds,
    IRetentionHoldScopeAuthorization scopes, IOrganizationUnitOfWork unit, ICommandActorAuthorization actors, IClock clock)
{
    public Task<OrganizationOperation<RetentionHold?>> ReadAsync(Guid organization, Guid actor, Guid hold,
        CancellationToken ct = default) => Owned(organization, actor, async () =>
        {
            var current = hold == Guid.Empty ? null : await holds.ReadAsync(organization, hold, ct);
            if (current is null) return OrganizationOperation<RetentionHold?>.Failure("retention_hold_not_found");
            if (!Valid(current, organization, hold, clock.UtcNow)) return OrganizationOperation<RetentionHold?>.Failure("retention_hold_source_unavailable");
            return await scopes.VerifyAsync(Scope(current), actor, ct)
                ? OrganizationOperation<RetentionHold?>.Success(current) : OrganizationOperation<RetentionHold?>.Failure("retention_hold_not_found");
        }, ct);

    public Task<OrganizationOperation<RetentionHold>> ApplyAsync(Guid organization, Guid actor, Guid hold,
        RetentionHoldScope scope, string reason, Guid key, string correlationId, CancellationToken ct = default) =>
        ChangeAsync(organization, actor, hold, 0, scope, reason, key, correlationId, ct);

    public Task<OrganizationOperation<RetentionHold>> ReleaseAsync(Guid organization, Guid actor, Guid hold,
        long expectedVersion, Guid key, string correlationId, CancellationToken ct = default) =>
        expectedVersion < 1
            ? Owned(organization, actor, () => Task.FromResult(OrganizationOperation<RetentionHold>.Failure("version_conflict")), ct)
            : ChangeAsync(organization, actor, hold, expectedVersion, null, null, key, correlationId, ct);

    private Task<OrganizationOperation<RetentionHold>> ChangeAsync(Guid organization, Guid actor, Guid hold,
        long expectedVersion, RetentionHoldScope? scope, string? reason, Guid key, string correlationId, CancellationToken ct) =>
        Owned(organization, actor, async () =>
        {
            OrganizationOperation<RetentionHold> Fail(string code) => OrganizationOperation<RetentionHold>.Failure(code);
            if (expectedVersion == long.MaxValue) return Fail("version_conflict");
            if (key == Guid.Empty) return Fail("idempotency_key_required");
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 256 || correlationId.Any(char.IsControl)) return Fail("invalid_correlation_id");
            var now = clock.UtcNow; var at = now.AddTicks(-(now.Ticks % 10));
            if (at.Offset != TimeSpan.Zero || at > DateTimeOffset.MaxValue.AddHours(-24)) return Fail("retention_hold_source_unavailable");
            RetentionHold? proposed = null;
            if (expectedVersion == 0)
            {
                if (scope is null || scope.OrganizationId != organization || reason is null) return Fail("invalid_retention_hold");
                var change = RetentionHoldLifecycle.Apply(hold, scope, actor, reason, at);
                if (!change.Succeeded) return Fail("invalid_retention_hold");
                proposed = change.Hold;
                if (!await scopes.VerifyAsync(scope, actor, ct)) return Fail("retention_hold_not_found");
            }
            else if (hold == Guid.Empty) return Fail("retention_hold_not_found");
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { operation = expectedVersion == 0 ? "APPLY" : "RELEASE", organization, hold, expectedVersion, scope, reason })));
            var receipt = await holds.ReadReceiptAsync(organization, actor, key, ct);
            if (receipt is not null)
            {
                var saved = receipt.Result; var changedAt = ChangedAt(saved);
                if (receipt.ActorId != actor || receipt.Key != key || !Valid(saved, organization, hold, at)
                    || receipt.ExpiresAt.Offset != TimeSpan.Zero || changedAt > DateTimeOffset.MaxValue.AddHours(-24)
                    || receipt.ExpiresAt != changedAt.AddHours(24) || receipt.Fingerprint.Length != 64
                    || !receipt.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')) return Fail("retention_hold_source_unavailable");
                if (!await scopes.VerifyAsync(Scope(saved), actor, ct)) return Fail("retention_hold_not_found");
                if (receipt.ExpiresAt <= at) return Fail("idempotency_key_expired");
                if (receipt.Fingerprint != fingerprint) return Fail("idempotency_key_conflict");
                if (saved.Version != expectedVersion + 1 || expectedVersion == 0 && (saved.Status != RetentionHoldStatus.Active
                    || saved.AppliedBy != actor || Scope(saved) != scope || saved.Reason != reason)
                    || expectedVersion > 0 && (saved.Status != RetentionHoldStatus.Released || saved.ReleasedBy != actor))
                    return Fail("retention_hold_source_unavailable");
                if (expectedVersion > 0)
                {
                    // Release input does not repeat original private application
                    // evidence. Cross-check it against the retained current head.
                    var head = await holds.ReadAsync(organization, hold, ct);
                    if (head is null || !Valid(head, organization, hold, at) || head.Version < saved.Version
                        || Scope(head) != Scope(saved) || head.Reason != saved.Reason || head.AppliedBy != saved.AppliedBy
                        || head.AppliedAt != saved.AppliedAt || head.Version == saved.Version && head != saved)
                        return Fail("retention_hold_source_unavailable");
                    if (!await scopes.VerifyAsync(Scope(head), actor, ct)) return Fail("retention_hold_not_found");
                }
                return OrganizationOperation<RetentionHold>.Success(saved);
            }
            var current = await holds.ReadAsync(organization, hold, ct);
            if (current is not null && !Valid(current, organization, hold, at)) return Fail("retention_hold_source_unavailable");
            if (current is not null && !await scopes.VerifyAsync(Scope(current), actor, ct)) return Fail("retention_hold_not_found");
            if ((current?.Version ?? 0) != expectedVersion) return Fail("version_conflict");
            if (expectedVersion > 0)
            {
                if (current is null) return Fail("retention_hold_not_found");
                var released = RetentionHoldLifecycle.Release(organization, current, expectedVersion, actor, at);
                if (!released.Succeeded) return Fail(released.Reason == RetentionHoldChangeReason.AlreadyReleased
                    ? "retention_hold_already_released" : "retention_hold_source_unavailable");
                proposed = released.Hold;
            }
            var parent = await organizations.FindOrganizationAsync(organization, ct);
            var membership = await organizations.FindMembershipAsync(organization, actor, ct);
            if (parent is not { Status: OrganizationStatus.Active } || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
                || !await actors.VerifyAsync(actor, ct)) return Fail("organization_not_found");
            if (at < parent.UpdatedAt || at < membership.UpdatedAt) return Fail("version_conflict");
            if (!await scopes.VerifyAsync(Scope(proposed!), actor, ct)) return Fail("retention_hold_not_found");
            var result = await holds.WriteAsync(expectedVersion, new(proposed!, actor, membership.Id, membership.Version,
                membership.Role, Guid.NewGuid(), correlationId, new(actor, key, fingerprint, proposed!, at.AddHours(24))), ct);
            if (result == RetentionHoldWriteResult.Written && !await scopes.VerifyAsync(Scope(proposed!), actor, ct)) return Fail("retention_hold_not_found");
            return result switch
            {
                RetentionHoldWriteResult.Written => OrganizationOperation<RetentionHold>.Success(proposed!),
                RetentionHoldWriteResult.VersionConflict => Fail("version_conflict"),
                RetentionHoldWriteResult.KeyConflict => Fail("idempotency_key_conflict"),
                _ => Fail("retention_hold_source_unavailable")
            };
        }, ct);

    private static RetentionHoldScope Scope(RetentionHold hold) => new(hold.OrganizationId, hold.RecordClass, hold.Category, hold.RecordId);
    private static DateTimeOffset ChangedAt(RetentionHold hold) => hold.ReleasedAt ?? hold.AppliedAt;
    private static bool Valid(RetentionHold hold, Guid organization, Guid id, DateTimeOffset now) =>
        hold.OrganizationId == organization && hold.Id == id && RecordRetentionRules.Validate(hold) is null
        && now.Offset == TimeSpan.Zero && hold.AppliedAt <= now && ChangedAt(hold) <= now;
    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) =>
        await actors.VerifyAsync(actor, ct) && (await organizations.FindOrganizationAsync(organization, ct))?.Status == OrganizationStatus.Active
        && await organizations.FindMembershipAsync(organization, actor, ct) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
    private Task<OrganizationOperation<T>> Owned<T>(Guid organization, Guid actor, Func<Task<OrganizationOperation<T>>> action, CancellationToken ct) =>
        organization == Guid.Empty ? Task.FromResult(OrganizationOperation<T>.Failure("organization_not_found"))
        : unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            if (!await Admitted(organization, actor, ct)) return OrganizationOperation<T>.Failure("organization_not_found");
            var result = await action();
            return result.Succeeded && !await Admitted(organization, actor, ct) ? OrganizationOperation<T>.Failure("organization_not_found") : result;
        }, ct);
}
