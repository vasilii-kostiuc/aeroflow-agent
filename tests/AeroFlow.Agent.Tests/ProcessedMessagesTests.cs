using AeroFlow.Agent.Dedupe;
using Microsoft.Extensions.Options;

namespace AeroFlow.Agent.Tests;

public sealed class ProcessedMessagesTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"aeroflow-agent-tests-{Guid.NewGuid()}");

    [Fact]
    public void SecondBeginOfSameMessageIsRejected()
    {
        var processed = Create();

        Assert.True(processed.TryBegin("m-1"));
        Assert.False(processed.TryBegin("m-1"));
        Assert.True(processed.TryBegin("m-2"));
    }

    [Fact]
    public void DoneMarkerSurvivesRestart()
    {
        var first = Create();
        Assert.True(first.TryBegin("m-1"));
        first.MarkDone("m-1");

        // A new instance models the agent process after a restart.
        var second = Create();
        Assert.False(second.TryBegin("m-1"));
    }

    [Fact]
    public void UnfinishedMessageIsReplayableAfterRestart()
    {
        var first = Create();
        Assert.True(first.TryBegin("m-1"));
        // Process died before MarkDone: the redelivery must play (at-least-once).

        var second = Create();
        Assert.True(second.TryBegin("m-1"));
    }

    private ProcessedMessages Create() =>
        new(Options.Create(new AgentOptions { CacheDir = _cacheDir }));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }
}
