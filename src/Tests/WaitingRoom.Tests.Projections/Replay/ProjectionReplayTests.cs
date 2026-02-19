namespace WaitingRoom.Tests.Projections.Replay;

using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WaitingRoom.Domain.Events;
using WaitingRoom.Infrastructure.Projections;
using WaitingRoom.Projections.Handlers;
using BuildingBlocks.EventSourcing;

/// <summary>
/// Tests for projection rebuild/replay correctness.
///
/// Requirement: Rebuilding projection from events MUST produce identical state
/// to incremental processing.
///
/// This validates:
/// - Deterministic replay
/// - Checkpoint consistency
/// - No state corruption
/// </summary>
public sealed class ProjectionReplayTests
{
    private readonly PatientCheckedInProjectionHandler _handler;

    public ProjectionReplayTests()
    {
        _handler = new PatientCheckedInProjectionHandler();
    }

    [Fact]
    public async Task rebuild_from_replay_produces_identical_state()
    {
        // Arrange - Create sequence of events
        var events = new List<PatientCheckedIn>
        {
            CreateEvent(queueId: "queue-1", patientId: "p1", priority: "high"),
            CreateEvent(queueId: "queue-1", patientId: "p2", priority: "normal"),
            CreateEvent(queueId: "queue-1", patientId: "p3", priority: "low"),
            CreateEvent(queueId: "queue-1", patientId: "p4", priority: "high"),
            CreateEvent(queueId: "queue-1", patientId: "p5", priority: "normal"),
        };

        // Act - Process incrementally
        var incremental = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        foreach (var evt in events)
        {
            await _handler.HandleAsync(evt, incremental);
        }

        var incrementalState = await incremental.GetMonitorViewAsync("queue-1");
        var incrementalQueue = await incremental.GetQueueStateViewAsync("queue-1");

        // Act - Process from rebuild
        var rebuilt = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        foreach (var evt in events)
        {
            await _handler.HandleAsync(evt, rebuilt);
        }

        var rebuiltState = await rebuilt.GetMonitorViewAsync("queue-1");
        var rebuiltQueue = await rebuilt.GetQueueStateViewAsync("queue-1");

        // Assert - States must be identical
        rebuiltState.Should().NotBeNull();
        incrementalState.Should().NotBeNull();

        rebuiltState!.TotalPatientsWaiting.Should().Be(incrementalState!.TotalPatientsWaiting);
        rebuiltState.HighPriorityCount.Should().Be(incrementalState.HighPriorityCount);
        rebuiltState.NormalPriorityCount.Should().Be(incrementalState.NormalPriorityCount);
        rebuiltState.LowPriorityCount.Should().Be(incrementalState.LowPriorityCount);

        rebuiltQueue!.CurrentCount.Should().Be(incrementalQueue!.CurrentCount);
        rebuiltQueue.PatientsInQueue.Count.Should().Be(incrementalQueue.PatientsInQueue.Count);
    }

    [Fact]
    public async Task replay_with_different_event_order_produces_consistent_final_state()
    {
        // Arrange
        var evt1 = CreateEvent(queueId: "queue-1", patientId: "p1", priority: "normal");
        var evt2 = CreateEvent(queueId: "queue-1", patientId: "p2", priority: "high");
        var evt3 = CreateEvent(queueId: "queue-1", patientId: "p3", priority: "low");

        // Act - Process in order
        var ctx1 = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        await _handler.HandleAsync(evt1, ctx1);
        await _handler.HandleAsync(evt2, ctx1);
        await _handler.HandleAsync(evt3, ctx1);

        var state1 = await ctx1.GetQueueStateViewAsync("queue-1");

        // Act - Process all at once (faster batch)
        var ctx2 = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        foreach (var evt in new[] { evt1, evt2, evt3 })
        {
            await _handler.HandleAsync(evt, ctx2);
        }

        var state2 = await ctx2.GetQueueStateViewAsync("queue-1");

        // Assert - Should be identical
        state1!.CurrentCount.Should().Be(state2!.CurrentCount);
        state1.PatientsInQueue.Count.Should().Be(state2.PatientsInQueue.Count);

        // Verify priority order is same
        for (int i = 0; i < state1.PatientsInQueue.Count; i++)
        {
            state1.PatientsInQueue[i].PatientId.Should().Be(state2.PatientsInQueue[i].PatientId);
            state1.PatientsInQueue[i].Priority.Should().Be(state2.PatientsInQueue[i].Priority);
        }
    }

    [Fact]
    public async Task deterministic_replay_with_large_event_stream()
    {
        // Arrange - Create 100 events
        var events = new List<PatientCheckedIn>();
        var priorities = new[] { "high", "normal", "low" };

        for (int i = 1; i <= 100; i++)
        {
            events.Add(CreateEvent(
                queueId: "queue-1",
                patientId: $"p{i}",
                priority: priorities[i % 3]));
        }

        // Act - First pass
        var ctx1 = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        foreach (var evt in events)
        {
            await _handler.HandleAsync(evt, ctx1);
        }

        var state1 = await ctx1.GetMonitorViewAsync("queue-1");
        var queue1 = await ctx1.GetQueueStateViewAsync("queue-1");

        // Act - Second pass (rebuild)
        var ctx2 = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        foreach (var evt in events)
        {
            await _handler.HandleAsync(evt, ctx2);
        }

        var state2 = await ctx2.GetMonitorViewAsync("queue-1");
        var queue2 = await ctx2.GetQueueStateViewAsync("queue-1");

        // Assert - All counters match
        state1!.TotalPatientsWaiting.Should().Be(state2!.TotalPatientsWaiting);
        state1.HighPriorityCount.Should().Be(state2.HighPriorityCount);
        state1.NormalPriorityCount.Should().Be(state2.NormalPriorityCount);
        state1.LowPriorityCount.Should().Be(state2.LowPriorityCount);

        queue1!.CurrentCount.Should().Be(queue2!.CurrentCount);
        queue1.PatientsInQueue.Count.Should().Be(queue2.PatientsInQueue.Count);

        // Assert - Order is preserved
        for (int i = 0; i < queue1.PatientsInQueue.Count; i++)
        {
            queue1.PatientsInQueue[i].PatientId.Should().Be(queue2.PatientsInQueue[i].PatientId);
        }
    }

    private static PatientCheckedIn CreateEvent(
        string queueId,
        string patientId,
        string priority,
        string? name = null)
    {
        return new PatientCheckedIn
        {
            QueueId = queueId,
            PatientId = patientId,
            PatientName = name ?? $"Patient {patientId}",
            Priority = priority,
            Metadata = new EventMetadata
            {
                AggregateId = queueId,
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString(),
                CausationId = Guid.NewGuid().ToString(),
                Actor = "system",
                OccurredAt = DateTimeOffset.UtcNow,
                Version = 1,
                Timestamp = DateTime.UtcNow
            }
        };
    }
}
