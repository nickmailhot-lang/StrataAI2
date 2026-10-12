namespace StrataAI.Application.Records;

public enum RetentionWorkDispositionKind { Board, List, Card, CardComment, Attachment }
public enum RetentionDispositionAdmission { Clear, Held, Unavailable }

// Server-only deletion boundary. Call after current object authorization and
// revision/confirmation checks, inside the owning Work transaction. Implementations
// inspect the complete affected family, never a paged UI projection. A successful
// original command receipt is acknowledged without invoking another disposition.
public interface IRetentionWorkDisposition
{
    Task<RetentionDispositionAdmission> CheckAsync(Guid organization, RetentionWorkDispositionKind kind,
        Guid record, CancellationToken ct);
}

// Organization offboarding makes every record and its administration unavailable.
// It must retain the owning Organization gate and refuse any current active hold.
public interface IRetentionOrganizationDisposition
{
    Task<RetentionDispositionAdmission> CheckAsync(Guid organization, CancellationToken ct);
}

public static class RetentionDispositionAdmissionErrors
{
    public static string? ErrorCode(this RetentionDispositionAdmission admission) => admission switch
    {
        RetentionDispositionAdmission.Clear => null,
        RetentionDispositionAdmission.Held => "retention_hold_prevents_disposition",
        _ => "retention_disposition_unavailable"
    };
}
