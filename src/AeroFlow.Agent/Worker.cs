using System.Text;
using AeroFlow.Agent.Audio;
using AeroFlow.Agent.Contracts;
using AeroFlow.Agent.Dedupe;
using AeroFlow.Agent.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AeroFlow.Agent;

/// <summary>
/// The agent's single long-running loop: consume agent_commands with prefetch 1
/// (strictly sequential — the single-active invariant lives in playback, the agent
/// simply never plays two things at once), play, report the verdict, ack.
///
/// No airport business logic lives here: the agent sees a job id and a neutral
/// audio sequence, nothing else.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly AgentOptions _options;
    private readonly IAudioCache _cache;
    private readonly IPlayerProcessRunner _player;
    private readonly ProcessedMessages _processed;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IOptions<AgentOptions> options,
        IAudioCache cache,
        IPlayerProcessRunner player,
        ProcessedMessages processed,
        ILogger<Worker> logger)
    {
        _options = options.Value;
        _cache = cache;
        _player = player;
        _processed = processed;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_options.AmqpUri) };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declarations are idempotent and match Messenger's defaults (durable queues),
        // so either side may start first.
        await channel.QueueDeclareAsync(_options.CommandsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_options.EventsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var publisher = new AgentEventPublisher(channel, _options.EventsQueue);
        var sequencePlayer = new SequencePlayer(_cache, _player);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            await HandleDeliveryAsync(channel, publisher, sequencePlayer, delivery, stoppingToken);
        };

        await channel.BasicConsumeAsync(_options.CommandsQueue, autoAck: false, consumer, cancellationToken: stoppingToken);
        _logger.LogInformation("Agent consuming {Queue} (simulate: {Simulate})", _options.CommandsQueue, _options.Simulate);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        AgentEventPublisher publisher,
        SequencePlayer sequencePlayer,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        PlaySequenceCommand command;
        try
        {
            command = PlaySequenceCommand.FromJson(Encoding.UTF8.GetString(delivery.Body.Span));
        }
        catch (ContractViolationException exception)
        {
            // A malformed message would fail forever; drop it loudly instead of
            // blocking the sound channel.
            _logger.LogError(exception, "Dropping non-contract message from {Queue}", _options.CommandsQueue);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);

            return;
        }

        if (!_processed.TryBegin(command.MessageId))
        {
            _logger.LogInformation("Skipping already processed command {MessageId} (job {JobId})", command.MessageId, command.JobId);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);

            return;
        }

        // Stale-command guard: commands that waited in the durable queue for too
        // long (agent offline, clock skew aside) must not sound days later — the
        // failure travels back so playback marks the job Failed and the queue
        // screen shows why nothing was heard.
        var maxAge = TimeSpan.FromSeconds(_options.MaxCommandAgeSeconds);
        if (command.IsStale(maxAge, DateTimeOffset.UtcNow))
        {
            var reason = $"stale command: issued {command.OccurredAt:O}, older than {_options.MaxCommandAgeSeconds}s";
            _logger.LogWarning("Job {JobId}: {Reason}", command.JobId, reason);
            await publisher.PublishAsync(AgentEvent.Failed(command.JobId, reason), cancellationToken);
            _processed.MarkDone(command.MessageId);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);

            return;
        }

        _logger.LogInformation("Playing job {JobId} ({Steps} steps)", command.JobId, command.Steps.Count);
        await publisher.PublishAsync(AgentEvent.Started(command.JobId), cancellationToken);

        var result = await sequencePlayer.PlayAsync(command, cancellationToken);

        if (result.Completed)
        {
            await publisher.PublishAsync(AgentEvent.Completed(command.JobId), cancellationToken);
            _logger.LogInformation("Job {JobId} completed", command.JobId);
        }
        else
        {
            await publisher.PublishAsync(AgentEvent.Failed(command.JobId, result.Reason!), cancellationToken);
            _logger.LogWarning("Job {JobId} failed: {Reason}", command.JobId, result.Reason);
        }

        _processed.MarkDone(command.MessageId);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }
}
