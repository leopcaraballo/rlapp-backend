namespace WaitingRoom.Worker.Services;

using Microsoft.Extensions.Logging;
using BuildingBlocks.EventSourcing;
using WaitingRoom.Application.Ports;
using WaitingRoom.Infrastructure.Persistence.Outbox;
using WaitingRoom.Infrastructure.Serialization;

/// <summary>
/// Outbox Dispatcher Service
///
/// Responsibilities:
/// - Fetch pending outbox messages
/// - Deserialize events
/// - Publish via IEventPublisher (idempotent)
/// - Mark as Dispatched or Failed
/// - Guarantee transactional consistency
/// - Implement retry with exponential backoff
/// - Handle poison messages
///
/// Architecture:
/// - Pure infrastructure service
/// - NO Domain logic
/// - NO Application logic
/// - Uses existing abstractions
/// - Deterministic behavior
/// </summary>
internal sealed class OutboxDispatcher
{
    private readonly IOutboxStore _outboxStore;
    private readonly IEventPublisher _publisher;
    private readonly EventSerializer _serializer;
    private readonly OutboxDispatcherOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IOutboxStore outboxStore,
        IEventPublisher publisher,
        EventSerializer serializer,
        OutboxDispatcherOptions options,
        ILogger<OutboxDispatcher> logger)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Dispatches a single batch of pending outbox messages.
    ///
    /// Process:
    /// 1. Fetch pending messages (respects next_attempt_at)
    /// 2. Deserialize events
    /// 3. Publish to message broker (idempotent)
    /// 4. Mark as Dispatched or Failed
    ///
    /// Guarantees:
    /// - Idempotent publish (duplicate detection in broker)
    /// - Retry with exponential backoff
    /// - Poison message handling (max retries → permanent failure)
    /// - Observable failures
    /// </summary>
    /// <returns>Number of messages dispatched successfully</returns>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await _outboxStore.GetPendingAsync(_options.BatchSize, cancellationToken);

        if (messages.Count == 0)
        {
            _logger.LogDebug("No pending outbox messages found");
            return 0;
        }

        _logger.LogInformation("Found {Count} pending outbox messages to dispatch", messages.Count);

        var successCount = 0;

        foreach (var message in messages)
        {
            try
            {
                await DispatchSingleMessageAsync(message, cancellationToken);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to dispatch outbox message {EventId} (attempt {Attempts})",
                    message.EventId,
                    message.Attempts + 1);

                await HandleFailureAsync(message, ex, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Dispatched {SuccessCount}/{TotalCount} outbox messages",
            successCount,
            messages.Count);

        return successCount;
    }

    /// <summary>
    /// Dispatches a single outbox message.
    ///
    /// Steps:
    /// 1. Deserialize event from JSON payload
    /// 2. Publish via IEventPublisher (RabbitMQ)
    /// 3. Mark as dispatched in outbox
    /// </summary>
    private async Task DispatchSingleMessageAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Dispatching outbox message {EventId} - {EventName}",
            message.EventId,
            message.EventName);

        // Deserialize event
        var domainEvent = _serializer.Deserialize(message.EventName, message.Payload);

        // Publish event
        await _publisher.PublishAsync(domainEvent, cancellationToken);

        // Mark as dispatched in outbox
        await _outboxStore.MarkDispatchedAsync(new[] { message.EventId }, cancellationToken);

        _logger.LogInformation(
            "Successfully dispatched event {EventId} - {EventName}",
            message.EventId,
            message.EventName);
    }

    /// <summary>
    /// Handles dispatch failure with exponential backoff.
    ///
    /// Retry Strategy:
    /// - Calculate delay: baseDelay * 2^(attempts)
    /// - Cap at maxDelay
    /// - Mark as Failed with next_attempt_at
    ///
    /// Poison Message Handling:
    /// - After maxRetryAttempts, mark as permanently Failed
    /// - Requires manual intervention
    /// </summary>
    private async Task HandleFailureAsync(
        OutboxMessage message,
        Exception error,
        CancellationToken cancellationToken)
    {
        var nextAttempt = message.Attempts + 1;

        if (nextAttempt >= _options.MaxRetryAttempts)
        {
            _logger.LogError(
                "Outbox message {EventId} exceeded max retry attempts ({MaxAttempts}). Marking as permanently failed.",
                message.EventId,
                _options.MaxRetryAttempts);

            // Mark as permanently failed (very long retry delay = poison message)
            await _outboxStore.MarkFailedAsync(
                new[] { message.EventId },
                $"Max retry attempts exceeded: {error.Message}",
                TimeSpan.FromDays(365), // Poison message - requires manual intervention
                cancellationToken);

            return;
        }

        // Calculate exponential backoff
        var baseDelay = TimeSpan.FromSeconds(_options.BaseRetryDelaySeconds);
        var maxDelay = TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds);
        var calculatedDelay = baseDelay * Math.Pow(2, message.Attempts);
        var retryDelay = calculatedDelay > maxDelay ? maxDelay : calculatedDelay;

        _logger.LogWarning(
            "Outbox message {EventId} failed (attempt {Attempts}/{MaxAttempts}). Retry in {RetryDelay}",
            message.EventId,
            nextAttempt,
            _options.MaxRetryAttempts,
            retryDelay);

        await _outboxStore.MarkFailedAsync(
            new[] { message.EventId },
            error.Message,
            retryDelay,
            cancellationToken);
    }
}
