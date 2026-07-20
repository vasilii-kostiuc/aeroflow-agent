using System.Text;
using System.Text.Json;
using AeroFlow.Agent.Audio;
using AeroFlow.Agent.Contracts;
using AeroFlow.Agent.Dedupe;
using AeroFlow.Agent.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AeroFlow.Agent;

/// <summary>
/// Consumes the neutral agent_commands contract. Playback owns queue ordering; the
/// agent owns a single physical player and accepts an audio.stop while its play
/// task runs in the background.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly AgentOptions _options;
    private readonly IAudioCache _cache;
    private readonly IPlayerProcessRunner _player;
    private readonly ProcessedMessages _processed;
    private readonly ILogger<Worker> _logger;
    private readonly Lock _activeLock = new();
    private ActivePlayback? _active;

    public Worker(IOptions<AgentOptions> options, IAudioCache cache, IPlayerProcessRunner player, ProcessedMessages processed, ILogger<Worker> logger)
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
        await channel.QueueDeclareAsync(_options.CommandsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_options.EventsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var publisher = new AgentEventPublisher(channel, _options.EventsQueue);
        var player = new SequencePlayer(_cache, _player);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, publisher, player, delivery, stoppingToken);
        await channel.BasicConsumeAsync(_options.CommandsQueue, autoAck: false, consumer, cancellationToken: stoppingToken);
        _logger.LogInformation("Agent consuming {Queue} (simulate: {Simulate})", _options.CommandsQueue, _options.Simulate);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleDeliveryAsync(IChannel channel, AgentEventPublisher publisher, SequencePlayer player, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var json = Encoding.UTF8.GetString(delivery.Body.Span);
        try
        {
            var command = CommandName(json);
            if (command == PlaySequenceCommand.CommandName)
            {
                await StartAsync(channel, publisher, player, PlaySequenceCommand.FromJson(json), delivery.DeliveryTag, stoppingToken);
                return;
            }

            if (command == StopPlaybackCommand.CommandName)
            {
                await StopAsync(channel, StopPlaybackCommand.FromJson(json), delivery.DeliveryTag, stoppingToken);
                return;
            }

            throw new ContractViolationException($"Unknown command \"{command}\".");
        }
        catch (ContractViolationException exception)
        {
            _logger.LogError(exception, "Dropping non-contract message from {Queue}", _options.CommandsQueue);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
    }

    private async Task StartAsync(IChannel channel, AgentEventPublisher publisher, SequencePlayer player, PlaySequenceCommand command, ulong deliveryTag, CancellationToken stoppingToken)
    {
        if (!_processed.TryBegin(command.MessageId))
        {
            await channel.BasicAckAsync(deliveryTag, false, stoppingToken);
            return;
        }

        if (command.IsStale(TimeSpan.FromSeconds(_options.MaxCommandAgeSeconds), DateTimeOffset.UtcNow))
        {
            var reason = $"stale command: issued {command.OccurredAt:O}, older than {_options.MaxCommandAgeSeconds}s";
            await publisher.PublishAsync(AgentEvent.Failed(command.JobId, reason), stoppingToken);
            _processed.MarkDone(command.MessageId);
            await channel.BasicAckAsync(deliveryTag, false, stoppingToken);
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_activeLock)
        {
            if (_active is not null)
            {
                cancellation.Dispose();
                throw new ContractViolationException("A playback job is already active.");
            }
            _active = new ActivePlayback(command.JobId, cancellation);
        }

        await publisher.PublishAsync(AgentEvent.Started(command.JobId), stoppingToken);
        _ = RunPlaybackAsync(command, player, publisher, cancellation);
        // Ack after starting, not after completing: a later audio.stop must be
        // deliverable while the player process is alive.
        await channel.BasicAckAsync(deliveryTag, false, stoppingToken);
    }

    private async Task StopAsync(IChannel channel, StopPlaybackCommand command, ulong deliveryTag, CancellationToken stoppingToken)
    {
        if (_processed.TryBegin(command.MessageId))
        {
            lock (_activeLock)
            {
                if (_active?.JobId == command.JobId)
                {
                    _active.Cancellation.Cancel();
                }
            }
            _processed.MarkDone(command.MessageId);
        }

        await channel.BasicAckAsync(deliveryTag, false, stoppingToken);
    }

    private async Task RunPlaybackAsync(PlaySequenceCommand command, SequencePlayer player, AgentEventPublisher publisher, CancellationTokenSource cancellation)
    {
        AgentEvent verdict;
        try
        {
            var result = await player.PlayAsync(command, cancellation.Token);
            verdict = result.Completed
                ? AgentEvent.Completed(command.JobId)
                : AgentEvent.Failed(command.JobId, result.Reason!);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            verdict = AgentEvent.Interrupted(command.JobId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Job {JobId} failed unexpectedly", command.JobId);
            verdict = AgentEvent.Failed(command.JobId, exception.Message);
        }

        // Release the active slot before reporting the verdict: the moment playback
        // hears a terminal event it may dispatch the next play command, and that
        // command must find the agent free instead of being dropped as a violation.
        _processed.MarkDone(command.MessageId);
        lock (_activeLock)
        {
            if (_active?.JobId == command.JobId)
            {
                _active = null;
            }
        }
        cancellation.Dispose();

        try
        {
            await publisher.PublishAsync(verdict, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Job {JobId}: failed to publish {Event}", command.JobId, verdict.Event);
        }
    }

    private static string CommandName(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String
                ? command.GetString()!
                : throw new ContractViolationException("Contract is missing \"command\".");
        }
        catch (JsonException exception)
        {
            throw new ContractViolationException("Body is not valid JSON.", exception);
        }
    }

    private sealed record ActivePlayback(string JobId, CancellationTokenSource Cancellation);
}
