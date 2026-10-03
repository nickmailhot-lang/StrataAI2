namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<IReadOnlyList<Guid>> ListReminderCandidateCardIdsAsync(Guid organizationId, Guid boardId, Guid? listId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_sync)
            return Task.FromResult<IReadOnlyList<Guid>>(_cards.Values.Where(card => card.OrganizationId == organizationId &&
                card.BoardId == boardId && (listId is null || card.ListId == listId)).Select(card => card.Id)
                .OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray());
    }
}
