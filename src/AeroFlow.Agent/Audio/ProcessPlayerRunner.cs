using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace AeroFlow.Agent.Audio;

/// <summary>
/// Runs the configured system player (default: ffplay) as a child process; the
/// process exiting is the end of the sound, its exit code is the verdict. The
/// command template keeps the player swappable per machine (ffplay/aplay on Linux,
/// ffplay.exe on the airport Windows box) without code changes.
/// </summary>
public sealed class ProcessPlayerRunner : IPlayerProcessRunner
{
    private readonly string _commandTemplate;

    public ProcessPlayerRunner(IOptions<AgentOptions> options)
    {
        _commandTemplate = options.Value.PlayerCommand;
    }

    public async Task<int> PlayAsync(string filePath, CancellationToken cancellationToken)
    {
        var parts = _commandTemplate.Replace("{file}", filePath).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var startInfo = new ProcessStartInfo(parts[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in parts.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start player \"{parts[0]}\".");

        await process.WaitForExitAsync(cancellationToken);

        return process.ExitCode;
    }
}
