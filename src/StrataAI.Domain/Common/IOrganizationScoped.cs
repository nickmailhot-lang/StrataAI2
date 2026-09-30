namespace StrataAI.Domain.Common;

/// <summary>
/// Marks tenant-owned records. Organization is the canonical product tenant boundary.
/// </summary>
public interface IOrganizationScoped
{
    Guid OrganizationId { get; }
}
