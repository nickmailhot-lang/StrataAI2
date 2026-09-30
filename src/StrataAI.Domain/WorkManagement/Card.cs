using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

public sealed class Card : DomainEntity, IOrganizationScoped
{
    public Card(
        Guid id,
        Guid organizationId,
        Guid boardId,
        Guid listId,
        string title,
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

        if (listId == Guid.Empty)
        {
            throw new ArgumentException("List ID cannot be empty.", nameof(listId));
        }

        OrganizationId = organizationId;
        BoardId = boardId;
        ListId = listId;
        Title = Require(title, nameof(title));
        Rank = Require(rank, nameof(rank));
    }

    public Guid OrganizationId { get; }

    public Guid BoardId { get; private set; }

    public Guid ListId { get; private set; }

    public string Title { get; private set; }

    public string Rank { get; private set; }

    public void Rename(string title, DateTimeOffset updatedAt)
    {
        Title = Require(title, nameof(title));
        MarkUpdated(updatedAt);
    }

    public void Move(Guid boardId, Guid listId, string rank, DateTimeOffset updatedAt)
    {
        if (boardId == Guid.Empty)
        {
            throw new ArgumentException("Board ID cannot be empty.", nameof(boardId));
        }

        if (listId == Guid.Empty)
        {
            throw new ArgumentException("List ID cannot be empty.", nameof(listId));
        }

        BoardId = boardId;
        ListId = listId;
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
