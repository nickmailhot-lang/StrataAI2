using StrataAI.Application.Common;

namespace StrataAI.Application.BackgroundJobs;

public interface IBackgroundJobHandler
{
    string JobType { get; }
    string ServiceIdentity { get; }
    Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken);
}

public enum JobProcessingResult { Empty, Completed, Retried, LeaseLost }

public interface IBackgroundJobDiagnostics
{
    void Record(ClaimedBackgroundJob job, JobProcessingResult outcome);
}

public sealed class BackgroundJobProcessor
{
    private readonly IBackgroundJobStore _store;
    private readonly IClock _clock;
    private readonly IReadOnlyDictionary<string, IBackgroundJobHandler> _handlers;
    private readonly IBackgroundJobDiagnostics? _diagnostics;

    public BackgroundJobProcessor(IBackgroundJobStore store, IClock clock, IEnumerable<IBackgroundJobHandler> handlers, IBackgroundJobDiagnostics? diagnostics = null)
    {
        _store = store;
        _clock = clock;
        _diagnostics = diagnostics;
        // Duplicate registrations are deployment errors, never a random choice.
        _handlers = handlers.ToDictionary(handler => handler.JobType, StringComparer.Ordinal);
    }

    public async Task<JobProcessingResult> ProcessOneAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default)
    {
        var job = await _store.ClaimAsync(organizationId, workerId, cancellationToken);
        if (job is null) return JobProcessingResult.Empty;
        // Defence in depth: a faulty adapter cannot dispatch another tenant's
        // job or use a lease owned by another process.
        if (job.OrganizationId != organizationId || job.WorkerId != workerId || job.ActorId == Guid.Empty)
            throw new InvalidOperationException("Invalid claimed job scope.");
        var remaining = job.LeaseExpiresAt - _clock.UtcNow - TimeSpan.FromSeconds(5);
        if (remaining <= TimeSpan.Zero) return Report(job, JobProcessingResult.LeaseLost);
        if (!_handlers.TryGetValue(job.JobType, out var handler))
            return await FailAsync(job, "job_handler_missing", cancellationToken);
        if (!StringComparer.Ordinal.Equals(job.ServiceIdentity, handler.ServiceIdentity))
            return await FailAsync(job, "job_service_denied", cancellationToken);

        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        execution.CancelAfter(remaining);
        try
        {
            await handler.ExecuteAsync(job, execution.Token).WaitAsync(execution.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown leaves the committed lease for a later process to recover.
            throw;
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            return await FailAsync(job, "job_execution_timeout", cancellationToken);
        }
        catch (Exception)
        {
            // Provider exceptions may contain credentials/message bodies. Only
            // this stable code may enter queue state or logs.
            return await FailAsync(job, "job_handler_failed", cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Report(job, await _store.CompleteAsync(organizationId, job.Id, job.LeaseId, workerId, cancellationToken)
            ? JobProcessingResult.Completed : JobProcessingResult.LeaseLost);
    }

    private async Task<JobProcessingResult> FailAsync(ClaimedBackgroundJob job, string code, CancellationToken cancellationToken) =>
        Report(job, await _store.FailAsync(job.OrganizationId, job.Id, job.LeaseId, job.WorkerId, code, cancellationToken)
            ? JobProcessingResult.Retried : JobProcessingResult.LeaseLost);

    private JobProcessingResult Report(ClaimedBackgroundJob job, JobProcessingResult outcome)
    {
        _diagnostics?.Record(job, outcome);
        return outcome;
    }
}
