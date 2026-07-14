using Microsoft.Extensions.Options;

namespace AeroFlow.Agent.Dedupe;

/// <summary>
/// Best-effort guard against playing a redelivered command twice: an in-memory set
/// plus a marker file per messageId in the cache directory (survives restarts).
///
/// The agent deliberately has no database, so this guarantee is not absolute — the
/// strict idempotency lives upstream in playback's CompletePlaybackJob (task 015);
/// a rare duplicate can at worst replay a sound, never corrupt state.
/// </summary>
public sealed class ProcessedMessages
{
    private readonly HashSet<string> _seen = [];
    private readonly string _markerDirectory;
    private readonly Lock _lock = new();

    public ProcessedMessages(IOptions<AgentOptions> options)
    {
        _markerDirectory = Path.Combine(options.Value.CacheDir, "processed");
    }

    /// <returns>false when this messageId was already handled — skip without playing.</returns>
    public bool TryBegin(string messageId)
    {
        lock (_lock)
        {
            if (_seen.Contains(messageId) || File.Exists(MarkerPath(messageId)))
            {
                return false;
            }

            _seen.Add(messageId);

            return true;
        }
    }

    /// <summary>Persist the marker once the final event for the command was published.</summary>
    public void MarkDone(string messageId)
    {
        Directory.CreateDirectory(_markerDirectory);
        File.WriteAllText(MarkerPath(messageId), "");
    }

    private string MarkerPath(string messageId) => Path.Combine(_markerDirectory, messageId);
}
