using StrataAI.Application.Identity;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationOperationalLifecycleDirectoryPage(Guid ActorId,
    IReadOnlyList<OrganizationOperationalLifecycleReview> Items, Guid? NextCursor);

public sealed class OrganizationOperationalLifecycleDirectoryService(IOrganizationStore routes,
    OrganizationOperationalLifecycleService lifecycle, ICommandActorAuthorization actors)
{
    public async Task<OrganizationOperation<OrganizationOperationalLifecycleDirectoryPage>> ReadAsync(Guid actor,
        Guid? after, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (actor == Guid.Empty || !await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
        if (after == Guid.Empty) return Failure("invalid_organization_cursor");
        var routing = await routes.ReadDirectoryRoutesAsync(actor, after, ct);
        if (!routing.Succeeded) return Failure(routing.ErrorCode ?? "organization_storage_unavailable");
        var hints = routing.Value;
        if (hints is null || hints.Count > 51) return Failure("organization_lifecycle_source_unavailable");
        Guid? previous = after;
        foreach (var hint in hints)
        {
            if (hint == Guid.Empty || previous is Guid preceding && Compare(hint, preceding) <= 0)
                return Failure("organization_lifecycle_source_unavailable");
            previous = hint;
        }
        var candidates = hints.Take(50).ToArray();
        var admitted = new List<Guid>(candidates.Length);
        foreach (var organization in candidates)
        {
            var review = await lifecycle.ReviewAsync(organization, actor, ct);
            if (review.Succeeded) admitted.Add(organization);
            else if (review.ErrorCode != "organization_not_found") return Failure(review.ErrorCode!);
        }
        // Earlier names/states are not carried across the rest of the page IO.
        // Routing hints and continuations confer no tenant administrative grant.
        var items = new List<OrganizationOperationalLifecycleReview>(admitted.Count);
        foreach (var organization in admitted)
        {
            var current = await lifecycle.ReviewAsync(organization, actor, ct);
            if (current.Succeeded) items.Add(current.Value!);
            else if (current.ErrorCode != "organization_not_found") return Failure(current.ErrorCode!);
        }
        if (!await actors.VerifyAsync(actor, ct)) return Failure("session_unavailable");
        ct.ThrowIfCancellationRequested();
        return OrganizationOperation<OrganizationOperationalLifecycleDirectoryPage>.Success(new(actor, items,
            hints.Count > 50 ? candidates[^1] : null));
    }
    private static int Compare(Guid left, Guid right) => string.CompareOrdinal(left.ToString("N"), right.ToString("N"));
    private static OrganizationOperation<OrganizationOperationalLifecycleDirectoryPage> Failure(string code) =>
        OrganizationOperation<OrganizationOperationalLifecycleDirectoryPage>.Failure(code);
}
