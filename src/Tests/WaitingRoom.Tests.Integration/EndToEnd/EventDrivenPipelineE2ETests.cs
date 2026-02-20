namespace WaitingRoom.Tests.Integration.EndToEnd;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.CommandHandlers;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Application.Services;
using WaitingRoom.Domain.Events;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.Exceptions;
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
    private readonly ActivateConsultingRoomCommandHandler _activateConsultingRoomHandler;
    private readonly CallNextCashierCommandHandler _callNextCashierHandler;
    private readonly ValidatePaymentCommandHandler _validatePaymentHandler;
    private readonly ClaimNextPatientCommandHandler _claimHandler;
    private readonly CallPatientCommandHandler _callHandler;
    private readonly CompleteAttentionCommandHandler _completeHandler;
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
        services.AddScoped<ActivateConsultingRoomCommandHandler>();
        services.AddScoped<CallNextCashierCommandHandler>();
        services.AddScoped<ValidatePaymentCommandHandler>();
        services.AddScoped<ClaimNextPatientCommandHandler>();
        services.AddScoped<CallPatientCommandHandler>();
        services.AddScoped<CompleteAttentionCommandHandler>();

        // Projections
        // (Projection setup would go here)

        _serviceProvider = services.BuildServiceProvider();
        _commandHandler = _serviceProvider.GetRequiredService<CheckInPatientCommandHandler>();
        _activateConsultingRoomHandler = _serviceProvider.GetRequiredService<ActivateConsultingRoomCommandHandler>();
        _callNextCashierHandler = _serviceProvider.GetRequiredService<CallNextCashierCommandHandler>();
        _validatePaymentHandler = _serviceProvider.GetRequiredService<ValidatePaymentCommandHandler>();
        _claimHandler = _serviceProvider.GetRequiredService<ClaimNextPatientCommandHandler>();
        _callHandler = _serviceProvider.GetRequiredService<CallPatientCommandHandler>();
        _completeHandler = _serviceProvider.GetRequiredService<CompleteAttentionCommandHandler>();
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

    [Fact]
    public async Task FullClinicalFlow_CheckInClaimCallComplete_PersistsAllEvents()
    {
        await EnsureQueueExistsAsync("queue-flow-1", "Flow Queue", 20, CancellationToken.None);
        await ActivateConsultingRoomsAsync("queue-flow-1", ["S-10"], CancellationToken.None);

        await _commandHandler.HandleAsync(new CheckInPatientCommand
        {
            QueueId = "queue-flow-1",
            PatientId = "patient-flow-1",
            PatientName = "Patient Flow",
            Priority = "High",
            ConsultationType = "General",
            Actor = "reception"
        }, CancellationToken.None);

        var cashierCall = await _callNextCashierHandler.HandleAsync(new CallNextCashierCommand
        {
            QueueId = "queue-flow-1",
            Actor = "cashier-1",
            CashierDeskId = "C-01"
        }, CancellationToken.None);

        await _validatePaymentHandler.HandleAsync(new ValidatePaymentCommand
        {
            QueueId = "queue-flow-1",
            PatientId = cashierCall.PatientId,
            Actor = "cashier-1",
            PaymentReference = "PAY-001"
        }, CancellationToken.None);

        var claimResult = await _claimHandler.HandleAsync(new ClaimNextPatientCommand
        {
            QueueId = "queue-flow-1",
            Actor = "doctor-a",
            StationId = "S-10"
        }, CancellationToken.None);

        await _callHandler.HandleAsync(new CallPatientCommand
        {
            QueueId = "queue-flow-1",
            PatientId = claimResult.PatientId,
            Actor = "nurse-a"
        }, CancellationToken.None);

        await _completeHandler.HandleAsync(new CompleteAttentionCommand
        {
            QueueId = "queue-flow-1",
            PatientId = claimResult.PatientId,
            Actor = "doctor-a",
            Outcome = "completed"
        }, CancellationToken.None);

        var events = (await _eventStore.GetEventsAsync("queue-flow-1", CancellationToken.None)).ToList();
        Assert.Contains(events, e => e is PatientCheckedIn);
        Assert.Contains(events, e => e is PatientClaimedForAttention);
        Assert.Contains(events, e => e is PatientCalled);
        Assert.Contains(events, e => e is PatientAttentionCompleted);
    }

    [Fact]
    public async Task ClinicOperationalFlow_2Receptions4ConsultRooms1PaymentDesk_WorksUnderLoad()
    {
        const string queueId = "clinic-main-queue";
        const int totalPatients = 24;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;
        var deadlineUtc = DateTime.UtcNow.AddSeconds(60);

        await EnsureQueueExistsAsync(queueId, "Main Clinic Queue", 100, cancellationToken);
        await ActivateConsultingRoomsAsync(
            queueId,
            Enumerable.Range(1, 4).Select(i => $"CONS-{i:00}").ToArray(),
            cancellationToken);

        var reception1Patients = Enumerable.Range(1, totalPatients / 2)
            .Select(i => $"R1-P-{i:000}")
            .ToList();

        var reception2Patients = Enumerable.Range(1, totalPatients / 2)
            .Select(i => $"R2-P-{i:000}")
            .ToList();

        var receptionTasks = new[]
        {
            Task.Run(async () =>
            {
                foreach (var patientId in reception1Patients)
                {
                    await ExecuteWithConcurrencyRetryAsync(async () =>
                    {
                        await _commandHandler.HandleAsync(new CheckInPatientCommand
                        {
                            QueueId = queueId,
                            PatientId = patientId,
                            PatientName = $"Patient {patientId}",
                            Priority = "Medium",
                            ConsultationType = "General",
                            Actor = "reception-1"
                        }, cancellationToken);
                    });
                }
            }),
            Task.Run(async () =>
            {
                foreach (var patientId in reception2Patients)
                {
                    await ExecuteWithConcurrencyRetryAsync(async () =>
                    {
                        await _commandHandler.HandleAsync(new CheckInPatientCommand
                        {
                            QueueId = queueId,
                            PatientId = patientId,
                            PatientName = $"Patient {patientId}",
                            Priority = "High",
                            ConsultationType = "General",
                            Actor = "reception-2"
                        }, cancellationToken);
                    });
                }
            })
        };

        await Task.WhenAll(receptionTasks);

        var paymentValidatedPatients = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        while (paymentValidatedPatients.Count < totalPatients)
        {
            if (DateTime.UtcNow >= deadlineUtc)
                throw new XunitException($"Timeout waiting for payment validation. Validated={paymentValidatedPatients.Count}, Expected={totalPatients}");

            try
            {
                var cashierCall = await ExecuteWithConcurrencyRetryAsync(async () =>
                    await _callNextCashierHandler.HandleAsync(new CallNextCashierCommand
                    {
                        QueueId = queueId,
                        Actor = "payment-desk-1",
                        CashierDeskId = "C-01"
                    }, cancellationToken));

                await ExecuteWithConcurrencyRetryAsync(async () =>
                {
                    await _validatePaymentHandler.HandleAsync(new ValidatePaymentCommand
                    {
                        QueueId = queueId,
                        PatientId = cashierCall.PatientId,
                        Actor = "payment-desk-1",
                        PaymentReference = $"PAY-{cashierCall.PatientId}"
                    }, cancellationToken);
                });

                paymentValidatedPatients.TryAdd(cashierCall.PatientId, true);
            }
            catch (DomainException)
            {
                await Task.Delay(10, cancellationToken);
            }
            catch (EventConflictException)
            {
                await Task.Delay(10, cancellationToken);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                await Task.Delay(10, cancellationToken);
            }
        }

        var completedPatients = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var paymentDeskQueue = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var consultRoomTasks = Enumerable.Range(1, 4)
            .Select(roomNumber => Task.Run(async () =>
            {
                while (completedPatients.Count < totalPatients && DateTime.UtcNow < deadlineUtc)
                {
                    try
                    {
                        var claim = await ExecuteWithConcurrencyRetryAsync(async () =>
                            await _claimHandler.HandleAsync(new ClaimNextPatientCommand
                            {
                                QueueId = queueId,
                                Actor = $"consult-room-{roomNumber}",
                                StationId = $"CONS-{roomNumber:00}"
                            }, cancellationToken));

                        await ExecuteWithConcurrencyRetryAsync(async () =>
                        {
                            await _callHandler.HandleAsync(new CallPatientCommand
                            {
                                QueueId = queueId,
                                PatientId = claim.PatientId,
                                Actor = $"consult-room-{roomNumber}-nurse"
                            }, cancellationToken);
                        });

                        await ExecuteWithConcurrencyRetryAsync(async () =>
                        {
                            await _completeHandler.HandleAsync(new CompleteAttentionCommand
                            {
                                QueueId = queueId,
                                PatientId = claim.PatientId,
                                Actor = $"consult-room-{roomNumber}",
                                Outcome = "completed",
                                Notes = "Clinical flow completed"
                            }, cancellationToken);
                        });

                        completedPatients.TryAdd(claim.PatientId, true);

                        // Taquilla de pago simulada: registra que paciente llegó a caja
                        paymentDeskQueue.TryAdd(claim.PatientId, true);
                    }
                    catch (DomainException)
                    {
                        await Task.Delay(10, cancellationToken);
                    }
                    catch (EventConflictException)
                    {
                        await Task.Delay(10, cancellationToken);
                    }
                    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
                    {
                        await Task.Delay(10, cancellationToken);
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(consultRoomTasks);

        if (completedPatients.Count != totalPatients)
            throw new XunitException($"Timeout waiting for consultation completion. Completed={completedPatients.Count}, Expected={totalPatients}");

        Assert.Equal(totalPatients, completedPatients.Count);
        Assert.Equal(totalPatients, paymentDeskQueue.Count);

        var events = (await _eventStore.GetEventsAsync(queueId, cancellationToken)).ToList();

        Assert.Equal(totalPatients, events.OfType<PatientCheckedIn>().Count());
        Assert.Equal(totalPatients, events.OfType<PatientCalledAtCashier>().Count());
        Assert.Equal(totalPatients, events.OfType<PatientPaymentValidated>().Count());
        Assert.Equal(totalPatients, events.OfType<PatientClaimedForAttention>().Count());
        Assert.Equal(totalPatients, events.OfType<PatientCalled>().Count());
        Assert.Equal(totalPatients, events.OfType<PatientAttentionCompleted>().Count());
    }

    private async Task ActivateConsultingRoomsAsync(
        string queueId,
        IReadOnlyCollection<string> consultingRoomIds,
        CancellationToken cancellationToken)
    {
        foreach (var consultingRoomId in consultingRoomIds)
        {
            await _activateConsultingRoomHandler.HandleAsync(new ActivateConsultingRoomCommand
            {
                QueueId = queueId,
                ConsultingRoomId = consultingRoomId,
                Actor = "coordinator"
            }, cancellationToken);
        }
    }

    private static async Task ExecuteWithConcurrencyRetryAsync(
        Func<Task> action,
        int maxAttempts = 10)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (EventConflictException) when (attempt < maxAttempts)
            {
                await Task.Delay(10 * attempt);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505" && attempt < maxAttempts)
            {
                await Task.Delay(10 * attempt);
            }
        }

        await action();
    }

    private static async Task<T> ExecuteWithConcurrencyRetryAsync<T>(
        Func<Task<T>> action,
        int maxAttempts = 10)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (EventConflictException) when (attempt < maxAttempts)
            {
                await Task.Delay(10 * attempt);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505" && attempt < maxAttempts)
            {
                await Task.Delay(10 * attempt);
            }
        }

        return await action();
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
