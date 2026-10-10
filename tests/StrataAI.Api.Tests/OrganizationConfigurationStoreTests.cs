using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static async Task<OrganizationOperation<T>> ConfigurationOwned<T>(IOrganizationUnitOfWork unit,
        Guid organization, Guid actor, Guid? target, bool creating, Func<Task<OrganizationOperation<T>>> operation, CancellationToken ct)
    {
        var result = await unit.ExecuteAsync(organization, actor, target, creating, operation, ct);
        Assert.True(result.Succeeded, result.ErrorCode);
        return result;
    }
    private static async Task<(Guid Actor, OrganizationRecord Parent)> ConfigurationParent(HttpClient owner, string name, CancellationToken ct)
    {
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (actor, body.GetProperty("organization").Deserialize<OrganizationRecord>(JsonSerializerOptions.Web)!);
    }

    private static OrganizationConfigurationRecord ConfigurationRevision(OrganizationRecord parent, Guid actor,
        long version, DateTimeOffset createdAt, DateTimeOffset at, OrganizationConfigurationData data) =>
        new(parent.Id, version, data, parent.Name, parent.Type, parent.Version, actor, Guid.NewGuid(), "configuration-fixture", createdAt, at);

    private static OrganizationConfigurationReceipt ConfigurationReceipt(OrganizationConfigurationRecord record) =>
        new(record.ActorId, Guid.NewGuid(), new string('A', 64), record, record.UpdatedAt.AddHours(24));

    [Fact]
    public async Task PRD_27_Demo_configuration_rollback_restores_state_history_event_receipt_and_registration_index()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Configuration parent", ct);
        var store = app.Services.GetRequiredService<IOrganizationConfigurationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var clock = app.Services.GetRequiredService<IClock>();
        var at = clock.UtcNow;
        var first = ConfigurationRevision(parent, actor, 1, at, at, new("Reviewed legal name", "CA-BC", "UTC", CorporationIdentifier: "REG-FIRST"));
        var firstReceipt = ConfigurationReceipt(first);
        var saved = await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
            OrganizationOperation<OrganizationConfigurationWriteResult>.Success(await store.WriteAsync(0, first, firstReceipt, ct)), ct);
        Assert.Equal(OrganizationConfigurationWriteResult.Written, saved.Value);
        var second = ConfigurationRevision(parent, actor, 2, first.CreatedAt, clock.UtcNow,
            first.Configuration with { CorporationIdentifier = "REG-ROLLED-BACK", LegalName = "Uncommitted legal name" });
        var secondReceipt = ConfigurationReceipt(second);
        var aborted = await unit.ExecuteAsync<bool>(parent.Id, actor, null, false, async () => {
            Assert.Equal(OrganizationConfigurationWriteResult.Written, await store.WriteAsync(1, second, secondReceipt, ct));
            return OrganizationOperation<bool>.Failure("injected_final_command_failure");
        }, ct);
        Assert.False(aborted.Succeeded);
        Assert.Equal("injected_final_command_failure", aborted.ErrorCode);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () => {
            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(await store.ReadAsync(parent.Id, ct)));
            Assert.Single(await store.ReadHistoryAsync(parent.Id, null, ct));
            var events = await store.ReadEventsAsync(parent.Id, 0, ct); Assert.Single(events);
            Assert.Equal(first.EventId, events[0].EventId); Assert.Equal(first.UpdatedAt, events[0].CreatedAt);
            Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, secondReceipt.Key, ct));
            Assert.NotNull(await store.ReadReceiptAsync(parent.Id, actor, firstReceipt.Key, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var (_, another) = await ConfigurationParent(owner, "Other configuration parent", ct);
        var nextAt = clock.UtcNow;
        var reclaimed = ConfigurationRevision(another, actor, 1, nextAt, nextAt, second.Configuration);
        var reclaimedResult = await ConfigurationOwned(unit, another.Id, actor, null, false, async () =>
            OrganizationOperation<OrganizationConfigurationWriteResult>.Success(await store.WriteAsync(0, reclaimed, ConfigurationReceipt(reclaimed), ct)), ct);
        Assert.Equal(OrganizationConfigurationWriteResult.Written, reclaimedResult.Value);
    }

    [Fact]
    public async Task PRD_27_Demo_configuration_deep_snapshots_do_not_retain_caller_or_reader_collection_mutations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Private snapshot parent", ct);
        var store = app.Services.GetRequiredService<IOrganizationConfigurationStore>(); var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var contacts = new[] { new OrganizationEmergencyContact("Original private contact", Phone: "Reviewed telephone") };
        var at = app.Services.GetRequiredService<IClock>().UtcNow;
        var record = ConfigurationRevision(parent, actor, 1, at, at, new("Reviewed legal name", "CA-BC", "UTC", EmergencyContacts: contacts));
        var receipt = ConfigurationReceipt(record);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () => {
            Assert.Equal(OrganizationConfigurationWriteResult.Written, await store.WriteAsync(0, record, receipt, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        contacts[0] = new("Changed caller contact", Phone: "Changed telephone");
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () => {
            var current = (await store.ReadAsync(parent.Id, ct))!;
            Assert.Equal("Original private contact", current.Configuration.EmergencyContacts![0].Name);
            ((IList<OrganizationEmergencyContact>)current.Configuration.EmergencyContacts)[0] = contacts[0];
            Assert.Equal("Original private contact", (await store.ReadAsync(parent.Id, ct))!.Configuration.EmergencyContacts![0].Name);
            Assert.Equal("Original private contact", (await store.ReadHistoryAsync(parent.Id, null, ct))[0].Configuration.EmergencyContacts![0].Name);
            Assert.Equal("Original private contact", (await store.ReadReceiptAsync(parent.Id, actor, receipt.Key, ct))!.Result.Configuration.EmergencyContacts![0].Name);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(parent.Id, ct));
    }

    [Fact]
    public async Task PRD_27_Demo_configuration_identifier_conflict_and_stale_version_have_no_protected_effects()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Existing configuration", ct);
        var (_, other) = await ConfigurationParent(owner, "Conflicting configuration", ct);
        var store = app.Services.GetRequiredService<IOrganizationConfigurationStore>(); var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var clock = app.Services.GetRequiredService<IClock>(); var at = clock.UtcNow;
        var original = ConfigurationRevision(parent, actor, 1, at, at, new("Reviewed name", "CA-BC", "UTC", CorporationIdentifier: "REG-EXAMPLE"));
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () => {
            Assert.Equal(OrganizationConfigurationWriteResult.Written, await store.WriteAsync(0, original, ConfigurationReceipt(original), ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        at = clock.UtcNow;
        var conflicting = ConfigurationRevision(other, actor, 1, at, at, original.Configuration with { Jurisdiction = "ca-bc", CorporationIdentifier = " reg-example " });
        var receipt = ConfigurationReceipt(conflicting);
        await ConfigurationOwned(unit, other.Id, actor, null, false, async () => {
            Assert.Equal(OrganizationConfigurationWriteResult.IdentifierConflict, await store.WriteAsync(0, conflicting, receipt, ct));
            Assert.Null(await store.ReadAsync(other.Id, ct)); Assert.Empty(await store.ReadHistoryAsync(other.Id, null, ct));
            Assert.Empty(await store.ReadEventsAsync(other.Id, 0, ct)); Assert.Null(await store.ReadReceiptAsync(other.Id, actor, receipt.Key, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () => {
            Assert.Equal(OrganizationConfigurationWriteResult.VersionConflict, await store.WriteAsync(0, original, ConfigurationReceipt(original), ct));
            Assert.Single(await store.ReadHistoryAsync(parent.Id, null, ct)); Assert.Single(await store.ReadEventsAsync(parent.Id, 0, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
    }
}
