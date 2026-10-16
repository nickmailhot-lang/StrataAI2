using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

public sealed class ResendIdentityEmailProvider(HttpClient client,string apiKey,Uri? endpoint=null) : IIdentityEmailProvider
{
    private const long MaximumReceiptBytes=65536;
    private readonly Uri _endpoint=endpoint ?? new Uri("https://api.resend.com/emails");
    public async Task<Guid> SendAsync(IdentityEmailMessage message,CancellationToken cancellationToken)
    {
        using var execution=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Headers-only completion excludes body reads from HttpClient.Timeout.
        // Keep one budget across request headers and the bounded success body.
        if (client.Timeout!=Timeout.InfiniteTimeSpan) execution.CancelAfter(client.Timeout);
        using var request=new HttpRequestMessage(HttpMethod.Post,_endpoint);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiKey);
        request.Headers.Add("Idempotency-Key",message.IdempotencyKey);
        request.Content=JsonContent.Create(new { from=message.Sender,to=new[]{message.Recipient},subject=message.Subject,text=message.Text });
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,execution.Token);
        if (!response.IsSuccessStatusCode)
        {
            // Resend also uses 409 while an earlier same-key request is still
            // processing. Retry the frozen key/body within the existing job
            // limits; never inspect error text or invent a new delivery key.
            var temporary=response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict || (int)response.StatusCode>=500;
            // Never read or persist failure bodies (they can echo the message).
            throw new IdentityEmailProviderException(temporary ? "identity_provider_unavailable" : "identity_provider_rejected",!temporary);
        }
        try
        {
            await response.Content.LoadIntoBufferAsync(Math.Min(MaximumReceiptBytes,client.MaxResponseContentBufferSize),execution.Token);
            using var document=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(execution.Token),cancellationToken:execution.Token);
            if (document.RootElement.ValueKind!=JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("id",out var property) || property.ValueKind!=JsonValueKind.String ||
                !Guid.TryParse(property.GetString(),out var receipt) || receipt==Guid.Empty)
                throw new IdentityEmailProviderException("identity_provider_receipt_invalid",false);
            execution.Token.ThrowIfCancellationRequested();
            return receipt;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException)
        {
            execution.Token.ThrowIfCancellationRequested();
            throw new IdentityEmailProviderException("identity_provider_receipt_invalid",false);
        }
    }
}
