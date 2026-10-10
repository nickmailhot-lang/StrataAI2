using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationConfigurationSynchronizationTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Admin)]
    public async Task PRD_27_Only_current_admin_scope_replays_original_ready_sources_in_an_owning_transaction(OrganizationRole role)
    {
        var f = new Fixture(); f.Reader.Scope = f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { Role = role } };
        var cursor = f.Codec.Encode(f.Reader.Scope!.Binding, 0);
        var result = await f.Service.ReadAsync(Organization, Actor, cursor, ct: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded); Assert.Same(f.Reader.Row.Source, Assert.Single(result.Value!.Events));
        Assert.False(result.Value.ResetRequired); Assert.Equal(1, f.Unit.Calls); Assert.Equal(2, f.Auth.Calls);
        Assert.True(f.Codec.TryDecode(f.Reader.Scope!.Binding, result.Value.Cursor, out var position)); Assert.Equal(1, position);
    }

    [Fact]
    public async Task PRD_27_Withdrawn_foreign_invalid_or_non_admin_scope_reads_no_private_head_or_events()
    {
        var baseline = new Fixture().Reader.Scope!;
        foreach (var scope in new OrganizationConfigurationReadScope?[] { null,
            baseline with { OrganizationActive = false }, baseline with { MembershipActive = false },
            baseline with { Binding = baseline.Binding with { Role = OrganizationRole.Member } },
            baseline with { Binding = baseline.Binding with { Role = (OrganizationRole)999 } },
            baseline with { Binding = baseline.Binding with { OrganizationId = Guid.NewGuid() } },
            baseline with { Binding = baseline.Binding with { ActorId = Guid.NewGuid() } },
            baseline with { Binding = baseline.Binding with { MembershipId = Guid.Empty } },
            baseline with { Binding = baseline.Binding with { MembershipVersion = 0 } } })
        {
            var f = new Fixture(); f.Reader.Scope = scope;
            var result = await f.Service.ReadAsync(Organization, Actor, null, ct: TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal("organization_not_found", result.ErrorCode);
            Assert.Equal(0, f.Reader.HeadReads); Assert.Equal(0, f.Reader.EventReads);
        }
        var denied = new Fixture(); denied.Auth.Active = false;
        Assert.False((await denied.Service.ReadAsync(Organization, Actor, null, ct: TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(0, denied.Reader.ScopeReads); Assert.Equal(0, denied.Reader.HeadReads);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("organization")]
    [InlineData("inactive")]
    [InlineData("demotion")]
    [InlineData("epoch")]
    [InlineData("role")]
    [InlineData("replacement")]
    public async Task PRD_27_Authority_changes_during_event_IO_discard_the_entire_page(string change)
    {
        var f = new Fixture(); var scope = f.Reader.Scope!;
        var cursor = f.Codec.Encode(scope.Binding, 0);
        f.Reader.OnRead = () =>
        {
            if (change == "account") f.Auth.Active = false;
            else f.Reader.Scope = change switch
            {
                "organization" => scope with { OrganizationActive = false },
                "inactive" => scope with { MembershipActive = false },
                "demotion" => scope with { Binding = scope.Binding with { Role = OrganizationRole.Member } },
                "epoch" => scope with { Binding = scope.Binding with { MembershipVersion = 2 } },
                "role" => scope with { Binding = scope.Binding with { Role = OrganizationRole.Admin } },
                _ => scope with { Binding = scope.Binding with { MembershipId = Guid.NewGuid() } }
            };
        };
        var result = await f.Service.ReadAsync(Organization, Actor, cursor, ct: TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal(1, f.Reader.EventReads);
    }

    [Fact]
    public async Task PRD_27_Bootstrap_or_foreign_cursor_returns_only_a_current_reset_and_rechecks_scope_after_head_IO()
    {
        foreach (var token in new string?[] { null, "foreign" })
        {
            var f = new Fixture(); var result = await f.Service.ReadAsync(Organization, Actor, token, ct: TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded); Assert.True(result.Value!.ResetRequired); Assert.Empty(result.Value.Events);
            Assert.Equal(0, f.Reader.EventReads); Assert.Equal(1, f.Reader.HeadReads); Assert.Equal(2, f.Auth.Calls);
            Assert.True(f.Codec.TryDecode(f.Reader.Scope!.Binding, result.Value.Cursor, out var position)); Assert.Equal(1, position);
        }
        var withdrawn = new Fixture(); withdrawn.Reader.OnHead = () => withdrawn.Auth.Active = false;
        var refused = await withdrawn.Service.ReadAsync(Organization, Actor, null, ct: TestContext.Current.CancellationToken);
        Assert.False(refused.Succeeded); Assert.Null(refused.Value);
    }

    [Fact]
    public async Task PRD_27_Recovery_cursor_currentness_rechecks_scope_after_decode_and_never_reads_history()
    {
        var f = new Fixture(); var cursor = f.Codec.Encode(f.Reader.Scope!.Binding, 1);
        Assert.True((await f.Service.IsCursorCurrentAsync(Organization, Actor, cursor, TestContext.Current.CancellationToken)).Value);
        f.Codec.OnDecode = () => f.Reader.Scope = f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { MembershipVersion = 2 } };
        var changed = await f.Service.IsCursorCurrentAsync(Organization, Actor, cursor, TestContext.Current.CancellationToken);
        Assert.True(changed.Succeeded); Assert.False(changed.Value);
        Assert.Equal(0, f.Reader.EventReads); Assert.Equal(0, f.Reader.HeadReads);
    }

    [Fact]
    public async Task PRD_27_Pending_delivery_and_missing_history_do_not_return_unpublished_sources()
    {
        var f = new Fixture(); var cursor = f.Codec.Encode(f.Reader.Scope!.Binding, 0);
        f.Reader.Row = f.Reader.Row with { ReadyAt = null };
        var pending = await f.Service.ReadAsync(Organization, Actor, cursor, ct: TestContext.Current.CancellationToken);
        Assert.True(pending.Succeeded); Assert.True(pending.Value!.Pending); Assert.Empty(pending.Value.Events);
        Assert.True(f.Codec.TryDecode(f.Reader.Scope!.Binding, pending.Value.Cursor, out var position)); Assert.Equal(0, position);
        f.Reader.Rows = [];
        var reset = await f.Service.ReadAsync(Organization, Actor, cursor, ct: TestContext.Current.CancellationToken);
        Assert.True(reset.Succeeded); Assert.True(reset.Value!.ResetRequired); Assert.Empty(reset.Value.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task PRD_27_Invalid_limits_open_no_transaction(int limit)
    {
        var f = new Fixture(); Assert.False((await f.Service.ReadAsync(Organization, Actor, null, limit, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(0, f.Unit.Calls);
    }

    private sealed class Fixture
    {
        public Unit Unit { get; } = new(); public Auth Auth { get; } = new(); public Codec Codec { get; } = new();
        public Reader Reader { get; }
        public OrganizationConfigurationSynchronizationService Service { get; }
        public Fixture() { Reader = new(Unit); Service = new(Reader, Codec, Unit, Auth); }
    }
    private sealed class Unit : IOrganizationUnitOfWork
    {
        public bool Inside { get; private set; } public int Calls { get; private set; }
        public async Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization, Guid actor, Guid? target, bool creating,
            Func<Task<OrganizationOperation<T>>> action, CancellationToken ct = default, bool allowDeletionRecovery = false)
        {
            Assert.Equal(Organization, organization); Assert.Equal(Actor, actor); Assert.Null(target);
            Assert.False(creating); Assert.False(allowDeletionRecovery); Calls++; Inside = true;
            try { return await action(); } finally { Inside = false; }
        }
    }
    private sealed class Auth : ICommandActorAuthorization
    {
        public bool Active { get; set; } = true; public int Calls { get; private set; }
        public Task<bool> VerifyAsync(Guid actor, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Active && actor == Actor); }
    }
    private sealed class Reader(Unit unit) : IOrganizationConfigurationEventReader
    {
        public OrganizationConfigurationReadScope? Scope { get; set; } = new(new(Organization, Actor, Guid.NewGuid(), 1, OrganizationRole.Owner), true, true);
        public OrganizationConfigurationEventCandidate Row { get; set; } = new(new(Guid.NewGuid(), Organization, Actor, 1, "source", Now), Now.AddSeconds(1));
        public IReadOnlyList<OrganizationConfigurationEventCandidate>? Rows { get; set; }
        public Action? OnRead { get; set; } public Action? OnHead { get; set; }
        public int ScopeReads { get; private set; } public int HeadReads { get; private set; } public int EventReads { get; private set; }
        public Task<OrganizationConfigurationReadScope?> GetScopeAsync(Guid org, Guid actor, CancellationToken ct)
        { Assert.True(unit.Inside); ScopeReads++; return Task.FromResult(Scope); }
        public Task<long> GetHeadAsync(Guid org, CancellationToken ct) { Assert.True(unit.Inside); HeadReads++; OnHead?.Invoke(); return Task.FromResult(1L); }
        public Task<OrganizationConfigurationEventBatch> ReadAsync(Guid org, long since, int limit, CancellationToken ct)
        { Assert.True(unit.Inside); EventReads++; OnRead?.Invoke(); return Task.FromResult(new OrganizationConfigurationEventBatch(1, Rows ?? [Row])); }
    }
    private sealed class Codec : IOrganizationConfigurationCursorCodec
    {
        private readonly Dictionary<string, (OrganizationConfigurationCursorBinding Binding, long Position)> tokens = [];
        public Action? OnDecode { get; set; }
        public string Encode(OrganizationConfigurationCursorBinding binding, long position)
        { var token = Guid.NewGuid().ToString("N"); tokens.Add(token, (binding, position)); return token; }
        public bool TryDecode(OrganizationConfigurationCursorBinding binding, string token, out long position)
        { OnDecode?.Invoke(); position = 0; if (!tokens.TryGetValue(token, out var value) || value.Binding != binding) return false; position = value.Position; return true; }
    }
}
