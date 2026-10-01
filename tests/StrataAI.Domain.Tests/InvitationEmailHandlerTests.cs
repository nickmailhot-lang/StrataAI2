using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class InvitationEmailHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2035-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
    private static readonly IdentityDeliveryOptions Options = new("sender@example.test", "https://app.example.test", "account");
    private static IdentityDeliveryTokenSigner Signer() => new("key", new Dictionary<string, string> { ["key"] = Key });
    private static (ClaimedBackgroundJob Job, InvitationMailIntent Intent) Fixture(IdentityDeliveryTokenSigner signer)
    {
        var org = Guid.NewGuid(); var invite = Guid.NewGuid(); var issuer = Guid.NewGuid(); var id = Guid.NewGuid();
        var job = new ClaimedBackgroundJob(id, org, InvitationEmailHandler.Type, issuer, InvitationEmailHandler.Identity, "correlation",
            JsonSerializer.Serialize(new { invitationId = invite }), 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(1));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signer.DeriveInvitation(org, invite, "key"))));
        return (job, new(id, org, invite, issuer, "recipient@example.test", InvitationSurface.Internal, "MEMBER", Now.AddDays(7),
            "key", Options.SenderAddress, Options.PublicOrigin, Options.ProviderAccount, 1, InvitationMailState.Pending, hash, true));
    }
    private static InvitationEmailHandler Handler(Store store, IdentityDeliveryTokenSigner signer, Provider provider) => new(store, signer, provider, Options, new Clock());

    [Theory]
    [InlineData(InvitationSurface.Internal, "MEMBER")]
    [InlineData(InvitationSurface.Portal, "OWNER")]
    public async Task Both_surfaces_use_fragment_only_proof_and_persist_lease_bound_receipt(InvitationSurface surface, string role)
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent with { Surface = surface, TargetRole = role }); var provider = new Provider();
        await Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken);
        var message = Assert.Single(provider.Messages);
        Assert.Equal(intent.RecipientEmail, message.Recipient);
        Assert.Contains($"https://app.example.test/invitation#token={signer.DeriveInvitation(intent.OrganizationId, intent.InvitationId, "key")}", message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", message.Text, StringComparison.Ordinal);
        Assert.Equal($"strataai-invitation/{intent.OrganizationId:N}/{intent.InvitationId:N}", message.IdempotencyKey);
        Assert.Equal(InvitationMailState.Sent, store.State); Assert.Equal(provider.Receipt, store.Receipt); Assert.Equal(job, store.FinishedClaim);
    }

    [Theory]
    [InlineData(InvitationMailState.Sent)]
    [InlineData(InvitationMailState.Cancelled)]
    [InlineData(InvitationMailState.Failed)]
    public async Task Already_terminal_ledger_never_sends_again(InvitationMailState state)
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent with { State = state, IsUsable = false }); var provider = new Provider();
        await Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Empty(provider.Messages); Assert.Null(store.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unusable_or_expired_invitation_cancels_without_provider_work(bool expired)
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer);
        var store = new Store(intent with { IsUsable = expired, ExpiresAt = expired ? Now.AddSeconds(-1) : intent.ExpiresAt }); var provider = new Provider();
        await Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Empty(provider.Messages); Assert.Equal(InvitationMailState.Cancelled, store.State); Assert.Equal("invitation_unusable", store.Error);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("invitation")]
    [InlineData("issuer")]
    [InlineData("job")]
    public async Task Faulty_adapter_cannot_substitute_scope_or_actor(string field)
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer);
        intent = field switch { "organization" => intent with { OrganizationId = Guid.NewGuid() }, "invitation" => intent with { InvitationId = Guid.NewGuid() },
            "issuer" => intent with { IssuerId = Guid.NewGuid() }, _ => intent with { JobId = Guid.NewGuid() } };
        var store = new Store(intent); var provider = new Provider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Empty(provider.Messages); Assert.Null(store.State);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"invitationId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"invitationId\":\"11111111-1111-4111-8111-111111111111\",\"recipient\":\"private\"}")]
    [InlineData("{\"invitationId\":\"11111111-1111-4111-8111-111111111111\",\"invitationId\":\"11111111-1111-4111-8111-111111111111\"}")]
    public async Task Job_metadata_requires_exactly_one_nonempty_reference(string metadata)
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent); var provider = new Provider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store, signer, provider).ExecuteAsync(job with { SafeMetadataJson = metadata }, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Loads); Assert.Empty(provider.Messages);
    }

    [Theory]
    [InlineData("hash", "invitation_token_hash_mismatch")]
    [InlineData("key", "invitation_token_key_missing")]
    [InlineData("account", "invitation_provider_account_changed")]
    [InlineData("template", "invitation_intent_invalid")]
    [InlineData("origin", "invitation_intent_invalid")]
    public async Task Unsafe_or_unreconstructable_intent_fails_without_sending(string change, string code)
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer);
        intent = change switch { "hash" => intent with { CanonicalTokenHash = new string('0', 64) }, "key" => intent with { KeyId = "missing" },
            "account" => intent with { ProviderAccount = "different" }, "template" => intent with { TemplateVersion = 99 }, _ => intent with { PublicOrigin = "https://app.example.test/unsafe?token=value" } };
        var store = new Store(intent); var provider = new Provider();
        await Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Empty(provider.Messages); Assert.Equal(InvitationMailState.Failed, store.State); Assert.Equal(code, store.Error);
    }

    [Fact]
    public async Task Lost_ledger_ack_repeats_identical_provider_message_and_key_then_acknowledges_receipt()
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent) { Accept = false }; var provider = new Provider(); var handler = Handler(store, signer, provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(job, TestContext.Current.CancellationToken));
        store.Accept = true; await handler.ExecuteAsync(job with { LeaseId = Guid.NewGuid(), AttemptCount = 2 }, TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Messages.Count); Assert.Equal(provider.Messages[0], provider.Messages[1]); Assert.Single(provider.AcceptedKeys);
        Assert.Equal(InvitationMailState.Sent, store.State);
    }

    [Fact]
    public async Task Transient_provider_failure_leaves_pending_and_generic_retry_stores_only_safe_code()
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent); var provider = new Provider { Failure = new Exception("private bearer/provider secret") };
        var queue = new Queue(job); var processor = new BackgroundJobProcessor(queue, new Clock(), [Handler(store, signer, provider)]);
        Assert.Equal(JobProcessingResult.Retried, await processor.ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal("job_handler_failed", queue.Error); Assert.Null(store.State); Assert.False(queue.Completed);
    }

    [Fact]
    public async Task Permanent_provider_failure_persists_only_constant_error_code()
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent);
        var provider = new Provider { Failure = new IdentityEmailProviderException("private-provider-secret", true) };
        await Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Equal(InvitationMailState.Failed, store.State); Assert.Equal("invitation_provider_rejected", store.Error); Assert.Null(store.Receipt);
    }

    [Fact]
    public async Task Abort_ignoring_provider_is_bounded_by_remaining_invitation_lease_budget()
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent);
        var provider = new Provider { Hang = true }; var execution = Handler(store, signer, provider).ExecuteAsync(job with { LeaseExpiresAt = Now.AddSeconds(5.05) }, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Null(store.State);
    }

    [Fact]
    public async Task Empty_provider_receipt_cannot_acknowledge_delivery()
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent); var provider = new Provider { Receipt = Guid.Empty };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store, signer, provider).ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Null(store.State); Assert.Null(store.Receipt);
    }

    [Fact]
    public async Task Ledger_sent_before_generic_completion_recovers_without_another_provider_call()
    {
        using var signer = Signer(); var (job, intent) = Fixture(signer); var store = new Store(intent); var provider = new Provider(); var queue = new Queue(job);
        var processor = new BackgroundJobProcessor(queue, new Clock(), [Handler(store, signer, provider)]);
        Assert.Equal(JobProcessingResult.Completed, await processor.ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        store.Intent = intent with { State = InvitationMailState.Sent };
        Assert.Equal(JobProcessingResult.Completed, await processor.ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Single(provider.Messages); Assert.True(queue.Completed);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Store(InvitationMailIntent intent) : IInvitationDeliveryStore
    {
        public InvitationMailIntent Intent { get; set; } = intent;
        public bool Accept { get; set; } = true;
        public int Loads { get; private set; }
        public InvitationMailState? State { get; private set; }
        public string? Error { get; private set; }
        public Guid? Receipt { get; private set; }
        public ClaimedBackgroundJob? FinishedClaim { get; private set; }
        public Task<InvitationMailIntent?> LoadAsync(ClaimedBackgroundJob job, CancellationToken ct) { Loads++; return Task.FromResult<InvitationMailIntent?>(Intent); }
        public Task<bool> FinishAsync(ClaimedBackgroundJob job, InvitationMailState state, string? code, Guid? receipt, CancellationToken ct)
        { if (Accept) { State = state; Error = code; Receipt = receipt; FinishedClaim = job; } return Task.FromResult(Accept); }
    }
    private sealed class Provider : IIdentityEmailProvider
    {
        public List<IdentityEmailMessage> Messages { get; } = [];
        public HashSet<string> AcceptedKeys { get; } = [];
        public Guid Receipt { get; init; } = Guid.NewGuid();
        public Exception? Failure { get; init; }
        public bool Hang { get; init; }
        public Task<Guid> SendAsync(IdentityEmailMessage message, CancellationToken ct)
        {
            Messages.Add(message);
            if (Failure is not null) return Task.FromException<Guid>(Failure);
            if (Hang) return new TaskCompletionSource<Guid>().Task;
            AcceptedKeys.Add(message.IdempotencyKey); return Task.FromResult(Receipt);
        }
    }
    private sealed class Queue(ClaimedBackgroundJob job) : IBackgroundJobStore
    {
        public string? Error { get; private set; }
        public bool Completed { get; private set; }
        public Task<ClaimedBackgroundJob?> ClaimAsync(Guid org, Guid worker, CancellationToken ct = default) => Task.FromResult<ClaimedBackgroundJob?>(job);
        public Task<bool> CompleteAsync(Guid org, Guid id, Guid lease, Guid worker, CancellationToken ct = default) { Completed = true; return Task.FromResult(true); }
        public Task<bool> FailAsync(Guid org, Guid id, Guid lease, Guid worker, string code, CancellationToken ct = default) { Error = code; return Task.FromResult(true); }
    }
}
