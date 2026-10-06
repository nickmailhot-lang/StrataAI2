using System.Text.Json;
using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Organizations;

public interface IOrganizationLifecycleDeliveryStore
{
    Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken);
}

public sealed class OrganizationLifecycleDeliveryHandler(IOrganizationLifecycleDeliveryStore store) : IBackgroundJobHandler
{
    public const string Type = "ORGANIZATION_LIFECYCLE_EVENT_READY";
    public const string Service = "organization-lifecycle-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Service;
    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != Type || job.ServiceIdentity != Service || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty
            || job.Id == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Organization lifecycle delivery scope is invalid.");
        var eventId = ParseEventId(job.SafeMetadataJson);
        if (!await store.MarkReadyAsync(job, eventId, cancellationToken))
            throw new InvalidOperationException("Organization lifecycle event is unavailable.");
    }
    public static Guid ParseEventId(string metadata)
    {
        try
        {
            if (metadata is null || metadata.Length > 64) throw Invalid();
            using var document = JsonDocument.Parse(metadata); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("eventId", out var value) || value.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(value.GetString(), "D", out var id) || id == Guid.Empty) throw Invalid();
            return id;
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new("Organization lifecycle references are invalid.");
}
