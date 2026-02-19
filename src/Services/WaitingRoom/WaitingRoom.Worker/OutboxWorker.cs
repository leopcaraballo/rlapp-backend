namespace WaitingRoom.Worker;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WaitingRoom.Worker.Services;

/// <summary>
/// Background Worker Service for Outbox Pattern Dispatcher.
///
/// Responsibilities:
/// - Run continuous polling loop
/// - Call OutboxDispatcher on interval
/// - Handle graceful shutdown
/// - CancellationToken safe
/// - Observable execution
///
/// Architecture:
/// - Infrastructure worker
/// - Delegates to OutboxDispatcher
/// - NO Domain logic
/// - NO Application logic
/// - Deterministic, resilient, observable
/// </summary>
internal sealed class OutboxWorker : BackgroundService
{
    private readonly OutboxDispatcher _dispatcher;
    private readonly OutboxDispatcherOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        OutboxDispatcher dispatcher,
        OutboxDispatcherOptions options,
        ILogger<OutboxWorker> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Main execution loop.
    ///
    /// Process:
    /// 1. Dispatch batch of pending messages
    /// 2. Wait for configured interval
    /// 3. Repeat until cancellation requested
    ///
    /// Guarantees:
    /// - Graceful shutdown on cancellation
    /// - Continues on transient failures
    /// - Observable lifecycle
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox Worker starting. Polling interval: {Interval} seconds, Batch size: {BatchSize}",
            _options.PollingIntervalSeconds,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatchedCount = await _dispatcher.DispatchBatchAsync(stoppingToken);

                if (dispatchedCount == 0)
                {
                    _logger.LogDebug("No messages dispatched. Waiting {Interval}s", _options.PollingIntervalSeconds);
                }
            }
            catch (Exception ex)
            {
                // Log error but continue processing
                // Individual message failures are handled in OutboxDispatcher
                // This catches infrastructure failures (DB connection, etc.)
                _logger.LogError(
                    ex,
                    "Unexpected error in outbox dispatcher loop. Continuing after delay.");
            }

            // Wait before next polling attempt
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during graceful shutdown
                _logger.LogInformation("Outbox Worker shutdown requested");
                break;
            }
        }

        _logger.LogInformation("Outbox Worker stopped gracefully");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox Worker is starting");
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox Worker is stopping");
        return base.StopAsync(cancellationToken);
    }
}
