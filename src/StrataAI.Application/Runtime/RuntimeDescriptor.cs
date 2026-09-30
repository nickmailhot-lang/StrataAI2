namespace StrataAI.Application.Runtime;

public sealed record RuntimeDescriptor(
    RuntimeMode Mode,
    string BuildRevision,
    string BuildVersion);
