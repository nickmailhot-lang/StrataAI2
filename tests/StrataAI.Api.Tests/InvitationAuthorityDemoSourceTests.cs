using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_60_Demo_authority_source_simulates_bounded_delivery_and_rolls_back_recipient_effects()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var service = app.Services.GetRequiredService<IOrganizationService>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var codec = app.Services.GetRequiredService<IInvitationRecipientCursorCodec>();
        var now = app.Services.GetRequiredService<StrataAI.Application.Common.IClock>().UtcNow;
        var lastRecipient = Guid.NewGuid(); var futureRecipient = Guid.NewGuid();
        foreach (var id in new[] { lastRecipient, futureRecipient })
            Assert.True(await identities.TryCreateUserAsync(new UserIdentity(id, $"authority-{id:N}@example.test", $"authority-{id:N}@example.test".ToUpperInvariant(),
                "Authority recipient", null, "en-CA", "America/Vancouver", AccountStatus.Active, true, "unused", now, now, 1), null, null, ct));
        for (var i = 1; i <= 206; i++)
        {
            var email = i == 205 ? $"authority-{lastRecipient:N}@example.test" : i == 206 ? $"authority-{futureRecipient:N}@example.test" : "demo@strataai.test";
            var at = i == 206 ? now.AddDays(1) : now.AddSeconds(-1).AddTicks(i);
            await invitations.CreateAsync(new InvitationRecord(Guid.NewGuid(), f.Organization, email, email.ToUpperInvariant(),
                i.ToString("x64"), InvitationSurface.Portal, "OWNER", f.Owner, at, now.AddDays(7), null, null), ct);
        }
        var start = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct);
        var lastStart = await replay.ReadAsync(lastRecipient, null, cancellationToken: ct);
        var futureStart = await replay.ReadAsync(futureRecipient, null, cancellationToken: ct);
        Assert.True(start.Succeeded); Assert.True(lastStart.Succeeded); Assert.True(futureStart.Succeeded);
        var before = (await store.FindOrganizationAsync(f.Organization, ct))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync<bool>(f.Organization, f.Owner, null, false, async () => {
            await store.UpdateOrganizationAsync(f.Organization, "Tentative delivered source", null, null, before.Version, now, ct);
            await store.AppendAuditAsync(f.Organization, f.Owner, "ORGANIZATION_UPDATED", "Organization", f.Organization, "delivered-rollback", ct);
            throw new InvalidOperationException("Injected failure after Demo page/effect delivery.");
        }, ct));
        Assert.Equal(before, await store.FindOrganizationAsync(f.Organization, ct));
        Assert.True((await replay.IsCursorCurrentAsync(DemoRecipient, start.Value!.Cursor, ct)).Value);
        Assert.True((await replay.IsCursorCurrentAsync(lastRecipient, lastStart.Value!.Cursor, ct)).Value);
        var key = Guid.NewGuid();
        Assert.True((await service.UpdateAsync(f.Organization, f.Owner, "Committed delivered source", null, null, before.Version, "committed-authority", ct, key)).Succeeded);
        Assert.True((await service.UpdateAsync(f.Organization, f.Owner, "Committed delivered source", null, null, before.Version, "same-key-retry", ct, key)).Succeeded);
        foreach (var pair in new[] { (Id: DemoRecipient, Email: "DEMO@STRATAAI.TEST", Cursor: start.Value.Cursor),
            (Id: lastRecipient, Email: $"authority-{lastRecipient:N}@example.test".ToUpperInvariant(), Cursor: lastStart.Value.Cursor) })
        {
            Assert.False((await replay.IsCursorCurrentAsync(pair.Id, pair.Cursor, ct)).Value);
            var reset = await replay.ReadAsync(pair.Id, pair.Cursor, cancellationToken: ct);
            Assert.True(reset.Succeeded); Assert.True(reset.Value!.ResetRequired); Assert.Empty(reset.Value.Events);
            Assert.True(codec.TryDecode(new(pair.Id, pair.Email, 1, 1), reset.Value.Cursor, out _));
            var quiet = await replay.ReadAsync(pair.Id, reset.Value.Cursor, cancellationToken: ct);
            Assert.True(quiet.Succeeded); Assert.False(quiet.Value!.ResetRequired); Assert.Empty(quiet.Value.Events);
        }
        Assert.True((await replay.IsCursorCurrentAsync(futureRecipient, futureStart.Value!.Cursor, ct)).Value);
        Assert.Null(await store.FindMembershipAsync(f.Organization, lastRecipient, ct));
    }
    [Fact]
    public async Task PRD_60_Demo_authority_source_refuses_a_proof_from_an_earlier_command()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var before = (await store.FindOrganizationAsync(f.Organization, ct))!;
        Assert.True((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () => {
            await store.UpdateOrganizationAsync(f.Organization, "Unaudited fixture mutation", null, null, before.Version, DateTimeOffset.UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () => {
            await store.AppendAuditAsync(f.Organization, f.Owner, "ORGANIZATION_UPDATED", "Organization", f.Organization, "late-fabricated-source", ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_60_Demo_authority_source_is_future_canonical_and_rolls_back_with_its_Organization_command(bool removal)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var service = app.Services.GetRequiredService<IOrganizationService>();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var before = (await store.FindOrganizationAsync(f.Organization, ct))!;
        var membership = (await store.FindMembershipAsync(f.Organization, f.Recipient, ct))!;
        var entityType = removal ? "User" : "Organization";
        var entity = removal ? f.Recipient : f.Organization;
        var eventType = removal ? "ORGANIZATION_MEMBER_REMOVED" : "ORGANIZATION_UPDATED";
        // An audit without its actual tentative transition must be rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () => {
            await store.AppendAuditAsync(f.Organization, f.Owner, eventType, entityType, entity, "unproven-source", ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct));
        // Failure occurs after the real mutation: proof/source/dedup state and
        // domain state must all roll back, permitting the exact same revision.
        if (removal)
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveMemberAsync(f.Organization, f.Owner, f.Recipient, new string('x', 65), ct));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(f.Organization, f.Owner, "Failed authority name", null, null, before.Version, new string('x', 65), ct));
        Assert.Equal(before, await store.FindOrganizationAsync(f.Organization, ct));
        Assert.Equal(membership, await store.FindMembershipAsync(f.Organization, f.Recipient, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync<bool>(f.Organization, f.Owner, null, false, async () => {
            if (removal)
                Assert.Equal(OrganizationRemoveMemberResult.Removed, await store.RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct));
            else
                Assert.NotNull(await store.UpdateOrganizationAsync(f.Organization, "Tentative late failure", null, null, before.Version, DateTimeOffset.UtcNow, ct));
            await store.AppendAuditAsync(f.Organization, f.Owner, eventType, entityType, entity, "tentative-actual-source", ct);
            throw new InvalidOperationException("Injected failure after authority publication.");
        }, ct));
        Assert.Equal(before, await store.FindOrganizationAsync(f.Organization, ct));
        Assert.Equal(membership, await store.FindMembershipAsync(f.Organization, f.Recipient, ct));
        if (removal)
            Assert.True((await service.RemoveMemberAsync(f.Organization, f.Owner, f.Recipient, "actual-removal-source", ct)).Succeeded);
        else
            Assert.True((await service.UpdateAsync(f.Organization, f.Owner, "Committed authority name", null, null, before.Version, "actual-parent-source", ct)).Succeeded);
        // A duplicate audit of the committed revision invents no new source.
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () => {
            await store.AppendAuditAsync(f.Organization, f.Owner, eventType, entityType, entity, "duplicate-authority-source", ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct));
    }
}
