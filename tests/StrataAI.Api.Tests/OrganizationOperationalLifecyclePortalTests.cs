using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("SUSPENDED")]
    [InlineData("DEACTIVATED")]
    public async Task PRD_27_Demo_operational_lifecycle_Portal_grant_survives_without_internal_or_lifecycle_authority(string inactiveStatus)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var portal = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(portal);
        var (_, parent) = await ConfigurationParent(owner, "Private Portal lifecycle parent", ct);
        var recipient = await portal.GetFromJsonAsync<JsonElement>("/me", ct);
        var recipientId = recipient.GetProperty("id").GetGuid();
        using var issued = await Mutate(owner, HttpMethod.Post, $"/organizations/{parent.Id}/invitations",
            new { email = recipient.GetProperty("email").GetString(), surface = "PORTAL", targetRole = "OWNER" });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invitation = await issued.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var accepted = await Mutate(portal, HttpMethod.Post, "/invitations/accept",
            new { token = invitation.GetProperty("invitationToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var grants = app.Services.GetRequiredService<IInvitationStore>();
        var members = app.Services.GetRequiredService<IOrganizationStore>();
        var route = $"/organizations/{parent.Id}/operational-lifecycle";
        var surface = $"/organizations/{parent.Id}/surface-access";

        async Task CheckBoundaries(bool active)
        {
            Assert.True(await grants.HasActivePortalAccessAsync(parent.Id, recipientId, ct));
            Assert.Null(await members.FindMembershipAsync(parent.Id, recipientId, ct));
            using var portalAccess = await portal.GetAsync(surface + "?surface=PORTAL", ct);
            Assert.Equal(active ? HttpStatusCode.OK : HttpStatusCode.NotFound, portalAccess.StatusCode);
            using var internalAccess = await portal.GetAsync(surface + "?surface=INTERNAL", ct);
            Assert.Equal(HttpStatusCode.NotFound, internalAccess.StatusCode);
            using var ownerPortal = await owner.GetAsync(surface + "?surface=PORTAL", ct);
            Assert.Equal(HttpStatusCode.NotFound, ownerPortal.StatusCode);
            foreach (var deniedRoute in new[] { route, route + "/events?afterVersion=0" })
            {
                using var denied = await portal.GetAsync(deniedRoute, ct);
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                Assert.DoesNotContain(parent.Name, await denied.Content.ReadAsStringAsync(ct));
            }
            using var deniedWrite = await Mutate(portal, HttpMethod.Patch, route,
                new { status = "SUSPENDED", version = parent.Version }, Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.NotFound, deniedWrite.StatusCode);
            Assert.DoesNotContain(parent.Name, await deniedWrite.Content.ReadAsStringAsync(ct));
        }

        await CheckBoundaries(true);
        var key = Guid.NewGuid().ToString("D");
        using var inactive = await Mutate(owner, HttpMethod.Patch, route,
            new { status = inactiveStatus, version = parent.Version }, key);
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        var original = await inactive.Content.ReadFromJsonAsync<JsonElement>(ct);
        await CheckBoundaries(false);
        var review = await owner.GetFromJsonAsync<JsonElement>(route, ct);
        Assert.Equal(inactiveStatus, review.GetProperty("status").GetString());
        Assert.Equal(parent.Version + 1, review.GetProperty("version").GetInt64());
        using var restored = await Mutate(owner, HttpMethod.Patch, route,
            new { status = "ACTIVE", version = parent.Version + 1 }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await CheckBoundaries(true);
        using var retry = await Mutate(owner, HttpMethod.Patch, route,
            new { status = inactiveStatus, version = parent.Version }, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        var final = await owner.GetFromJsonAsync<JsonElement>(route, ct);
        Assert.Equal("ACTIVE", final.GetProperty("status").GetString());
        Assert.Equal(parent.Version + 2, final.GetProperty("version").GetInt64());
    }
}
