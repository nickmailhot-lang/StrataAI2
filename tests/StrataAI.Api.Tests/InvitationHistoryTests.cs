using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // ONBOARD-FR-003/004, PRD-60-TC-02/05/10: bounded administrative
    // history preserves lifecycle while excluding bearer and provider secrets.
    [Fact]
    public async Task Administrative_invitation_history_is_bounded_and_contains_no_proof()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var me = await owner.GetFromJsonAsync<JsonElement>("/me", ct); var actor = me.GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Invitation history" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IInvitationStore>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var ids = new HashSet<Guid>(); var hashes = new List<string>(); var now = DateTimeOffset.UtcNow;
        for (var n = 0; n < 52; n++)
        {
            var id = Guid.NewGuid(); ids.Add(id); var hash = tokens.Hash(Guid.NewGuid().ToString()); hashes.Add(hash);
            await store.CreateAsync(new(id, org, $"history-{n}@example.test", $"HISTORY-{n}@EXAMPLE.TEST", hash,
                InvitationSurface.Internal, "MEMBER", actor, now, now.AddDays(7), null, n == 0 ? now : null), ct);
        }
        var page = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/invitations", ct);
        Assert.Equal(50, page.GetProperty("items").GetArrayLength());
        var cursor = page.GetProperty("nextCursor").GetGuid();
        var second = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/invitations?after={cursor}", ct);
        Assert.Equal(2, second.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var rows = page.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.True(ids.SetEquals(rows.Select(row => row.GetProperty("id").GetGuid())));
        Assert.Single(rows, row => row.GetProperty("revokedAt").ValueKind != JsonValueKind.Null);
        foreach (var row in rows) Assert.Equal(JsonValueKind.Null, row.GetProperty("deliveryState").ValueKind);
        var json = page.GetRawText() + second.GetRawText();
        foreach (var hash in hashes) Assert.DoesNotContain(hash, json, StringComparison.Ordinal);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", json, StringComparison.OrdinalIgnoreCase);
        using var invalid = await owner.GetAsync($"/organizations/{org}/invitations?after=invalid", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    // PRD-60-TC-04/06: invitations expose recipient data only to current admins.
    [Fact]
    public async Task Invitation_history_rejects_members_foreign_accounts_and_removed_administrators()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        var me = await other.GetFromJsonAsync<JsonElement>("/me", ct); var target = me.GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private invitation history" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        async Task Denied()
        {
            using var response = await other.GetAsync($"/organizations/{org}/invitations", ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Private invitation history", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }
        await Denied();
        await organizations.AddOrRestoreMemberAsync(org, target, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        await Denied();
        await organizations.AddOrRestoreMemberAsync(org, target, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        using var allowed = await other.GetAsync($"/organizations/{org}/invitations", ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await organizations.RemoveMemberAsync(org, target, DateTimeOffset.UtcNow, ct);
        await Denied();
    }
}
