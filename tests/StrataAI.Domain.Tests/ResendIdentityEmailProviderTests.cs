using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
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
    [InlineData(409,false)]
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

    [Theory]
    [InlineData(400,true)]
    [InlineData(401,true)]
    [InlineData(408,false)]
    [InlineData(409,false)]
    [InlineData(429,false)]
    [InlineData(500,false)]
    [InlineData(503,false)]
    public async Task ARCH_08_FR_010_ErrorStatusIsClassifiedWithoutReadingItsBody(int status,bool permanent)
    {
        var content=new ControlledContent((_,_)=>throw new InvalidOperationException("synthetic-private-provider-body"),64);
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content=content })));
        client.MaxResponseContentBufferSize=65536;
        var error=await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
        Assert.Equal(permanent,error.Permanent);
        Assert.Equal(permanent ? "identity_provider_rejected" : "identity_provider_unavailable",error.Code);
        Assert.False(content.WasRead);
        Assert.True(content.WasDisposed);
        Assert.DoesNotContain("private",error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARCH_08_FR_010_OversizedSuccessfulReceiptIsRefusedForKnownAndStreamingLength(bool knownLength)
    {
        var bytes=Encoding.UTF8.GetBytes($"{{\"id\":\"{Guid.NewGuid()}\"}}".PadRight(65537));
        var content=new ControlledContent((stream,token)=>stream.WriteAsync(bytes,token).AsTask(),knownLength ? bytes.Length : null);
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=content })));
        var error=await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
        Assert.Equal("identity_provider_receipt_invalid",error.Code);
        Assert.False(error.Permanent);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task ARCH_08_FR_010_ReceiptAtTheExistingProductionLimitCanStillSucceed()
    {
        var receipt=Guid.NewGuid();
        var bytes=Encoding.UTF8.GetBytes($"{{\"id\":\"{receipt}\"}}".PadRight(65536));
        var content=new ControlledContent((stream,token)=>stream.WriteAsync(bytes,token).AsTask());
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=content })));
        Assert.Equal(receipt,await new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
        Assert.True(content.WasRead);
        Assert.True(content.WasDisposed);
    }

    [Theory]
    [InlineData(64,true)]
    [InlineData(65,false)]
    public async Task ARCH_08_FR_010_AnExplicitSmallerClientReceiptLimitStillApplies(int bytes,bool accepted)
    {
        var receipt=Guid.NewGuid();
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content=new StringContent($"{{\"id\":\"{receipt}\"}}".PadRight(bytes)) })));
        client.MaxResponseContentBufferSize=64;
        var operation=new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken);
        if (accepted) Assert.Equal(receipt,await operation);
        else Assert.Equal("identity_provider_receipt_invalid",(await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>operation)).Code);
    }

    [Theory]
    [InlineData(401,true)]
    [InlineData(409,false)]
    [InlineData(503,false)]
    public async Task ARCH_08_FR_010_LoopbackHttpErrorDoesNotWaitForTheWithheldBody(int status,bool permanent)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        using var cleanup=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cleanup.CancelAfter(TimeSpan.FromSeconds(6));
        var headersSent=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBody=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ServeAsync()
        {
            using var peer=await listener.AcceptTcpClientAsync(cleanup.Token);
            using var stream=peer.GetStream();
            using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,leaveOpen:true);
            Assert.Equal("POST /emails HTTP/1.1",await reader.ReadLineAsync(cleanup.Token));
            var lines=0;
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cleanup.Token))) Assert.True(++lines<=32);
            // Reject from headers without sending any response body at all.
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Length: 128\r\nConnection: close\r\n\r\n"),cleanup.Token);
            headersSent.SetResult();
            await releaseBody.Task.WaitAsync(cleanup.Token);
        }
        var peerTask=ServeAsync();
        using var client=new HttpClient(new SocketsHttpHandler { UseProxy=false,AllowAutoRedirect=false });
        client.Timeout=TimeSpan.FromSeconds(2); client.MaxResponseContentBufferSize=65536;
        try
        {
            var endpoint=new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/emails");
            var operation=new ResendIdentityEmailProvider(client,"test-credential",endpoint).SendAsync(Message(),cleanup.Token);
            await headersSent.Task.WaitAsync(cleanup.Token);
            var error=await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>operation);
            Assert.Equal(permanent,error.Permanent);
            Assert.Equal(permanent ? "identity_provider_rejected" : "identity_provider_unavailable",error.Code);
            Assert.False(releaseBody.Task.IsCompleted);
        }
        finally
        {
            releaseBody.TrySetResult(); cleanup.Cancel(); listener.Stop();
            try { await peerTask; }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { }
            catch (SocketException) when (cleanup.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData("private-malformed-json")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task ARCH_08_FR_010_InvalidSuccessBodiesHaveOnlyTheFixedRetryableReceiptError(string body)
    {
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(body) })));
        var error=await Assert.ThrowsAsync<IdentityEmailProviderException>(()=>new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),TestContext.Current.CancellationToken));
        Assert.Equal("identity_provider_receipt_invalid",error.Code);
        Assert.False(error.Permanent);
        Assert.DoesNotContain("private",error.Message);
    }

    [Fact]
    public async Task ARCH_08_FR_010_ResponseBodySharesTheClientDeadlineAndIsDisposed()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var content=new ControlledContent(async (_,token)=> { entered.SetResult(); await Task.Delay(Timeout.Infinite,token); });
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=content })));
        client.Timeout=TimeSpan.FromMilliseconds(200);
        using var cleanup=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cleanup.CancelAfter(TimeSpan.FromSeconds(3));
        var operation=new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),cleanup.Token);
        await entered.Task.WaitAsync(cleanup.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>operation);
        Assert.False(cleanup.IsCancellationRequested);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task ARCH_08_FR_010_CallerCancellationReachesReceiptReadWithInfiniteClientTimeout()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var content=new ControlledContent(async (_,token)=> { entered.SetResult(); await Task.Delay(Timeout.Infinite,token); });
        using var client=new HttpClient(new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=content })));
        client.Timeout=Timeout.InfiniteTimeSpan;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var operation=new ResendIdentityEmailProvider(client,"test-credential").SendAsync(Message(),cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>operation);
        Assert.True(content.WasDisposed);
    }

    private static IdentityEmailMessage Message()=>new("sender@example.test","recipient@example.test","test subject","https://app.example.test/reset-password#token=test-only-token","stable-version-key");
    private sealed class Transport(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> execute):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>execute(request,cancellationToken); }

    private sealed class ControlledContent(Func<Stream,CancellationToken,Task> write,long? length=null):HttpContent
    {
        public bool WasRead { get; private set; }
        public bool WasDisposed { get; private set; }
        protected override bool TryComputeLength(out long size) { size=length ?? 0; return length.HasValue; }
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>SerializeToStreamAsync(stream,context,CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken cancellationToken)
        { WasRead=true; return write(stream,cancellationToken); }
        protected override void Dispose(bool disposing) { WasDisposed=true; base.Dispose(disposing); }
    }
}
