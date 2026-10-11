using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;
public sealed class OrganizationOperationalLifecycleReplayTests
{
    [Fact]
    public async Task PRD_27_FR007_Replay_current_internal_member_receives_only_published_lifecycle_facts()
    {
        var f = new Fixture(); f.Scope = f.Scope with { Role = OrganizationRole.Member, Status = OrganizationStatus.Deactivated };
        f.Rows = [f.Publication(2, true)];
        var result = await f.Read(); Assert.True(result.Succeeded); var source = Assert.Single(result.Value!.Events);
        Assert.Equal(f.Organization, source.OrganizationId); Assert.Equal(2, source.Version);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("Private actor caption", json); Assert.DoesNotContain("Private correlation", json);
    }
    [Fact]
    public async Task PRD_27_FR007_Replay_never_advances_past_an_unpublished_gap()
    {
        var f = new Fixture(); f.Rows = [f.Publication(2, false), f.Publication(3, true)];
        var blocked = await f.Read(); Assert.True(blocked.Succeeded); Assert.Empty(blocked.Value!.Events);
        Assert.True(blocked.Value.Pending); Assert.Null(blocked.Value.NextVersion);
        f.Rows = [f.Publication(2, true), f.Publication(3, false)];
        var prefix = await f.Read(); Assert.Equal(2, Assert.Single(prefix.Value!.Events).Version);
        Assert.True(prefix.Value.Pending); Assert.Equal(2, prefix.Value.NextVersion);
        var resumed = await f.Read(2); Assert.Empty(resumed.Value!.Events); Assert.True(resumed.Value.Pending);
    }
    [Fact]
    public async Task PRD_27_FR007_Replay_bounds_and_resumes_ready_sources_without_skipping()
    {
        var f = new Fixture(); f.Scope = f.Scope with { Version = 52 };
        f.Rows = Enumerable.Range(2, 51).Select(version => f.Publication(version, true)).ToArray();
        var page = await f.Read(); Assert.Equal(50, page.Value!.Events.Count); Assert.Equal(51, page.Value.NextVersion);
        var next = await f.Read(51); Assert.Equal(52, Assert.Single(next.Value!.Events).Version); Assert.Null(next.Value.NextVersion);
    }
    [Fact]
    public async Task PRD_27_FR007_Replay_withdrawn_membership_or_changed_capacity_returns_no_partial_page()
    {
        foreach (var epoch in new[] { "inactive", "epoch", "role" })
        {
            var f = new Fixture(); f.Rows = [f.Publication(2, true)];
            f.OnEvents = () => f.Scope = epoch switch {
                "inactive" => f.Scope with { MembershipActive = false },
                "epoch" => f.Scope with { MembershipVersion = 2 }, _ => f.Scope with { Role = OrganizationRole.Member } };
            var result = await f.Read(); Assert.Equal("organization_not_found", result.ErrorCode); Assert.Null(result.Value);
        }
    }
    [Fact]
    public async Task PRD_27_FR007_Replay_session_or_parent_change_is_rejected_after_IO()
    {
        var f = new Fixture(); f.Rows = [f.Publication(2, true)]; f.OnEvents = () => f.Active = false;
        Assert.Equal("session_unavailable", (await f.Read()).ErrorCode);
        f = new Fixture(); f.Rows = [f.Publication(2, true)]; f.OnEvents = () => f.Scope = f.Scope with { Version = 4 };
        Assert.Equal("version_conflict", (await f.Read()).ErrorCode);
    }
    [Fact]
    public async Task PRD_27_FR007_Replay_malformed_foreign_duplicate_or_unbounded_sources_fail_closed()
    {
        var f = new Fixture(); var valid = f.Publication(2, true);
        foreach (var rows in new[] { new[]{valid, valid}, new[]{valid with { Source = valid.Source with { OrganizationId = Guid.NewGuid() } }},
            new[]{valid with { Source = valid.Source with { EventId = Guid.Empty } }}, new[]{valid with { ReadyAt = valid.Source.CreatedAt.AddTicks(-10) }},
            Enumerable.Repeat(valid, 52).ToArray() })
        {
            f.Rows = rows; f.Raw = true;
            var result = await f.Read(); Assert.Equal("organization_lifecycle_source_unavailable", result.ErrorCode); Assert.Null(result.Value);
        }
    }
    [Fact]
    public async Task PRD_27_FR007_Replay_invalid_cursor_and_inactive_admission_never_load_events()
    {
        var f = new Fixture(); Assert.Equal("invalid_organization_cursor", (await f.Read(-1)).ErrorCode);
        Assert.Equal("invalid_organization_cursor", (await f.Read(4)).ErrorCode); Assert.Equal(0, f.EventReads);
        f.Scope = f.Scope with { Status = OrganizationStatus.Deleted };
        Assert.Equal("organization_not_found", (await f.Read()).ErrorCode); Assert.Equal(0, f.EventReads);
    }
    private sealed class Fixture : IOrganizationOperationalLifecycleUnitOfWork, IOrganizationOperationalLifecycleStore,
        IOrganizationOperationalLifecycleEventReader, ICommandActorAuthorization
    {
        public Guid Organization { get; } = Guid.NewGuid(); public Guid Actor { get; } = Guid.NewGuid();
        public OrganizationOperationalLifecycleScope Scope { get; set; }
        public IReadOnlyList<OrganizationOperationalLifecyclePublication> Rows { get; set; } = [];
        public Action? OnEvents { get; set; } public bool Active { get; set; } = true; public bool Raw { get; set; } public int EventReads { get; private set; }
        public Fixture() { var at = DateTimeOffset.UtcNow; at = at.AddTicks(-(at.Ticks % 10));
            Scope = new(Organization, OrganizationStatus.Suspended, 3, at, Actor, "Current member", Guid.NewGuid(), 1, OrganizationRole.Owner, true, "Private name"); }
        public OrganizationOperationalLifecyclePublication Publication(long version, bool ready) => new(new(Organization, Guid.NewGuid(), Actor,
            "Private actor caption", "ORGANIZATION_OWNER", Scope.MembershipId, 1,
            version % 2 == 0 ? OrganizationStatus.Active : OrganizationStatus.Suspended,
            version % 2 == 0 ? OrganizationStatus.Suspended : OrganizationStatus.Active,
            version, "Private correlation", Scope.UpdatedAt), ready ? Scope.UpdatedAt : null);
        public Task<OrganizationOperation<OrganizationOperationalLifecycleReplayPage>> Read(long after = 0) =>
            new OrganizationOperationalLifecycleReplayService(this, this, this, this).ReadAsync(Organization, Actor, after, TestContext.Current.CancellationToken);
        public Task<bool> VerifyAsync(Guid actor, CancellationToken ct = default) => Task.FromResult(Active && actor == Actor);
        public Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization, Guid actor, Func<Task<OrganizationOperation<T>>> action, CancellationToken ct) => action();
        public Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization, Guid actor, CancellationToken ct) => Task.FromResult<OrganizationOperationalLifecycleScope?>(Scope);
        public Task<OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>> ReadAsync(Guid organization, Guid actor, long afterVersion, CancellationToken ct)
        { EventReads++; OnEvents?.Invoke(); return Task.FromResult(OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>.Success(
            Raw ? Rows : Rows.Where(row => row.Source.Version > afterVersion).Take(51).ToArray())); }
        public Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => throw new InvalidOperationException("Replay cannot read receipts.");
        public Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long version, OrganizationOperationalLifecycleTransition source, OrganizationOperationalLifecycleReceipt receipt, CancellationToken ct) => throw new InvalidOperationException("Replay cannot write.");
    }
}
