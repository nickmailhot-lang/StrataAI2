namespace StrataAI.Application.WorkManagement;

// The caller owns the actor identity transaction and final session proof.
// A 24-hour request receipt returns the immutable original, not the new
// candidate identity/clock. Digest belongs to the receipt, never event metadata.
public interface IBoardFilterInteractionReplayStore
{
    Task<SearchInteractionEvent> AppendOrReplayAsync(Guid requestId, string fingerprint,
        SearchInteractionEvent candidate, CancellationToken cancellationToken = default);
}
