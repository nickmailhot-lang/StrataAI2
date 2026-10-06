using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-03-TC-06/07/08: one committed creator, membership and historical result.
    [Fact]
    public async Task Concurrent_Organization_creation_retries_preserve_later_metadata_and_reject_changed_intent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid(); var body = new { name = "Original creation", description = "Reviewed" };
        var path = $"/organizations?expectedActorId={actor}";
        var responses = await Task.WhenAll(Mutate(owner, HttpMethod.Post, path, body, key.ToString()),
            Mutate(owner, HttpMethod.Post, path, body, key.ToString()));
        Guid org; string original;
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            original = await responses[0].Content.ReadAsStringAsync(ct);
            Assert.Equal(original, await responses[1].Content.ReadAsStringAsync(ct));
            org = JsonDocument.Parse(original).RootElement.GetProperty("organization").GetProperty("id").GetGuid();
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        Assert.Single(await store.ListOrganizationsForUserAsync(actor, ct));
        var member = await store.FindMembershipAsync(org, actor, ct);
        Assert.NotNull(member); Assert.True(member.Active); Assert.Equal(OrganizationRole.Owner, member.Role);
        using var later = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", new { name = "Later metadata", version = 1 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var current = await store.FindOrganizationAsync(org, ct);
        using var replay = await Mutate(owner, HttpMethod.Post, path, body, key.ToString());
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadAsStringAsync(ct));
        using var conflict = await Mutate(owner, HttpMethod.Post, path, new { name = "Different creation", description = "Reviewed" }, key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(current, await store.FindOrganizationAsync(org, ct)); Assert.Single(await store.ListOrganizationsForUserAsync(actor, ct));
    }

    [Fact]
    public async Task Organization_creation_replay_requires_current_access_and_original_account()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var otherActor = (await other.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var body = new { name = "Private creation" };
        var path = $"/organizations?expectedActorId={actor}";
        using var created = await Mutate(owner, HttpMethod.Post, path, body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        using var wrongAccount = await Mutate(other, HttpMethod.Post, path, body, key);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAccount.StatusCode);
        Assert.Empty(await store.ListOrganizationsForUserAsync(otherActor, ct));
        await store.AddOrRestoreMemberAsync(org, otherActor, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        using var leave = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/leave", new { });
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        using var denied = await Mutate(owner, HttpMethod.Post, path, body, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Private creation", await denied.Content.ReadAsStringAsync(ct));
        Assert.False((await store.FindMembershipAsync(org, actor, ct))!.Active);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var rejoined = await store.FindMembershipAsync(org, actor, ct);
        using var recovered = await Mutate(owner, HttpMethod.Post, path, body, key);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Equal(rejoined, await store.FindMembershipAsync(org, actor, ct));
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111111111111111111111111111")]
    public async Task Organization_creation_invalid_retry_key_creates_no_parent_or_owner(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var denied = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invalid key creation" }, key);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Empty(await app.Services.GetRequiredService<IOrganizationStore>().ListOrganizationsForUserAsync(actor, ct));
    }

    [Fact]
    public async Task Organization_creation_final_session_expiry_rolls_back_parent_owner_and_receipt()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new SignInReceiptExpiryClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid();
        var org = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"strataai:organization:create:v1:{actor:D}:{key:D}"))[..16]);
        var receipts = app.Services.GetRequiredService<IOrganizationCreationReplayStore>();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var hash = app.Services.GetRequiredService<ISecureTokenService>().Hash(cookie.Split('=', 2)[1]);
        var session = await identities.FindRevocationSessionProofAsync(hash, ct); Assert.NotNull(session);
        var observed = false;
        clock.AfterReceipt = () => {
            var receipt = receipts.ReadAsync(org, actor, key, ct).GetAwaiter().GetResult();
            if (receipt is null) return null;
            Assert.Equal("Uncommitted creation", receipt.Result.Organization.Name);
            Assert.NotNull(store.FindMembershipAsync(org, actor, ct).GetAwaiter().GetResult());
            observed = true; return session.ExpiresAt;
        };
        var body = new { name = "Uncommitted creation" };
        using var denied = await Mutate(owner, HttpMethod.Post, "/organizations", body, key.ToString());
        Assert.True(observed); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("Uncommitted creation", await denied.Content.ReadAsStringAsync(ct));
        clock.AfterReceipt = null;
        Assert.Null(await store.FindOrganizationAsync(org, ct)); Assert.Null(await store.FindMembershipAsync(org, actor, ct));
        Assert.Null(await receipts.ReadAsync(org, actor, key, ct));
        Assert.Equal(session, await identities.FindRevocationSessionProofAsync(hash, ct));
        using var retry = await Mutate(owner, HttpMethod.Post, "/organizations", body, key.ToString());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Post, "/organizations", body, key.ToString());
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Single(await store.ListOrganizationsForUserAsync(actor, ct));
    }
}
