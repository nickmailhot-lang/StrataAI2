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
    // PRD-03/18: storage transition guards cannot restart a deleting parent.
    [Fact]
    public async Task Organization_store_deletion_transition_requires_active_parent_and_does_not_reapply()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "One lifecycle transition" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var initial = await store.FindOrganizationAsync(org, ct); Assert.NotNull(initial);
        Assert.False(await store.MarkDeletingAsync(org, initial.Version + 1, initial.UpdatedAt.AddMinutes(1), ct));
        Assert.Equal(initial, await store.FindOrganizationAsync(org, ct));
        Assert.True(await store.MarkDeletingAsync(org, initial.Version, initial.UpdatedAt.AddMinutes(1), ct));
        var deleting = await store.FindOrganizationAsync(org, ct); Assert.NotNull(deleting);
        Assert.Equal(OrganizationStatus.Deleting, deleting.Status); Assert.Equal(initial.Version + 1, deleting.Version);
        Assert.False(await store.MarkDeletingAsync(org, deleting.Version, deleting.UpdatedAt.AddMinutes(1), ct));
        Assert.Equal(deleting, await store.FindOrganizationAsync(org, ct));
    }

    // PRD-03-TC-06/07/08: original 202 acknowledges the request, not completed deletion.
    [Fact]
    public async Task Concurrent_Organization_deletion_retries_acknowledge_once_after_parent_becomes_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Deletion receipt" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid(); var path = $"/organizations/{org}?version=1&expectedActorId={actor}";
        var responses = await Task.WhenAll(Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()),
            Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()));
        try
        {
            foreach(var response in responses)
            {
                Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
                Assert.Equal($"/organizations/{org}/deletion-requests/{key}",response.Headers.Location!.ToString());
                Assert.Equal(key,(await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("requestId").GetGuid());
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var observation=await owner.GetAsync($"/organizations/{org}/deletion-requests/{key}?expectedActorId={actor}",ct);
        Assert.Equal(HttpStatusCode.OK,observation.StatusCode);
        var observed=await observation.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("PENDING",observed.GetProperty("state").GetString());Assert.Equal(key,observed.GetProperty("requestId").GetGuid());
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationDeletionReplayStore>();
        var current = await store.FindOrganizationAsync(org, ct); Assert.NotNull(current);
        Assert.Equal(OrganizationStatus.Deleting, current.Status); Assert.Equal(2, current.Version);
        var receipt = await receipts.ReadAsync(org, actor, key, ct); Assert.NotNull(receipt);
        using var canonical = await owner.GetAsync($"/organizations/{org}", ct); Assert.Equal(HttpStatusCode.NotFound, canonical.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        using var conflict = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}?version=2", new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var fresh = await Mutate(owner, HttpMethod.Delete, path, new { }, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.NotFound, fresh.StatusCode);
        Assert.Equal(current, await store.FindOrganizationAsync(org, ct)); Assert.Equal(receipt, await receipts.ReadAsync(org, actor, key, ct));
    }

    [Fact]
    public async Task Organization_deletion_recovery_requires_original_account_and_current_Owner_not_Admin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private deletion" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var path = $"/organizations/{org}?version=1&expectedActorId={actor}";
        using var switched = await Mutate(other, HttpMethod.Delete, path, new { }, key); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
        using var accepted = await Mutate(owner, HttpMethod.Delete, path, new { }, key); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var store = app.Services.GetRequiredService<IOrganizationStore>(); var current = await store.FindOrganizationAsync(org, ct);
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var member = await store.FindMembershipAsync(org, actor, ct);
        using var denied = await Mutate(owner, HttpMethod.Delete, path, new { }, key); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Private deletion", await denied.Content.ReadAsStringAsync(ct)); Assert.Equal(current, await store.FindOrganizationAsync(org, ct));
        Assert.Equal(member, await store.FindMembershipAsync(org, actor, ct));
        using var update = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", new { name = "Forbidden update", version = 2 });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode); Assert.Equal(current, await store.FindOrganizationAsync(org, ct));
    }

    [Fact]
    public async Task Organization_deletion_receipt_publication_then_session_expiry_rolls_back_parent_and_acknowledgment()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new SignInReceiptExpiryClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Expiry deletion" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>(); var before = await store.FindOrganizationAsync(org, ct);
        var member = await store.FindMembershipAsync(org, actor, ct); var key = Guid.NewGuid();
        var receipts = app.Services.GetRequiredService<IOrganizationDeletionReplayStore>();
        var identity = app.Services.GetRequiredService<IIdentityStore>();
        var hash = app.Services.GetRequiredService<ISecureTokenService>().Hash(cookie.Split('=', 2)[1]);
        var session = await identity.FindRevocationSessionProofAsync(hash, ct); Assert.NotNull(session); var observed = false;
        clock.AfterReceipt = () => {
            if (receipts.ReadAsync(org, actor, key, ct).GetAwaiter().GetResult() is null) return null;
            Assert.Equal(OrganizationStatus.Deleting, store.FindOrganizationAsync(org, ct).GetAwaiter().GetResult()!.Status);
            observed = true; return session.ExpiresAt;
        };
        var path = $"/organizations/{org}?version=1&expectedActorId={actor}";
        using var denied = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.True(observed); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); clock.AfterReceipt = null;
        Assert.Equal(before, await store.FindOrganizationAsync(org, ct)); Assert.Equal(member, await store.FindMembershipAsync(org, actor, ct));
        Assert.Null(await receipts.ReadAsync(org, actor, key, ct)); Assert.Equal(session, await identity.FindRevocationSessionProofAsync(hash, ct));
        using var retry = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()); Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString()); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(2, (await store.FindOrganizationAsync(org, ct))!.Version);
    }
}
