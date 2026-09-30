using System.Security.Cryptography;
using System.Text;
using StrataAI.Application.Common;

namespace StrataAI.Application.Identity;

public enum IdentityDeliveryPass { Empty, Sent, Cancelled, Retried, Failed, LeaseLost }
public interface IIdentityDeliveryDiagnostics { void Record(IdentityDeliveryJob job,IdentityDeliveryPass outcome); }

public sealed class IdentityDeliveryProcessor(IIdentityDeliveryStore store,IIdentityDeliveryTokenSigner signer,
    IIdentityEmailProvider provider,IdentityDeliveryOptions options,IClock clock,IIdentityDeliveryDiagnostics? diagnostics=null)
{
    public async Task<IdentityDeliveryPass> ProcessOneAsync(Guid workerId,CancellationToken cancellationToken)
    {
        var job = await store.ClaimAsync(workerId,cancellationToken);
        if (job is null) return IdentityDeliveryPass.Empty;
        if (job.WorkerId!=workerId || job.UserId==Guid.Empty || job.Id==Guid.Empty)
            throw new InvalidOperationException("Invalid identity delivery claim.");
        if (job.ProviderAccount!=options.ProviderAccount)
            return await FinishAsync(job,IdentityDeliveryOutcome.Failed,"identity_provider_account_changed",null,cancellationToken);
        var deadline = (job.LeaseExpiresAt < job.ExpiresAt ? job.LeaseExpiresAt : job.ExpiresAt) - clock.UtcNow - TimeSpan.FromSeconds(5);
        if (deadline<=TimeSpan.Zero) return Report(job,IdentityDeliveryPass.LeaseLost);
        var hash = await store.GetUsableTokenHashAsync(job,clock.UtcNow,cancellationToken);
        if (hash is null) return await FinishAsync(job,IdentityDeliveryOutcome.Cancelled,"identity_token_unusable",null,cancellationToken);
        string token;
        try { token=signer.Derive(job.Id,job.Purpose,job.KeyId); }
        catch (InvalidOperationException) { return await FinishAsync(job,IdentityDeliveryOutcome.Failed,"identity_token_key_missing",null,cancellationToken); }
        var calculated = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash),Encoding.ASCII.GetBytes(calculated)))
            return await FinishAsync(job,IdentityDeliveryOutcome.Failed,"identity_token_hash_mismatch",null,cancellationToken);
        var verification = job.Purpose==IdentityTokenPurpose.VerifyEmail;
        var link = $"{job.PublicOrigin}/{(verification ? "verify-email" : "reset-password")}#token={token}";
        var subject = verification ? "Verify your StrataAI2 email" : "Reset your StrataAI2 password";
        var message = new IdentityEmailMessage(job.SenderAddress,job.RecipientEmail,subject,
            $"{subject}:\n\n{link}\n\nThis link expires. If you did not request it, ignore this email.",
            $"strataai-identity/{(verification ? "verify" : "reset")}/{job.Id:N}");
        using var execution=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        execution.CancelAfter(deadline);
        Guid receipt;
        try { receipt=await provider.SendAsync(message,execution.Token).WaitAsync(execution.Token); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (IdentityEmailProviderException error)
        { return await FinishAsync(job,error.Permanent ? IdentityDeliveryOutcome.Failed : IdentityDeliveryOutcome.Retry,error.Code,null,cancellationToken); }
        catch (Exception)
        { return await FinishAsync(job,IdentityDeliveryOutcome.Retry,"identity_provider_unavailable",null,cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested();
        return await FinishAsync(job,IdentityDeliveryOutcome.Sent,null,receipt,cancellationToken);
    }

    private async Task<IdentityDeliveryPass> FinishAsync(IdentityDeliveryJob job,IdentityDeliveryOutcome outcome,string? code,Guid? receipt,CancellationToken cancellationToken)
    {
        if (!await store.FinishAsync(job,outcome,code,receipt,cancellationToken)) return Report(job,IdentityDeliveryPass.LeaseLost);
        return Report(job,outcome switch { IdentityDeliveryOutcome.Sent=>IdentityDeliveryPass.Sent,
            IdentityDeliveryOutcome.Cancelled=>IdentityDeliveryPass.Cancelled,IdentityDeliveryOutcome.Retry=>IdentityDeliveryPass.Retried,_=>IdentityDeliveryPass.Failed });
    }
    private IdentityDeliveryPass Report(IdentityDeliveryJob job,IdentityDeliveryPass outcome)
    { diagnostics?.Record(job,outcome); return outcome; }
}
