namespace StrataAI.Application.WorkManagement;

// Internal producer port: current actor/group admission and explicit confirmation
// must precede this call. Reserve only a new mass delivery delta in the owning
// comment/event/notification/receipt transaction; refused commands roll it back.
public interface ICardMassMentionQuota
{
    const int MaximumReservations = 3;
    Task<bool> TryReserveAsync(WorkEvent source, CancellationToken cancellationToken = default);
}
