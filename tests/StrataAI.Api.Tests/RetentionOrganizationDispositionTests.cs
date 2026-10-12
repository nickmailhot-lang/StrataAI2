using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("organization")]
    [InlineData("class")]
    [InlineData("category")]
    [InlineData("record")]
    public async Task PRD_70_Organization_deletion_refuses_every_active_hold_scope_before_parent_audit_receipt_and_job_changes(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        CountingOrganizationDisposition? reads = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(d => d.ServiceType == typeof(IRetentionOrganizationDisposition));
            services.AddSingleton<IRetentionOrganizationDisposition>(p => reads = new(
                (IRetentionOrganizationDisposition)ActivatorUtilities.GetServiceOrCreateInstance(p, original.ImplementationType!)));
        });
        using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(foreign);
        var outsider = (await foreign.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        if (kind == "record") await ClassifyDispositionSource(owner, f, false, ct);
        var scope = kind switch
        {
            "organization" => new StrataAI.Domain.Records.RetentionHoldScope(f.Organization, null, null, null),
            "class" => new(f.Organization, StrataAI.Domain.Records.RetentionRecordClass.Document, null, null),
            "category" => new(f.Organization, StrataAI.Domain.Records.RetentionRecordClass.Document, "unclassified.offboarding", null),
            _ => new(f.Organization, StrataAI.Domain.Records.RetentionRecordClass.Document, "reviewed.held", f.Attachment)
        };
        var hold = Guid.NewGuid(); var holds = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        Assert.True((await holds.ApplyAsync(f.Organization, f.Actor, hold, scope,
            "Private offboarding hold reason", Guid.NewGuid(), "hold-before-offboarding", ct)).Succeeded);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationDeletionReplayStore>();
        var before = await organizations.FindOrganizationAsync(f.Organization, ct); Assert.NotNull(before);
        var work = app.Services.GetRequiredService<InMemoryWorkManagementStore>();
        var audits = work.AuditSnapshot(f.Organization).ToArray();
        var key = Guid.NewGuid(); var path = $"/organizations/{f.Organization}?version={before.Version}&expectedActorId={f.Actor}";
        Assert.NotNull(reads); Assert.Equal(0, reads.Calls);
        using var denied = await Mutate(foreign, HttpMethod.Delete,
            $"/organizations/{f.Organization}?version={before.Version}&expectedActorId={outsider}", new { }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(0, reads.Calls);
        using var stale = await Mutate(owner, HttpMethod.Delete,
            $"/organizations/{f.Organization}?version={before.Version + 1}&expectedActorId={f.Actor}", new { }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(0, reads.Calls);
        using var refused = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode); Assert.Equal(1, reads.Calls);
        var problem = await refused.Content.ReadAsStringAsync(ct);
        Assert.Contains("retention_hold_prevents_disposition", problem);
        Assert.DoesNotContain("Private offboarding hold reason", problem);
        Assert.DoesNotContain(hold.ToString(), problem); Assert.DoesNotContain(f.Attachment.ToString(), problem);
        Assert.Equal(before, await organizations.FindOrganizationAsync(f.Organization, ct));
        Assert.True(audits.SequenceEqual(work.AuditSnapshot(f.Organization)));
        Assert.Null(await receipts.ReadAsync(f.Organization, f.Actor, key, ct));
        using var absent = await owner.GetAsync($"/organizations/{f.Organization}/deletion-requests/{key}?expectedActorId={f.Actor}", ct);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.True((await holds.ReleaseAsync(f.Organization, f.Actor, hold, 1,
            Guid.NewGuid(), "reviewed-offboarding-release", ct)).Succeeded);
        using var accepted = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode); Assert.Equal(2, reads.Calls);
        var saved = await receipts.ReadAsync(f.Organization, f.Actor, key, ct); Assert.NotNull(saved);
        var deleting = await organizations.FindOrganizationAsync(f.Organization, ct);
        Assert.Equal(OrganizationStatus.Deleting, deleting!.Status);
        using var recovery = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Accepted, recovery.StatusCode); Assert.Equal(2, reads.Calls);
        Assert.Equal(saved, await receipts.ReadAsync(f.Organization, f.Actor, key, ct));
        Assert.Equal(deleting, await organizations.FindOrganizationAsync(f.Organization, ct));
        using var pending = await owner.GetAsync($"/organizations/{f.Organization}/deletion-requests/{key}?expectedActorId={f.Actor}", ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal("PENDING", (await pending.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task PRD_70_unavailable_Organization_disposition_keeps_active_parent_and_retries_original_request_after_recovery()
    {
        var ct = TestContext.Current.CancellationToken; var projection = new OrganizationDispositionProjectionFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IRetentionOrganizationDisposition>(projection));
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Unconfirmed offboarding", ct);
        var key = Guid.NewGuid(); var path = $"/organizations/{parent.Id}?version={parent.Version}&expectedActorId={actor}";
        using var unavailable = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("retention_disposition_unavailable", (await unavailable.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        Assert.Equal(OrganizationStatus.Active, (await organizations.FindOrganizationAsync(parent.Id, ct))!.Status);
        Assert.Null(await app.Services.GetRequiredService<IOrganizationDeletionReplayStore>().ReadAsync(parent.Id, actor, key, ct));
        projection.Available = true;
        using var recovered = await Mutate(owner, HttpMethod.Delete, path, new { }, key.ToString());
        Assert.Equal(HttpStatusCode.Accepted, recovered.StatusCode);
    }

    private sealed class CountingOrganizationDisposition(IRetentionOrganizationDisposition inner) : IRetentionOrganizationDisposition
    {
        public int Calls { get; private set; }
        public Task<RetentionDispositionAdmission> CheckAsync(Guid organization, CancellationToken ct)
        { Calls++; return inner.CheckAsync(organization, ct); }
    }
    // Explicit unavailable projection control; never a grant of scope authority.
    private sealed class OrganizationDispositionProjectionFixture : IRetentionOrganizationDisposition
    {
        public bool Available { get; set; }
        public Task<RetentionDispositionAdmission> CheckAsync(Guid organization, CancellationToken ct) =>
            Task.FromResult(Available ? RetentionDispositionAdmission.Clear : RetentionDispositionAdmission.Unavailable);
    }
}
