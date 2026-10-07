using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
