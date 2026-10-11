using System.Security.Cryptography;
using System.Text.Json;

namespace StrataAI.Application.Organizations;

public static class OrganizationOperationalLifecycleRules
{
    public static string Fingerprint(Guid organization, OrganizationStatus status, long expectedVersion) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { organization, status, expectedVersion })));

    public static bool ValidWrite(OrganizationOperationalLifecycleScope scope, long expectedVersion,
        OrganizationOperationalLifecycleTransition source, OrganizationOperationalLifecycleReceipt receipt, DateTimeOffset now) =>
        scope.Admitted(source.OrganizationId, source.ActorId) && expectedVersion > 0 && expectedVersion < long.MaxValue
        && scope.Version == expectedVersion && source.Version == expectedVersion + 1
        && source.PreviousStatus == scope.Status && source.Status != scope.Status
        && source.Status is OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated
        && source.EventId != Guid.Empty && source.ActorLabel == scope.ActorLabel
        && source.MembershipId == scope.MembershipId && source.MembershipVersion == scope.MembershipVersion
        && source.ActingCapacity == (scope.Role == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN")
        && source.CreatedAt >= scope.UpdatedAt && source.CreatedAt <= now
        && source.CreatedAt.Offset == TimeSpan.Zero && source.CreatedAt.Ticks % 10 == 0
        && source.CreatedAt <= DateTimeOffset.MaxValue.AddHours(-24)
        && !string.IsNullOrWhiteSpace(source.CorrelationId) && source.CorrelationId.Length <= 256 && !source.CorrelationId.Any(char.IsControl)
        && receipt.ActorId == source.ActorId && receipt.Key != Guid.Empty && receipt.Result == source
        && receipt.ExpiresAt == source.CreatedAt.AddHours(24)
        && receipt.Fingerprint == Fingerprint(source.OrganizationId, source.Status, expectedVersion);
}
