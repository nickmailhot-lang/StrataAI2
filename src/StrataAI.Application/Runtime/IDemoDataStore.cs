namespace StrataAI.Application.Runtime;

public interface IDemoDataStore
{
    int SampleVersion { get; }

    DemoState GetState();

    DemoState Reset();

    DemoState Clear();
}

public sealed record DemoState(
    int SampleVersion,
    IReadOnlyList<DemoOrganization> Organizations);

public sealed record DemoOrganization(
    Guid Id,
    string Name,
    IReadOnlyList<DemoBoard> Boards);

public sealed record DemoBoard(
    Guid Id,
    string Name);
