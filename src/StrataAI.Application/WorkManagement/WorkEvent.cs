using StrataAI.Application.BackgroundJobs;
using System.Text.Json;

namespace StrataAI.Application.WorkManagement;

// Content-free durable invalidation envelope. Consumers must authorize before
// disclosing it; Organization context alone does not grant Board access.
public sealed record WorkEvent(Guid EventId, Guid OrganizationId, Guid BoardId,
    Guid ActorId, string EventType, string EntityType, Guid EntityId, long Version,
    string CorrelationId, DateTimeOffset CreatedAt);

public interface IWorkEventStore
{
    Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default);
}

public interface IWorkEventDeliveryStore
{
    Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid boardId, Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed class WorkEventDeliveryHandler(IWorkEventDeliveryStore delivery) : IBackgroundJobHandler
{
    public const string Type = "WORK_EVENT_READY";
    public const string Service = "work-event-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Service;

    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        if (job.JobType != Type || job.ServiceIdentity != Service || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty)
            throw new InvalidOperationException("Invalid work event job scope.");
        using var document = JsonDocument.Parse(job.SafeMetadataJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("eventId", out var eventValue) || !eventValue.TryGetGuid(out var eventId) || eventId == Guid.Empty ||
            !root.TryGetProperty("boardId", out var boardValue) || !boardValue.TryGetGuid(out var boardId) || boardId == Guid.Empty)
            throw new InvalidOperationException("Invalid work event references.");
        if (!await delivery.MarkReadyAsync(job, boardId, eventId, cancellationToken))
            throw new InvalidOperationException("Work event is unavailable.");
    }
}
