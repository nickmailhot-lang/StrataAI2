using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

public sealed partial class IdentityLoginRetrySecrets : IIdentityLoginRetrySecrets, IDisposable
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _disposed;
    public string CurrentKeyVersion { get; }
    public IdentityLoginRetrySecrets(string currentKeyVersion, IReadOnlyDictionary<string, string> keys)
    {
        if (keys.Count is < 1 or > 16 || !KeyVersionPattern().IsMatch(currentKeyVersion)) throw new ArgumentException("Invalid sign-in retry key ring.");
        try
        {
            foreach (var (version, encoded) in keys)
            {
                if (!KeyVersionPattern().IsMatch(version)) throw new ArgumentException("Invalid sign-in retry key version.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(encoded); }
                catch (FormatException) { throw new ArgumentException("Invalid sign-in retry key encoding."); }
                if (bytes.Length != 32) { CryptographicOperations.ZeroMemory(bytes); throw new ArgumentException("Sign-in retry keys must contain 32 bytes."); }
                _keys.Add(version, bytes);
            }
            if (!_keys.ContainsKey(currentKeyVersion)) throw new ArgumentException("Current sign-in retry key is absent.");
            CurrentKeyVersion = currentKeyVersion;
        }
        catch { foreach (var bytes in _keys.Values) CryptographicOperations.ZeroMemory(bytes); throw; }
    }
    public bool TryDeriveSession(Guid userId, Guid sessionId, string keyVersion, out string token)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty) { token = ""; return false; }
        var message = Encoding.UTF8.GetBytes($"strataai:login-retry:v1\0SESSION\0{keyVersion}\0{userId:N}\0{sessionId:N}");
        return TryMac(message, keyVersion, false, out token);
    }
    public bool TryFingerprint(Guid userId, Guid intentKey, string emailNormalized, string password, string keyVersion, out string fingerprint)
    {
        if (userId == Guid.Empty || intentKey == Guid.Empty) { fingerprint = ""; return false; }
        var message = JsonSerializer.SerializeToUtf8Bytes(new { Purpose = "strataai:login-retry:v1:INTENT", keyVersion, userId, intentKey, emailNormalized, password });
        try { return TryMac(message, keyVersion, true, out fingerprint); }
        finally { CryptographicOperations.ZeroMemory(message); }
    }
    private bool TryMac(byte[] message, string version, bool hexadecimal, out string result)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_keys.TryGetValue(version, out var key)) { result = ""; return false; }
            var mac = HMACSHA256.HashData(key, message);
            try { result = hexadecimal ? Convert.ToHexStringLower(mac) : Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
            finally { CryptographicOperations.ZeroMemory(mac); }
            return true;
        }
    }
    public void Dispose() { lock (_sync) { if (_disposed) return; foreach (var key in _keys.Values) CryptographicOperations.ZeroMemory(key); _disposed = true; } }
    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyVersionPattern();
}
