using System.Text.Json;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

// One canonical current-snapshot backing store, shared without a service
// dependency cycle. Work receives only a neutral binding check, not private data.
internal sealed class DemoOrganizationConfigurationState(DemoWorkTransactionScope scope)
{
    internal Dictionary<Guid, string> Current { get; } = [];
    internal bool IsCurrentIntake(Guid organization, Guid list)
    {
        if (!scope.Owns(organization)) throw new InvalidOperationException("Intake binding requires its owning transaction.");
        if (!Current.TryGetValue(organization, out var json)) return false;
        using var snapshot = JsonDocument.Parse(json);
        return snapshot.RootElement.GetProperty("Configuration").TryGetProperty("IntakeListId", out var id)
            && id.ValueKind == JsonValueKind.String && id.GetGuid() == list;
    }
}
