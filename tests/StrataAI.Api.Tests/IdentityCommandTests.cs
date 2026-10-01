using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-03 / AUTH-FR-007/010: consistent preference validation before account creation.
    [Theory]
    [InlineData("en", "UTC", "invalid_locale")]
    [InlineData("en-CA", "Not/AZone", "invalid_timezone")]
    public async Task Registration_rejects_invalid_profile_preferences(string locale, string timezone, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var email = $"preferences-{Guid.NewGuid():N}@example.test";
        using var result = await Mutate(client, HttpMethod.Post, "/auth/register", new {
            email, password = "api-host-correct-horse", displayName = "Preferences", locale, timezone });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(code, (await result.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Null(await app.Services.GetRequiredService<IIdentityStore>().FindUserByNormalizedEmailAsync(email.ToUpperInvariant(), ct));
    }

    [Fact]
    public async Task Registration_and_profile_edit_return_browser_usable_canonical_timezones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var email = $"canonical-{Guid.NewGuid():N}@example.test";
        using var registration = await Mutate(client, HttpMethod.Post, "/auth/register", new {
            email, password = "api-host-correct-horse", displayName = "Preferences", locale = "en-CA", timezone = "Pacific Standard Time" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var user = (await registration.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("user");
        Assert.Equal("America/Los_Angeles", user.GetProperty("timezone").GetString());
        using var login = await Mutate(client, HttpMethod.Post, "/auth/login", new { email, password = "api-host-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var edit = await Mutate(client, HttpMethod.Patch, "/me", new { timezone = "Eastern Standard Time", version = 1 });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal("America/New_York", (await edit.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("timezone").GetString());
    }

    // PRD-02/60: preserve user history while disabling every active session.
    [Fact]
    public async Task Self_deactivation_preserves_account_and_rejects_every_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(first);
        var user = await first.GetFromJsonAsync<JsonElement>("/me", ct);
        Assert.Equal("ACTIVE", user.GetProperty("status").GetString());
        var id = user.GetProperty("id").GetGuid();
        using var login = await Mutate(other, HttpMethod.Post, "/auth/login", new { email = user.GetProperty("email").GetString(), password = "api-host-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var deactivated = await Mutate(first, HttpMethod.Post, "/me/deactivate", new { });
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        using var firstDenied = await first.GetAsync("/me", ct);
        using var otherDenied = await other.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, firstDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherDenied.StatusCode);
        var stored = await app.Services.GetRequiredService<IIdentityStore>().FindUserByIdAsync(id, ct);
        Assert.NotNull(stored);
        Assert.Equal(AccountStatus.Deactivated, stored.Status);
        Assert.Equal(user.GetProperty("email").GetString(), stored.Email);
    }
}
