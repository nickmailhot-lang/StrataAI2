namespace StrataAI.Domain.Common;

/// <summary>
/// Base for mutable domain records. Identity is immutable and every mutation must
/// advance UpdatedAt and Version through domain/application behavior.
/// </summary>
public abstract class DomainEntity
{
    protected DomainEntity(Guid id, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity ID cannot be empty.", nameof(id));
        }

        Id = id;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = 1;
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    protected void MarkUpdated(DateTimeOffset updatedAt)
    {
        if (updatedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                "Updated timestamp cannot move backwards.");
        }

        UpdatedAt = updatedAt;
        Version++;
    }
}
