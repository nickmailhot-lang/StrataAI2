using System.Text.Json;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationConfigurationActivityTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 10, 10, 10, 30, 0, TimeSpan.Zero);
    private static OrganizationConfigurationRecord Source(long version) => new(Organization, version,
        new("Never disclose the private legal name", "CA-BC", "UTC") { CivicAddress = "Private address" },
        "Private parent caption", "STRATA", 1, Actor, Guid.NewGuid(), "Original correlation", At, At.AddTicks(version));

    [Fact]
    public async Task PRD_27_Activity_projects_original_sources_without_configuration_or_current_account_lookup()
    {
        var f = new Fixture(3); var original = f.Store.Rows.ToArray();
        var page = await f.Read(); Assert.True(page.Succeeded); Assert.Null(page.Value!.NextCursor);
        Assert.Equal(3, page.Value.Items.Count);
        for (var i = 0; i < 3; i++)
        {
            var item = page.Value.Items[i]; Assert.Equal(original[i].EventId, item.EventId);
            Assert.Equal(original[i].ActorId, item.ActorId); Assert.Equal(original[i].UpdatedAt, item.CreatedAt);
            Assert.Equal(original[i].CorrelationId, item.CorrelationId); Assert.Null(item.BoardId);
            Assert.Equal(Organization, item.EntityId); Assert.Equal("OrganizationConfiguration", item.EntityType);
            Assert.Empty(item.Metadata);
            Assert.Equal("Original administrative caption", item.ActorLabel);
            Assert.Equal("ORGANIZATION_OWNER", item.ActingCapacity);
        }
        var wire = JsonSerializer.Serialize(page.Value);
        Assert.DoesNotContain("Never disclose", wire); Assert.DoesNotContain("Private address", wire);
        Assert.DoesNotContain("Private parent caption", wire); Assert.Equal(2, f.Auth.Calls);
    }

    [Fact]
    public async Task PRD_27_Activity_uses_a_bounded_stable_descending_continuation_and_freezes_returned_items()
    {
        var f = new Fixture(53); var first = await f.Read(); Assert.True(first.Succeeded);
        Assert.Equal(50, first.Value!.Items.Count); Assert.NotNull(first.Value.NextCursor);
        Assert.Equal(53, first.Value.Items[0].Version); Assert.Equal(4, first.Value.Items[^1].Version);
        f.Store.Rows.Insert(0, Source(54)); f.Reader.Head = 54;
        var second = await f.Read(first.Value.NextCursor); Assert.True(second.Succeeded);
        Assert.Equal(new long[] {3,2,1}, second.Value!.Items.Select(i => i.Version)); Assert.Null(second.Value.NextCursor);
        Assert.Throws<NotSupportedException>(() => ((IList<OrganizationConfigurationActivityItem>)first.Value.Items).Clear());
        Assert.Equal(new long?[] {null,4}, f.Store.Boundaries);
    }

    [Fact]
    public async Task PRD_27_Activity_retains_historical_actor_identity_without_reauthorizing_that_actor()
    {
        var f = new Fixture(3); var historicalActor = Guid.NewGuid();
        f.Store.Rows = f.Store.Rows.Select(row => row with { ActorId = historicalActor }).ToList();
        foreach (var key in f.Store.Facts.Keys.ToArray()) f.Store.Facts[key] = f.Store.Facts[key] with { ActorId = historicalActor };
        // Auth admits only the current viewer. Historical source actors need
        // not retain accounts, memberships or their former operational grant.
        var result = await f.Read(); Assert.True(result.Succeeded);
        Assert.All(result.Value!.Items, item => Assert.Equal(historicalActor, item.ActorId));
        Assert.Equal(2, f.Auth.Calls);
    }

    [Theory]
    [InlineData("member")] [InlineData("foreign")] [InlineData("inactive")]
    [InlineData("account")] [InlineData("missing")]
    public async Task PRD_27_Activity_denial_precedes_private_source_and_malformed_cursor_reads(string cause)
    {
        var f = new Fixture(3);
        if (cause == "account") f.Auth.Active = false;
        else if (cause == "missing") f.Reader.Scope = null;
        else f.Reader.Scope = cause switch
        {
            "member" => f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { Role = OrganizationRole.Member } },
            "foreign" => f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { OrganizationId = Guid.NewGuid() } },
            _ => f.Reader.Scope! with { OrganizationActive = false }
        };
        var page = await f.Read("invalid"); Assert.False(page.Succeeded); Assert.Null(page.Value);
        Assert.Equal("organization_not_found", page.ErrorCode); Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Reader.HeadReads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PRD_27_Activity_rechecks_account_and_membership_epoch_after_history_io(bool epoch)
    {
        var f = new Fixture(3); f.Store.OnRead = () =>
        {
            if (epoch) f.Reader.Scope = f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { MembershipVersion = 2 } };
            else f.Auth.Active = false;
        };
        var page = await f.Read(); Assert.False(page.Succeeded); Assert.Null(page.Value);
        Assert.Equal(epoch ? "configuration_source_unavailable" : "organization_not_found", page.ErrorCode);
    }

    [Theory]
    [InlineData("actor")] [InlineData("tenant")] [InlineData("gap")] [InlineData("time")]
    [InlineData("correlation")] [InlineData("identity")] [InlineData("head")]
    public async Task PRD_27_Activity_refuses_malformed_history_instead_of_repairing_original_attribution(string defect)
    {
        var f = new Fixture(3); var row = f.Store.Rows[1];
        f.Store.Rows[1] = defect switch
        {
            "actor" => row with { ActorId = Guid.Empty },
            "tenant" => row with { OrganizationId = Guid.NewGuid() },
            "gap" => row with { Version = 1 },
            "time" => row with { UpdatedAt = At.AddSeconds(-1) },
            "correlation" => row with { CorrelationId = "Invalid\ncontrol" },
            "identity" => row with { EventId = f.Store.Rows[0].EventId },
            _ => row
        };
        if (defect == "head") f.Reader.Head = 4;
        var page = await f.Read(); Assert.False(page.Succeeded); Assert.Null(page.Value);
        Assert.Equal("configuration_source_unavailable", page.ErrorCode);
    }

    [Fact]
    public async Task PRD_27_Activity_rejects_unbound_cursor_and_oversized_store_window_without_returning_partial_rows()
    {
        var f = new Fixture(3); var invalid = await f.Read("foreign");
        Assert.Equal("invalid_configuration_activity_cursor", invalid.ErrorCode); Assert.Equal(0, f.Store.Reads);
        f.Store.IgnoreBound = true; f.Store.Rows = Enumerable.Range(1,52).Reverse().Select(i => Source(i)).ToList(); f.Reader.Head = 52;
        var excess = await f.Read(); Assert.Equal("configuration_source_unavailable", excess.ErrorCode); Assert.Null(excess.Value);
    }

    [Theory]
    [InlineData("missing")][InlineData("extra")][InlineData("duplicate")][InlineData("foreign")]
    [InlineData("actor")][InlineData("version")][InlineData("caption")][InlineData("control")]
    [InlineData("capacity")][InlineData("membership")][InlineData("epoch")][InlineData("legacy-forgery")]
    public async Task PRD_27_Activity_refuses_missing_or_mismatched_immutable_attribution(string corruption)
    {
        var f = new Fixture(3);
        f.Store.RewriteFacts = rows => corruption switch
        {
            "missing" => rows.Skip(1).ToArray(),
            "extra" => rows.Append(rows[0] with { EventId = Guid.NewGuid() }).ToArray(),
            "duplicate" => new[] { rows[1], rows[1], rows[2] },
            _ => rows.Select((row, index) => index != 0 ? row : corruption switch
            {
                "foreign" => row with { OrganizationId = Guid.NewGuid() },
                "actor" => row with { ActorId = Guid.NewGuid() },
                "version" => row with { Version = row.Version + 1 },
                "caption" => row with { ActorLabel = " " },
                "control" => row with { ActorLabel = "caption\ncontrol" },
                "capacity" => row with { ActingCapacity = "COUNCIL_MEMBER" },
                "membership" => row with { SourceMembershipId = Guid.Empty },
                "epoch" => row with { SourceMembershipVersion = 0 },
                _ => row with { ActingCapacity = "UNRECORDED" }
            }).ToArray()
        };
        var result = await f.Read(); Assert.False(result.Succeeded); Assert.Null(result.Value);
        Assert.Equal("configuration_source_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task PRD_27_Activity_keeps_captured_capacity_independent_of_current_viewer_role_and_epoch()
    {
        var f = new Fixture(3);
        f.Reader.Scope = f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { Role = OrganizationRole.Admin, MembershipVersion = 8 } };
        var result = await f.Read(); Assert.True(result.Succeeded);
        Assert.All(result.Value!.Items, row => {
            Assert.Equal("Original administrative caption", row.ActorLabel);
            Assert.Equal("ORGANIZATION_OWNER", row.ActingCapacity);
        });
        var legacy = f.Store.Facts.Keys.First();
        f.Store.Facts[legacy] = f.Store.Facts[legacy] with { ActorLabel = "Historical actor " + Actor,
            ActingCapacity = "UNRECORDED", SourceMembershipId = null, SourceMembershipVersion = null };
        var retained = await f.Read(); Assert.True(retained.Succeeded);
        Assert.Equal("UNRECORDED", retained.Value!.Items.Single(row => row.EventId == legacy).ActingCapacity);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task PRD_27_Activity_rechecks_admission_after_attribution_io(bool epoch)
    {
        var f = new Fixture(3); f.Store.OnAttributionRead = () => {
            if (epoch) f.Reader.Scope = f.Reader.Scope! with { Binding = f.Reader.Scope!.Binding with { MembershipVersion = 2 } };
            else f.Auth.Active = false;
        };
        var result = await f.Read(); Assert.False(result.Succeeded); Assert.Null(result.Value);
        Assert.Equal(epoch ? "configuration_source_unavailable" : "organization_not_found", result.ErrorCode);
    }

    private sealed class Fixture
    {
        public Unit Unit {get;} = new(); public Auth Auth {get;} = new(); public Codec Codec {get;} = new();
        public Reader Reader {get;} public Store Store {get;} public OrganizationConfigurationActivityService Service {get;}
        public Fixture(int count) {Reader=new(Unit) {Head=count}; Store=new(Unit) {Rows=Enumerable.Range(1,count).Reverse().Select(i=>Source(i)).ToList()};
            foreach(var row in Store.Rows) Store.Facts.Add(row.EventId,new(Organization,row.EventId,row.ActorId,row.Version,
                "Original administrative caption","ORGANIZATION_OWNER",Guid.NewGuid(),1));
            Service=new(Store,Reader,Codec,Unit,Auth,Store);}
        public Task<OrganizationOperation<OrganizationConfigurationActivityPage>> Read(string? cursor=null) => Service.ReadAsync(Organization,Actor,cursor,TestContext.Current.CancellationToken);
    }
    private sealed class Unit : IOrganizationUnitOfWork
    {
        public bool Inside {get;private set;}
        public async Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization,Guid actor,Guid? target,bool creating,
            Func<Task<OrganizationOperation<T>>> action,CancellationToken ct=default,bool allowDeletionRecovery=false)
        {Assert.Equal(Organization,organization); Assert.Equal(Actor,actor); Assert.Null(target); Assert.False(creating); Inside=true; try{return await action();}finally{Inside=false;}}
    }
    private sealed class Auth : ICommandActorAuthorization
    {
        public bool Active {get;set;}=true; public int Calls {get;private set;}
        public Task<bool> VerifyAsync(Guid actor,CancellationToken ct=default){ct.ThrowIfCancellationRequested();Calls++;return Task.FromResult(Active && actor==Actor);}
    }
    private sealed class Codec : IOrganizationConfigurationActivityCursorCodec
    {
        private readonly Dictionary<string,(OrganizationConfigurationCursorBinding Binding,long Before)> tokens=[];
        public string Encode(OrganizationConfigurationCursorBinding binding,long before){var token=Guid.NewGuid().ToString("N");tokens.Add(token,(binding,before));return token;}
        public bool TryDecode(OrganizationConfigurationCursorBinding binding,string token,out long before){before=0;if(!tokens.TryGetValue(token,out var value)||value.Binding!=binding)return false;before=value.Before;return true;}
    }
    private sealed class Reader(Unit unit) : IOrganizationConfigurationEventReader
    {
        public OrganizationConfigurationReadScope? Scope {get;set;}=new(new(Organization,Actor,Guid.NewGuid(),1,OrganizationRole.Owner),true,true);
        public long Head {get;set;} public int HeadReads {get;private set;}
        public Task<OrganizationConfigurationReadScope?> GetScopeAsync(Guid org,Guid actor,CancellationToken ct){Assert.True(unit.Inside);return Task.FromResult(Scope);}
        public Task<long> GetHeadAsync(Guid org,CancellationToken ct){Assert.True(unit.Inside);HeadReads++;return Task.FromResult(Head);}
        public Task<OrganizationConfigurationEventBatch> ReadAsync(Guid org,long since,int limit,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Store(Unit unit) : IOrganizationConfigurationStore, IOrganizationConfigurationAttributionReader
    {
        public Dictionary<Guid,OrganizationConfigurationAttribution> Facts {get;}=[];
        public Func<IReadOnlyList<OrganizationConfigurationAttribution>,IReadOnlyList<OrganizationConfigurationAttribution>>? RewriteFacts {get;set;}
        public Action? OnAttributionRead {get;set;}
        public Task<IReadOnlyList<OrganizationConfigurationAttribution>> ReadAttributionAsync(Guid org,IReadOnlyList<Guid> eventIds,CancellationToken ct)
        {
            Assert.True(unit.Inside); Assert.Equal(Organization,org); Assert.InRange(eventIds.Count,0,51); OnAttributionRead?.Invoke();
            IReadOnlyList<OrganizationConfigurationAttribution> rows=eventIds.Select(id=>Facts[id]).ToArray();
            return Task.FromResult(RewriteFacts?.Invoke(rows)??rows);
        }
        public List<OrganizationConfigurationRecord> Rows {get;set;}=[]; public Action? OnRead {get;set;} public int Reads {get;private set;} public bool IgnoreBound {get;set;}
        public List<long?> Boundaries {get;}=[];
        public Task<IReadOnlyList<OrganizationConfigurationRecord>> ReadHistoryAsync(Guid org,long? before,CancellationToken ct)
        {Assert.True(unit.Inside);Assert.Equal(Organization,org);Reads++;Boundaries.Add(before);OnRead?.Invoke();return Task.FromResult<IReadOnlyList<OrganizationConfigurationRecord>>(Rows.Where(r=>before is null||r.Version<before).Take(IgnoreBound?52:51).ToArray());}
        public Task<OrganizationConfigurationRecord?> ReadAsync(Guid org,CancellationToken ct)=>throw new NotSupportedException();
        public Task<OrganizationConfigurationReceipt?> ReadReceiptAsync(Guid org,Guid actor,Guid key,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationConfigurationEvent>> ReadEventsAsync(Guid org,long after,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> IntakeAvailableAsync(Guid org,Guid? board,Guid? list,CancellationToken ct)=>throw new NotSupportedException();
        public Task<OrganizationConfigurationWriteResult> WriteAsync(long expected,OrganizationConfigurationRecord record,OrganizationConfigurationReceipt receipt,CancellationToken ct)=>throw new NotSupportedException();
    }
}
