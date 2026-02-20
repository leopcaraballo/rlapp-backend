namespace WaitingRoom.Projections.EventSubscription;

using BuildingBlocks.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Text.Json;

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
            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);

            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            foreach (var pattern in _routingPatterns)
            {
                _channel.QueueBind(
                    queue: _queueName,
                    exchange: _exchangeName,
                    routingKey: pattern,
                    arguments: null);
            }

            _consumer = new EventingBasicConsumer(_channel);
            _consumer.Received += (sender, args) => OnMessageReceived(args);
            _consumer.Shutdown += OnConsumerShutdown;
            _consumer.Registered += OnConsumerRegistered;
            _consumer.Unregistered += OnConsumerUnregistered;
            _consumer.ConsumerCancelled += OnConsumerCancelled;

            _channel.BasicConsume(
                queue: _queueName,
                autoAck: false,
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
            _channel?.Close();
            _connection?.Close();
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

            var json = JsonSerializer.Deserialize<JsonElement>(message);
            var eventName = json.GetProperty("eventName").GetString() ?? string.Empty;
            var @event = _eventSerializer.Deserialize(eventName, message);

            EventReceived?.Invoke(this, new EventReceivedArgs
            {
                Event = @event,
                ReceivedAt = DateTime.UtcNow,
                RoutingKey = args.RoutingKey
            });

            _channel?.BasicAck(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredArgs
            {
                Exception = ex,
                Message = $"Error processing message: {ex.Message}",
                OccurredAt = DateTime.UtcNow
            });

            if (_channel != null)
            {
                try
                {
                    _channel.BasicNack(args.DeliveryTag, false, true);
                }
                catch (Exception nackEx)
                {
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
    }

    private void OnConsumerUnregistered(object? sender, ConsumerEventArgs args)
    {
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
