using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

// Durable jobs store token ID, purpose and key version, never the bearer token.
// API and Worker reconstruct the same token only with the runtime key ring.
public sealed partial class IdentityDeliveryTokenSigner : IIdentityDeliveryTokenSigner, IDisposable
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _disposed;
    public string CurrentKeyId { get; }

    public IdentityDeliveryTokenSigner(string currentKeyId, IReadOnlyDictionary<string, string> base64Keys)
    {
        if (base64Keys.Count is < 1 or > 16 || !KeyIdPattern().IsMatch(currentKeyId))
            throw new ArgumentException("Invalid identity token key ring.");
        try
        {
            foreach (var (id, value) in base64Keys)
            {
                if (!KeyIdPattern().IsMatch(id)) throw new ArgumentException("Invalid identity token key ID.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(value); }
                catch (FormatException) { throw new ArgumentException("Identity token keys must be base64 encoded."); }
                if (bytes.Length != 32)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    throw new ArgumentException("Identity token keys must contain 32 bytes.");
                }
                _keys.Add(id, bytes);
            }
            if (!_keys.ContainsKey(currentKeyId)) throw new ArgumentException("Current identity token key is absent.");
            CurrentKeyId = currentKeyId;
        }
        catch
        {
            foreach (var bytes in _keys.Values) CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    public string Derive(Guid tokenId, IdentityTokenPurpose purpose, string keyId)
    {
        if (tokenId == Guid.Empty) throw new ArgumentException("Token ID cannot be empty.", nameof(tokenId));
        var purposeName = purpose switch
        {
            IdentityTokenPurpose.VerifyEmail => "VERIFY_EMAIL",
            IdentityTokenPurpose.ResetPassword => "RESET_PASSWORD",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_keys.TryGetValue(keyId, out var key)) throw new InvalidOperationException("Identity token key version is unavailable.");
            var context = Encoding.UTF8.GetBytes($"strataai:identity:v1\0{keyId}\0{purposeName}\0{tokenId:N}");
            var token = HMACSHA256.HashData(key, context);
            return Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            foreach (var bytes in _keys.Values) CryptographicOperations.ZeroMemory(bytes);
            _disposed = true;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();
}
