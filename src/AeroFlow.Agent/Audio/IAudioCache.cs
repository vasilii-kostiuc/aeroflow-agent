namespace AeroFlow.Agent.Audio;

/// <summary>
/// Resolves an audioAssetId to a playable local file, downloading it from
/// aeroflow-core's download endpoint on a cache miss.
/// </summary>
public interface IAudioCache
{
    /// <exception cref="AudioUnavailableException">
    /// The asset cannot be obtained (unknown id, inactive asset, core unreachable).
    /// </exception>
    Task<string> GetFilePathAsync(Guid audioAssetId, CancellationToken cancellationToken);
}

public sealed class AudioUnavailableException : Exception
{
    public AudioUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
