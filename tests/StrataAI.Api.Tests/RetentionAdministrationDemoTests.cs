using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(null, null, "ORGANIZATION")]
    [InlineData(RetentionRecordClass.Document, null, "CLASS")]
    [InlineData(RetentionRecordClass.Document, "unclassified.category", "CATEGORY")]
    public async Task PRD_70_broad_holds_use_real_owning_administration_and_suspend_then_release_unclassified_work(
        RetentionRecordClass? recordClass, string? category, string scopeKind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(foreign);
        var (outsider, _) = await ConfigurationParent(foreign, "Other administration", ct);
        var authorization = app.Services.GetRequiredService<IRetentionHoldScopeAuthorization>();
        Assert.IsType<RetentionHoldAuthorization>(authorization);
        var holds = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var guard = app.Services.GetRequiredService<IRetentionWorkDisposition>();
        var scope = new RetentionHoldScope(f.Organization, recordClass, category, null);
        var id = Guid.NewGuid(); var key = Guid.NewGuid(); var releaseKey = Guid.NewGuid();
        Assert.False(await authorization.VerifyAsync(scope, f.Actor, ct));
        var applied = await holds.ApplyAsync(f.Organization, f.Actor, id, scope,
            "Private broad hold reason", key, "real-broad-apply", ct);
        Assert.True(applied.Succeeded); Assert.Equal(1, applied.Value!.Version);
        Assert.Equal(applied.Value, (await holds.ReadAsync(f.Organization, f.Actor, id, ct)).Value);
        Assert.False((await holds.ReadAsync(f.Organization, outsider, id, ct)).Succeeded);
        Assert.False((await holds.ReleaseAsync(f.Organization, outsider, id, 1, Guid.NewGuid(), "foreign-release", ct)).Succeeded);
        async Task SetRole(OrganizationRole role) => await ConfigurationOwned(unit, f.Organization, f.Actor, outsider, false, async () =>
        {
            await organizations.AddOrRestoreMemberAsync(f.Organization, outsider, role,
                app.Services.GetRequiredService<StrataAI.Application.Common.IClock>().UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        await SetRole(OrganizationRole.Member);
        Assert.False((await holds.ReadAsync(f.Organization, outsider, id, ct)).Succeeded);
        await SetRole(OrganizationRole.Admin);
        Assert.Equal(applied.Value, (await holds.ReadAsync(f.Organization, outsider, id, ct)).Value);
        await SetRole(OrganizationRole.Member);
        Assert.False((await holds.ReadAsync(f.Organization, outsider, id, ct)).Succeeded);
        Assert.False((await holds.ReleaseAsync(f.Organization, outsider, id, 1, Guid.NewGuid(), "withdrawn-release", ct)).Succeeded);
        await ConfigurationOwned(unit, f.Organization, f.Actor, null, false, async () =>
        {
            foreach (var (kind, identity) in new[] { (RetentionWorkDispositionKind.Board, f.Board),
                (RetentionWorkDispositionKind.List, f.List), (RetentionWorkDispositionKind.Card, f.Card),
                (RetentionWorkDispositionKind.Attachment, f.Attachment) })
                Assert.Equal(RetentionDispositionAdmission.Held, await guard.CheckAsync(f.Organization, kind, identity, ct));
            if (recordClass is null)
                Assert.Equal(RetentionDispositionAdmission.Held,
                    await guard.CheckAsync(f.Organization, RetentionWorkDispositionKind.CardComment, f.Comment, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var released = await holds.ReleaseAsync(f.Organization, f.Actor, id, 1, releaseKey, "real-broad-release", ct);
        Assert.True(released.Succeeded); Assert.Equal(RetentionHoldStatus.Released, released.Value!.Status);
        Assert.Equal(applied.Value, (await holds.ApplyAsync(f.Organization, f.Actor, id, scope,
            "Private broad hold reason", key, "original-broad-apply", ct)).Value);
        Assert.Equal(released.Value, (await holds.ReleaseAsync(f.Organization, f.Actor, id, 1,
            releaseKey, "original-broad-release", ct)).Value);
        await ConfigurationOwned(unit, f.Organization, f.Actor, null, false, async () =>
        {
            Assert.Equal(RetentionDispositionAdmission.Clear,
                await guard.CheckAsync(f.Organization, RetentionWorkDispositionKind.Board, f.Board, ct));
            Assert.Equal(2, app.Services.GetRequiredService<InMemoryRetentionHoldStore>().ReadHistory(f.Organization, id, ct).Count);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var audits = app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(f.Organization)
            .Where(a => a.EntityType == "RETENTION_HOLD").ToArray();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal(scopeKind, audit.HoldMetadata!.ScopeKind));
        var serialized = System.Text.Json.JsonSerializer.Serialize(audits);
        Assert.DoesNotContain("Private broad hold reason", serialized);
        if (category is not null) Assert.DoesNotContain(category, serialized);
    }

    [Fact]
    public async Task PRD_70_broad_authority_refuses_corrupt_hints_unowned_scope_and_cannot_replace_record_admission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct);
        var authorization = app.Services.GetRequiredService<IRetentionHoldScopeAuthorization>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var hint = new RetentionHoldReference(f.Organization, Guid.NewGuid(), 1, null, false, null, null, null, null, null);
        Assert.False(await authorization.VerifyAsync(hint, f.Actor, ct));
        await ConfigurationOwned(unit, f.Organization, f.Actor, null, false, async () =>
        {
            Assert.True(await authorization.VerifyAsync(hint, f.Actor, ct));
            foreach (var invalid in new[] { hint with { HoldId = Guid.Empty }, hint with { Version = 0 },
                hint with { RecordClass = (RetentionRecordClass)999 }, hint with { CategoryScoped = true },
                hint with { ContextVersion = 1 }, hint with { ClassificationBoardId = f.Board },
                hint with { ApplicationBoardId = f.Board }, hint with { ApplicationSourceVersion = 1 } })
                Assert.False(await authorization.VerifyAsync(invalid, f.Actor, ct));
            foreach (var invalid in new[] { new RetentionHoldScope(f.Organization, null, "category", null),
                new(f.Organization, RetentionRecordClass.Document, "bad category", null),
                new(f.Organization, (RetentionRecordClass)999, null, null),
                new(f.Organization, RetentionRecordClass.OperationalWork, null, f.Card) })
                Assert.False(await authorization.VerifyAsync(invalid, f.Actor, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
    }
}
