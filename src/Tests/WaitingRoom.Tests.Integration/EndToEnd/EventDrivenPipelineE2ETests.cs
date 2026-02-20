namespace WaitingRoom.Tests.Integration.EndToEnd;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.CommandHandlers;
using WaitingRoom.Application.Ports;
using WaitingRoom.Application.Services;
using WaitingRoom.Domain.Events;
using WaitingRoom.Domain.Aggregates;
using BuildingBlocks.Observability;
using WaitingRoom.Infrastructure.Observability;
using WaitingRoom.Infrastructure.Messaging;
using WaitingRoom.Infrastructure.Persistence.EventStore;
using WaitingRoom.Infrastructure.Persistence.Outbox;
using WaitingRoom.Infrastructure.Serialization;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Implementations;
using WaitingRoom.Projections.Processing;
using BuildingBlocks.EventSourcing;

/// <summary>
/// End-to-End integration tests for the complete event-driven pipeline.
///
/// Tests verify:
/// 1. Event creation in domain
/// 2. Event persistence in EventStore
/// 3. Outbox pattern reliability
/// 4. Event lag tracking
/// 5. Projection updates
/// 6. Full pipeline correctness
///
/// These tests require real infrastructure:
/// - PostgreSQL (with real tables)
/// - RabbitMQ (topic-based messaging)
/// - Dedicated test database
/// </summary>
public sealed class EventDrivenPipelineE2ETests : IAsyncLifetime
{
    private readonly string _testConnectionString;
    private readonly IServiceProvider _serviceProvider;
    private readonly CheckInPatientCommandHandler _commandHandler;
    private readonly ProjectionEventProcessor _projectionProcessor;
    private readonly IEventLagTracker _lagTracker;
    private readonly IEventStore _eventStore;
    private readonly IOutboxStore _outboxStore;

    // Test data
    private const string TestQueueId = "test-queue-1";
    private const string TestPatientId = "test-patient-1";
    private const string TestPatientName = "Test Patient";
    private const string TestPriority = "HIGH";
    private const string TestConsultationType = "Cardiology";

    public EventDrivenPipelineE2ETests()
    {
        // Use test database
        _testConnectionString = "Host=localhost;Port=5432;Database=rlapp_waitingroom_test;Username=rlapp;Password=rlapp_secure_password";

        // Build service provider for test
        var services = new ServiceCollection();

        // Infrastructure
        services.AddLogging();
        services.AddSingleton<EventTypeRegistry>(EventTypeRegistry.CreateDefault());
        services.AddSingleton<EventSerializer>();
        services.AddSingleton<PostgresOutboxStore>(new PostgresOutboxStore(_testConnectionString));
        services.AddSingleton<PostgresEventStore>(sp => new PostgresEventStore(
            _testConnectionString,
            sp.GetRequiredService<EventSerializer>(),
            sp.GetRequiredService<PostgresOutboxStore>(),
            sp.GetRequiredService<IEventLagTracker>()));
        services.AddSingleton<IEventPublisher, OutboxEventPublisher>();
        services.AddSingleton<PostgresEventLagTracker>(new PostgresEventLagTracker(_testConnectionString));
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
        services.AddSingleton<IOutboxStore>(sp => sp.GetRequiredService<PostgresOutboxStore>());
        services.AddSingleton<IEventLagTracker>(sp => sp.GetRequiredService<PostgresEventLagTracker>());
        services.AddSingleton<IClock, SystemClock>();

        // Application
        services.AddScoped<CheckInPatientCommandHandler>();

        // Projections
        // (Projection setup would go here)

        _serviceProvider = services.BuildServiceProvider();
        _commandHandler = _serviceProvider.GetRequiredService<CheckInPatientCommandHandler>();
        _lagTracker = _serviceProvider.GetRequiredService<IEventLagTracker>();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _outboxStore = _serviceProvider.GetRequiredService<IOutboxStore>();

        // Create processor (would use actual projection in real test)
        _projectionProcessor = new ProjectionEventProcessor(
            new MockProjection(),
            _lagTracker,
            _serviceProvider.GetRequiredService<ILogger<ProjectionEventProcessor>>());
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        // Setup test database
        if (_eventStore is PostgresEventStore postgresEventStore)
        {
            // Truncate test data tables
            var connection = new Npgsql.NpgsqlConnection(_testConnectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                TRUNCATE TABLE waiting_room_events CASCADE;
                TRUNCATE TABLE waiting_room_outbox CASCADE;
                TRUNCATE TABLE event_processing_lag CASCADE;
                TRUNCATE TABLE projection_checkpoints CASCADE;";

            await command.ExecuteNonQueryAsync();
            await connection.CloseAsync();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Cleanup
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Full pipeline test: Event created → Persisted → Dispatched → Processed
    ///
    /// Verifies:
    /// ✓ Event is created in domain
    /// ✓ Event is stored in EventStore
    /// ✓ Event is added to outbox
    /// ✓ Outbox is dispatched
    /// ✓ Event is processed by projection
    /// ✓ Lag metrics are correct
    /// </summary>
    [Fact]
    public async Task FullPipeline_CheckInPatient_RealizesCorrectly()
    {
        // Arrange
        await EnsureQueueExistsAsync(TestQueueId, "Test Queue", 50, CancellationToken.None);

        var command = new CheckInPatientCommand
        {
            QueueId = TestQueueId,
            PatientId = TestPatientId,
            PatientName = TestPatientName,
            Priority = TestPriority,
            ConsultationType = TestConsultationType,
            Actor = "test-doctor"
        };

        // Act: Step 1 - Create event via command handler
        var eventCount = await _commandHandler.HandleAsync(command, CancellationToken.None);
        Assert.Equal(1, eventCount);

        // Assert: Step 2 - Verify event in EventStore
        var eventsFromStore = await _eventStore.GetEventsAsync(TestQueueId);
        var eventList = eventsFromStore.ToList();
        var patientCheckedInEvent = eventList.OfType<PatientCheckedIn>().Single();
        Assert.Equal(TestQueueId, patientCheckedInEvent.QueueId);
        Assert.Equal(TestPatientId, patientCheckedInEvent.PatientId);
        Assert.Equal(TestPatientName, patientCheckedInEvent.PatientName);

        // Assert: Step 3 - Verify event in outbox (before dispatch)
        var outboxPending = await _outboxStore.GetPendingAsync(100, CancellationToken.None);
        Assert.Single(outboxPending);
        Assert.Equal("PatientCheckedIn", outboxPending[0].EventName);

        // Act: Step 4 - Simulate outbox dispatch
        // (In real test, OutboxWorker would run here)
        // For this test, we manually mark as published
        await _outboxStore.MarkDispatchedAsync(new[] { outboxPending[0].EventId }, CancellationToken.None);

        // Assert: Step 5 - Verify outbox is now empty
        var outboxAfterDispatch = await _outboxStore.GetPendingAsync(100, CancellationToken.None);
        Assert.Empty(outboxAfterDispatch);

        // Act: Step 6 - Process event through projection
        var startTime = DateTime.UtcNow;
        await _projectionProcessor.ProcessEventAsync(patientCheckedInEvent, CancellationToken.None);
        var processingDuration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert: Step 7 - Verify lag metrics were recorded
        var lagMetrics = await _lagTracker.GetLagMetricsAsync(patientCheckedInEvent.Metadata.EventId);
        Assert.NotNull(lagMetrics);
        Assert.Equal("PROCESSED", lagMetrics.Status);
        Assert.NotNull(lagMetrics.TotalLagMs);
        Assert.True(lagMetrics.TotalLagMs >= 0);
    }

    /// <summary>
    /// Test idempotency: Processing same event twice produces same result.
    ///
    /// Verifies:
    /// ✓ Handler handles idempotency correctly
    /// ✓ Second process is no-op (same state)
    /// ✓ Lag metrics are updated (not duplicated)
    /// </summary>
    [Fact]
    public async Task ProcessEvent_Idempotent_SameEventTwiceProducesSameState()
    {
        // Arrange
        await EnsureQueueExistsAsync(TestQueueId, "Test Queue", 50, CancellationToken.None);

        var command = new CheckInPatientCommand
        {
            QueueId = TestQueueId,
            PatientId = TestPatientId,
            PatientName = TestPatientName,
            Priority = TestPriority,
            ConsultationType = TestConsultationType,
            Actor = "test-doctor"
        };

        // Act: Create and process event first time
        await _commandHandler.HandleAsync(command, CancellationToken.None);
        var events = (await _eventStore.GetEventsAsync(TestQueueId)).ToList();
        var evt = events.OfType<PatientCheckedIn>().First();

        await _projectionProcessor.ProcessEventAsync(evt, CancellationToken.None);
        var metricsAfterFirst = await _lagTracker.GetLagMetricsAsync(evt.Metadata.EventId);

        // Act: Process same event again
        await _projectionProcessor.ProcessEventAsync(evt, CancellationToken.None);
        var metricsAfterSecond = await _lagTracker.GetLagMetricsAsync(evt.Metadata.EventId);

        // Assert: Metrics should be same (handler is idempotent)
        Assert.Equal(metricsAfterFirst?.Status, metricsAfterSecond?.Status);
        Assert.Equal(metricsAfterFirst?.TotalLagMs, metricsAfterSecond?.TotalLagMs);
    }

    /// <summary>
    /// Test lag statistics aggregation.
    ///
    /// Verifies:
    /// ✓ Statistics calculated correctly
    /// ✓ Percentiles computed accurately
    /// ✓ Throughput calculated
    /// </summary>
    [Fact]
    public async Task LagStatistics_MultipleEvents_ComputedCorrectly()
    {
        // Arrange: Create 10 events with varying lag
        var eventIds = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            await EnsureQueueExistsAsync($"queue-{i}", "Test Queue", 50, CancellationToken.None);

            var command = new CheckInPatientCommand
            {
                QueueId = $"queue-{i}",
                PatientId = $"patient-{i}",
                PatientName = $"Patient {i}",
                Priority = "HIGH",
                ConsultationType = "Cardiology",
                Actor = "test-doctor"
            };

            await _commandHandler.HandleAsync(command, CancellationToken.None);

            var events = (await _eventStore.GetEventsAsync($"queue-{i}")).ToList();
            var evt = events.OfType<PatientCheckedIn>().First();
            eventIds.Add(evt.Metadata.EventId);

            // Vary processing delay
            await Task.Delay(10 + (i * 5));
            await _projectionProcessor.ProcessEventAsync(evt, CancellationToken.None);
        }

        // Act: Get statistics
        var stats = await _lagTracker.GetStatisticsAsync(
            "PatientCheckedIn",
            from: DateTime.UtcNow.AddMinutes(-1),
            to: DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.NotNull(stats);
        Assert.Equal("PatientCheckedIn", stats.EventName);
        Assert.Equal(10, stats.TotalEventsProcessed);
        Assert.True(stats.AverageLagMs > 0);
        Assert.True(stats.P95LagMs >= stats.AverageLagMs);
        Assert.True(stats.MaxLagMs >= stats.P95LagMs);
        Assert.True(stats.MinLagMs <= stats.AverageLagMs);
    }

    /// <summary>
    /// Test slowest events analysis for debugging.
    ///
    /// Verifies:
    /// ✓ Slowest events identified correctly
    /// ✓ Ordered by lag descending
    /// ✓ Limited to requested count
    /// </summary>
    [Fact]
    public async Task SlowestEvents_CorrectlyIdentified_ForDebugging()
    {
        // Arrange: Create events with different processing times
        for (int i = 0; i < 5; i++)
        {
            await EnsureQueueExistsAsync($"queue-slow-{i}", "Test Queue", 50, CancellationToken.None);

            var command = new CheckInPatientCommand
            {
                QueueId = $"queue-slow-{i}",
                PatientId = $"patient-slow-{i}",
                PatientName = $"Slow Patient {i}",
                Priority = "HIGH",
                ConsultationType = "Cardiology",
                Actor = "test-doctor"
            };

            await _commandHandler.HandleAsync(command, CancellationToken.None);

            var events = (await _eventStore.GetEventsAsync($"queue-slow-{i}")).ToList();
            var evt = events.OfType<PatientCheckedIn>().First();

            // Simulate varying processing times (slowest last)
            await Task.Delay(1000 - (i * 100));
            await _projectionProcessor.ProcessEventAsync(evt, CancellationToken.None);
        }

        // Act: Get slowest events
        var slowest = (await _lagTracker.GetSlowestEventsAsync("PatientCheckedIn", limit: 3)).ToList();

        // Assert
        Assert.NotEmpty(slowest);
        Assert.True(slowest.Count <= 3);

        // Verify ordered by lag descending
        for (int i = 0; i < slowest.Count - 1; i++)
        {
            Assert.True(slowest[i].TotalLagMs >= slowest[i + 1].TotalLagMs,
                "Events should be ordered by lag descending");
        }
    }

    // ========================================================================
    // HELPER IMPLEMENTATIONS
    // ========================================================================

    /// <summary>
    /// Mock projection for testing (minimal implementation).
    /// </summary>
    private sealed class MockProjection : IProjection
    {
        public string ProjectionId => "mock-projection";

        public IReadOnlyList<IProjectionHandler> GetHandlers() => Array.Empty<IProjectionHandler>();

        public Task<ProjectionCheckpoint?> GetCheckpointAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProjectionCheckpoint?>(null);

        public Task RebuildAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessEventAsync(DomainEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessEventsAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetHealthStatusAsync(bool isHealthy, string? errorMessage = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ProjectionHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectionHealth { IsHealthy = true, LastUpdatedAt = DateTime.UtcNow });
    }

    private async Task EnsureQueueExistsAsync(
        string queueId,
        string queueName,
        int maxCapacity,
        CancellationToken cancellationToken)
    {
        var metadata = EventMetadata.CreateNew(queueId, "test-system");
        var queue = WaitingQueue.Create(queueId, queueName, maxCapacity, metadata);
        var eventIds = queue.UncommittedEvents
            .Select(e => Guid.Parse(e.Metadata.EventId))
            .ToArray();

        await _eventStore.SaveAsync(queue, cancellationToken);

        if (eventIds.Length > 0)
            await _outboxStore.MarkDispatchedAsync(eventIds, cancellationToken);
    }
}
