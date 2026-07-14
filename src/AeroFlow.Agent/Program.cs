using AeroFlow.Agent;
using AeroFlow.Agent.Audio;
using AeroFlow.Agent.Dedupe;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// AGENT_Agent__Simulate=true style overrides on top of appsettings.json.
builder.Configuration.AddEnvironmentVariables("AGENT_");
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

builder.Services.AddHttpClient<IAudioCache, DiskAudioCache>((provider, client) =>
{
    client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<AgentOptions>>().Value.CoreBaseUrl);
});

builder.Services.AddSingleton<ProcessedMessages>();
builder.Services.AddSingleton<IPlayerProcessRunner>(provider =>
    provider.GetRequiredService<IOptions<AgentOptions>>().Value.Simulate
        ? ActivatorUtilities.CreateInstance<SimulatedPlayerRunner>(provider)
        : ActivatorUtilities.CreateInstance<ProcessPlayerRunner>(provider));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
