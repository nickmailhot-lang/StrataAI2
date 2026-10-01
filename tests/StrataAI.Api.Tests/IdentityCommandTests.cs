using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02/60: preserve user history while disabling every active session.
    [Fact]
    public async Task Self_deactivation_preserves_account_and_rejects_every_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(first);
        var user = await first.GetFromJsonAsync<JsonElement>("/me", ct);
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
