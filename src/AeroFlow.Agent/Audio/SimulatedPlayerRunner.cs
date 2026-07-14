using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroFlow.Agent.Audio;

/// <summary>
/// AGENT_SIMULATE=1: no audio device required (docker, CI). Logs the file and waits
/// a fixed interval instead of playing, so the full chain is exercised end to end.
/// </summary>
public sealed class SimulatedPlayerRunner : IPlayerProcessRunner
{
    private readonly ILogger<SimulatedPlayerRunner> _logger;
    private readonly int _itemDurationMs;

    public SimulatedPlayerRunner(ILogger<SimulatedPlayerRunner> logger, IOptions<AgentOptions> options)
    {
        _logger = logger;
        _itemDurationMs = options.Value.SimulatedItemMs;
    }

    public async Task<int> PlayAsync(string filePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SIMULATE playing {File} for {Duration}ms", filePath, _itemDurationMs);
        await Task.Delay(_itemDurationMs, cancellationToken);

        return 0;
    }
}
