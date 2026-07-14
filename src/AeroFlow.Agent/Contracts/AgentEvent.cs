using System.Text.Json;

namespace AeroFlow.Agent.Contracts;

/// <summary>
/// Outbound integration event to aeroflow-playback (queue agent_events).
/// Neutral JSON with the `event` discriminator in the body (ADR 002): playback maps
/// completed/failed onto its idempotent CompletePlaybackJob/FailPlaybackJob and only
/// logs started.
/// </summary>
public sealed record AgentEvent(string Event, string MessageId, string JobId, string? Reason)
{
    public static AgentEvent Started(string jobId) => new("audio.playback_started", NewId(), jobId, null);

    public static AgentEvent Completed(string jobId) => new("audio.playback_completed", NewId(), jobId, null);

    public static AgentEvent Failed(string jobId, string reason) => new("audio.playback_failed", NewId(), jobId, reason);

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("event", Event);
            writer.WriteString("messageId", MessageId);
            writer.WriteString("jobId", JobId);
            if (Reason is not null)
            {
                writer.WriteString("reason", Reason);
            }

            writer.WriteString("occurredAt", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string NewId() => Guid.CreateVersion7().ToString();
}
