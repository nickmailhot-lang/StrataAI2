using System.Security.Cryptography;
using System.Text.Json;

namespace StrataAI.Application.WorkManagement;

// The host supplies only a validated retry key; Application has no HTTP dependency.
public interface IWorkCommandContext
{
    Guid? IdempotencyKey { get; }
}

public sealed record WorkCommand(Guid ActorId, Guid? Key, string Fingerprint, string ScopeFailureCode)
{
    public static WorkCommand Create(Guid actorId, Guid? key, string operation, Guid resourceId,
        object input, string scopeFailureCode) => new(actorId, key,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { operation, resourceId, input }))),
            scopeFailureCode);
}
