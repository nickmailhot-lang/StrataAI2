using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

public sealed class Checklist : DomainEntity, IOrganizationScoped
{
    public Checklist(Guid id, Guid organizationId, Guid cardId, string title, string rank, DateTimeOffset createdAt)
        : base(id, createdAt.ToUniversalTime())
    {
        ChecklistValues.RequireId(organizationId, nameof(organizationId));
        ChecklistValues.RequireId(cardId, nameof(cardId));
        OrganizationId = organizationId; CardId = cardId;
        Title = ChecklistValues.RequireText(title, 160, nameof(title));
        Rank = ChecklistValues.RequireRank(rank);
    }
    public Guid OrganizationId { get; }
    public Guid CardId { get; }
    public string Title { get; private set; }
    public string Rank { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool Rename(string title, DateTimeOffset at)
    {
        RequireActive(); var value = ChecklistValues.RequireText(title, 160, nameof(title));
        if (value == Title) return false;
        MarkUpdated(at.ToUniversalTime()); Title = value; return true;
    }
    public bool Reorder(string rank, DateTimeOffset at)
    {
        RequireActive(); var value = ChecklistValues.RequireRank(rank);
        if (value == Rank) return false;
        MarkUpdated(at.ToUniversalTime()); Rank = value; return true;
    }
    public bool Delete(DateTimeOffset at)
    {
        if (DeletedAt is not null) return false;
        MarkUpdated(at.ToUniversalTime()); DeletedAt = UpdatedAt; return true;
    }
    public ChecklistProgress Progress(IEnumerable<ChecklistItem> items)
    {
        var ids = new HashSet<Guid>(); var total = 0; var completed = 0;
        foreach (var item in items)
        {
            if (item.OrganizationId != OrganizationId || item.ChecklistId != Id || !ids.Add(item.Id))
                throw new ArgumentException("Items must belong uniquely to this Checklist.", nameof(items));
            if (item.DeletedAt is not null) continue;
            total++; if (item.Completed) completed++;
        }
        return new(completed, total);
    }
    private void RequireActive()
    {
        if (DeletedAt is not null) throw new InvalidOperationException("Checklist is deleted.");
    }
}

public sealed record ChecklistProgress
{
    public ChecklistProgress(int completed, int total)
    {
        if (completed < 0 || total < 0 || completed > total) throw new ArgumentOutOfRangeException(nameof(completed));
        Completed = completed; Total = total;
    }
    public int Completed { get; }
    public int Total { get; }
    public decimal Ratio => Total == 0 ? 0 : (decimal)Completed / Total;
    public decimal Percent => Ratio * 100;
}

internal static class ChecklistValues
{
    internal static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("ID cannot be empty.", name);
    }
    internal static string RequireText(string value, int maximum, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximum || normalized.Contains('\0'))
            throw new ArgumentException("Text is required and must fit its limit.", name);
        return normalized;
    }
    internal static string RequireRank(string rank)
    {
        // Same canonical 30-digit range as the existing Application RankToken.
        if (rank is null || rank.Length != 30 || rank.Any(value => !char.IsAsciiDigit(value))
            || rank.All(value => value == '0') || rank.All(value => value == '9'))
            throw new ArgumentException("Invalid rank token.", nameof(rank));
        return rank;
    }
}
