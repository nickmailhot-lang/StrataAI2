using System.Text.Json;
using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationDeletionAttempt(Guid RequestId, Guid StepId, long AcceptedVersion)
{
    public static OrganizationDeletionAttempt Parse(string json)
    {
        if (json is null || json.Length > 256) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                || !root.TryGetProperty("requestId", out var request) || request.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(request.GetString(), "D", out var requestId) || requestId == Guid.Empty
                || !root.TryGetProperty("stepId", out var step) || step.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(step.GetString(), "D", out var stepId) || stepId == Guid.Empty
                || !root.TryGetProperty("acceptedVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt64(out var accepted) || accepted < 2 || accepted == long.MaxValue) throw Invalid();
            return new(requestId, stepId, accepted);
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new("Organization deletion references are invalid.");
}

public static class OrganizationDeletionJobs
{
    public const string Type = "ORGANIZATION_DELETE_PAGE";
    public const string Service = "organization-lifecycle";
    public const int PageSize = 128;
    public static NewBackgroundJob Create(Guid organizationId, Guid actorId, OrganizationDeletionAttempt attempt, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (organizationId == Guid.Empty || actorId == Guid.Empty || attempt.RequestId == Guid.Empty
            || attempt.StepId == Guid.Empty || attempt.AcceptedVersion < 2 || attempt.AcceptedVersion == long.MaxValue)
            throw new InvalidOperationException("Organization deletion publication is unavailable.");
        if (string.IsNullOrEmpty(correlationId) || correlationId.Length > 64
            || correlationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            throw new ArgumentException("Deletion correlation identity is invalid.", nameof(correlationId));
        return new(Guid.NewGuid(), organizationId, Type, $"organization-deletion/{attempt.RequestId:N}/{attempt.StepId:N}",
            actorId, Service, correlationId,
            JsonSerializer.Serialize(new { requestId = attempt.RequestId, stepId = attempt.StepId, acceptedVersion = attempt.AcceptedVersion }));
    }
}

public interface IOrganizationDeletionJobPublisher
{
    // Accepted request/checkpoint/first job borrow the owning Organization command.
    // Matching publication returns false without resetting progress or requeuing.
    Task<bool> PublishAsync(Guid organizationId, Guid actorId, Guid requestId,
        long acceptedVersion, string correlationId, CancellationToken cancellationToken);
}

public interface IOrganizationDeletionPageStore
{
    // Restricted owning tenant transaction only: validate the current request,
    // step and exact unexpired lease; couple bounded effects, audit/events and
    // checkpoint with the next immutable step/job, or terminal completion.
    // Already committed steps acknowledge without repeating their effects.
    // A false result must roll back every tentative effect and publication.
    Task<bool> ApplyPageAsync(ClaimedBackgroundJob job, OrganizationDeletionAttempt attempt,
        int pageSize, CancellationToken cancellationToken);
}

public sealed class OrganizationDeletionPageHandler(IOrganizationDeletionPageStore store) : IBackgroundJobHandler
{
    public string JobType => OrganizationDeletionJobs.Type;
    public string ServiceIdentity => OrganizationDeletionJobs.Service;
    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != JobType || job.ServiceIdentity != ServiceIdentity || job.OrganizationId == Guid.Empty
            || job.ActorId == Guid.Empty || job.Id == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Organization deletion job scope is invalid.");
        var attempt = OrganizationDeletionAttempt.Parse(job.SafeMetadataJson);
        if (!await store.ApplyPageAsync(job, attempt, OrganizationDeletionJobs.PageSize, cancellationToken))
            throw new InvalidOperationException("Organization deletion page is unavailable.");
    }
}
