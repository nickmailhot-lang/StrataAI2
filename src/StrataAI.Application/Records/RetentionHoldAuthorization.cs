using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Server-only evidence of an already-owned tenant transaction. Authorization
// never opens a nested transaction or accepts a caller-supplied permission bit.
public interface IRetentionAdministrationTransaction
{
    bool Owns(Guid organization);
}

public sealed class RetentionAdministrationAuthorization(IOrganizationStore organizations,
    ICommandActorAuthorization actors, IRetentionAdministrationTransaction transaction)
{
    public async Task<bool> VerifyAsync(Guid organization, Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || actor == Guid.Empty || !transaction.Owns(organization)) return false;
        if (!await actors.VerifyAsync(actor, ct)) return false;
        var parent = await organizations.FindOrganizationAsync(organization, ct);
        var member = await organizations.FindMembershipAsync(organization, actor, ct);
        // Same explicit retention administration role boundary as policy writes.
        // Broad holds expose no constituent record titles, counts or bodies.
        return parent is { Status: OrganizationStatus.Active }
            && member is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            && transaction.Owns(organization) && await actors.VerifyAsync(actor, ct);
    }
}

// A broad retention administration grant cannot authorize a record-specific
// hold: those retain original, application and current owning Board gates.
public sealed class RetentionHoldAuthorization(RetentionWorkAuthorization work,
    RetentionAdministrationAuthorization administration) : IRetentionHoldScopeAuthorization
{
    public Task<bool> VerifyAsync(RetentionHoldScope scope, Guid actor, CancellationToken ct)
    {
        if (scope.RecordId is not null) return work.VerifyAsync(scope, actor, ct);
        if (scope.RecordClass is { } kind && !Enum.IsDefined(kind)
            || scope.Category is not null && (scope.RecordClass is null || !Category(scope.Category)))
            return Task.FromResult(false);
        return administration.VerifyAsync(scope.OrganizationId, actor, ct);
    }

    public Task<bool> VerifyAsync(RetentionHoldReference reference, Guid actor, CancellationToken ct)
    {
        if (reference.RecordId is not null) return work.VerifyAsync(reference, actor, ct);
        if (reference.HoldId == Guid.Empty || reference.Version < 1
            || reference.RecordClass is { } kind && !Enum.IsDefined(kind)
            || reference.CategoryScoped && reference.RecordClass is null
            || reference.ContextVersion is not null || reference.ClassificationBoardId is not null
            || reference.ApplicationBoardId is not null || reference.ApplicationSourceVersion is not null)
            return Task.FromResult(false);
        return administration.VerifyAsync(reference.OrganizationId, actor, ct);
    }

    private static bool Category(string value) => value is { Length: > 0 and <= 64 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}
