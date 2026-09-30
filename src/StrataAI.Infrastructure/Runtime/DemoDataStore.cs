using StrataAI.Application.Runtime;

namespace StrataAI.Infrastructure.Runtime;

internal sealed class DemoDataStore : IDemoDataStore
{
    private const int CurrentSampleVersion = 1;
    private readonly object _sync = new();
    private DemoState _state = CreateSampleState();

    public int SampleVersion => CurrentSampleVersion;

    public DemoState GetState()
    {
        lock (_sync)
        {
            return _state;
        }
    }

    public DemoState Reset()
    {
        lock (_sync)
        {
            _state = CreateSampleState();
            return _state;
        }
    }

    public DemoState Clear()
    {
        lock (_sync)
        {
            _state = new DemoState(CurrentSampleVersion, []);
            return _state;
        }
    }

    private static DemoState CreateSampleState()
    {
        var organizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var boardId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        return new DemoState(
            CurrentSampleVersion,
            [
                new DemoOrganization(
                    organizationId,
                    "Quail Ridge Demo",
                    [
                        new DemoBoard(boardId, "Council Operations"),
                    ])
            ]);
    }
}
