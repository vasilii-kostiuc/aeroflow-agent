using AeroFlow.Agent.Audio;
using AeroFlow.Agent.Contracts;

namespace AeroFlow.Agent.Tests;

public sealed class SequencePlayerTests
{
    private static PlaySequenceCommand Command(params PlaybackStep[] steps) =>
        new("m-1", "j-1", steps, 1, DateTimeOffset.UtcNow);

    [Fact]
    public async Task PlaysEveryAssetInOrderAndCompletes()
    {
        var cache = new FakeCache();
        var runner = new FakeRunner();
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();

        var result = await new SequencePlayer(cache, runner).PlayAsync(
            Command(new PlayAssetStep(assetA), new PauseStep(0), new PlayAssetStep(assetB)),
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal([$"/cache/{assetA}.wav", $"/cache/{assetB}.wav"], runner.Played);
    }

    [Fact]
    public async Task FailsWhenAssetIsUnavailable()
    {
        var missing = Guid.NewGuid();
        var cache = new FakeCache { Unavailable = missing };
        var runner = new FakeRunner();

        var result = await new SequencePlayer(cache, runner).PlayAsync(
            Command(new PlayAssetStep(missing), new PlayAssetStep(Guid.NewGuid())),
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains(missing.ToString(), result.Reason);
        // Playback stops on the first failure — nothing after the broken asset plays.
        Assert.Empty(runner.Played);
    }

    [Fact]
    public async Task FailsWhenPlayerExitsNonZero()
    {
        var cache = new FakeCache();
        var runner = new FakeRunner { ExitCode = 1 };

        var result = await new SequencePlayer(cache, runner).PlayAsync(
            Command(new PlayAssetStep(Guid.NewGuid())),
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("code 1", result.Reason);
    }

    [Fact]
    public async Task CancellationDuringPlayPropagatesAsOperationCanceled()
    {
        var cache = new FakeCache();
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingRunner(cancellation);

        // The runner cancels mid-play, as an audio.stop would: the player must not
        // swallow the cancellation into a Failure result — the Worker maps it to
        // audio.playback_interrupted, not audio.playback_failed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SequencePlayer(cache, runner).PlayAsync(
                Command(new PlayAssetStep(Guid.NewGuid()), new PlayAssetStep(Guid.NewGuid())),
                cancellation.Token));

        Assert.Equal(1, runner.Started);
    }

    [Fact]
    public async Task CancellationDuringPauseStepPropagatesAsOperationCanceled()
    {
        var cache = new FakeCache();
        var runner = new FakeRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SequencePlayer(cache, runner).PlayAsync(
                Command(new PauseStep(60_000), new PlayAssetStep(Guid.NewGuid())),
                cancellation.Token));

        Assert.Empty(runner.Played);
    }

    private sealed class CancellingRunner(CancellationTokenSource cancellation) : IPlayerProcessRunner
    {
        public int Started { get; private set; }

        public async Task<int> PlayAsync(string filePath, CancellationToken cancellationToken)
        {
            Started++;
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }
    }

    private sealed class FakeCache : IAudioCache
    {
        public Guid? Unavailable { get; init; }

        public Task<string> GetFilePathAsync(Guid audioAssetId, CancellationToken cancellationToken)
        {
            if (audioAssetId == Unavailable)
            {
                throw new AudioUnavailableException($"Core returned 404 for asset {audioAssetId}.");
            }

            return Task.FromResult($"/cache/{audioAssetId}.wav");
        }
    }

    private sealed class FakeRunner : IPlayerProcessRunner
    {
        public int ExitCode { get; init; }

        public List<string> Played { get; } = [];

        public Task<int> PlayAsync(string filePath, CancellationToken cancellationToken)
        {
            if (ExitCode == 0)
            {
                Played.Add(filePath);
            }

            return Task.FromResult(ExitCode);
        }
    }
}
