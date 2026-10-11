namespace StrataAI.Infrastructure.Organizations;

// Additional capability beyond a generic Organization transaction. Ordinary
// active-work and deletion-recovery commands cannot borrow this admission.
internal sealed class OrganizationOperationalLifecycleCommandScope
{
    private readonly AsyncLocal<(Guid Organization, Guid Actor)?> _organization = new();
    public bool Owns(Guid organization, Guid actor) => organization != Guid.Empty && actor != Guid.Empty
        && _organization.Value?.Organization == organization && _organization.Value?.Actor == actor;
    public IDisposable Enter(Guid organization, Guid actor)
    {
        if (organization == Guid.Empty || actor == Guid.Empty || _organization.Value is not null)
            throw new InvalidOperationException("An owning lifecycle transaction is required.");
        _organization.Value = (organization, actor);
        return new Lease(() => _organization.Value = null);
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}
