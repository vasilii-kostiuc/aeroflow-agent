namespace AeroFlow.Agent.Audio;

/// <summary>
/// Plays one local audio file to the end. Abstracted so the sequence logic is
/// testable and the simulate mode needs no audio device.
/// </summary>
public interface IPlayerProcessRunner
{
    /// <returns>The player process exit code; non-zero means the file failed to play.</returns>
    Task<int> PlayAsync(string filePath, CancellationToken cancellationToken);
}
