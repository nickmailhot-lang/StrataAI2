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
    [Fact]
    // PRD-01/03-TC-01/04/05/10: canonical metadata requires current internal membership.
    public async Task ARCH02_surface_admission_requires_current_separate_grants_and_returns_no_private_metadata()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var portal = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(portal); await RegisterAndLogin(outsider);
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Private admission fixture" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var route = $"/organizations/{org}/surface-access";
        using var internalOwner = await owner.GetAsync(route + "?surface=INTERNAL", ct);
        Assert.Equal(HttpStatusCode.OK, internalOwner.StatusCode);
        using var metadata = await owner.GetAsync($"/organizations/{org}", ct);
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.True(metadata.Headers.CacheControl!.Private); Assert.True(metadata.Headers.CacheControl.NoStore);
        var detail = await metadata.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(org, detail.GetProperty("organization").GetProperty("id").GetGuid());
        Assert.Equal("Private admission fixture", detail.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal(1, detail.GetProperty("organization").GetProperty("version").GetInt64());
        Assert.Equal((int)OrganizationRole.Owner, detail.GetProperty("role").GetInt32());
        using var ownerPortal = await owner.GetAsync(route + "?surface=PORTAL", ct);
        Assert.Equal(HttpStatusCode.NotFound, ownerPortal.StatusCode);
        using var outsiderInternal = await outsider.GetAsync(route + "?surface=INTERNAL", ct);
        Assert.Equal(HttpStatusCode.NotFound, outsiderInternal.StatusCode);
        using var outsiderMetadata = await outsider.GetAsync($"/organizations/{org}", ct);
        Assert.Equal(HttpStatusCode.NotFound, outsiderMetadata.StatusCode);
        Assert.True(outsiderMetadata.Headers.CacheControl!.Private); Assert.True(outsiderMetadata.Headers.CacheControl.NoStore);
        Assert.Equal("organization_not_found", (await outsiderMetadata.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.DoesNotContain("Private admission fixture", await outsiderMetadata.Content.ReadAsStringAsync(ct));
        using var emptyMetadata = await owner.GetAsync($"/organizations/{Guid.Empty}", ct);
        Assert.Equal(HttpStatusCode.NotFound, emptyMetadata.StatusCode);
        using var outsiderPortal = await outsider.GetAsync(route + "?surface=PORTAL", ct);
        Assert.Equal(HttpStatusCode.NotFound, outsiderPortal.StatusCode);
        using var invalid = await owner.GetAsync(route + "?surface=UNKNOWN", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_access_surface", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        foreach (var surface in new[] { "INTERNAL", "PORTAL" })
        {
            using var empty = await owner.GetAsync($"/organizations/{Guid.Empty}/surface-access?surface={surface}", ct);
            Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
            Assert.Equal("organization_not_found", (await empty.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }

        // Seed the existing separate Portal grant via invitation acceptance;
        // never manufacture internal membership as a side effect of admission.
        var profile = await portal.GetFromJsonAsync<JsonElement>("/me", ct);
        var user = profile.GetProperty("id").GetGuid(); var email = profile.GetProperty("email").GetString()!;
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var now = DateTimeOffset.UtcNow; var hash = Guid.NewGuid().ToString("N");
        await invitations.CreateAsync(new(Guid.NewGuid(), org, email, email.ToUpperInvariant(), hash,
            InvitationSurface.Portal, "OWNER", ownerId, now, now.AddHours(1), null, null), ct);
        Assert.True((await invitations.AcceptAsync(hash, user, email.ToUpperInvariant(), now, ct)).Succeeded);
        using var portalAdmitted = await portal.GetAsync(route + "?surface=PORTAL", ct);
        Assert.Equal(HttpStatusCode.OK, portalAdmitted.StatusCode);
        var admission = await portalAdmitted.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(new[] { "organizationId", "surface" }, admission.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(org, admission.GetProperty("organizationId").GetGuid()); Assert.Equal("PORTAL", admission.GetProperty("surface").GetString());
        using var portalInternal = await portal.GetAsync(route + "?surface=INTERNAL", ct);
        Assert.Equal(HttpStatusCode.NotFound, portalInternal.StatusCode);
        using var portalMetadata = await portal.GetAsync($"/organizations/{org}", ct);
        Assert.Equal(HttpStatusCode.NotFound, portalMetadata.StatusCode);
        Assert.DoesNotContain("Private admission fixture", await portalMetadata.Content.ReadAsStringAsync(ct));
        using var differentScope = await portal.GetAsync($"/organizations/{Guid.NewGuid()}/surface-access?surface=PORTAL", ct);
        Assert.Equal(HttpStatusCode.NotFound, differentScope.StatusCode);
        var members = app.Services.GetRequiredService<IOrganizationStore>();
        Assert.Null(await members.FindMembershipAsync(org, user, ct));
        await members.AddOrRestoreMemberAsync(org, user, OrganizationRole.Member, now, ct);
        using var both = await portal.GetAsync(route + "?surface=INTERNAL", ct); Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        using var memberMetadata = await portal.GetAsync($"/organizations/{org}", ct); Assert.Equal(HttpStatusCode.OK, memberMetadata.StatusCode);
        var memberDetail = await memberMetadata.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal((int)OrganizationRole.Member, memberDetail.GetProperty("role").GetInt32());
        Assert.Equal(1, memberDetail.GetProperty("organization").GetProperty("version").GetInt64());
        Assert.Equal("Private admission fixture", memberDetail.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal(OrganizationRemoveMemberResult.Removed, await members.RemoveMemberAsync(org, user, now.AddSeconds(1), ct));
        using var revoked = await portal.GetAsync(route + "?surface=INTERNAL", ct); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        using var revokedMetadata = await portal.GetAsync($"/organizations/{org}", ct); Assert.Equal(HttpStatusCode.NotFound, revokedMetadata.StatusCode);
        using var stillPortal = await portal.GetAsync(route + "?surface=PORTAL", ct); Assert.Equal(HttpStatusCode.OK, stillPortal.StatusCode);
        Assert.True(await members.MarkDeletingAsync(org, 1, now.AddSeconds(2), ct));
        using var inactivePortal = await portal.GetAsync(route + "?surface=PORTAL", ct); Assert.Equal(HttpStatusCode.NotFound, inactivePortal.StatusCode);
        using var inactiveInternal = await owner.GetAsync(route + "?surface=INTERNAL", ct); Assert.Equal(HttpStatusCode.NotFound, inactiveInternal.StatusCode);
        using var inactiveMetadata = await owner.GetAsync($"/organizations/{org}", ct); Assert.Equal(HttpStatusCode.NotFound, inactiveMetadata.StatusCode);
        using var loggedOut = await Mutate(portal, HttpMethod.Post, "/auth/logout", new { }); Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        using var noSession = await portal.GetAsync(route + "?surface=PORTAL", ct); Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        using var noMetadataSession = await portal.GetAsync($"/organizations/{org}", ct); Assert.Equal(HttpStatusCode.Unauthorized, noMetadataSession.StatusCode);
        using var emptyNoSession = await portal.GetAsync($"/organizations/{Guid.Empty}/surface-access?surface=PORTAL", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, emptyNoSession.StatusCode);
    }
}
