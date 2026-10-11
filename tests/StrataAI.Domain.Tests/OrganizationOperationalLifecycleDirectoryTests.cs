using System.Reflection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;
public sealed class OrganizationOperationalLifecycleDirectoryTests
{
    private static readonly Guid Actor = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static Guid Id(int value) => Guid.Parse($"01000000-0000-4000-8000-{value:000000000000}");
    [Fact]
    public async Task PRD_27_FR007_Directory_routing_hints_never_disclose_withdrawn_member_or_terminal_names()
    {
        var f = new Fixture([Id(1),Id(2),Id(3)]);f.Store.Scopes[Id(2)] = f.Store.Scopes[Id(2)] with { Role = OrganizationRole.Member };
        f.Store.Scopes[Id(3)] = f.Store.Scopes[Id(3)] with { Status = OrganizationStatus.Deleted };
        var result = await f.Service.ReadAsync(Actor,null,TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);Assert.Equal(Id(1),Assert.Single(result.Value!.Items).OrganizationId);
        Assert.Equal(OrganizationStatus.Suspended,result.Value.Items[0].Status);
    }
    [Fact]
    public async Task PRD_27_FR007_Directory_withdrawal_during_page_IO_removes_earlier_private_names()
    {
        var f = new Fixture([Id(1),Id(2)]);
        f.Store.BeforeRead = organization => { if (organization == Id(2)) f.Store.Scopes[Id(1)] = f.Store.Scopes[Id(1)] with { MembershipActive=false }; };
        var result = await f.Service.ReadAsync(Actor,null,TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);Assert.Equal(Id(2),Assert.Single(result.Value!.Items).OrganizationId);
    }
    [Fact]
    public async Task PRD_27_FR007_Directory_rejects_unbounded_duplicate_empty_unordered_or_regressed_hints()
    {
        foreach (var hints in new[] { new[]{Guid.Empty},new[]{Id(1),Id(1)},new[]{Id(2),Id(1)},Enumerable.Range(1,52).Select(Id).ToArray() })
        {
            var f = new Fixture(hints);var result = await f.Service.ReadAsync(Actor,null,TestContext.Current.CancellationToken);
            Assert.Equal("organization_lifecycle_source_unavailable",result.ErrorCode);Assert.Null(result.Value);
        }
        var regression = new Fixture([Id(1)]);
        Assert.Equal("organization_lifecycle_source_unavailable",(await regression.Service.ReadAsync(Actor,Id(1),TestContext.Current.CancellationToken)).ErrorCode);
    }
    [Fact]
    public async Task PRD_27_FR007_Directory_empty_authorized_page_still_advances_bounded_membership_hints()
    {
        var f = new Fixture(Enumerable.Range(1,51).Select(Id).ToArray());
        foreach (var id in f.Store.Scopes.Keys.ToArray())f.Store.Scopes[id] = f.Store.Scopes[id] with { Role=OrganizationRole.Member };
        var result = await f.Service.ReadAsync(Actor,null,TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);Assert.Empty(result.Value!.Items);Assert.Equal(Id(50),result.Value.NextCursor);
    }
    [Fact]
    public async Task PRD_27_FR007_Directory_session_withdrawal_returns_no_partial_page()
    {
        var f = new Fixture([Id(1),Id(2)]);f.Store.BeforeRead = organization => { if(organization == Id(2))f.Auth.Active=false; };
        var result = await f.Service.ReadAsync(Actor,null,TestContext.Current.CancellationToken);
        Assert.Equal("session_unavailable",result.ErrorCode);Assert.Null(result.Value);
    }
    private sealed class Fixture
    {
        public Store Store {get;} = new();public Auth Auth {get;} = new();public OrganizationOperationalLifecycleDirectoryService Service {get;}
        public Fixture(Guid[] hints)
        {
            foreach(var id in hints.Where(id=>id!=Guid.Empty).Distinct())Store.Scopes[id] = new(id,OrganizationStatus.Suspended,2,
                DateTimeOffset.UtcNow,Actor,"Administrator",Guid.NewGuid(),1,OrganizationRole.Owner,true,"Private name");
            var routes = DispatchProxy.Create<IOrganizationStore,RoutesProxy>();((RoutesProxy)(object)routes).Hints = hints;
            Service = new(routes,new(new Unit(),Store,Auth,new SystemClock()),Auth);
        }
    }
    public class RoutesProxy : DispatchProxy
    {
        public Guid[] Hints {get;set;} = [];
        protected override object? Invoke(MethodInfo? method,object?[]? args) => method?.Name == nameof(IOrganizationStore.ReadDirectoryRoutesAsync)
            ? Task.FromResult(OrganizationOperation<IReadOnlyList<Guid>>.Success(Hints)) : throw new InvalidOperationException("Only membership routing is permitted.");
    }
    private sealed class Auth : ICommandActorAuthorization
    {
        public bool Active {get;set;} = true;
        public Task<bool> VerifyAsync(Guid actor,CancellationToken ct=default)=>Task.FromResult(Active && actor==Actor);
    }
    private sealed class Unit : IOrganizationOperationalLifecycleUnitOfWork
    {
        public Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization,Guid actor,Func<Task<OrganizationOperation<T>>> action,CancellationToken ct)=>action();
    }
    private sealed class Store : IOrganizationOperationalLifecycleStore
    {
        public Dictionary<Guid,OrganizationOperationalLifecycleScope> Scopes {get;}=[];public Action<Guid>? BeforeRead {get;set;}
        public Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization,Guid actor,CancellationToken ct)
        {BeforeRead?.Invoke(organization);return Task.FromResult<OrganizationOperationalLifecycleScope?>(Scopes.GetValueOrDefault(organization));}
        public Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization,Guid actor,Guid key,CancellationToken ct)=>throw new InvalidOperationException("Directory cannot read receipts.");
        public Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long version,OrganizationOperationalLifecycleTransition transition,OrganizationOperationalLifecycleReceipt receipt,CancellationToken ct)=>throw new InvalidOperationException("Directory cannot write.");
    }
}
