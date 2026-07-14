using System.Text.Json;

namespace AeroFlow.Agent.Contracts;

/// <summary>
/// Inbound integration command from aeroflow-playback (queue agent_commands).
/// The agent owns its own copy of the contract and rebuilds it from the JSON body,
/// discriminated by the `command` field — never by the producer's class names.
/// The agent knows nothing about flights or announcements: only a job id and a
/// neutral audio sequence.
/// </summary>
public sealed record PlaySequenceCommand(
    string MessageId,
    string JobId,
    IReadOnlyList<PlaybackStep> Steps,
    int SchemaVersion)
{
    public const string CommandName = "audio.play_sequence";

    /// <summary>
    /// Parses the wire JSON. Throws <see cref="ContractViolationException"/> when the
    /// body is not a valid audio.play_sequence contract.
    /// </summary>
    public static PlaySequenceCommand FromJson(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ContractViolationException("Body is not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;

            var command = GetRequiredString(root, "command");
            if (command != CommandName)
            {
                throw new ContractViolationException($"Unknown command \"{command}\".");
            }

            var messageId = GetRequiredString(root, "messageId");
            var jobId = GetRequiredString(root, "jobId");
            var schemaVersion = root.TryGetProperty("schemaVersion", out var version) ? version.GetInt32() : 1;

            if (!root.TryGetProperty("audioSequence", out var sequence) || sequence.ValueKind != JsonValueKind.Array)
            {
                throw new ContractViolationException("Contract is missing \"audioSequence\".");
            }

            return new PlaySequenceCommand(messageId, jobId, BuildSteps(sequence), schemaVersion);
        }
    }

    /// <summary>
    /// Flattens the language groups into one ordered playback plan: groups by
    /// sortOrder, items in their given order. Unknown item types are rejected so a
    /// newer producer cannot be half-played silently.
    /// </summary>
    private static List<PlaybackStep> BuildSteps(JsonElement sequence)
    {
        var groups = new List<(int SortOrder, JsonElement Items)>();
        foreach (var group in sequence.EnumerateArray())
        {
            var sortOrder = group.TryGetProperty("sortOrder", out var order) ? order.GetInt32() : 0;
            if (!group.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new ContractViolationException("Sequence group is missing \"items\".");
            }

            groups.Add((sortOrder, items));
        }

        var steps = new List<PlaybackStep>();
        foreach (var (_, items) in groups.OrderBy(group => group.SortOrder))
        {
            foreach (var item in items.EnumerateArray())
            {
                var type = GetRequiredString(item, "type");
                steps.Add(type switch
                {
                    "audio_asset" => new PlayAssetStep(Guid.Parse(GetRequiredString(item, "audioAssetId"))),
                    "pause" => new PauseStep(item.TryGetProperty("durationMs", out var ms) ? ms.GetInt32() : 0),
                    _ => throw new ContractViolationException($"Unknown sequence item type \"{type}\"."),
                });
            }
        }

        return steps;
    }

    private static string GetRequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ContractViolationException($"Contract is missing \"{property}\".");
        }

        return value.GetString()!;
    }
}

/// <summary>One unit of the flattened playback plan.</summary>
public abstract record PlaybackStep;

public sealed record PlayAssetStep(Guid AudioAssetId) : PlaybackStep;

public sealed record PauseStep(int DurationMs) : PlaybackStep;

/// <summary>The inbound message does not match the agent_commands contract.</summary>
public sealed class ContractViolationException : Exception
{
    public ContractViolationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
