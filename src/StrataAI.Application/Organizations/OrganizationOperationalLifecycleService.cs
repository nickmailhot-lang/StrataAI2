using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.Organizations;

// Registration and HTTP exposure follow owning Production/Demo persistence.
// This coordinator never borrows active-work or deletion-recovery admission.
public sealed class OrganizationOperationalLifecycleService(IOrganizationOperationalLifecycleUnitOfWork unit,
    IOrganizationOperationalLifecycleStore store, ICommandActorAuthorization actors, IClock clock)
{
    public Task<OrganizationOperation<OrganizationOperationalLifecycleReview>> ReviewAsync(Guid organization,
        Guid actor, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || actor == Guid.Empty)
            return Task.FromResult(OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("organization_not_found"));
        return unit.ExecuteAsync<OrganizationOperationalLifecycleReview>(organization, actor, async () =>
        {
            if (!await actors.VerifyAsync(actor, ct))
                return OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("session_unavailable");
            var scope = await store.ReadScopeAsync(organization, actor, ct);
            if (scope?.Admitted(organization, actor) != true || string.IsNullOrWhiteSpace(scope.OrganizationName)
                || scope.OrganizationName.Length > 160 || scope.OrganizationName.Any(char.IsControl))
                return OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("organization_not_found");
            if (!await actors.VerifyAsync(actor, ct))
                return OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("session_unavailable");
            var current = await store.ReadScopeAsync(organization, actor, ct);
            if (current?.Admitted(organization, actor) != true || current.MembershipId != scope.MembershipId
                || current.MembershipVersion != scope.MembershipVersion || current.Role != scope.Role)
                return OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("organization_not_found");
            if (current.Status != scope.Status || current.Version != scope.Version || current.UpdatedAt != scope.UpdatedAt
                || current.OrganizationName != scope.OrganizationName)
                return OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("version_conflict");
            if (!await actors.VerifyAsync(actor, ct))
                return OrganizationOperation<OrganizationOperationalLifecycleReview>.Failure("session_unavailable");
            ct.ThrowIfCancellationRequested();
            return OrganizationOperation<OrganizationOperationalLifecycleReview>.Success(new(organization, actor,
                current.Status, current.Version, current.UpdatedAt,
                current.Role == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN", current.OrganizationName));
        }, ct);
    }

    public Task<OrganizationOperation<OrganizationOperationalLifecycleTransition>> ChangeAsync(Guid organization,
        Guid actor, OrganizationStatus status, long expectedVersion, Guid key, string correlationId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || actor == Guid.Empty) return Task.FromResult(Missing());
        return unit.ExecuteAsync(organization, actor, async () =>
        {
            if (!await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
            var scope = await store.ReadScopeAsync(organization, actor, ct);
            if (scope?.Admitted(organization, actor) != true) return Missing();
            // Invalid intent is disclosed only after current tenant/admin admission.
            if (status is not (OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated))
                return Failure("invalid_organization_lifecycle");
            if (expectedVersion < 1 || expectedVersion == long.MaxValue) return Failure("version_conflict");
            if (key == Guid.Empty) return Failure("idempotency_key_required");
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 256 || correlationId.Any(char.IsControl))
                return Failure("invalid_correlation_id");
            var fingerprint = OrganizationOperationalLifecycleRules.Fingerprint(organization, status, expectedVersion);
            var receipt = await store.ReadReceiptAsync(organization, actor, key, ct);
            OrganizationOperationalLifecycleTransition result;
            var recovery = receipt is not null;
            if (receipt is not null)
            {
                if (!ReceiptValid(receipt, organization, actor, key)) return Failure("organization_lifecycle_source_unavailable");
                if (receipt.ExpiresAt <= clock.UtcNow) return Failure("idempotency_key_expired");
                if (receipt.Fingerprint != fingerprint) return Failure("idempotency_key_conflict");
                if (receipt.Result.Status != status || receipt.Result.Version != expectedVersion + 1
                    || scope.Version < receipt.Result.Version || clock.UtcNow < receipt.Result.CreatedAt)
                    return Failure("organization_lifecycle_source_unavailable");
                result = receipt.Result;
            }
            else
            {
                if (scope.Version != expectedVersion) return Failure("version_conflict");
                if (scope.Status == status) return Failure("organization_lifecycle_unchanged");
                // PostgreSQL stores microseconds. Keep the immutable source and
                // aggregate clock identical in both runtime modes.
                var at = clock.UtcNow.ToUniversalTime();
                at = at.AddTicks(-(at.Ticks % 10));
                if (at < scope.UpdatedAt || at > DateTimeOffset.MaxValue.AddHours(-24)) return Failure("version_conflict");
                result = new(organization, Guid.NewGuid(), actor, scope.ActorLabel,
                    scope.Role == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN",
                    scope.MembershipId, scope.MembershipVersion, scope.Status, status,
                    checked(expectedVersion + 1), correlationId, at);
                var written = await store.WriteAsync(expectedVersion, result,
                    new(actor, key, fingerprint, result, at.AddHours(24)), ct);
                if (written != OrganizationOperationalLifecycleWriteResult.Written)
                    return Failure(written switch
                    {
                        OrganizationOperationalLifecycleWriteResult.VersionConflict => "version_conflict",
                        OrganizationOperationalLifecycleWriteResult.KeyConflict => "idempotency_key_conflict",
                        _ => "organization_lifecycle_source_unavailable",
                    });
            }
            // IO may have waited past session expiry or withdrawn current admin
            // authority. Failure causes the owning transaction to roll back.
            if (!await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
            var current = await store.ReadScopeAsync(organization, actor, ct);
            if (current?.Admitted(organization, actor) != true
                || current.MembershipId != scope.MembershipId || current.MembershipVersion != scope.MembershipVersion
                || current.Role != scope.Role) return Missing();
            if (!recovery && (current.Status != result.Status || current.Version != result.Version || current.UpdatedAt != result.CreatedAt))
                return Failure("organization_lifecycle_source_unavailable");
            ct.ThrowIfCancellationRequested();
            return OrganizationOperation<OrganizationOperationalLifecycleTransition>.Success(result);
        }, ct);
    }

    private static bool ReceiptValid(OrganizationOperationalLifecycleReceipt receipt, Guid organization, Guid actor, Guid key)
    {
        var source = receipt.Result;
        return source is not null && receipt.ActorId == actor && receipt.Key == key && receipt.Fingerprint is { Length: 64 }
            && receipt.Fingerprint.All(char.IsAsciiHexDigit) && source.OrganizationId == organization && source.ActorId == actor
            && source.EventId != Guid.Empty && source.MembershipId != Guid.Empty && source.MembershipVersion > 0
            && source.Version > 1 && source.PreviousStatus != source.Status
            && source.PreviousStatus is OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated
            && source.Status is OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated
            && source.ActingCapacity is "ORGANIZATION_OWNER" or "ORGANIZATION_ADMIN"
            && !string.IsNullOrWhiteSpace(source.ActorLabel) && source.ActorLabel.Length <= 160 && !source.ActorLabel.Any(char.IsControl)
            && !string.IsNullOrWhiteSpace(source.CorrelationId) && source.CorrelationId.Length <= 256 && !source.CorrelationId.Any(char.IsControl)
            && source.CreatedAt <= DateTimeOffset.MaxValue.AddHours(-24)
            && receipt.ExpiresAt == source.CreatedAt.AddHours(24);
    }

    private static OrganizationOperation<OrganizationOperationalLifecycleTransition> Missing() => Failure("organization_not_found");
    private static OrganizationOperation<OrganizationOperationalLifecycleTransition> Failure(string code) =>
        OrganizationOperation<OrganizationOperationalLifecycleTransition>.Failure(code);
}
