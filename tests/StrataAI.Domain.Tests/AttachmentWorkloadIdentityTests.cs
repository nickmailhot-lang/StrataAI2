using System.Net;
using System.Net.Sockets;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace StrataAI.Domain.Tests;

[CollectionDefinition("Attachment AWS environment", DisableParallelization = true)]
public sealed class AttachmentAwsEnvironmentCollection;

[Collection("Attachment AWS environment")]
public sealed class AttachmentWorkloadIdentityTests
{
    private const string Oidc = "private-fixture-oidc-token";
    private const string Role = "arn:aws:iam::123456789012:role/attachment-fixture";
    private const string Access = "ASIAFIXTUREONLY000000";
    private const string Session = "private-fixture-session-token";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_07_AttachmentWebIdentityUsesRealSdkExchangeAndTemporaryS3Credentials(bool refused)
    {
        var root = Path.Combine(Path.GetTempPath(), "strataai-web-identity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var token = Path.Combine(root, "token"); File.WriteAllText(token, Oidc);
        var config = Path.Combine(root, "empty-config"); File.WriteAllText(config, "");
        var values = new Dictionary<string, string?>
        {
            ["AWS_WEB_IDENTITY_TOKEN_FILE"] = token, ["AWS_ROLE_ARN"] = Role, ["AWS_ROLE_SESSION_NAME"] = "attachment-fixture",
            ["AWS_REGION"] = "us-east-1", ["AWS_DEFAULT_REGION"] = "us-east-1", ["AWS_EC2_METADATA_DISABLED"] = "true",
            ["AWS_CONFIG_FILE"] = config, ["AWS_SHARED_CREDENTIALS_FILE"] = config,
            ["AWS_IGNORE_CONFIGURED_ENDPOINT_URLS"] = "false", ["NO_PROXY"] = "127.0.0.1,localhost",
        };
        var previous = values.Keys.Append("AWS_ENDPOINT_URL_STS").ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            await using var sts = new Sts(refused); values["AWS_ENDPOINT_URL_STS"] = sts.Endpoint;
            foreach (var (key, value) in values) Environment.SetEnvironmentVariable(key, value);
            using var credentials = AssumeRoleWithWebIdentityCredentials.FromEnvironmentVariables();
            Assert.Equal(token, credentials.WebIdentityTokenFile); Assert.Equal(Role, credentials.RoleArn);
            using var transport = new S3Transport();
            using var client = new AmazonS3Client(credentials, new AmazonS3Config
            { RegionEndpoint = RegionEndpoint.USEast1, HttpClientFactory = new S3Factory(transport), MaxErrorRetry = 0 });
            var request = new GetObjectMetadataRequest
            { BucketName = "strataai-private-fixture", Key = "objects/fixture", ExpectedBucketOwner = "123456789012" };
            if (refused)
            {
                var failure = await Assert.ThrowsAsync<AmazonClientException>(() => client.GetObjectMetadataAsync(request, TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
                var error = Assert.IsAssignableFrom<AmazonServiceException>(failure.InnerException);
                Assert.Equal("AccessDenied", error.ErrorCode); Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
                Assert.Equal(0, transport.Calls);
            }
            else
            {
                var first = await credentials.GetCredentialsAsync().WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
                Assert.Equal(Access, first.AccessKey); Assert.Equal(Session, first.Token); Assert.True(first.UseToken);
                var cached = await credentials.GetCredentialsAsync(); Assert.Equal(first.AccessKey, cached.AccessKey);
                var response = await client.GetObjectMetadataAsync(request, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode); Assert.Equal(1, transport.Calls);
            }
            await sts.Completed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal(1, sts.Calls); Assert.Equal(Oidc, File.ReadAllText(token));
        }
        finally
        {
            foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value);
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), Path.GetDirectoryName(Path.GetFullPath(root)));
            Assert.StartsWith("strataai-web-identity-", Path.GetFileName(root)); Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Sts : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        public Task Completed { get; }
        public int Calls;
        public string Endpoint { get; }
        public Sts(bool refused)
        {
            cancellation.CancelAfter(TimeSpan.FromSeconds(12)); listener.Start();
            Endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            Completed = Serve(refused);
        }
        private async Task Serve(bool refused)
        {
            using var peer = await listener.AcceptTcpClientAsync(cancellation.Token); using var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellation.Token); Assert.Equal("POST / HTTP/1.1", line);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellation.Token)))
            {
                Assert.True(headers.Count < 64); var split = line.IndexOf(':'); Assert.True(split > 0);
                headers.Add(line[..split], line[(split + 1)..].Trim());
            }
            Assert.False(headers.ContainsKey("Authorization"));
            var count = int.Parse(headers["Content-Length"], System.Globalization.CultureInfo.InvariantCulture); Assert.InRange(count, 1, 8192);
            var body = new char[count]; var read = 0;
            while (read < count) { var next = await reader.ReadAsync(body.AsMemory(read), cancellation.Token); Assert.True(next > 0); read += next; }
            var fields = new string(body).Split('&').Select(q => q.Split('=', 2)).ToDictionary(q => WebUtility.UrlDecode(q[0]), q => WebUtility.UrlDecode(q[1]));
            Assert.Equal("AssumeRoleWithWebIdentity", fields["Action"]); Assert.Equal(Oidc, fields["WebIdentityToken"]);
            Assert.Equal(Role, fields["RoleArn"]); Assert.Equal("attachment-fixture", fields["RoleSessionName"]);
            Interlocked.Increment(ref Calls);
            var xml = refused
                ? "<ErrorResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><Error><Type>Sender</Type><Code>AccessDenied</Code><Message>private-fixture-denied</Message></Error><RequestId>fixture</RequestId></ErrorResponse>"
                : "<AssumeRoleWithWebIdentityResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><AssumeRoleWithWebIdentityResult><Credentials><AccessKeyId>" + Access
                    + "</AccessKeyId><SecretAccessKey>private-fixture-secret-key</SecretAccessKey><SessionToken>" + Session + "</SessionToken><Expiration>"
                    + DateTimeOffset.UtcNow.AddHours(1).ToString("O") + "</Expiration></Credentials></AssumeRoleWithWebIdentityResult><ResponseMetadata><RequestId>fixture</RequestId></ResponseMetadata></AssumeRoleWithWebIdentityResponse>";
            var bytes = Encoding.UTF8.GetBytes(xml); var status = refused ? "403 Forbidden" : "200 OK";
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/xml\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellation.Token); await stream.WriteAsync(bytes, cancellation.Token); await stream.FlushAsync(cancellation.Token);
        }
        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel(); listener.Stop();
            try { await Completed; } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { cancellation.Dispose(); }
        }
    }
    private sealed class S3Factory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => true;
    }
    private sealed class S3Transport : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https", request.RequestUri!.Scheme); Assert.EndsWith(".amazonaws.com", request.RequestUri.Host);
            Assert.Equal(HttpMethod.Head, request.Method); Assert.Equal("/objects/fixture", request.RequestUri.AbsolutePath);
            Assert.Equal("123456789012", Assert.Single(request.Headers.GetValues("x-amz-expected-bucket-owner")));
            Assert.Equal(Session, Assert.Single(request.Headers.GetValues("x-amz-security-token")));
            var signature = Assert.Single(request.Headers.GetValues("Authorization"));
            Assert.StartsWith("AWS4-HMAC-SHA256 ", signature); Assert.Contains("Credential=" + Access + "/", signature);
            Assert.DoesNotContain(Oidc, string.Join(";", request.Headers.Select(h => string.Join(",", h.Value))));
            Interlocked.Increment(ref Calls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        }
    }
}
