namespace AeroFlow.Agent;

/// <summary>
/// Agent configuration. Bound from appsettings.json and overridable with
/// AGENT_-prefixed environment variables (e.g. AGENT_Simulate=true).
/// </summary>
public sealed class AgentOptions
{
    /// <summary>AMQP connection string to the shared RabbitMQ broker.</summary>
    public string AmqpUri { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>
    /// CA certificate of an amqps:// broker with a private CA (ca.crt from the
    /// server); empty to trust the system store.
    /// </summary>
    public string BrokerCaCertPath { get; set; } = "";

    /// <summary>Queue with inbound commands from aeroflow-playback.</summary>
    public string CommandsQueue { get; set; } = "agent_commands";

    /// <summary>Queue with outbound status events to aeroflow-playback.</summary>
    public string EventsQueue { get; set; } = "agent_events";

    /// <summary>Base URL of aeroflow-core for the audio download endpoint.</summary>
    public string CoreBaseUrl { get; set; } = "http://localhost:8080/";

    /// <summary>Token for the download endpoint (AGENT_TOKEN of core), sent as a bearer token.</summary>
    public string CoreToken { get; set; } = "";

    /// <summary>Local directory for downloaded audio files and processed markers.</summary>
    public string CacheDir { get; set; } = "var/cache";

    /// <summary>System player command; {file} is replaced with the audio file path.</summary>
    public string PlayerCommand { get; set; } = "ffplay -nodisp -autoexit -loglevel error {file}";

    /// <summary>
    /// Commands older than this are reported failed instead of played: an airport
    /// announcement is meaningful for minutes, but the durable queue survives for
    /// days (e.g. commands accumulated while the agent was offline). The margin
    /// also absorbs clock skew between the playback host and this machine.
    /// </summary>
    public int MaxCommandAgeSeconds { get; set; } = 300;

    /// <summary>When true, logs and waits instead of playing (no audio device needed).</summary>
    public bool Simulate { get; set; }

    /// <summary>Simulated duration of one audio item, milliseconds.</summary>
    public int SimulatedItemMs { get; set; } = 300;
}
