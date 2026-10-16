using System.Net;

namespace StrataAI.Domain.Tests;

// Real adapter/processor composition: the provider has a same-key request in
// progress, then acknowledges it. No provider payload is logged or persisted.
internal sealed class ResendConflictTransport : HttpMessageHandler
{
    public Guid Receipt { get; } = Guid.NewGuid();
    public List<(string Key, string Body)> Requests { get; } = [];
    public int Effects { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(cancellationToken)));
        if (Requests.Count == 1) return new HttpResponseMessage(HttpStatusCode.Conflict)
        { Content = new StringContent("{\"name\":\"concurrent_idempotent_requests\",\"message\":\"synthetic-private-provider-text\"}") };
        Effects++;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"{Receipt}\"}}") };
    }
}
