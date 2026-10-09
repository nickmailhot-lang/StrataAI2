using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_15_Repeated_card_history_bounds_target_admission_reads_without_losing_event_identity()
    {
        var ct = TestContext.Current.CancellationToken; var reads = 0;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(service => service.ServiceType == typeof(IWorkBoardAuthorization));
            services.Remove(original);
            services.Add(new ServiceDescriptor(typeof(IWorkBoardAuthorization), provider =>
            {
                var inner = (IWorkBoardAuthorization)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.GetServiceOrCreateInstance(provider, original.ImplementationType!));
                return new CountActivityBoardReads(inner, () => Interlocked.Increment(ref reads));
            }, original.Lifetime));
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var journal = app.Services.GetRequiredService<IWorkEventStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var transactions = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var at = DateTimeOffset.UtcNow.AddDays(1);
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Repeated history", null, null, at, ct);
        var identities = new HashSet<Guid>();
        var seeded = await transactions.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true), async () =>
        {
            for (var index = 0; index < 51; index++)
            {
                var id = Guid.NewGuid(); identities.Add(id);
                await journal.AppendAsync(new(id, f.Organization, f.Board, f.Owner, "CARD_UPDATED", "Card", card.Id,
                    index + 1, "fixture", at.AddSeconds(index)), ct);
            }
            return WorkOperation<bool>.Success(true);
        }, ct);
        Assert.True(seeded.Succeeded); Interlocked.Exchange(ref reads, 0);
        using var response = await member.GetAsync($"/cards/{card.Id}/activity", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var events = page.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(50, events.Length);
        Assert.All(events, item => Assert.Contains(item.GetProperty("eventId").GetGuid(), identities));
        Assert.Equal(50, events.Select(item => item.GetProperty("eventId").GetGuid()).Distinct().Count());
        Assert.NotNull(page.GetProperty("nextCursor").GetString());
        // A repeated target must not require one Board admission per event at
        // each security boundary. Preserve bounded fresh checks at those boundaries.
        Assert.InRange(Volatile.Read(ref reads), 1, 12);
    }

    private sealed class CountActivityBoardReads(IWorkBoardAuthorization inner, Action count) : IWorkBoardAuthorization
    {
        public Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid boardId, Guid? actorId, CancellationToken cancellationToken = default)
        { count(); return inner.GetSyncScopeAsync(boardId, actorId, cancellationToken); }
    }
}
