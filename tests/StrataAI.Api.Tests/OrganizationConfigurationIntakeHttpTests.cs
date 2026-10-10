using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_intake_HTTP_empty_private_page_cursor_admission_account_binding_and_archive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "HTTP intake parent", ct);
        var (_, other) = await ConfigurationParent(owner, "Foreign intake parent", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>(); var clock = app.Services.GetRequiredService<IClock>();
        async Task<BoardRecord> Board(Guid organization) => (await ConfigurationOwned(unit, organization, actor, null, false,
            async () => OrganizationOperation<BoardRecord>.Success(await work.CreateBoardAsync(organization, actor, Guid.NewGuid(),
                "Protected intake title", null, BoardVisibility.Private, "COLOR", "purple", clock.UtcNow, ct)), ct)).Value!;
        var local = await Board(parent.Id); var foreign = await Board(other.Id);
        var path = $"/organizations/{parent.Id}/configuration/intake-boards/{local.Id}/lists";
        using var empty = await owner.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode); Assert.True(empty.Headers.CacheControl!.Private); Assert.True(empty.Headers.CacheControl.NoStore);
        var page = await empty.Content.ReadFromJsonAsync<ConfigurationIntakeListPage>(ct);
        Assert.Equal(parent.Id, page!.OrganizationId); Assert.Equal(local.Id, page.Board.Id); Assert.Empty(page.Items); Assert.Null(page.NextAfterRank);
        foreach (var query in new[] { "?afterRank=", "?afterRank=1", "?afterRank=1&afterRank=2" })
        {
            using var invalid = await owner.GetAsync(path + query, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_configuration_intake_cursor", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            using var hidden = await owner.GetAsync($"/organizations/{Guid.NewGuid()}/configuration/intake-boards/{local.Id}/lists" + query, ct);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode); Assert.DoesNotContain(local.Name, await hidden.Content.ReadAsStringAsync(ct));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString("D"));
        using var switched = await owner.SendAsync(request, ct); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
        using var substituted = await owner.GetAsync($"/organizations/{parent.Id}/configuration/intake-boards/{foreign.Id}/lists", ct);
        Assert.Equal(HttpStatusCode.BadRequest, substituted.StatusCode); Assert.DoesNotContain(foreign.Name, await substituted.Content.ReadAsStringAsync(ct));
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.NotNull(await work.SetBoardLifecycleAsync(local.Id, BoardLifecycleState.Active, BoardLifecycleState.Archived, local.Version, clock.UtcNow, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        using var archived = await owner.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.BadRequest, archived.StatusCode);
        Assert.Equal("configuration_intake_unavailable", (await archived.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.DoesNotContain(local.Name, await archived.Content.ReadAsStringAsync(ct));
    }
}
