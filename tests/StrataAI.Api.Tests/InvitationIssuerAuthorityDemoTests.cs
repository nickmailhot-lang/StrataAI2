using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_60_Demo_issuer_authority_rolls_back_and_deduplicates_across_Organizations(bool returnedFailure)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
            services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture()));
        using var owner = app.CreateClient(); using var issuer = app.CreateClient();
        var f = await NotificationFixture(app, owner, issuer, ct);
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var inner = app.Services.GetRequiredService<IdentityService>();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var codec = app.Services.GetRequiredService<IInvitationRecipientCursorCodec>();
        var now = app.Services.GetRequiredService<StrataAI.Application.Common.IClock>().UtcNow;
        var second = Guid.NewGuid(); var future = Guid.NewGuid();
        foreach (var id in new[] { second, future })
            Assert.True(await identities.TryCreateUserAsync(new UserIdentity(id, $"issuer-recipient-{id:N}@example.test",
                $"issuer-recipient-{id:N}@example.test".ToUpperInvariant(), "Recipient fixture", null, "en-CA", "America/Vancouver",
                AccountStatus.Active, true, "unused-fixture", now, now, 1), null, null, ct));
        // Explicit disposable routing fixtures, not authorization/HTTP evidence.
        // 205 owning scopes and 205 rows in the first exercise both page axes.
        async Task Seed(Guid organization, string email, DateTimeOffset at)
            => await invitations.CreateAsync(new InvitationRecord(Guid.NewGuid(), organization, email, email.ToUpperInvariant(),
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), InvitationSurface.Portal, "OWNER", f.Recipient,
                at, now.AddDays(7), null, null), ct);
        for (var i = 0; i < 205; i++)
            await Seed(i == 0 ? f.Organization : Guid.NewGuid(), "demo@strataai.test", now.AddSeconds(-2));
        for (var i = 0; i < 203; i++) await Seed(f.Organization, "demo@strataai.test", now.AddSeconds(-1).AddTicks(i));
        await Seed(f.Organization, $"issuer-recipient-{second:N}@example.test", now.AddSeconds(-1));
        await Seed(Guid.NewGuid(), $"issuer-recipient-{future:N}@example.test", now.AddDays(1));
        var demoStart = (await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct)).Value!;
        var secondStart = (await replay.ReadAsync(second, null, cancellationToken: ct)).Value!;
        var futureStart = (await replay.ReadAsync(future, null, cancellationToken: ct)).Value!;
        var original = await identities.FindUserByIdAsync(f.Recipient, ct);
        var originalEvents = (await identities.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(f.Recipient, async () => {
            await identities.AppendDomainEventAsync(f.Recipient, "USER_DEACTIVATED", "unproven", ct);
            return IdentityOperation<bool>.Success(true);
        }, ct));
        async Task<IdentityOperation<bool>> Failing()
        {
            Assert.True((await inner.DeactivateAsync(f.Recipient, "tentative-issuer", ct)).Succeeded);
            if (returnedFailure) return IdentityOperation<bool>.Failure("fixture_refusal");
            throw new InvalidOperationException("Injected failure after recipient authority delivery.");
        }
        if (returnedFailure) Assert.False((await unit.ExecuteDeactivationAsync(f.Recipient, Failing, ct)).Succeeded);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteDeactivationAsync(f.Recipient, Failing, ct));
        Assert.Equal(original, await identities.FindUserByIdAsync(f.Recipient, ct));
        Assert.True((await replay.IsCursorCurrentAsync(DemoRecipient, demoStart.Cursor, ct)).Value);
        Assert.True((await replay.IsCursorCurrentAsync(second, secondStart.Cursor, ct)).Value);
        var hash = app.Services.GetRequiredService<ISecureTokenService>().Hash(f.RecipientCookie.Split('=', 2)[1]);
        var key = Guid.NewGuid(); var correlation = new string('c', 120);
        Task<IdentityOperation<bool>> Commit() => unit.ExecuteRevocationAsync(f.Recipient, hash, key, IdentityRevocationKind.Deactivate,
            correlation, actor => inner.DeactivateAsync(actor, correlation, ct), ct);
        Assert.True((await Commit()).Succeeded); Assert.True((await Commit()).Succeeded);
        foreach (var pair in new[] { (Id: DemoRecipient, Email: "DEMO@STRATAAI.TEST", Cursor: demoStart.Cursor),
            (Id: second, Email: $"issuer-recipient-{second:N}@example.test".ToUpperInvariant(), Cursor: secondStart.Cursor) })
        {
            var reset = await replay.ReadAsync(pair.Id, pair.Cursor, cancellationToken: ct);
            Assert.True(reset.Succeeded); Assert.True(reset.Value!.ResetRequired); Assert.Empty(reset.Value.Events);
            Assert.True(codec.TryDecode(new(pair.Id, pair.Email, 1, 1), reset.Value.Cursor, out _));
            Assert.True((await replay.IsCursorCurrentAsync(pair.Id, reset.Value.Cursor, ct)).Value);
        }
        Assert.True((await replay.IsCursorCurrentAsync(future, futureStart.Cursor, ct)).Value);
        var events = (await identities.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events;
        Assert.Equal(originalEvents + 1, events.Count);
        Assert.Equal(correlation, Assert.Single(events, e => e.EventType == "USER_DEACTIVATED").CorrelationId);
    }
}
