namespace StrataAI.Application.Identity;

public sealed record IdentityDeliveryJob(Guid Id, Guid UserId, IdentityTokenPurpose Purpose, string KeyId,
    string CorrelationId, string RecipientEmail, string SenderAddress, string PublicOrigin, string ProviderAccount,
    int AttemptCount, Guid LeaseId, Guid WorkerId, DateTimeOffset LeaseExpiresAt, DateTimeOffset ExpiresAt);
public enum IdentityDeliveryOutcome { Sent, Cancelled, Retry, Failed }
public interface IIdentityDeliveryStore
{
    Task<IdentityDeliveryJob?> ClaimAsync(Guid workerId, CancellationToken cancellationToken);
    Task<string?> GetUsableTokenHashAsync(IdentityDeliveryJob job, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(IdentityDeliveryJob job, IdentityDeliveryOutcome outcome, string? errorCode, Guid? receiptId, CancellationToken cancellationToken);
}
public sealed record IdentityEmailMessage(string Sender, string Recipient, string Subject, string Text, string IdempotencyKey);
public interface IIdentityEmailProvider
{
    Task<Guid> SendAsync(IdentityEmailMessage message, CancellationToken cancellationToken);
}
public sealed class IdentityEmailProviderException(string code, bool permanent) : Exception("Identity email provider failed.")
{
    public string Code { get; } = code;
    public bool Permanent { get; } = permanent;
}
