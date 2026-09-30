using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

public sealed class Board : DomainEntity, IOrganizationScoped
{
    public Board(Guid id, Guid organizationId, string name, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID cannot be empty.", nameof(organizationId));
        }

        OrganizationId = organizationId;
        Name = RequireName(name);
    }

    public Guid OrganizationId { get; }

    public string Name { get; private set; }

    public void Rename(string name, DateTimeOffset updatedAt)
    {
        Name = RequireName(name);
        MarkUpdated(updatedAt);
    }

    private static string RequireName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Board name is required.", nameof(value));
        }

        return normalized;
    }
}
