namespace WaitingRoom.Tests.Projections.Replay;

using BuildingBlocks.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WaitingRoom.Domain.Events;
using WaitingRoom.Infrastructure.Projections;
using WaitingRoom.Projections.Handlers;
using Xunit;

public sealed class AttentionWorkflowProjectionTests
{
    [Fact]
    public async Task ClaimCallComplete_UpdatesNextTurnAndHistory()
    {
        var context = new InMemoryWaitingRoomProjectionContext(new NullLogger<InMemoryWaitingRoomProjectionContext>());
        var checkedInHandler = new PatientCheckedInProjectionHandler();
        var cashierCalledHandler = new PatientCalledAtCashierProjectionHandler();
        var paymentValidatedHandler = new PatientPaymentValidatedProjectionHandler();
        var claimedHandler = new PatientClaimedForAttentionProjectionHandler();
        var calledHandler = new PatientCalledProjectionHandler();
        var completedHandler = new PatientAttentionCompletedProjectionHandler();

        var queueId = "queue-1";
        var patientId = "p-1";

        await checkedInHandler.HandleAsync(CreateCheckedIn(queueId, patientId), context);
        await cashierCalledHandler.HandleAsync(CreateCashierCalled(queueId, patientId), context);
        await paymentValidatedHandler.HandleAsync(CreatePaymentValidated(queueId, patientId), context);
        await claimedHandler.HandleAsync(CreateClaimed(queueId, patientId), context);
        await calledHandler.HandleAsync(CreateCalled(queueId, patientId), context);

        var nextTurn = await context.GetNextTurnViewAsync(queueId);
        nextTurn.Should().NotBeNull();
        nextTurn!.Status.Should().Be("called");

        await completedHandler.HandleAsync(CreateCompleted(queueId, patientId), context);

        var nextAfterCompletion = await context.GetNextTurnViewAsync(queueId);
        nextAfterCompletion.Should().BeNull();

        var history = await context.GetRecentAttentionHistoryAsync(queueId, 10);
        history.Should().HaveCount(1);
        history[0].PatientId.Should().Be(patientId);
    }

    private static PatientCheckedIn CreateCheckedIn(string queueId, string patientId) => new()
    {
        QueueId = queueId,
        PatientId = patientId,
        PatientName = "Patient",
        Priority = "high",
        ConsultationType = "General",
        QueuePosition = 0,
        CheckInTime = DateTime.UtcNow,
        Metadata = NewMetadata(queueId)
    };

    private static PatientClaimedForAttention CreateClaimed(string queueId, string patientId) => new()
    {
        QueueId = queueId,
        PatientId = patientId,
        PatientName = "Patient",
        Priority = "high",
        ConsultationType = "General",
        ClaimedAt = DateTime.UtcNow,
        Metadata = NewMetadata(queueId)
    };

    private static PatientCalledAtCashier CreateCashierCalled(string queueId, string patientId) => new()
    {
        QueueId = queueId,
        PatientId = patientId,
        PatientName = "Patient",
        Priority = "high",
        ConsultationType = "General",
        CalledAt = DateTime.UtcNow,
        Metadata = NewMetadata(queueId)
    };

    private static PatientPaymentValidated CreatePaymentValidated(string queueId, string patientId) => new()
    {
        QueueId = queueId,
        PatientId = patientId,
        PatientName = "Patient",
        Priority = "high",
        ConsultationType = "General",
        ValidatedAt = DateTime.UtcNow,
        Metadata = NewMetadata(queueId)
    };

    private static PatientCalled CreateCalled(string queueId, string patientId) => new()
    {
        QueueId = queueId,
        PatientId = patientId,
        CalledAt = DateTime.UtcNow,
        Metadata = NewMetadata(queueId)
    };

    private static PatientAttentionCompleted CreateCompleted(string queueId, string patientId) => new()
    {
        QueueId = queueId,
        PatientId = patientId,
        PatientName = "Patient",
        Priority = "high",
        ConsultationType = "General",
        CompletedAt = DateTime.UtcNow,
        Metadata = NewMetadata(queueId)
    };

    private static EventMetadata NewMetadata(string aggregateId) => new()
    {
        AggregateId = aggregateId,
        EventId = Guid.NewGuid().ToString(),
        CorrelationId = Guid.NewGuid().ToString(),
        CausationId = Guid.NewGuid().ToString(),
        Actor = "system",
        IdempotencyKey = Guid.NewGuid().ToString(),
        OccurredAt = DateTime.UtcNow,
        Version = 1,
        SchemaVersion = 1
    };
}
