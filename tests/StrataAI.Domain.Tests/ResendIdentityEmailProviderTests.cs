using System.Net;
using System.Text.Json;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Domain.Tests;

public sealed class ResendIdentityEmailProviderTests
{
    [Fact]
    public async Task ARCH_07_AC_002_AdapterUsesIdempotencyHeaderAndNeverTokenInUrl()
    {
        var receipt=Guid.NewGuid();
        var transport=new Transport(async (request,token)=>
        {
            Assert.Equal("https://api.resend.com/emails",request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
            Assert.Equal("test-credential",request.Headers.Authorization.Parameter);
            Assert.Equal("stable-version-key",request.Headers.GetValues("Idempotency-Key").Single());
            using var document=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("recipient@example.test",document.RootElement.GetProperty("to")[0].GetString());
            Assert.Contains("#token=test-only-token",document.RootElement.GetProperty("text").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent($"{{\"id\":\"{receipt}\"}}") };
        });
        using var client=new HttpClient(transport);
        Assert.Equal(receipt,await new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(429,false)]
    [InlineData(500,false)]
    [InlineData(401,true)]
    [InlineData(400,true)]
    public async Task ARCH_07_TC_01_ProviderErrorsAreClassifiedWithoutBodyDisclosure(int status,bool permanent)
    {
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content=new StringContent("sensitive-message-and-token") })));
        var error=await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
        Assert.Equal(permanent,error.Permanent);
        Assert.DoesNotContain("sensitive",error.Message);
        Assert.Equal(permanent ? "identity_provider_rejected" : "identity_provider_unavailable",error.Code);
    }

    [Fact]
    public async Task ARCH_07_TC_01_MissingReceiptCannotBeMarkedSent()
    {
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("{}") })));
        var error=await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
        Assert.Equal("identity_provider_receipt_invalid",error.Code);
        Assert.False(error.Permanent);
    }

    private static IdentityEmailMessage Message()=>new("sender@example.test","recipient@example.test","test subject","https://app.example.test/reset-password#token=test-only-token","stable-version-key");
    private sealed class Transport(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> execute):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>execute(request,cancellationToken); }
}
