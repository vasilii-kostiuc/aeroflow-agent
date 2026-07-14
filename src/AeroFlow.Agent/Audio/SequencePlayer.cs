using AeroFlow.Agent.Contracts;

namespace AeroFlow.Agent.Audio;

/// <summary>
/// Executes the flattened playback plan of one job: resolves each asset through the
/// cache and plays it, sleeps on pauses. Returns a verdict instead of throwing so
/// the worker maps it 1:1 onto playback_completed / playback_failed.
/// </summary>
public sealed class SequencePlayer
{
    private readonly IAudioCache _cache;
    private readonly IPlayerProcessRunner _player;

    public SequencePlayer(IAudioCache cache, IPlayerProcessRunner player)
    {
        _cache = cache;
        _player = player;
    }

    public async Task<PlaybackResult> PlayAsync(PlaySequenceCommand command, CancellationToken cancellationToken)
    {
        foreach (var step in command.Steps)
        {
            switch (step)
            {
                case PauseStep pause when pause.DurationMs > 0:
                    await Task.Delay(pause.DurationMs, cancellationToken);
                    break;

                case PlayAssetStep asset:
                    string filePath;
                    try
                    {
                        filePath = await _cache.GetFilePathAsync(asset.AudioAssetId, cancellationToken);
                    }
                    catch (AudioUnavailableException exception)
                    {
                        return PlaybackResult.Failure($"Asset {asset.AudioAssetId}: {exception.Message}");
                    }

                    var exitCode = await _player.PlayAsync(filePath, cancellationToken);
                    if (exitCode != 0)
                    {
                        return PlaybackResult.Failure($"Player exited with code {exitCode} on asset {asset.AudioAssetId}.");
                    }

                    break;
            }
        }

        return PlaybackResult.Success();
    }
}

public sealed record PlaybackResult(bool Completed, string? Reason)
{
    public static PlaybackResult Success() => new(true, null);

    public static PlaybackResult Failure(string reason) => new(false, reason);
}
