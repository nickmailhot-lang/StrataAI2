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
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_03_60_Demo_terminal_authority_invalidates_nonmember_after_accepted_actor_retirement_and_rolls_back_late_failure(bool lateFailure)
    {
        var ct = TestContext.Current.CancellationToken;
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(ct);
        TerminalPublicationFailure? failure = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(d => d.ServiceType == typeof(IDemoOrganizationDeletionCompletionPublisher));
            services.AddSingleton<IDemoOrganizationDeletionCompletionPublisher>(provider => failure = new(
                (IDemoOrganizationDeletionCompletionPublisher)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), stopped, false)
                { Armed = lateFailure });
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(recipient);
        var profile = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        var actor = profile.GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Retired issuer terminal fixture" });
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var invited = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations",
            new { email = profile.GetProperty("email").GetString(), surface = "PORTAL", targetRole = "OWNER" });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var invitation = (await invited.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var initial = await replay.ReadAsync(actor, null, cancellationToken: ct); Assert.True(initial.Succeeded);
        var request = Guid.NewGuid();
        using var accepted = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}?version=1", new { }, request.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.False((await replay.IsCursorCurrentAsync(actor, initial.Value!.Cursor, ct)).Value);
        // Real HTTP retirement revokes the original session; accepted work must
        // retain its immutable authority instead of borrowing that session.
        using var retired = await Mutate(owner, HttpMethod.Post, "/me/deactivate", new { });
        Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);
        var pending = await replay.ReadAsync(actor, null, cancellationToken: ct); Assert.True(pending.Succeeded);
        Assert.True((await replay.IsCursorCurrentAsync(actor, pending.Value!.Cursor, ct)).Value);
        var simulator = app.Services.GetRequiredService<IDemoOrganizationDeletionSimulation>();
        if (lateFailure)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => simulator.AdvanceAsync(ct));
            Assert.True((await replay.IsCursorCurrentAsync(actor, pending.Value.Cursor, ct)).Value);
            Assert.Equal(OrganizationStatus.Deleting, (await app.Services.GetRequiredService<IOrganizationStore>().FindOrganizationAsync(org, ct))!.Status);
            failure!.Armed = false;
        }
        Assert.True(await simulator.AdvanceAsync(ct)); Assert.False(await simulator.AdvanceAsync(ct));
        Assert.False((await replay.IsCursorCurrentAsync(actor, pending.Value.Cursor, ct)).Value);
        var completed = await replay.ReadAsync(actor, pending.Value.Cursor, cancellationToken: ct);
        Assert.True(completed.Succeeded); Assert.True(completed.Value!.ResetRequired); Assert.Empty(completed.Value.Events);
        Assert.True((await replay.IsCursorCurrentAsync(actor, completed.Value.Cursor, ct)).Value);
        var quiet = await replay.ReadAsync(actor, completed.Value.Cursor, cancellationToken: ct);
        Assert.True(quiet.Succeeded); Assert.False(quiet.Value!.ResetRequired); Assert.Empty(quiet.Value.Events);
        Assert.Null(await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(org, actor, ct));
        using var denied = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{invitation}/accept", new { });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal("invalid_or_expired_invitation", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var privateObservation = await recipient.GetAsync($"/organizations/{org}/deletion-requests/{request}", ct);
        Assert.Equal(HttpStatusCode.NotFound, privateObservation.StatusCode);
        using var graph = await recipient.GetAsync($"/organizations/{org}", ct); Assert.Equal(HttpStatusCode.NotFound, graph.StatusCode);
        using var account = await recipient.GetAsync("/me", ct); Assert.Equal(HttpStatusCode.OK, account.StatusCode);
    }
}
