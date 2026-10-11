using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Unregistered, backend-private review command. This is exact producer evidence,
// not a bearer grant, HTTP projection, classification or permission to read bodies.
public sealed class RetentionSourceReviewService(IRetentionProducerReferences producers,
    IRetentionSourceReferenceReader classifications, RetentionProducerAuthorization authorization,
    IOrganizationStore organizations, ICommandActorAuthorization actors, IOrganizationUnitOfWork unit)
{
    public Task<OrganizationOperation<bool>> AdmitAsync(Guid organization, Guid actor, CancellationToken ct = default) =>
        organization == Guid.Empty
            ? Task.FromResult(OrganizationOperation<bool>.Failure("organization_not_found"))
            : unit.ExecuteAsync(organization, actor, null, false, async () =>
                await Admitted(organization, actor, ct)
                    ? OrganizationOperation<bool>.Success(true)
                    : OrganizationOperation<bool>.Failure("organization_not_found"), ct);

    public Task<OrganizationOperation<RetentionProducerReference>> ReviewAsync(Guid organization,
        Guid actor, RetentionProducerKind kind, Guid record, CancellationToken ct = default) =>
        organization == Guid.Empty
            ? Task.FromResult(OrganizationOperation<RetentionProducerReference>.Failure("organization_not_found"))
            : unit.ExecuteAsync(organization, actor, null, false, async () =>
            {
                if (!await Admitted(organization, actor, ct)) return Fail("organization_not_found");
                if (record == Guid.Empty || kind is not (RetentionProducerKind.Card or RetentionProducerKind.CardComment or RetentionProducerKind.Attachment))
                    return Fail("retention_source_unavailable");
                var review = await producers.ReadAsync(organization, kind, record, ct);
                if (review is null || review.OrganizationId != organization || review.Kind != kind || review.RecordId != record
                    || !await authorization.VerifyAsync(review, actor, ct)) return Fail("retention_source_unavailable");
                var recordClass = kind == RetentionProducerKind.Attachment ? RetentionRecordClass.Document : RetentionRecordClass.OperationalWork;
                // Existing classification must not become a new review or disclose
                // category/jurisdiction through a first-classification command.
                if (await classifications.ReadSourceAsync(organization, recordClass, record, ct) is not null)
                    return Fail("retention_source_unavailable");
                if (await producers.ReadAsync(organization, kind, record, ct) != review
                    || !await authorization.VerifyAsync(review, actor, ct)
                    || await classifications.ReadSourceAsync(organization, recordClass, record, ct) is not null
                    || !await Admitted(organization, actor, ct)) return Fail("retention_source_unavailable");
                return OrganizationOperation<RetentionProducerReference>.Success(review);
            }, ct);

    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) => actor != Guid.Empty
        && await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct)) is { Status: OrganizationStatus.Active }
        && (await organizations.FindMembershipAsync(organization, actor, ct)) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
    private static OrganizationOperation<RetentionProducerReference> Fail(string code) => OrganizationOperation<RetentionProducerReference>.Failure(code);
}
