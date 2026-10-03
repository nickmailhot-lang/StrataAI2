using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

public sealed class ChecklistItem : DomainEntity, IOrganizationScoped
{
    public ChecklistItem(Guid id, Guid organizationId, Guid checklistId, string text, string rank, DateTimeOffset createdAt)
        : base(id, createdAt.ToUniversalTime())
    {
        ChecklistValues.RequireId(organizationId, nameof(organizationId));
        ChecklistValues.RequireId(checklistId, nameof(checklistId));
        OrganizationId = organizationId; ChecklistId = checklistId;
        Text = ChecklistValues.RequireText(text, 2000, nameof(text)); Rank = ChecklistValues.RequireRank(rank);
    }
    public Guid OrganizationId { get; }
    public Guid ChecklistId { get; }
    public string Text { get; private set; }
    public string Rank { get; private set; }
    public bool Completed { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool Edit(string text, DateTimeOffset at)
    {
        RequireActive(); var value = ChecklistValues.RequireText(text, 2000, nameof(text));
        if (value == Text) return false;
        MarkUpdated(at.ToUniversalTime()); Text = value; return true;
    }
    public bool Reorder(string rank, DateTimeOffset at)
    {
        RequireActive(); var value = ChecklistValues.RequireRank(rank);
        if (value == Rank) return false;
        MarkUpdated(at.ToUniversalTime()); Rank = value; return true;
    }
    public bool SetCompleted(bool completed, Guid actor, DateTimeOffset at)
    {
        RequireActive(); ChecklistValues.RequireId(actor, nameof(actor));
        if (Completed == completed) return false;
        MarkUpdated(at.ToUniversalTime()); Completed = completed;
        CompletedAt = completed ? UpdatedAt : null; CompletedBy = completed ? actor : null;
        return true;
    }
    public bool Delete(DateTimeOffset at)
    {
        if (DeletedAt is not null) return false;
        MarkUpdated(at.ToUniversalTime()); DeletedAt = UpdatedAt; return true;
    }
    private void RequireActive()
    {
        if (DeletedAt is not null) throw new InvalidOperationException("Checklist item is deleted.");
    }
}
