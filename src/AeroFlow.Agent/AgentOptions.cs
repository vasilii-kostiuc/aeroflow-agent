namespace AeroFlow.Agent;

/// <summary>
/// Agent configuration. Bound from appsettings.json and overridable with
/// AGENT_-prefixed environment variables (e.g. AGENT_Simulate=true).
/// </summary>
public sealed class AgentOptions
{
    /// <summary>AMQP connection string to the shared RabbitMQ broker.</summary>
    public string AmqpUri { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>Queue with inbound commands from aeroflow-playback.</summary>
    public string CommandsQueue { get; set; } = "agent_commands";

    /// <summary>Queue with outbound status events to aeroflow-playback.</summary>
    public string EventsQueue { get; set; } = "agent_events";

    /// <summary>Base URL of aeroflow-core for the audio download endpoint.</summary>
    public string CoreBaseUrl { get; set; } = "http://localhost:8080/";

    /// <summary>Local directory for downloaded audio files and processed markers.</summary>
    public string CacheDir { get; set; } = "var/cache";

    /// <summary>System player command; {file} is replaced with the audio file path.</summary>
    public string PlayerCommand { get; set; } = "ffplay -nodisp -autoexit -loglevel error {file}";

    /// <summary>When true, logs and waits instead of playing (no audio device needed).</summary>
    public bool Simulate { get; set; }

    /// <summary>Simulated duration of one audio item, milliseconds.</summary>
    public int SimulatedItemMs { get; set; } = 300;
}
