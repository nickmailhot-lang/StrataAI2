using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_Demo_configuration_publication_requires_an_atomic_private_audit_and_separate_recipient_stream()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Configuration publication", ct);
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var sync = app.Services.GetRequiredService<OrganizationConfigurationSynchronizationService>();
        var reader = app.Services.GetRequiredService<IOrganizationConfigurationEventReader>();
        var metadata = app.Services.GetRequiredService<IOrganizationMetadataEventReader>();
        var audits = app.Services.GetRequiredService<InMemoryWorkManagementStore>();
        var publicBefore = await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
            OrganizationOperation<long>.Success(await metadata.GetHeadAsync(parent.Id, ct)), ct);
        var initial = await sync.ReadAsync(parent.Id, actor, null, ct: ct);
        Assert.True(initial.Succeeded, initial.ErrorCode); Assert.True(initial.Value!.ResetRequired); Assert.Empty(initial.Value.Events);
        var service = app.Services.GetRequiredService<OrganizationConfigurationService>();
        var correlation = new string('r', 256);
        var changed = await service.ChangeAsync(parent.Id, actor,
            new OrganizationConfigurationData("Private immutable legal name", "CA-BC", "UTC"), 0, Guid.NewGuid(), correlation, ct);
        Assert.True(changed.Succeeded, changed.ErrorCode);
        var source = changed.Value!;
        var audit = Assert.Single(audits.AuditSnapshot(parent.Id), row => row.EventType == "ORGANIZATION_CONFIGURATION_CHANGED");
        Assert.Equal(source.EventId, audit.Id); Assert.Equal(actor, audit.ActorId); Assert.Equal(source.UpdatedAt, audit.CreatedAt);
        Assert.Equal(parent.Id, audit.EntityId); Assert.Equal("OrganizationConfiguration", audit.EntityType); Assert.Equal(correlation, audit.CorrelationId);
        Assert.DoesNotContain(source.Configuration.LegalName, JsonSerializer.Serialize(audit), StringComparison.Ordinal);
        var page = await sync.ReadAsync(parent.Id, actor, initial.Value.Cursor, ct: ct);
        Assert.True(page.Succeeded, page.ErrorCode); Assert.False(page.Value!.ResetRequired);
        Assert.False(page.Value.Pending); Assert.False(page.Value.HasMore);
        var delivered = Assert.Single(page.Value.Events);
        Assert.Equal(source.EventId, delivered.EventId); Assert.Equal(source.UpdatedAt, delivered.CreatedAt);
        Assert.Equal(source.Version, delivered.Version); Assert.Equal(correlation, delivered.CorrelationId);
        Assert.True((await sync.IsCursorCurrentAsync(parent.Id, actor, page.Value.Cursor, ct)).Value);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            var batch = await reader.ReadAsync(parent.Id, 0, 1, ct);
            Assert.Equal(1, batch.Head); var row = Assert.Single(batch.Rows);
            Assert.NotNull(row.ReadyAt); Assert.True(row.ReadyAt >= row.Source.CreatedAt); Assert.Null(row.StoredSourceTime);
            Assert.Equal(publicBefore.Value, await metadata.GetHeadAsync(parent.Id, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetHeadAsync(parent.Id, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(parent.Id, 0, 1, ct));
    }

    [Fact]
    public async Task PRD_27_Demo_configuration_aborted_command_restores_audit_and_first_publication()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Aborted publication", ct);
        var store = app.Services.GetRequiredService<IOrganizationConfigurationStore>();
        var reader = app.Services.GetRequiredService<IOrganizationConfigurationEventReader>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var audits = app.Services.GetRequiredService<InMemoryWorkManagementStore>();
        var at = app.Services.GetRequiredService<IClock>().UtcNow;
        var source = ConfigurationRevision(parent, actor, 1, at, at, new("Uncommitted private legal name", "CA-BC", "UTC"));
        var result = await unit.ExecuteAsync<bool>(parent.Id, actor, null, false, async () =>
        {
            Assert.Equal(OrganizationConfigurationWriteResult.Written, await store.WriteAsync(0, source, ConfigurationReceipt(source), ct));
            Assert.Contains(audits.AuditSnapshot(parent.Id), row => row.Id == source.EventId);
            Assert.NotNull(Assert.Single((await reader.ReadAsync(parent.Id, 0, 1, ct)).Rows).ReadyAt);
            return OrganizationOperation<bool>.Failure("injected_publication_rollback");
        }, ct);
        Assert.False(result.Succeeded); Assert.Equal("injected_publication_rollback", result.ErrorCode);
        Assert.DoesNotContain(audits.AuditSnapshot(parent.Id), row => row.Id == source.EventId);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.Equal(0, await reader.GetHeadAsync(parent.Id, ct));
            Assert.Empty((await reader.ReadAsync(parent.Id, 0, 1, ct)).Rows);
            Assert.Null(await store.ReadAsync(parent.Id, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
    }
}
