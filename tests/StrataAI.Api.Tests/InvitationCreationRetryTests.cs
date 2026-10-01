using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD60_first_invitation_ack_uses_storage_precision_and_matches_replay()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.Parse("2035-01-01T00:00:00.1234567+00:00", System.Globalization.CultureInfo.InvariantCulture);
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(new InvitationPrecisionClock(now));
            var original = services.Single(item => item.ServiceType == typeof(IInvitationStore));
            services.Remove(original);
            services.AddSingleton<IInvitationStore>(provider => new PrecisionInvitationStore(
                (IInvitationStore)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)));
        });
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Persisted invitation precision" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var input = new { email = "precision@example.test", surface = "INTERNAL", targetRole = "MEMBER" };
        using var first = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", input, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var ack = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var expires = now.AddDays(7); var storedExpiry = new DateTimeOffset(expires.Ticks - expires.Ticks % 10, expires.Offset);
        Assert.NotEqual(expires, storedExpiry); Assert.Equal(storedExpiry, ack.GetProperty("expiresAt").GetDateTimeOffset());
        using var retry = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", input, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(ack.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
    }

    private sealed class InvitationPrecisionClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    // Models the provider's canonical microsecond precision without requiring
    // PostgreSQL for the host regression. Real-provider equality remains in CI.
    private sealed class PrecisionInvitationStore(IInvitationStore inner) : IInvitationStore
    {
        public Task<InvitationRecord> CreateAsync(InvitationRecord row, CancellationToken ct = default) => inner.CreateAsync(row with {
            CreatedAt = new(row.CreatedAt.Ticks - row.CreatedAt.Ticks % 10, row.CreatedAt.Offset),
            ExpiresAt = new(row.ExpiresAt.Ticks - row.ExpiresAt.Ticks % 10, row.ExpiresAt.Offset) }, ct);
        public Task<InvitationCreationReplay?> FindCreationReplayAsync(Guid org, Guid actor, Guid key, CancellationToken ct = default) => inner.FindCreationReplayAsync(org, actor, key, ct);
        public Task SaveCreationReplayAsync(Guid org, Guid actor, Guid key, string fingerprint, Guid invitation, CancellationToken ct = default) => inner.SaveCreationReplayAsync(org, actor, key, fingerprint, invitation, ct);
        public Task<IReadOnlyList<PendingInvitation>> ListPendingForEmailAsync(string email, DateTimeOffset now, Guid? after, CancellationToken ct = default) => inner.ListPendingForEmailAsync(email, now, after, ct);
        public Task<InvitationRecord?> FindActiveByIdForEmailAsync(Guid id, Guid actor, string email, DateTimeOffset now, CancellationToken ct = default) => inner.FindActiveByIdForEmailAsync(id, actor, email, now, ct);
        public Task<InvitationRecord?> FindActiveByTokenHashAsync(string hash, DateTimeOffset now, CancellationToken ct = default) => inner.FindActiveByTokenHashAsync(hash, now, ct);
        public Task<InvitationAcceptStoreResult> AcceptAsync(string hash, Guid user, string email, DateTimeOffset now, CancellationToken ct = default) => inner.AcceptAsync(hash, user, email, now, ct);
        public Task<bool> RevokeAsync(Guid org, Guid id, DateTimeOffset now, CancellationToken ct = default) => inner.RevokeAsync(org, id, now, ct);
    }

    [Fact]
    public async Task PRD60_invitation_creation_retry_returns_same_token_free_ack_and_does_not_restore_revoked_invitation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invitation retry" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var input = new { email = "invite-retry@example.test", surface = "INTERNAL", targetRole = "ADMIN" };
        var route = $"/organizations/{org}/invitations";
        using var first = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var ack = await first.Content.ReadAsStringAsync(ct); var invitation = JsonSerializer.Deserialize<JsonElement>(ack);
        Assert.Equal(JsonValueKind.Null, invitation.GetProperty("invitationToken").ValueKind);
        using var repeated = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.Equal(ack, await repeated.Content.ReadAsStringAsync(ct));
        using var normalized = await Mutate(owner, HttpMethod.Post, route, new { email = " INVITE-RETRY@example.test ", surface = "internal", targetRole = "admin" }, key);
        Assert.Equal(HttpStatusCode.Created, normalized.StatusCode); Assert.Equal(ack, await normalized.Content.ReadAsStringAsync(ct));
        using var reused = await Mutate(owner, HttpMethod.Post, route, new { email = "another@example.test", surface = "INTERNAL", targetRole = "ADMIN" }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        Assert.True((await app.Services.GetRequiredService<IInvitationService>().RevokeAsync(org, actor, invitation.GetProperty("id").GetGuid(), "fixture", ct)).Succeeded);
        using var revokedRetry = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, revokedRetry.StatusCode);
        Assert.Equal(ack, await revokedRetry.Content.ReadAsStringAsync(ct));
        var receipt = await app.Services.GetRequiredService<IInvitationStore>().FindCreationReplayAsync(org, actor, Guid.Parse(key), ct);
        Assert.NotNull(receipt!.Invitation.RevokedAt);
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task PRD60_invitation_creation_rejects_invalid_retry_keys(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invalid invitation key" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var response = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email = "invite@example.test", surface = "INTERNAL", targetRole = "MEMBER" }, key);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PRD60_invitation_creation_replay_requires_current_administration_and_owner_grant_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Fresh invitation authority" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>(); var key = Guid.NewGuid().ToString();
        var input = new { email = "owner-grant@example.test", surface = "INTERNAL", targetRole = "OWNER" }; var route = $"/organizations/{org}/invitations";
        using var first = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        using var demoted = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.Forbidden, demoted.StatusCode);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var denied = await Mutate(owner, HttpMethod.Post, route, input, key); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("owner-grant@example.test", await denied.Content.ReadAsStringAsync(ct));
    }
}
