namespace StrataAI.Application.WorkManagement;

// Demo processes committed, process-local references. Production uses its separate Worker.
public interface IDemoCardReminderProcessing
{
    Task<bool> AdvanceAsync(CancellationToken cancellationToken = default);
}
