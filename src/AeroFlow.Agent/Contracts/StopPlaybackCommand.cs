using System.Text.Json;

namespace AeroFlow.Agent.Contracts;

/// <summary>Neutral control command that stops one currently active job.</summary>
public sealed record StopPlaybackCommand(string MessageId, string JobId)
{
    public const string CommandName = "audio.stop";

    public static StopPlaybackCommand FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var command = Required(root, "command");
            if (command != CommandName)
            {
                throw new ContractViolationException($"Unknown command \"{command}\".");
            }

            return new StopPlaybackCommand(Required(root, "messageId"), Required(root, "jobId"));
        }
        catch (JsonException exception)
        {
            throw new ContractViolationException("Body is not valid JSON.", exception);
        }
    }

    private static string Required(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ContractViolationException($"Contract is missing \"{property}\".");
}
