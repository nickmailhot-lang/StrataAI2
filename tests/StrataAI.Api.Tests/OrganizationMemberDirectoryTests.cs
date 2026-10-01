using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD03_exact_member_review_distinguishes_current_absence_only_for_authorized_administrators()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Exact membership review" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var user = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await store.AddOrRestoreMemberAsync(org, user, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/members/{user}", ct);
        Assert.Equal(org, current.GetProperty("organizationId").GetGuid()); Assert.Equal(0, current.GetProperty("actorRole").GetInt32());
        Assert.Equal(user, current.GetProperty("member").GetProperty("userId").GetGuid());
        using var denied = await member.GetAsync($"/organizations/{org}/members/{user}", ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{user}?expectedVersion=1", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var absent = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/members/{user}", ct);
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("member").ValueKind);
        using var foreign = await owner.GetAsync($"/organizations/{Guid.NewGuid()}/members/{user}", ct); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("email", await foreign.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task PRD03_member_removal_rejects_stale_consent_after_role_change()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Removal consent" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var user = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        await store.AddOrRestoreMemberAsync(org, user, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var observed = (await store.FindMembershipAsync(org, user, ct))!;
        await store.AddOrRestoreMemberAsync(org, user, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var changed = (await store.FindMembershipAsync(org, user, ct))!;
        Assert.True(changed.Version > observed.Version);
        using var stale = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{user}?expectedVersion={observed.Version}", new { });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("member_version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(changed, await store.FindMembershipAsync(org, user, ct));
        using var invalid = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{user}?expectedVersion=0", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var unrelated = await Mutate(member, HttpMethod.Delete, $"/organizations/{Guid.NewGuid()}/members/{user}?expectedVersion=0", new { });
        Assert.Equal(HttpStatusCode.NotFound, unrelated.StatusCode);
        using var current = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{user}?expectedVersion={changed.Version}", new { });
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
        Assert.False((await store.FindMembershipAsync(org, user, ct))!.Active);
    }

    [Fact]
    public async Task PRD03_admin_member_directory_is_bounded_and_contiguous_without_private_identity_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Paged directory" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var members = app.Services.GetRequiredService<IOrganizationStore>(); var identities = app.Services.GetRequiredService<IIdentityStore>();
        var expected = new HashSet<Guid> { (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid() };
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 66; i++)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"); var email = $"directory-{i}@example.test";
            Assert.True(await identities.TryCreateUserAsync(new(id, email, email.ToUpperInvariant(), $"Directory member {i}",
                null, "en-CA", "UTC", AccountStatus.Active, true, "unused-directory-fixture-hash", now, now, 1), null, null, ct));
            await members.AddOrRestoreMemberAsync(org, id, OrganizationRole.Member, now, ct); expected.Add(id);
        }
        var first = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/members", ct);
        Assert.Equal(org, first.GetProperty("organizationId").GetGuid()); Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetGuid();
        Assert.Equal(cursor, first.GetProperty("items")[49].GetProperty("userId").GetGuid());
        var second = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/members?after={cursor}", ct);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var rows = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(67, rows.Length); Assert.True(expected.SetEquals(rows.Select(row => row.GetProperty("userId").GetGuid())));
        var actual = rows.Select(row => row.GetProperty("userId").GetGuid().ToString("N")).ToArray();
        Assert.Equal(actual.Order(StringComparer.Ordinal), actual);
        Assert.DoesNotContain("passwordHash", first.GetRawText()); Assert.DoesNotContain("tokenHash", first.GetRawText());
        Assert.DoesNotContain("emailNormalized", first.GetRawText());
    }

    [Fact]
    public async Task PRD03_member_directory_rejects_unrelated_members_and_demoted_administrators()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Protected directory" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var user = (await other.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        foreach (var role in new OrganizationRole?[] { null, OrganizationRole.Member, OrganizationRole.Admin, OrganizationRole.Member })
        {
            if (role is not null) await store.AddOrRestoreMemberAsync(org, user, role.Value, DateTimeOffset.UtcNow, ct);
            using var response = await other.GetAsync($"/organizations/{org}/members", ct);
            Assert.Equal(role == OrganizationRole.Admin ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            if (role != OrganizationRole.Admin) Assert.DoesNotContain("Protected directory", await response.Content.ReadAsStringAsync(ct));
        }
        using var wrong = await owner.GetAsync($"/organizations/{Guid.NewGuid()}/members", ct); Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var badCursor = await owner.GetAsync($"/organizations/{org}/members?after=not-a-cursor", ct); Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
        using var emptyCursor = await owner.GetAsync($"/organizations/{org}/members?after={Guid.Empty}", ct); Assert.Equal(HttpStatusCode.BadRequest, emptyCursor.StatusCode);
    }

    [Fact]
    public async Task PRD03_directory_retains_historical_membership_without_counting_an_inactive_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(other);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Historical directory" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var user = (await other.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, user, OrganizationRole.Owner, DateTimeOffset.UtcNow, ct);
        Assert.True(await app.Services.GetRequiredService<IIdentityStore>().DeactivateUserAsync(user, DateTimeOffset.UtcNow, ct));
        var page = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/members", ct);
        var historical = page.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("userId").GetGuid() == user);
        Assert.Equal("DEACTIVATED", historical.GetProperty("accountStatus").GetString()); Assert.False(historical.GetProperty("isUsableOwner").GetBoolean());
        Assert.Single(page.GetProperty("items").EnumerateArray(), row => row.GetProperty("isUsableOwner").GetBoolean());
        using var frozen = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}?version=1", new { }); Assert.Equal(HttpStatusCode.Accepted, frozen.StatusCode);
        using var hidden = await owner.GetAsync($"/organizations/{org}/members", ct); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
