using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class DataProtectedActivityCursorCodec(IDataProtectionProvider protection, IClock clock) : IActivityCursorCodec
{
    private readonly IDataProtector _protector = protection.CreateProtector("StrataAI2.ActivityCursor", "v1");
    private sealed record Payload(int Version, ActivityCursorBinding Binding, ActivityCursor Position, DateTimeOffset ExpiresAt);
    public string Encode(ActivityCursorBinding binding, ActivityCursor position)
    {
        if (!Valid(binding)) throw new ArgumentException("Activity cursor binding is invalid.");
        ActivityEventSourceWindow.RequireCursor(position.CreatedAt, position.EventId);
        return _protector.Protect(JsonSerializer.Serialize(new Payload(1, binding, position, clock.UtcNow.AddMinutes(15))));
    }
    public bool TryDecode(ActivityCursorBinding binding, string token, out ActivityCursor? position)
    {
        position = null;
        if (!Valid(binding) || string.IsNullOrEmpty(token) || token.Length > 2048) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (payload is null || payload.Version != 1 || payload.Binding != binding || payload.Position is null ||
                payload.ExpiresAt.Offset != TimeSpan.Zero || payload.ExpiresAt <= clock.UtcNow) return false;
            ActivityEventSourceWindow.RequireCursor(payload.Position.CreatedAt, payload.Position.EventId);
            position = payload.Position; return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        { return false; }
    }
    private static bool Valid(ActivityCursorBinding binding) => binding.OrganizationId != Guid.Empty &&
        binding.ViewerId != Guid.Empty && binding.TargetId != Guid.Empty && Enum.IsDefined(binding.Kind);
}
