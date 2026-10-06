using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Organization_metadata_expiry_after_receipt_restores_edit_and_original_session_before_same_key_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SignInReceiptExpiryClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Before expiry" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationMetadataReplayStore>();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var hash = app.Services.GetRequiredService<ISecureTokenService>().Hash(cookie.Split('=', 2)[1]);
        var session = await identities.FindRevocationSessionProofAsync(hash, ct); Assert.NotNull(session);
        var before = await organizations.FindOrganizationAsync(org, ct);
        var key = Guid.NewGuid(); var observed = false;
        clock.AfterReceipt = () => {
            var receipt = receipts.ReadAsync(org, actor, key, ct).GetAwaiter().GetResult();
            if (receipt is null) return null;
            Assert.Equal("Uncommitted metadata", receipt.Result.Name);
            Assert.Equal(before!.Version + 1, receipt.Result.Version);
            observed = true; return session.ExpiresAt;
        };
        var body = new { name = "Uncommitted metadata", version = before!.Version };
        using var denied = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
        Assert.True(observed); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("Uncommitted metadata", await denied.Content.ReadAsStringAsync(ct));
        clock.AfterReceipt = null;
        Assert.Equal(before, await organizations.FindOrganizationAsync(org, ct));
        Assert.Null(await receipts.ReadAsync(org, actor, key, ct));
        Assert.Equal(session, await identities.FindRevocationSessionProofAsync(hash, ct));
        Assert.NotNull(await identities.FindActiveSessionAsync(hash, clock.Instant, ct));
        using var retry = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key.ToString());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(before.Version + 1, (await organizations.FindOrganizationAsync(org, ct))!.Version);
    }

    [Fact]
    public async Task Concurrent_Organization_metadata_retries_return_one_committed_revision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Concurrent metadata" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var body = new { name = "Committed once", version = 1 };
        var responses = await Task.WhenAll(Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key),
            Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(await responses[0].Content.ReadAsStringAsync(ct), await responses[1].Content.ReadAsStringAsync(ct));
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        Assert.Equal(2, (await store.FindOrganizationAsync(org, ct))!.Version);
    }
    // PRD-03-TC-06/07/08: original acknowledgment does not reapply an older edit.
    [Fact]
    public async Task Organization_metadata_replay_preserves_later_edits_and_checks_current_permissions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var admin = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(admin);
        var actor = (await admin.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Original" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await store.AddOrRestoreMemberAsync(org, actor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var key = Guid.NewGuid();
        async Task<HttpResponseMessage> Send(string name)
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/organizations/{org}")
            { Content = JsonContent.Create(new { name, description = "Reviewed", logoUrl = (string?)null, version = 1 }) };
            request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("Idempotency-Key", key.ToString());
            return await admin.SendAsync(request, ct);
        }
        using var first = await Send("First edit"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var originalAck = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var later = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", new { name = "Later edit", version = 2 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var current = await store.FindOrganizationAsync(org, ct);
        using var replay = await Send("First edit"); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(originalAck.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        Assert.Equal(current, await store.FindOrganizationAsync(org, ct));
        using var conflict = await Send("Different edit"); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{actor}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var denied = await Send("First edit"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("First edit", await denied.Content.ReadAsStringAsync(ct));
        Assert.Equal(current, await store.FindOrganizationAsync(org, ct));
    }
}
