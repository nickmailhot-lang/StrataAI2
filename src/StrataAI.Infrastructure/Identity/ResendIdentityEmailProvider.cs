using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

public sealed class ResendIdentityEmailProvider(HttpClient client,string apiKey,Uri? endpoint=null) : IIdentityEmailProvider
{
    private readonly Uri _endpoint=endpoint ?? new Uri("https://api.resend.com/emails");
    public async Task<Guid> SendAsync(IdentityEmailMessage message,CancellationToken cancellationToken)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,_endpoint);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiKey);
        request.Headers.Add("Idempotency-Key",message.IdempotencyKey);
        request.Content=JsonContent.Create(new { from=message.Sender,to=new[]{message.Recipient},subject=message.Subject,text=message.Text });
        using var response=await client.SendAsync(request,cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var temporary=response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)response.StatusCode>=500;
            // Never read or persist failure bodies (they can echo the message).
            throw new IdentityEmailProviderException(temporary ? "identity_provider_unavailable" : "identity_provider_rejected",!temporary);
        }
        using var document=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),cancellationToken:cancellationToken);
        if (!document.RootElement.TryGetProperty("id",out var property) || property.ValueKind!=JsonValueKind.String ||
            !Guid.TryParse(property.GetString(),out var receipt) || receipt==Guid.Empty)
            throw new IdentityEmailProviderException("identity_provider_receipt_invalid",false);
        return receipt;
    }
}
