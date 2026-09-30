using StrataAI.Domain.Common;

namespace StrataAI.Domain.Organizations;

public sealed class Organization : DomainEntity
{
    public Organization(Guid id, string name, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Name = RequireName(name);
    }

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
            throw new ArgumentException("Organization name is required.", nameof(value));
        }

        return normalized;
    }
}
