using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("INTERNAL", "MEMBER")]
    [InlineData("PORTAL", "OWNER")]
    public async Task Invitation_publication_uses_reconstructable_proof_and_does_not_republish_creation_retry(string surface, string role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var signer = new IdentityDeliveryTokenSigner("mail-test", new Dictionary<string, string> {
            ["mail-test"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) });
        var publisher = new CapturingInvitationPublisher(signer);
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IInvitationMailPublisher>(publisher));
        using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Mail publication council" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid();
        var payload = new { email = "published-recipient@example.test", surface, targetRole = role };
        async Task<HttpResponseMessage> Create()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/organizations/{org}/invitations") { Content = JsonContent.Create(payload) };
            request.Headers.Add("X-StrataAI-Request", "1");
            request.Headers.Add("Idempotency-Key", key.ToString());
            return await owner.SendAsync(request, ct);
        }
        using var issued = await Create();
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var acknowledgment = await issued.Content.ReadAsStringAsync(ct);
        var id = JsonDocument.Parse(acknowledgment).RootElement.GetProperty("id").GetGuid();
        using var retry = await Create();
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(acknowledgment, await retry.Content.ReadAsStringAsync(ct));
        Assert.Equal(1, publisher.TokenCalls);
        var published = Assert.Single(publisher.Published);
        Assert.Equal(id, published.Invitation.Id);
        Assert.Equal(org, published.Invitation.OrganizationId);
        Assert.Equal("mail-test", published.KeyId);
        var proof = signer.DeriveInvitation(org, id, published.KeyId);
        Assert.DoesNotContain(proof, acknowledgment, StringComparison.Ordinal);
        Assert.Equal(app.Services.GetRequiredService<ISecureTokenService>().Hash(proof), published.Invitation.TokenHash);
        Assert.NotNull(await app.Services.GetRequiredService<IInvitationStore>().FindActiveByTokenHashAsync(
            published.Invitation.TokenHash, DateTimeOffset.UtcNow, ct));
        using var conflictRequest = new HttpRequestMessage(HttpMethod.Post, $"/organizations/{org}/invitations") {
            Content = JsonContent.Create(new { email = "different@example.test", surface, targetRole = role }) };
        conflictRequest.Headers.Add("X-StrataAI-Request", "1"); conflictRequest.Headers.Add("Idempotency-Key", key.ToString());
        using var conflict = await owner.SendAsync(conflictRequest, ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Single(publisher.Published);
    }

    private sealed class CapturingInvitationPublisher(IInvitationDeliveryTokenSigner signer) : IInvitationMailPublisher
    {
        public int TokenCalls { get; private set; }
        public List<(InvitationRecord Invitation, string KeyId)> Published { get; } = [];
        public InvitationDeliveryToken CreateToken(Guid organizationId, Guid invitationId)
        {
            TokenCalls++;
            return new(signer.DeriveInvitation(organizationId, invitationId, signer.CurrentKeyId), signer.CurrentKeyId);
        }
        public Task PublishAsync(InvitationRecord invitation, string keyId, string correlationId, CancellationToken cancellationToken)
        {
            Published.Add((invitation, keyId));
            return Task.CompletedTask;
        }
    }
}
