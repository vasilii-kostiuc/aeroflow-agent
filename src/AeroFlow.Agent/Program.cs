using System.Net.Http.Headers;
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
    var options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(options.CoreBaseUrl);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.CoreToken);
});

builder.Services.AddSingleton<ProcessedMessages>();
builder.Services.AddSingleton<IPlayerProcessRunner>(provider =>
    provider.GetRequiredService<IOptions<AgentOptions>>().Value.Simulate
        ? ActivatorUtilities.CreateInstance<SimulatedPlayerRunner>(provider)
        : ActivatorUtilities.CreateInstance<ProcessPlayerRunner>(provider));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
