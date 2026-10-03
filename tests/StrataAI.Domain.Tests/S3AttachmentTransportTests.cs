using System.Net;
using System.Text;
using System.Xml.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

// Exercise the real SDK signing/serialization/response pipeline without a cloud
// account or network. Only its final HttpClient transport is substituted.
public sealed class S3AttachmentTransportTests
{
    private sealed class Factory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => true;
    }
    private sealed class Transport(AttachmentObjectReference reference, bool embeddedError) : HttpMessageHandler
    {
        public int Parts, Completions, Aborts;
        public readonly List<string> Privacy = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Assert.Equal("https", uri.Scheme); Assert.EndsWith(".amazonaws.com", uri.Host);
            Assert.StartsWith("strataai-private-fixture.", uri.Host);
            Assert.True(request.Headers.TryGetValues("x-amz-expected-bucket-owner", out var owners)); Assert.Equal("123456789012", Assert.Single(owners));
            // AWS installs its signature as a raw header. Assert the exact
            // signing scheme without depending on HttpClient's typed parser.
            Assert.True(request.Headers.TryGetValues("Authorization", out var authorization), "SDK transport must carry an authorization header.");
            Assert.True(Assert.Single(authorization).StartsWith("AWS4-HMAC-SHA256 ", StringComparison.Ordinal), "SDK transport must carry a SigV4 signature.");
            Assert.False(request.Headers.Contains("x-amz-acl"));
            var query = uri.Query;
            if (query.Contains("publicAccessBlock", StringComparison.Ordinal))
            {
                Privacy.Add("block"); return Xml("PublicAccessBlockConfiguration", "<BlockPublicAcls>true</BlockPublicAcls><IgnorePublicAcls>true</IgnorePublicAcls><BlockPublicPolicy>true</BlockPublicPolicy><RestrictPublicBuckets>true</RestrictPublicBuckets>");
            }
            if (query.Contains("policyStatus", StringComparison.Ordinal)) { Privacy.Add("policy"); return Xml("PolicyStatus", "<IsPublic>false</IsPublic>"); }
            if (query.Contains("ownershipControls", StringComparison.Ordinal)) { Privacy.Add("ownership"); return Xml("OwnershipControls", "<Rule><ObjectOwnership>BucketOwnerEnforced</ObjectOwnership></Rule>"); }
            Assert.Equal("/" + reference.ObjectKey, uri.AbsolutePath);
            if (request.Method == HttpMethod.Post && query.Contains("uploads", StringComparison.Ordinal))
            {
                Assert.Equal("application/octet-stream", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal("AES256", Assert.Single(request.Headers.GetValues("x-amz-server-side-encryption")));
                return Xml("InitiateMultipartUploadResult", $"<Bucket>strataai-private-fixture</Bucket><Key>{reference.ObjectKey}</Key><UploadId>fixture-upload-id</UploadId>");
            }
            if (request.Method == HttpMethod.Put && query.Contains("partNumber=1", StringComparison.Ordinal))
            {
                Assert.Contains("uploadId=fixture-upload-id", query); Parts++;
                var body = Encoding.UTF8.GetString(await request.Content!.ReadAsByteArrayAsync(ct)); Assert.Contains("fixture-bytes", body);
                var response = new HttpResponseMessage(HttpStatusCode.OK); response.Headers.ETag = new("\"fixture-part\""); response.Content = new StringContent(""); return response;
            }
            if (request.Method == HttpMethod.Post && query.Contains("uploadId=fixture-upload-id", StringComparison.Ordinal))
            {
                Completions++; Assert.Equal("*", Assert.Single(request.Headers.GetValues("If-None-Match")));
                var xml = XDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("1", Assert.Single(xml.Descendants(), x => x.Name.LocalName == "PartNumber").Value);
                Assert.Equal("\"fixture-part\"", Assert.Single(xml.Descendants(), x => x.Name.LocalName == "ETag").Value);
                if (embeddedError) return Xml("Error", "<Code>InternalError</Code><Message>private provider response</Message><RequestId>fixture-request</RequestId>");
                return Xml("CompleteMultipartUploadResult", $"<Bucket>strataai-private-fixture</Bucket><Key>{reference.ObjectKey}</Key><ETag>\"fixture-object\"</ETag>");
            }
            if (request.Method == HttpMethod.Delete && query.Contains("uploadId=fixture-upload-id", StringComparison.Ordinal))
            { Aborts++; return new(HttpStatusCode.NoContent); }
            throw new InvalidOperationException("Unexpected provider request in isolated transport fixture.");
        }
        private static HttpResponseMessage Xml(string root, string body) => new(HttpStatusCode.OK)
        { Content = new StringContent($"<{root} xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">{body}</{root}>", Encoding.UTF8, "application/xml") };
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_07_TC_01_Real_sdk_sends_private_signed_conditional_requests_and_refuses_embedded_completion_errors(bool embeddedError)
    {
        var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid()); using var transport = new Transport(reference, embeddedError);
        // Non-secret fixture credentials are never sent to a network or output.
        using var client = new AmazonS3Client(new BasicAWSCredentials("fixture-access", "fixture-secret"), new AmazonS3Config
        { RegionEndpoint = RegionEndpoint.USEast1, HttpClientFactory = new Factory(transport), MaxErrorRetry = 0 });
        var store = new S3AttachmentObjectStorage(client, "strataai-private-fixture", "123456789012");
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("fixture-bytes")); var ct = TestContext.Current.CancellationToken;
        if (embeddedError)
        {
            var failure = await Assert.ThrowsAsync<AttachmentStorageException>(() => store.WritePrivateAsync(reference, source, 100, ct));
            Assert.Equal("object_storage_unavailable", failure.Code); Assert.DoesNotContain("private provider", failure.ToString()); Assert.Equal(1, transport.Aborts);
        }
        else { var result = await store.WritePrivateAsync(reference, source, 100, ct); Assert.Equal(13, result.SizeBytes); Assert.Equal(0, transport.Aborts); }
        Assert.Equal(new[] { "block", "policy", "ownership" }, transport.Privacy); Assert.Equal(1, transport.Parts); Assert.Equal(1, transport.Completions); Assert.True(source.CanRead);
    }
}
