using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_production_host_resolves_complete_real_classification_provider_graph()
    {
        await using var app = new RuntimeCompositionTests.RuntimeCompositionFactory("production"); using var client = app.CreateClient();
        var providers = app.Services;
        var store = providers.GetRequiredService<IRetentionSourceCommandStore>();
        Assert.IsType<PostgresRetentionSourceStore>(store);
        Assert.Same(store, providers.GetRequiredService<IRetentionSourceReferenceReader>());
        Assert.IsType<PostgresRetentionProducerReferences>(providers.GetRequiredService<IRetentionProducerReferences>());
        Assert.IsType<PostgresRetentionAuthorizationReferences>(providers.GetRequiredService<IRetentionWorkSourceReferences>());
        Assert.NotNull(providers.GetRequiredService<RetentionSourceReviewService>());
        Assert.NotNull(providers.GetRequiredService<RetentionSourceClassificationService>());
        // Disconnected Production must remain unavailable, never fall back
        // to Demo data or manufacture a review/classification for a request.
        using var health = await client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
    }

    [Fact]
    public async Task PRD_70_mapped_classification_HTTP_owns_real_providers_confirms_three_sources_and_recovers_after_movement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        using var anonymous = app.CreateClient(); await RegisterAndLogin(owner); await RegisterAndLogin(foreign);
        var (actor, parent) = await ConfigurationParent(owner, "HTTP classification", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var commandStore = app.Services.GetRequiredService<IRetentionSourceCommandStore>();
        Assert.Same(commandStore, app.Services.GetRequiredService<IRetentionSourceReferenceReader>());
        Assert.Same(commandStore, app.Services.GetRequiredService<IRetentionProducerReferences>());
        Assert.Same(commandStore, app.Services.GetRequiredService<IRetentionWorkSourceReferences>());
        var board = Guid.NewGuid(); var destination = Guid.NewGuid(); var list = Guid.NewGuid(); var destinationList = Guid.NewGuid();
        var card = Guid.NewGuid(); var comment = Guid.NewGuid(); var attachment = Guid.NewGuid();
        var now = app.Services.GetRequiredService<IClock>().UtcNow; now = now.AddTicks(-(now.Ticks % 10));
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            await work.CreateBoardAsync(parent.Id, actor, board, "Private Board", null, BoardVisibility.Private, "COLOR", null, now, ct);
            await work.CreateBoardAsync(parent.Id, actor, destination, "Moved Board", null, BoardVisibility.Private, "COLOR", null, now, ct);
            await work.CreateListAsync(board, list, "Private list", null, now, ct);
            await work.CreateListAsync(destination, destinationList, "Moved list", null, now, ct);
            await work.CreateCardAsync(list, card, "Secret source title", null, null, now, ct);
            await ((ICardCommentStore)work).CreateAsync(comment, parent.Id, card, actor, "Secret comment body", now, ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(attachment, parent.Id, card, actor, "Secret attachment", "https://example.test/private-source", now, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var originals = new List<(string Path, object Request, string Key, string Response)>();
        foreach (var (kind, id) in new[] { ("card", card), ("cardComment", comment), ("attachment", attachment) })
        {
            var root = $"/organizations/{parent.Id}/retention-sources/{kind}/{id}";
            using var denied = await foreign.GetAsync(root + "/review", ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var unsigned = await anonymous.GetAsync(root + "/review", ct); Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
            using var review = await owner.GetAsync(root + "/review", ct); Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            Assert.True(review.Headers.CacheControl!.Private); Assert.True(review.Headers.CacheControl.NoStore);
            var evidenceText = await review.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain("Secret", evidenceText); Assert.DoesNotContain("private-source", evidenceText);
            var evidence = await review.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(kind, evidence.GetProperty("kind").GetString()); Assert.Equal(board, evidence.GetProperty("boardId").GetGuid());
            var request = new { review = evidence, category = "explicit.reviewed", jurisdiction = (string?)null, confirmed = true };
            var path = root + "/classification"; var key = Guid.NewGuid().ToString("D");
            using var unconfirmed = await Mutate(owner, HttpMethod.Post, path,
                new { review = evidence, category = "explicit.reviewed", jurisdiction = (string?)null, confirmed = false }, key);
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
            using var foreignValidation = await Mutate(foreign, HttpMethod.Post, path, new { invalid = true }, key);
            Assert.Equal(HttpStatusCode.NotFound, foreignValidation.StatusCode);
            owner.DefaultRequestHeaders.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString("D"));
            using var switched = await Mutate(owner, HttpMethod.Post, path, request, key); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
            owner.DefaultRequestHeaders.Remove("X-StrataAI-Expected-Actor");
            using var first = await Mutate(owner, HttpMethod.Post, path, request, key); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.True(first.Headers.CacheControl!.Private); Assert.True(first.Headers.CacheControl.NoStore);
            var body = await first.Content.ReadAsStringAsync(ct); using var parsed = JsonDocument.Parse(body);
            Assert.Equal(kind == "attachment" ? "document" : "operationalWork", parsed.RootElement.GetProperty("reference").GetProperty("recordClass").GetString());
            Assert.Equal(actor, parsed.RootElement.GetProperty("actorId").GetGuid());
            Assert.DoesNotContain("fingerprint", body); Assert.DoesNotContain("keyId", body);
            using var retry = await Mutate(owner, HttpMethod.Post, path, request, key);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(body, await retry.Content.ReadAsStringAsync(ct));
            using var conflict = await Mutate(owner, HttpMethod.Post, path,
                new { review = evidence, category = "different", jurisdiction = (string?)null, confirmed = true }, key);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var newKey = await Mutate(owner, HttpMethod.Post, path, request, Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.NotFound, newKey.StatusCode);
            originals.Add((path, request, key, body));
        }
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            await work.MoveCardAsync(card, destinationList, null, 1, now.AddSeconds(1), ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        foreach (var original in originals)
        {
            using var retry = await Mutate(owner, HttpMethod.Post, original.Path, original.Request, original.Key);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(original.Response, await retry.Content.ReadAsStringAsync(ct));
            using var denied = await Mutate(foreign, HttpMethod.Post, original.Path, original.Request, original.Key);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("explicit.reviewed", await denied.Content.ReadAsStringAsync(ct));
        }
        Assert.Equal(3, ((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id).Count(a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED"));
    }
}
