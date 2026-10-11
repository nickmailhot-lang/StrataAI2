using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

public sealed record RetentionPolicyInput(RetentionRecordClass RecordClass, string Category,
    string? Jurisdiction, RetentionAnchor Anchor, int? RetentionDays,
    RecordDispositionAction Action, string Source, string? Notes);
public sealed record RetentionPolicyReceipt(Guid ActorId, Guid Key, string Fingerprint,
    RecordRetentionPolicy Result, DateTimeOffset ExpiresAt);
public sealed record RetentionPolicyWrite(RecordRetentionPolicy Policy, Guid ActorId,
    Guid MembershipId, long MembershipVersion, OrganizationRole ActingCapacity,
    Guid EventId, string CorrelationId, RetentionPolicyReceipt Receipt);
public enum RetentionPolicyWriteResult { Written, VersionConflict, NamespaceConflict, KeyConflict, SourceUnavailable }

// Implementations participate in the owning Organization transaction, append the
// immutable audit/revision/receipt graph atomically and never open a nested unit.
// Runtime registration follows both Production and Demo adapters.
public interface IRetentionPolicyStore
{
    Task<RecordRetentionPolicy?> ReadAsync(Guid organization, Guid policy, CancellationToken ct);
    Task<RetentionPolicyReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
    Task<RetentionPolicyWriteResult> WriteAsync(long expectedVersion, RetentionPolicyWrite write, CancellationToken ct);
}

public sealed class RetentionPolicyService(IOrganizationStore organizations,
    IRetentionPolicyStore policies, IOrganizationUnitOfWork unit,
    ICommandActorAuthorization actors, IClock clock)
{
    public Task<OrganizationOperation<RecordRetentionPolicy?>> ReadAsync(Guid organization,
        Guid actor, Guid policy, CancellationToken ct = default) => Owned(organization, actor, async () =>
        {
            if (policy == Guid.Empty) return OrganizationOperation<RecordRetentionPolicy?>.Failure("retention_policy_not_found");
            var source = await policies.ReadAsync(organization, policy, ct);
            return source is null || Valid(source, organization, policy)
                ? OrganizationOperation<RecordRetentionPolicy?>.Success(source)
                : OrganizationOperation<RecordRetentionPolicy?>.Failure("retention_policy_source_unavailable");
        }, ct);

    public Task<OrganizationOperation<RecordRetentionPolicy>> ChangeAsync(Guid organization, Guid actor,
        Guid policy, RetentionPolicyInput? input, long expectedVersion, Guid key,
        string correlationId, CancellationToken ct = default) => Owned(organization, actor, async () =>
        {
            if (expectedVersion < 0 || expectedVersion == long.MaxValue)
                return OrganizationOperation<RecordRetentionPolicy>.Failure("version_conflict");
            if (key == Guid.Empty) return OrganizationOperation<RecordRetentionPolicy>.Failure("idempotency_key_required");
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 256 || correlationId.Any(char.IsControl))
                return OrganizationOperation<RecordRetentionPolicy>.Failure("invalid_correlation_id");
            if (input is null) return OrganizationOperation<RecordRetentionPolicy>.Failure("invalid_retention_policy");
            // PostgreSQL timestamptz retains microseconds. Canonicalize before
            // composing both the response and its receipt so refresh/replay match.
            var now = clock.UtcNow;
            var at = now.AddTicks(-(now.Ticks % 10));
            var proposed = new RecordRetentionPolicy(policy, organization, expectedVersion + 1,
                input.RecordClass, input.Category, input.Jurisdiction, input.Anchor,
                input.RetentionDays, input.Action, input.Source, input.Notes, at, at);
            var invalid = RecordRetentionRules.Validate(proposed);
            if (invalid is not null)
                return OrganizationOperation<RecordRetentionPolicy>.Failure("invalid_retention_policy_" + invalid);
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { organization, policy, expectedVersion, input })));
            var receipt = await policies.ReadReceiptAsync(organization, actor, key, ct);
            if (receipt is not null)
            {
                if (receipt.ActorId != actor || receipt.Key != key || !Valid(receipt.Result, organization, policy)
                    || receipt.ExpiresAt.Offset != TimeSpan.Zero
                    || receipt.Result.UpdatedAt > at || receipt.Result.UpdatedAt > DateTimeOffset.MaxValue.AddHours(-24)
                    || receipt.ExpiresAt != receipt.Result.UpdatedAt.AddHours(24))
                    return OrganizationOperation<RecordRetentionPolicy>.Failure("retention_policy_source_unavailable");
                if (receipt.ExpiresAt <= at)
                    return OrganizationOperation<RecordRetentionPolicy>.Failure("idempotency_key_expired");
                if (receipt.Fingerprint != fingerprint)
                    return OrganizationOperation<RecordRetentionPolicy>.Failure("idempotency_key_conflict");
                var saved = receipt.Result;
                if (saved.Version != expectedVersion + 1 || new RetentionPolicyInput(saved.RecordClass,
                    saved.Category, saved.Jurisdiction, saved.Anchor, saved.RetentionDays,
                    saved.Action, saved.Source, saved.Notes) != input)
                    return OrganizationOperation<RecordRetentionPolicy>.Failure("retention_policy_source_unavailable");
                return OrganizationOperation<RecordRetentionPolicy>.Success(saved);
            }
            var current = await policies.ReadAsync(organization, policy, ct);
            if (current is not null && !Valid(current, organization, policy))
                return OrganizationOperation<RecordRetentionPolicy>.Failure("retention_policy_source_unavailable");
            if ((current?.Version ?? 0) != expectedVersion)
                return OrganizationOperation<RecordRetentionPolicy>.Failure("version_conflict");
            // Identity keeps its original applicability. Moving scopes creates a
            // separately reviewed policy; it cannot rewrite retained references.
            if (current is not null && (current.RecordClass != input.RecordClass
                || current.Category != input.Category || current.Jurisdiction != input.Jurisdiction))
                return OrganizationOperation<RecordRetentionPolicy>.Failure("retention_policy_scope_immutable");
            var parent = (await organizations.FindOrganizationAsync(organization, ct))!;
            var membership = (await organizations.FindMembershipAsync(organization, actor, ct))!;
            if (parent is not { Status: OrganizationStatus.Active }
                || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
                || !await actors.VerifyAsync(actor, ct))
                return OrganizationOperation<RecordRetentionPolicy>.Failure("organization_not_found");
            if (at < parent.UpdatedAt || at < membership.UpdatedAt || at < current?.UpdatedAt
                || at > DateTimeOffset.MaxValue.AddHours(-24))
                return OrganizationOperation<RecordRetentionPolicy>.Failure("version_conflict");
            proposed = proposed with { CreatedAt = current?.CreatedAt ?? at };
            var result = await policies.WriteAsync(expectedVersion, new(proposed, actor, membership.Id,
                membership.Version, membership.Role, Guid.NewGuid(), correlationId,
                new(actor, key, fingerprint, proposed, at.AddHours(24))), ct);
            return result switch
            {
                RetentionPolicyWriteResult.Written => OrganizationOperation<RecordRetentionPolicy>.Success(proposed),
                RetentionPolicyWriteResult.VersionConflict => OrganizationOperation<RecordRetentionPolicy>.Failure("version_conflict"),
                RetentionPolicyWriteResult.NamespaceConflict => OrganizationOperation<RecordRetentionPolicy>.Failure("retention_policy_scope_unavailable"),
                RetentionPolicyWriteResult.KeyConflict => OrganizationOperation<RecordRetentionPolicy>.Failure("idempotency_key_conflict"),
                _ => OrganizationOperation<RecordRetentionPolicy>.Failure("retention_policy_source_unavailable")
            };
        }, ct);

    private static bool Valid(RecordRetentionPolicy source, Guid organization, Guid policy) =>
        source.OrganizationId == organization && source.Id == policy && RecordRetentionRules.Validate(source) is null;

    private Task<OrganizationOperation<T>> Owned<T>(Guid organization, Guid actor,
        Func<Task<OrganizationOperation<T>>> action, CancellationToken ct) => organization == Guid.Empty
        ? Task.FromResult(OrganizationOperation<T>.Failure("organization_not_found"))
        : unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            if (!await Admitted(organization, actor, ct)) return OrganizationOperation<T>.Failure("organization_not_found");
            var result = await action();
            return result.Succeeded && !await Admitted(organization, actor, ct)
                ? OrganizationOperation<T>.Failure("organization_not_found") : result;
        }, ct);

    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) =>
        await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct))?.Status == OrganizationStatus.Active
        && await organizations.FindMembershipAsync(organization, actor, ct)
            is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
}
