using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_27_Omitted_and_explicit_default_share_the_original_creation_intent(bool explicitFirst)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var body = new Dictionary<string, object?> { ["name"] = "Default classification", ["description"] = null };
        if (explicitFirst) body["type"] = "STRATA";
        var key = Guid.NewGuid().ToString();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadAsStringAsync(ct);
        var id = JsonDocument.Parse(original).RootElement.GetProperty("organization").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var before = await store.FindOrganizationAsync(id, ct);
        if (explicitFirst) body.Remove("type"); else body["type"] = "STRATA";
        using var replay = await Mutate(owner, HttpMethod.Post, "/organizations", body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(before, await store.FindOrganizationAsync(id, ct));
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        Assert.Single(await store.ListOrganizationsForUserAsync(actor, ct));
    }

    [Fact]
    public async Task PRD_27_Legacy_receipt_without_classification_recovers_generic_without_asserting_strata()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid();
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"strataai:organization:create:v1:{actor:D}:{key:D}"))[..16]);
        const string name = "Unclassified legacy organization"; string? description = null;
        var now = app.Services.GetRequiredService<IClock>().UtcNow;
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var receipts = app.Services.GetRequiredService<IOrganizationCreationReplayStore>();
        var seeded = await app.Services.GetRequiredService<IOrganizationUnitOfWork>().ExecuteAsync(id, actor, null, true, async () =>
        {
            // Upgrade preserves the old tenant as Generic; its receipt predates the type field.
            var organization = await store.CreateOrganizationAsync(actor, id, name, description, now, ct, "GENERIC");
            await store.AppendAuditAsync(id, actor, "ORGANIZATION_CREATED", "Organization", id, "legacy-fixture", ct);
            var oldPayload = JsonNode.Parse(JsonSerializer.Serialize(new OrganizationSummary(organization, OrganizationRole.Owner)))!;
            Assert.True(oldPayload["Organization"]!.AsObject().Remove("Type"));
            var original = oldPayload.Deserialize<OrganizationSummary>()!;
            Assert.Equal("GENERIC", original.Organization.Type);
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { name, description })));
            await receipts.SaveAsync(id, actor, key, new(fingerprint, original, now.AddHours(24)), ct);
            return OrganizationOperation<OrganizationSummary>.Success(original);
        }, ct);
        Assert.True(seeded.Succeeded);
        var before = await store.FindOrganizationAsync(id, ct);
        var receiptBefore = await receipts.ReadAsync(id, actor, key, ct);
        using var replay = await Mutate(owner, HttpMethod.Post, "/organizations", new { name, description }, key.ToString());
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var recovered = await replay.Content.ReadFromJsonAsync<OrganizationSummary>(ct);
        Assert.Equal(seeded.Value, recovered);
        using var refused = await Mutate(owner, HttpMethod.Post, "/organizations", new { name, description, type = "STRATA" }, key.ToString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("idempotency_conflict", (await refused.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(before, await store.FindOrganizationAsync(id, ct));
        Assert.Equal(receiptBefore, await receipts.ReadAsync(id, actor, key, ct));
        Assert.Single(await store.ListOrganizationsForUserAsync(actor, ct));
    }
}
