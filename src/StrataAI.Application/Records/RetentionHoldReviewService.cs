using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Review of an already-classified hold source is distinct from first
// classification. Only identities/revisions/lifecycle are disclosed here.
public sealed class RetentionHoldReviewService(IRetentionProducerReferences producers,
    RetentionProducerAuthorization producerAuthority, RetentionWorkAuthorization sourceAuthority,
    IOrganizationStore organizations, ICommandActorAuthorization actors, IOrganizationUnitOfWork unit)
{
    public Task<OrganizationOperation<RetentionProducerReference>> ReviewAsync(Guid organization, Guid actor,
        RetentionProducerKind kind, Guid record, CancellationToken ct = default) =>
        unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            OrganizationOperation<RetentionProducerReference> Fail(string code) => OrganizationOperation<RetentionProducerReference>.Failure(code);
            if (!await Admitted(organization, actor, ct)) return Fail("organization_not_found");
            var recordClass = kind switch
            {
                RetentionProducerKind.Card or RetentionProducerKind.CardComment => RetentionRecordClass.OperationalWork,
                RetentionProducerKind.Attachment => RetentionRecordClass.Document,
                _ => (RetentionRecordClass?)null
            };
            if (recordClass is null || record == Guid.Empty) return Fail("retention_hold_not_found");
            if (!await sourceAuthority.VerifySourceAsync(organization, recordClass.Value, record, actor, ct))
                return Fail("retention_hold_not_found");
            var evidence = await producers.ReadAsync(organization, kind, record, ct);
            if (evidence is null || !await producerAuthority.VerifyAsync(evidence, actor, ct)) return Fail("retention_hold_not_found");
            if (!await sourceAuthority.VerifySourceAsync(organization, recordClass.Value, record, actor, ct)
                || !await Admitted(organization, actor, ct)) return Fail("retention_hold_not_found");
            return OrganizationOperation<RetentionProducerReference>.Success(evidence);
        }, ct);

    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) =>
        organization != Guid.Empty && actor != Guid.Empty && await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct)) is { Status: OrganizationStatus.Active }
        && (await organizations.FindMembershipAsync(organization, actor, ct)) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
}
