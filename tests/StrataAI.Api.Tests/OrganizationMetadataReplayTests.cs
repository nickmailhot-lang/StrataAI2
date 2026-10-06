using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
