namespace WaitingRoom.Projections.Worker;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WaitingRoom.Projections.EventSubscription;
using WaitingRoom.Projections.Processing;

/// <summary>
/// Background worker service that runs projections.
///
/// Responsibilities:
/// - Listen for events from message broker
/// - Route events to projection processor
/// - Handle graceful shutdown
/// - Observable lifecycle and health
///
/// Architecture:
/// - Hosted service (runs alongside API)
/// - Subscribes to RabbitMQ topic events
/// - Updates read models in real-time
/// - Tracks lag and health metrics
/// </summary>
internal sealed class ProjectionWorker : BackgroundService
{
    private readonly IProjectionEventSubscriber _subscriber;
    private readonly ProjectionEventProcessor _processor;
    private readonly ILogger<ProjectionWorker> _logger;

    public ProjectionWorker(
        IProjectionEventSubscriber subscriber,
        ProjectionEventProcessor processor,
        ILogger<ProjectionWorker> logger)
    {
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Projection worker starting");

        try
        {
            // Wire up event handlers
            _subscriber.EventReceived += (sender, args) => OnEventReceived(args, stoppingToken);
            _subscriber.ErrorOccurred += (sender, args) => OnErrorOccurred(args);

            // Start subscriber
            await _subscriber.StartAsync(stoppingToken);

            _logger.LogInformation("Projection worker ready and listening for events");

            // Keep running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Projection worker cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in projection worker");
            throw;
        }
        finally
        {
            await _subscriber.StopAsync(stoppingToken);
            await _subscriber.DisposeAsync();
            _logger.LogInformation("Projection worker stopped");
        }
    }

    private async void OnEventReceived(
        EventReceivedArgs args,
        CancellationToken cancellation)
    {
        try
        {
            _logger.LogDebug(
                "Projection worker received event {EventType} (routing: {RoutingKey})",
                args.Event.GetType().Name,
                args.RoutingKey);

            // Process event through projection
            await _processor.ProcessEventAsync(args.Event, cancellation);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing event {EventType} in projection worker",
                args.Event.GetType().Name);
        }
    }

    private void OnErrorOccurred(ErrorOccurredArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Projection worker error: {Message}",
            args.Message);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Projection worker shutdown initiated");
        await base.StopAsync(cancellationToken);
    }
}
