namespace WaitingRoom.Projections.EventSubscription;

using BuildingBlocks.EventSourcing;
using BuildingBlocks.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Text.Json;

/// <summary>
/// RabbitMQ event subscriber for projections.
///
/// Responsibilities:
/// - Connect to RabbitMQ topic exchange
/// - Subscribe to WaitingRoom domain events
/// - Deserialize events from JSON
/// - Route to projection handlers
/// - Support graceful shutdown
///
/// Architecture:
/// - Listen for events published by OutboxWorker
/// - Feed events to projection engine
/// - Track processing lag
/// - Handle connection failures gracefully
/// </summary>
public interface IProjectionEventSubscriber : IAsyncDisposable
{
    /// <summary>
    /// Start listening for events on the configured topic.
    /// </summary>
    Task StartAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Stop listening and cleanup resources.
    /// </summary>
    Task StopAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Event raised when an event is received.
    /// </summary>
    event EventHandler<EventReceivedArgs>? EventReceived;

    /// <summary>
    /// Event raised when an error occurs.
    /// </summary>
    event EventHandler<ErrorOccurredArgs>? ErrorOccurred;
}

/// <summary>
/// Event args for received events.
/// </summary>
public sealed class EventReceivedArgs : EventArgs
{
    public required DomainEvent Event { get; init; }
    public required DateTime ReceivedAt { get; init; }
    public required string RoutingKey { get; init; }
}

/// <summary>
/// Event args for errors.
/// </summary>
public sealed class ErrorOccurredArgs : EventArgs
{
    public required Exception Exception { get; init; }
    public required string Message { get; init; }
    public required DateTime OccurredAt { get; init; }
}

/// <summary>
/// RabbitMQ implementation of IProjectionEventSubscriber.
///
/// Subscribes to topic-based events from message broker.
/// Enables real-time projection updates as events occur.
/// </summary>
internal sealed class RabbitMqProjectionEventSubscriber : IProjectionEventSubscriber
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IEventSerializer _eventSerializer;
    private readonly string _exchangeName;
    private readonly string _queueName;
    private readonly string[] _routingPatterns;

    private IConnection? _connection;
    private IModel? _channel;
    private EventingBasicConsumer? _consumer;
    private bool _disposed;

    public event EventHandler<EventReceivedArgs>? EventReceived;
    public event EventHandler<ErrorOccurredArgs>? ErrorOccurred;

    public RabbitMqProjectionEventSubscriber(
        IConnectionFactory connectionFactory,
        IEventSerializer eventSerializer,
        string exchangeName = "waiting_room_events",
        string queueName = "waiting-room-projection-queue",
        string[]? routingPatterns = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _eventSerializer = eventSerializer ?? throw new ArgumentNullException(nameof(eventSerializer));
        _exchangeName = exchangeName;
        _queueName = queueName;
        _routingPatterns = routingPatterns ?? new[] { "waiting_room.*" };
    }

    public Task StartAsync(CancellationToken cancellation = default)
    {
        try
        {
            // Create connection
            _connection = _connectionFactory.CreateConnection();

            // Create channel
            _channel = _connection.CreateModel();

            // Declare exchange
            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);

            // Declare queue
            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Bind queue to exchange with routing patterns
            foreach (var pattern in _routingPatterns)
            {
                _channel.QueueBind(
                    queue: _queueName,
                    exchange: _exchangeName,
                    routingKey: pattern,
                    arguments: null);
            }

            // Setup consumer
            _consumer = new EventingBasicConsumer(_channel);
            _consumer.Received += (sender, args) => OnMessageReceived(args);
            _consumer.Shutdown += OnConsumerShutdown;
            _consumer.Registered += OnConsumerRegistered;
            _consumer.Unregistered += OnConsumerUnregistered;
            _consumer.ConsumerCancelled += OnConsumerCancelled;

            // Start consuming
            _channel.BasicConsume(
                queue: _queueName,
                autoAck: false, // Manual acknowledgment for reliability
                consumerTag: $"projection-consumer-{Guid.NewGuid()}",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: _consumer);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
            {
                Exception = ex,
                Message = $"Failed to start projection event subscriber: {ex.Message}",
                OccurredAt = DateTime.UtcNow
            });
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellation = default)
    {
        try
        {
            if (_channel != null)
            {
                _channel.Close();
            }

            if (_connection != null)
            {
                _connection.Close();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
            {
                Exception = ex,
                Message = $"Error during shutdown: {ex.Message}",
                OccurredAt = DateTime.UtcNow
            });
        }

        return Task.CompletedTask;
    }

    private void OnMessageReceived(BasicDeliverEventArgs args)
    {
        try
        {
            var body = args.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            // Try to deserialize event
            var json = JsonSerializer.Deserialize<JsonElement>(message);
            var eventName = json.GetProperty("eventName").GetString() ?? string.Empty;

            // Deserialize the event
            var @event = _eventSerializer.Deserialize(eventName, message);

            // Raise event received
            EventReceived?.Invoke(this, new EventReceivedArgs
            {
                Event = @event,
                ReceivedAt = DateTime.UtcNow,
                RoutingKey = args.RoutingKey
            });

            // Acknowledge message after successful processing
            if (_channel != null)
            {
                _channel.BasicAck(args.DeliveryTag, false);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
            {
                Exception = ex,
                Message = $"Error processing message: {ex.Message}",
                OccurredAt = DateTime.UtcNow
            });

            // Nack the message for retry
            if (_channel != null)
            {
                try
                {
                    _channel.BasicNack(args.DeliveryTag, false, true);
                }
                catch (Exception nackEx)
                {
                    // Nack failed - notify but don't throw  
                    // (already in error handler, message will be redelivered on reconnect)
                    ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
                    {
                        Exception = nackEx,
                        Message = $"Failed to nack message {args.DeliveryTag}: {nackEx.Message}",
                        OccurredAt = DateTime.UtcNow
                    });
                }
            }
        }
    }

    private void OnConsumerShutdown(object? sender, ShutdownEventArgs args)
    {
        ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
        {
            Exception = new InvalidOperationException("Consumer shutdown"),
            Message = $"Consumer shutdown: {args.ReplyText}",
            OccurredAt = DateTime.UtcNow
        });
    }

    private void OnConsumerRegistered(object? sender, ConsumerEventArgs args)
    {
        // Consumer registered successfully
    }

    private void OnConsumerUnregistered(object? sender, ConsumerEventArgs args)
    {
        // Consumer unregistered
    }

    private void OnConsumerCancelled(object? sender, ConsumerEventArgs args)
    {
        ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
        {
            Exception = new InvalidOperationException("Consumer cancelled"),
            Message = "Consumer was cancelled",
            OccurredAt = DateTime.UtcNow
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            await StopAsync();
        }
        finally
        {
            _connection?.Dispose();
            _channel?.Dispose();
            _disposed = true;
        }
    }
}
