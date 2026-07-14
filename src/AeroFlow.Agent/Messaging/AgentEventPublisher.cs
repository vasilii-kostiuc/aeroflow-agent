using System.Text;
using AeroFlow.Agent.Contracts;
using RabbitMQ.Client;

namespace AeroFlow.Agent.Messaging;

public interface IAgentEventPublisher
{
    Task PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Publishes agent status events to the agent_events queue as persistent messages.
/// The channel is provided by the worker that owns the connection lifecycle.
/// </summary>
public sealed class AgentEventPublisher : IAgentEventPublisher
{
    private readonly IChannel _channel;
    private readonly string _queue;

    public AgentEventPublisher(IChannel channel, string queue)
    {
        _channel = channel;
        _queue = queue;
    }

    public async Task PublishAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            Type = agentEvent.Event,
        };

        await _channel.BasicPublishAsync(
            exchange: "",
            routingKey: _queue,
            mandatory: false,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(agentEvent.ToJson()),
            cancellationToken: cancellationToken);
    }
}
