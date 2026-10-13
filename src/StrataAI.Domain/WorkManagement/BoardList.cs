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

    public Guid BoardId { get; private set; }

    public string Name { get; private set; }

    public string Rank { get; private set; }

    public void Rename(string name, DateTimeOffset updatedAt)
    {
        var normalizedName = Require(name, nameof(name));
        MarkUpdated(updatedAt);
        Name = normalizedName;
    }

    public void Reorder(string rank, DateTimeOffset updatedAt)
    {
        var normalizedRank = Require(rank, nameof(rank));
        MarkUpdated(updatedAt);
        Rank = normalizedRank;
    }

    public void MoveToBoard(Guid destinationOrganizationId, Guid destinationBoardId, string rank, DateTimeOffset updatedAt)
    {
        if (destinationOrganizationId != OrganizationId)
        {
            throw new ArgumentException("List movement must remain within its Organization.", nameof(destinationOrganizationId));
        }

        if (destinationBoardId == Guid.Empty)
        {
            throw new ArgumentException("Board ID cannot be empty.", nameof(destinationBoardId));
        }

        var normalizedRank = Require(rank, nameof(rank));
        // Complete validation before changing the canonical parent or rank.
        MarkUpdated(updatedAt);
        BoardId = destinationBoardId;
        Rank = normalizedRank;
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
