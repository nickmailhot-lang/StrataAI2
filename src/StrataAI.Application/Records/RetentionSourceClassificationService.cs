using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Backend-private until HTTP review/confirmation and both host compositions are
// verified. Review is exact source evidence, never a retained permission grant.
public sealed class RetentionSourceClassificationService(IRetentionSourceCommandStore sources,
    RetentionProducerAuthorization producers, RetentionWorkAuthorization classified,
    IOrganizationStore organizations, ICommandActorAuthorization actors, IOrganizationUnitOfWork unit, IClock clock)
{
    public Task<OrganizationOperation<RetentionSourceClassification>> ClassifyAsync(Guid organization, Guid actor,
        RetentionProducerReference review, string category, string? jurisdiction, Guid key, string correlationId,
        CancellationToken ct = default) => organization == Guid.Empty
        ? Task.FromResult(Fail("organization_not_found"))
        : unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            if (!await Admitted(organization, actor, ct)) return Fail("organization_not_found");
            if (key == Guid.Empty) return Fail("idempotency_key_required");
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 256 || correlationId.Any(char.IsControl)) return Fail("invalid_correlation_id");
            if (review is null || review.OrganizationId != organization || review.RecordId == Guid.Empty
                || review.Kind is not (RetentionProducerKind.Card or RetentionProducerKind.CardComment or RetentionProducerKind.Attachment))
                return Fail("retention_source_unavailable");
            var recordClass = review.Kind == RetentionProducerKind.Attachment ? RetentionRecordClass.Document : RetentionRecordClass.OperationalWork;
            if (RecordRetentionRules.Validate(new RetentionPolicyApplicability(organization, recordClass, category, jurisdiction)) is not null)
                return Fail("invalid_retention_classification");
            var now = clock.UtcNow; var at = now.AddTicks(-(now.Ticks % 10));
            if (at.Offset != TimeSpan.Zero || at > DateTimeOffset.MaxValue.AddHours(-24)) return Fail("retention_source_unavailable");
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { operation = "CLASSIFY_SOURCE", organization, review, category, jurisdiction })));
            var hint = await sources.ReadReceiptReferenceAsync(organization, actor, key, ct);
            if (hint is not null)
            {
                // Authorize the original and current producer Boards before reading
                // private result/fingerprint. Moves never replace original admission.
                if (hint.OrganizationId != organization || !await classified.VerifySourceAsync(organization, hint.RecordClass, hint.RecordId, actor, ct))
                    return Fail("retention_source_unavailable");
                var receipt = await sources.ReadReceiptAsync(organization, actor, key, ct);
                if (receipt is null || receipt.ActorId != actor || receipt.Key != key || receipt.Result.Reference != hint
                    || receipt.Result.ActorId != actor || receipt.Result.EventId == Guid.Empty
                    || receipt.Result.RegisteredAt.Offset != TimeSpan.Zero || receipt.Result.RegisteredAt > at
                    || receipt.Result.RegisteredAt > DateTimeOffset.MaxValue.AddHours(-24)
                    || receipt.ExpiresAt != receipt.Result.RegisteredAt.AddHours(24) || receipt.ExpiresAt.Offset != TimeSpan.Zero
                    || receipt.Fingerprint.Length != 64 || !receipt.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')
                    || await sources.ReadReceiptReferenceAsync(organization, actor, key, ct) != hint
                    || !await classified.VerifySourceAsync(organization, hint.RecordClass, hint.RecordId, actor, ct)
                    || !await Admitted(organization, actor, ct)) return Fail("retention_source_unavailable");
                if (receipt.ExpiresAt <= at) return Fail("idempotency_key_expired");
                if (receipt.Fingerprint != fingerprint) return Fail("idempotency_key_conflict");
                if (hint.RecordClass != recordClass || hint.RecordId != review.RecordId || hint.Kind != review.Kind
                    || hint.OriginalBoardId != review.BoardId || hint.SourceVersion != review.SourceVersion
                    || hint.ParentCardId != review.ParentCardId || receipt.Result.Category != category || receipt.Result.Jurisdiction != jurisdiction)
                    return Fail("retention_source_unavailable");
                return OrganizationOperation<RetentionSourceClassification>.Success(receipt.Result);
            }
            if (!await producers.VerifyAsync(review, actor, ct)
                || await sources.ReadSourceAsync(organization, recordClass, review.RecordId, ct) is not null)
                return Fail("retention_source_unavailable");
            var membership = await organizations.FindMembershipAsync(organization, actor, ct);
            if (membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
                || !await Admitted(organization, actor, ct)) return Fail("organization_not_found");
            var eventId = Guid.NewGuid();
            var reference = await sources.CaptureAsync(new(organization, recordClass, review.RecordId, review.Kind, category, jurisdiction,
                review.SourceVersion, review.CardVersion, actor, membership.Id, membership.Version, membership.Role, eventId, correlationId, at, review, key, fingerprint), ct);
            var expectedReference = new RetentionSourceReference(organization, recordClass, review.RecordId, review.Kind,
                review.ParentCardId, 1, review.BoardId, review.SourceVersion);
            if (reference != expectedReference || !await producers.VerifyAsync(review, actor, ct)
                || !await classified.VerifySourceAsync(organization, recordClass, review.RecordId, actor, ct)
                || !await Admitted(organization, actor, ct)) return Fail("retention_source_unavailable");
            var committed = await sources.ReadReceiptAsync(organization, actor, key, ct);
            var expected = new RetentionSourceClassification(expectedReference, category, jurisdiction, actor, eventId, at);
            if (committed is null || committed.ActorId != actor || committed.Key != key || committed.Result != expected
                || committed.Fingerprint != fingerprint || committed.ExpiresAt != at.AddHours(24)
                || await sources.ReadReceiptReferenceAsync(organization, actor, key, ct) != reference
                || !await classified.VerifySourceAsync(organization, recordClass, review.RecordId, actor, ct)
                || !await Admitted(organization, actor, ct)) return Fail("retention_source_unavailable");
            return OrganizationOperation<RetentionSourceClassification>.Success(expected);
        }, ct);

    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) => actor != Guid.Empty
        && await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct)) is { Status: OrganizationStatus.Active }
        && (await organizations.FindMembershipAsync(organization, actor, ct)) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
    private static OrganizationOperation<RetentionSourceClassification> Fail(string code) => OrganizationOperation<RetentionSourceClassification>.Failure(code);
}
