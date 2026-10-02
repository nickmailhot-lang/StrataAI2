using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Kanban_metrics_measure_moves_conflicts_and_denials_without_private_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var board = await TelemetryBoard(owner, ct);
        async Task<Guid> CreateList(string name)
        {
            using var response = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        }
        var source = await CreateList("Private source title"); var destination = await CreateList("Private destination title");
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/cards", new { title = "Private card title" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var capture = new SharingMetricCapture(app.Services.GetRequiredService<BoardSharingTelemetry>().Meter);
        var key = Guid.NewGuid().ToString(); var command = new { destinationListId = destination, expectedVersion = 1 };
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", command, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        using var recovered = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", command, key);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", command);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var denied = await Mutate(outsider, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = source, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var reordered = await Mutate(owner, HttpMethod.Patch, $"/lists/{source}", new { name = "Private source title", version = 1, moveToEnd = true });
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, $"/lists/{source}", new { name = "Private source title", version = 2, beforeListId = source });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var samples = await capture.WaitAsync(6, ct); AssertSafeSharingSamples(samples, 6);
        Assert.Equal(2, samples.Count(sample => sample.Name == "strataai.board_sharing.requests"
            && sample.Tags["operation"] as string == "card_move" && sample.Tags["outcome"] as string == "success" && sample.Tags["keyed_attempt"] is true));
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "card_move" && sample.Tags["error_code"] as string == "version_conflict");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "card_move" && sample.Tags["error_code"] as string == "card_not_found");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "list_update" && sample.Tags["error_code"] as string == "invalid_move_position");
    }
}
