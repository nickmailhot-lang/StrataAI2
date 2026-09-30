using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

/// <summary>
/// A Kanban list. Named BoardList in code to avoid collision with System.Collections.Generic.List.
/// </summary>
public sealed class BoardList : DomainEntity, IOrganizationScoped
{
    public BoardList(
        Guid id,
        Guid organizationId,
        Guid boardId,
        string name,
        string rank,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID cannot be empty.", nameof(organizationId));
        }

        if (boardId == Guid.Empty)
        {
            throw new ArgumentException("Board ID cannot be empty.", nameof(boardId));
        }

        OrganizationId = organizationId;
        BoardId = boardId;
        Name = Require(name, nameof(name));
        Rank = Require(rank, nameof(rank));
    }

    public Guid OrganizationId { get; }

    public Guid BoardId { get; }

    public string Name { get; private set; }

    public string Rank { get; private set; }

    public void Rename(string name, DateTimeOffset updatedAt)
    {
        Name = Require(name, nameof(name));
        MarkUpdated(updatedAt);
    }

    public void Reorder(string rank, DateTimeOffset updatedAt)
    {
        Rank = Require(rank, nameof(rank));
        MarkUpdated(updatedAt);
    }

    private static string Require(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalized;
    }
}
