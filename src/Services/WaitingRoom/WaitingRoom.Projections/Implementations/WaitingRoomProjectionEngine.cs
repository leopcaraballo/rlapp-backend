namespace WaitingRoom.Projections.Implementations;

using BuildingBlocks.EventSourcing;
using Microsoft.Extensions.Logging;
using WaitingRoom.Application.Ports;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Handlers;

/// <summary>
/// WaitingRoom projection engine.
///
/// Orchestrates:
/// - Event handler registration
/// - Event routing to correct handler
/// - Checkpoint management
/// - Rebuild process
///
/// This projection handles all WaitingQueue aggregate events
/// and keeps WaitingRoomMonitorView and QueueStateView in sync.
///
/// Invariants:
/// - Handlers are stateless and reusable
/// - Events processed in order
/// - Same events → same state (deterministic)
/// - Idempotent: processing twice = no change
/// </summary>
public sealed class WaitingRoomProjectionEngine : IProjection
{
    public string ProjectionId => "waiting-room-projection";

    private readonly IWaitingRoomProjectionContext _context;
    private readonly IEventStore _eventStore;
    private readonly ILogger<WaitingRoomProjectionEngine> _logger;
    private readonly Dictionary<string, IProjectionHandler> _handlers;

    public WaitingRoomProjectionEngine(
        IWaitingRoomProjectionContext context,
        IEventStore eventStore,
        ILogger<WaitingRoomProjectionEngine> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Register all handlers
        // Each handler processes a specific domain event type
        _handlers = new Dictionary<string, IProjectionHandler>
        {
            [nameof(Domain.Events.WaitingQueueCreated)] = new WaitingQueueCreatedProjectionHandler(),
            [nameof(Domain.Events.PatientCheckedIn)] = new PatientCheckedInProjectionHandler(),
            [nameof(Domain.Events.PatientCalledAtCashier)] = new PatientCalledAtCashierProjectionHandler(),
            [nameof(Domain.Events.PatientPaymentValidated)] = new PatientPaymentValidatedProjectionHandler(),
            [nameof(Domain.Events.ConsultingRoomActivated)] = new ConsultingRoomActivatedProjectionHandler(),
            [nameof(Domain.Events.ConsultingRoomDeactivated)] = new ConsultingRoomDeactivatedProjectionHandler(),
            [nameof(Domain.Events.PatientClaimedForAttention)] = new PatientClaimedForAttentionProjectionHandler(),
            [nameof(Domain.Events.PatientCalled)] = new PatientCalledProjectionHandler(),
            [nameof(Domain.Events.PatientAttentionCompleted)] = new PatientAttentionCompletedProjectionHandler(),
        };
    }

    /// <summary>
    /// Gets all registered event handlers.
    /// </summary>
    public IReadOnlyList<IProjectionHandler> GetHandlers()
        => _handlers.Values.ToList().AsReadOnly();

    /// <summary>
    /// Gets current checkpoint (progress tracking).
    /// Returns null if projection never run.
    /// </summary>
    public async Task<ProjectionCheckpoint?> GetCheckpointAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.GetCheckpointAsync(ProjectionId, cancellationToken);
    }

    /// <summary>
    /// Rebuilds projection from scratch.
    ///
    /// Process:
    /// 1. Log rebuild start
    /// 2. Clear all projection data and idempotency keys
    /// 3. Get ALL events from EventStore in order
    /// 4. Process each event through appropriate handler
    /// 5. Update checkpoint with final version
    /// 6. Log completion
    ///
    /// This is deterministic:
        /// - Same events always produce same state
        /// - Result identical to incremental processing
        /// - Useful for validation, migration, recovery
    /// </summary>
    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting projection rebuild: {ProjectionId}", ProjectionId);

            // Step 1: Clear all projection state
            await _context.ClearAsync(ProjectionId, cancellationToken);
            _logger.LogInformation("Cleared projection data for {ProjectionId}", ProjectionId);

            // Step 2: Get all events from EventStore (deterministic order)
            var allEvents = await _eventStore.GetAllEventsAsync(cancellationToken);
            _logger.LogInformation("Retrieved {EventCount} events for replay", allEvents.Count());

            // Step 3: Replay each event
            long processedCount = 0;
            long lastEventVersion = 0;

            foreach (var @event in allEvents)
            {
                await ProcessEventInternalAsync(@event, cancellationToken);
                processedCount++;
                lastEventVersion = @event.Metadata.Version;

                if (processedCount % 1000 == 0)
                    _logger.LogInformation("Processed {Count} events during rebuild", processedCount);
            }

            // Step 4: Update checkpoint to indicate rebuild complete
            var checkpoint = new ProjectionCheckpoint
            {
                ProjectionId = ProjectionId,
                LastEventVersion = lastEventVersion,
                CheckpointedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = Guid.NewGuid().ToString(),
                Status = "rebuild-complete"
            };

            await _context.SaveCheckpointAsync(checkpoint, cancellationToken);
            _logger.LogInformation(
                "Rebuild complete: {EventCount} events processed, final version {Version}",
                processedCount,
                lastEventVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuild failed for projection {ProjectionId}", ProjectionId);
            throw;
        }
    }

    /// <summary>
    /// Processes a single event through projection.
    /// Used by event stream subscribers or Outbox worker.
    /// </summary>
    public async Task ProcessEventAsync(
        DomainEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (@event == null)
            throw new ArgumentNullException(nameof(@event));

        await ProcessEventInternalAsync(@event, cancellationToken);

        // Update checkpoint
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionId = ProjectionId,
            LastEventVersion = @event.Metadata.Version,
            CheckpointedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Status = "processing"
        };

        await _context.SaveCheckpointAsync(checkpoint, cancellationToken);
    }

    /// <summary>
    /// Processes multiple events (batch operation).
    /// Used during rebuild or batch imports.
    /// </summary>
    public async Task ProcessEventsAsync(
        IEnumerable<DomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events == null)
            throw new ArgumentNullException(nameof(events));

        var eventList = events.ToList();

        foreach (var @event in eventList)
        {
            await ProcessEventInternalAsync(@event, cancellationToken);
        }

        // Single checkpoint update for batch
        if (eventList.Any())
        {
            var checkpoint = new ProjectionCheckpoint
            {
                ProjectionId = ProjectionId,
                LastEventVersion = eventList.Max(e => e.Metadata.Version),
                CheckpointedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = Guid.NewGuid().ToString(),
                Status = "batch-processed"
            };

            await _context.SaveCheckpointAsync(checkpoint, cancellationToken);
        }
    }

    /// <summary>
    /// Internal event processing logic.
    /// Routes event to appropriate handler.
    /// </summary>
    private async Task ProcessEventInternalAsync(
        DomainEvent @event,
        CancellationToken cancellationToken)
    {
        var eventName = @event.EventName;

        if (!_handlers.TryGetValue(eventName, out var handler))
        {
            _logger.LogWarning("No handler registered for event type {EventName}", eventName);
            return; // Silently skip unknown events
        }

        try
        {
            // Cast context to extended interface
            if (_context is not IWaitingRoomProjectionContext waitingRoomContext)
                throw new InvalidOperationException($"Context must implement {nameof(IWaitingRoomProjectionContext)}");

            await handler.HandleAsync(@event, waitingRoomContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Handler error for event {EventName} (version {Version})",
                eventName,
                @event.Metadata.Version);
            throw;
        }
    }
}
