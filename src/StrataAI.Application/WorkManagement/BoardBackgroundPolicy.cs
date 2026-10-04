namespace StrataAI.Application.WorkManagement;

// Stable, named built-ins. Presentation maps these names to theme colors;
// arbitrary CSS, URLs and object references are not COLOR selections.
public static class BoardBackgroundPolicy
{
    public static IReadOnlyList<string> Colors { get; } = Array.AsReadOnly(new[] { "blue", "green", "red", "purple", "orange", "gray" });
}
