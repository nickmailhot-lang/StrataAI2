using System.Security.Cryptography;
using System.Text;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Domain.Tests;

public sealed class IdentityDeliveryProcessorTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
    private static readonly string Key=Convert.ToBase64String(Enumerable.Range(1,32).Select(i=>(byte)i).ToArray());
    private static IdentityDeliveryJob Job() => new(Guid.NewGuid(),Guid.NewGuid(),IdentityTokenPurpose.ResetPassword,"key",
        "identity-test","recipient@example.test","sender@example.test","https://app.example.test","account",1,
        Guid.NewGuid(),Guid.NewGuid(),Now.AddMinutes(2),Now.AddMinutes(30));
    private static IdentityDeliveryOptions Options()=>new("new-sender@example.test","https://changed.example.test","account");
    private static IdentityDeliveryTokenSigner Signer()=>new("key",new Dictionary<string,string>{["key"]=Key});

    [Fact]
    public async Task PRD_02_TC_01_SendsSnapshotLinkWithStableIdempotencyAndReceipt()
    {
        var job=Job(); using var signer=Signer(); var store=new Store(job,signer); var provider=new Provider();
        var processor=new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock());
        Assert.Equal(IdentityDeliveryPass.Sent,await processor.ProcessOneAsync(job.WorkerId,TestContext.Current.CancellationToken));
        Assert.Equal(IdentityDeliveryOutcome.Sent,store.Outcome);
        Assert.Equal(provider.Receipt,store.Receipt);
        Assert.Equal(job.SenderAddress,provider.Messages.Single().Sender);
        Assert.Contains($"https://app.example.test/reset-password#token={signer.Derive(job.Id,job.Purpose,job.KeyId)}",provider.Messages.Single().Text);
        Assert.Equal($"strataai-identity/reset/{job.Id:N}",provider.Messages.Single().IdempotencyKey);
        Assert.DoesNotContain("changed.example.test",provider.Messages.Single().Text);
    }

    [Fact]
    public async Task ARCH_07_AC_002_TimeoutRetryUsesIdenticalProviderPayloadAndKey()
    {
        var job=Job(); using var signer=Signer(); var store=new Store(job,signer);
        var provider=new Provider { FailOnce=true };
        var processor=new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock());
        Assert.Equal(IdentityDeliveryPass.Retried,await processor.ProcessOneAsync(job.WorkerId,TestContext.Current.CancellationToken));
        Assert.Equal("identity_provider_unavailable",store.Error);
        Assert.Equal(IdentityDeliveryPass.Sent,await processor.ProcessOneAsync(job.WorkerId,TestContext.Current.CancellationToken));
        Assert.Equal(2,provider.Messages.Count); Assert.Equal(provider.Messages[0],provider.Messages[1]);
    }

    [Theory]
    [InlineData("invalid",IdentityDeliveryPass.Cancelled,"identity_token_unusable")]
    [InlineData("key",IdentityDeliveryPass.Failed,"identity_token_key_missing")]
    [InlineData("hash",IdentityDeliveryPass.Failed,"identity_token_hash_mismatch")]
    [InlineData("account",IdentityDeliveryPass.Failed,"identity_provider_account_changed")]
    public async Task PRD_24_TC_04_InvalidTokensAndAccountChangesCannotSend(string fault,IdentityDeliveryPass expected,string code)
    {
        var original=Job(); var job=fault=="key" ? original with { KeyId="missing" } : fault=="account" ? original with { ProviderAccount="other-account" } : original;
        using var signer=Signer(); var store=new Store(job,signer) { Unusable=fault=="invalid",HashMismatch=fault=="hash" };
        var provider=new Provider(); var processor=new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock());
        Assert.Equal(expected,await processor.ProcessOneAsync(job.WorkerId,TestContext.Current.CancellationToken));
        Assert.Equal(code,store.Error); Assert.Empty(provider.Messages);
    }

    [Fact]
    public async Task PRD_24_TC_04_PurposeSubstitutionDoesNotSend()
    {
        var reset=Job(); var verification=reset with { Purpose=IdentityTokenPurpose.VerifyEmail };
        using var signer=Signer(); var store=new Store(verification,signer) { StoredHash=Hash(signer.Derive(reset.Id,reset.Purpose,reset.KeyId)) };
        var provider=new Provider();
        Assert.Equal(IdentityDeliveryPass.Failed,await new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock()).ProcessOneAsync(reset.WorkerId,TestContext.Current.CancellationToken));
        Assert.Empty(provider.Messages);
    }

    [Fact]
    public async Task ARCH_07_TC_01_PermanentProviderRejectionStopsRetries()
    {
        var job=Job(); using var signer=Signer(); var store=new Store(job,signer); var provider=new Provider { Permanent=true };
        Assert.Equal(IdentityDeliveryPass.Failed,await new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock()).ProcessOneAsync(job.WorkerId,TestContext.Current.CancellationToken));
        Assert.Equal("identity_provider_rejected",store.Error);
    }

    [Fact]
    public async Task ARCH_07_TC_01_ShutdownDoesNotAcknowledgeOrRetryAnAmbiguousSend()
    {
        var job=Job(); using var signer=Signer(); var store=new Store(job,signer);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider=new Provider { Execute=token=> { stop.Cancel(); return Task.FromCanceled<Guid>(token); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock()).ProcessOneAsync(job.WorkerId,stop.Token));
        Assert.Null(store.Outcome);
    }

    [Fact]
    public async Task ARCH_07_TC_01_LeaseLossIsNotReportedAsSent()
    {
        var job=Job(); using var signer=Signer(); var store=new Store(job,signer) { Accept=false };
        Assert.Equal(IdentityDeliveryPass.LeaseLost,await new IdentityDeliveryProcessor(store,signer,new Provider(),Options(),new Clock()).ProcessOneAsync(job.WorkerId,TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARCH_07_TC_01_WrongWorkerCannotDispatch()
    {
        var job=Job(); using var signer=Signer(); var store=new Store(job,signer); var provider=new Provider();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new IdentityDeliveryProcessor(store,signer,provider,Options(),new Clock()).ProcessOneAsync(Guid.NewGuid(),TestContext.Current.CancellationToken));
        Assert.Empty(provider.Messages);
    }

    private static string Hash(string raw)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    private sealed class Clock:IClock { public DateTimeOffset UtcNow=>Now; }
    private sealed class Store(IdentityDeliveryJob job,IIdentityDeliveryTokenSigner signer):IIdentityDeliveryStore
    {
        public bool Unusable {get;init;}
        public bool HashMismatch {get;init;}
        public string? StoredHash {get;init;}
        public bool Accept {get;init;}=true;
        public IdentityDeliveryOutcome? Outcome {get;private set;}
        public string? Error {get;private set;}
        public Guid? Receipt {get;private set;}
        public Task<IdentityDeliveryJob?> ClaimAsync(Guid workerId,CancellationToken cancellationToken)=>Task.FromResult<IdentityDeliveryJob?>(job);
        public Task<string?> GetUsableTokenHashAsync(IdentityDeliveryJob ignored,DateTimeOffset now,CancellationToken cancellationToken)=>Task.FromResult<string?>(
            Unusable ? null : StoredHash ?? (HashMismatch || job.KeyId=="missing" ? new string('0',64) : Hash(signer.Derive(job.Id,job.Purpose,job.KeyId))));
        public Task<bool> FinishAsync(IdentityDeliveryJob ignored,IdentityDeliveryOutcome outcome,string? errorCode,Guid? receiptId,CancellationToken cancellationToken)
        { Outcome=outcome; Error=errorCode; Receipt=receiptId; return Task.FromResult(Accept); }
    }
    private sealed class Provider:IIdentityEmailProvider
    {
        public Guid Receipt {get;}=Guid.NewGuid();
        public bool FailOnce {get;init;}
        public bool Permanent {get;init;}
        public Func<CancellationToken,Task<Guid>>? Execute {get;init;}
        public List<IdentityEmailMessage> Messages {get;}=[];
        public Task<Guid> SendAsync(IdentityEmailMessage message,CancellationToken cancellationToken)
        {
            Messages.Add(message);
            if (Execute is not null) return Execute(cancellationToken);
            if (Permanent) return Task.FromException<Guid>(new IdentityEmailProviderException("identity_provider_rejected",true));
            if (FailOnce && Messages.Count==1) return Task.FromException<Guid>(new HttpRequestException("sensitive provider body"));
            return Task.FromResult(Receipt);
        }
    }
}
