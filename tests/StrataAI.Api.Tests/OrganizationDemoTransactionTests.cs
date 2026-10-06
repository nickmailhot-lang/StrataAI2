using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("actor")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task PRD_03_Demo_Organization_command_restores_cross_store_mutations_unless_committed(string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var actorFence = new OrganizationTransactionActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(actorFence));
        using var client = app.CreateClient();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var org = Guid.NewGuid(); var actor = Guid.NewGuid(); var board = Guid.NewGuid();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<OrganizationOperation<bool>> Mutate()
        {
            await organizations.CreateOrganizationAsync(actor, org, "Rollback private Organization", null, DateTimeOffset.UtcNow, ct);
            await work.CreateBoardAsync(org, actor, board, "Rollback private Board", null, BoardVisibility.Private, "COLOR", "blue", DateTimeOffset.UtcNow, ct);
            if (outcome == "actor") actorFence.Allowed = false;
            if (outcome == "exception") throw new InvalidOperationException("fixture failure after mutation");
            if (outcome == "cancel") cancel.Cancel();
            return outcome == "failure" ? OrganizationOperation<bool>.Failure("fixture_refused") : OrganizationOperation<bool>.Success(true);
        }
        if (outcome == "exception")
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(org, actor, null, true, Mutate, cancel.Token));
        else if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(org, actor, null, true, Mutate, cancel.Token));
        else
        {
            var result = await unit.ExecuteAsync(org, actor, null, true, Mutate, cancel.Token);
            Assert.Equal(outcome == "success", result.Succeeded);
            if (outcome == "actor") Assert.Equal("session_unavailable", result.ErrorCode);
        }
        Assert.Equal(outcome == "success", await organizations.FindOrganizationAsync(org, ct) is not null);
        Assert.Equal(outcome == "success", await organizations.FindMembershipAsync(org, actor, ct) is not null);
        Assert.Equal(outcome == "success", await work.FindBoardAsync(board, ct) is not null);
        // Refusal must release both gates, allowing a fresh command to commit.
        actorFence.Allowed = true;
        var fresh = Guid.NewGuid();
        Assert.True((await unit.ExecuteAsync(fresh, actor, null, true, async () => {
            await organizations.CreateOrganizationAsync(actor, fresh, "Fresh after rollback", null, DateTimeOffset.UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct)).Succeeded);
    }

    [Fact]
    public async Task PRD_03_Demo_Organization_rollback_does_not_overwrite_a_queued_Work_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture()));
        using var client = app.CreateClient();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var orgUnit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var workUnit = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var org = Guid.NewGuid(); var actor = Guid.NewGuid(); var board = Guid.NewGuid();
        await organizations.CreateOrganizationAsync(actor, org, "Concurrent Organization", null, DateTimeOffset.UtcNow, ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var denied = orgUnit.ExecuteAsync(org, actor, null, false, async () => {
            await organizations.UpdateOrganizationAsync(org, "Uncommitted name", null, null, 1, DateTimeOffset.UtcNow, ct);
            entered.SetResult(); await release.Task.WaitAsync(ct);
            return OrganizationOperation<bool>.Failure("fixture_refused");
        }, ct);
        await entered.Task.WaitAsync(ct);
        var later = workUnit.ExecuteReadAsync(org, actor, "fixture_refused", () => Task.FromResult(true), async () => {
            dispatched.SetResult();
            await work.CreateBoardAsync(org, actor, board, "Retained Work commit", null, BoardVisibility.Private, "COLOR", "blue", DateTimeOffset.UtcNow, ct);
            return WorkOperation<bool>.Success(true);
        }, ct);
        // WaitAsync on the occupied gate has synchronously yielded; Work must
        // not capture or mutate stores until Organization restores its snapshot.
        Assert.False(dispatched.Task.IsCompleted);
        release.SetResult();
        Assert.False((await denied.WaitAsync(ct)).Succeeded);
        Assert.True((await later.WaitAsync(ct)).Succeeded);
        Assert.Equal("Concurrent Organization", (await organizations.FindOrganizationAsync(org, ct))!.Name);
        Assert.NotNull(await work.FindBoardAsync(board, ct));
    }

    private sealed class OrganizationTransactionActorFixture : ICommandActorAuthorization
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Allowed); }
    }
}
