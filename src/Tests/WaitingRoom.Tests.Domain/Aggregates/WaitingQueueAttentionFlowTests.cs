namespace WaitingRoom.Tests.Domain.Aggregates;

using BuildingBlocks.EventSourcing;
using FluentAssertions;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.Exceptions;
using WaitingRoom.Domain.Invariants;
using WaitingRoom.Domain.ValueObjects;
using Xunit;

public sealed class WaitingQueueAttentionFlowTests
{
    [Fact]
    public void CheckInPatient_EntersWaitingCashierState()
    {
        var queue = CreateQueue();

        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        queue.CurrentCashierPatientId.Should().BeNull();
        queue.CurrentCashierState.Should().BeNull();
        queue.Patients.Should().ContainSingle(p => p.PatientId.Value == "P-1");
    }

    [Fact]
    public void CallNextAtCashier_MovesPatientToCashierState()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        var patientId = queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        patientId.Should().Be("P-1");
        queue.CurrentCashierPatientId.Should().Be("P-1");
        queue.CurrentCashierState.Should().Be(WaitingQueueInvariants.CashierCalledState);
    }

    [Fact]
    public void ValidatePayment_ReleasesCashierAndAllowsConsultationQueue()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.ValidatePayment(new ValidatePaymentRequest
        {
            PatientId = PatientId.Create("P-1"),
            ValidatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.CurrentCashierPatientId.Should().BeNull();
        queue.CurrentCashierState.Should().BeNull();
    }

    [Fact]
    public void MarkPaymentPending_SetsPaymentPendingState()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.MarkPaymentPending(new MarkPaymentPendingRequest
        {
            PatientId = PatientId.Create("P-1"),
            PendingAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.CurrentCashierPatientId.Should().Be("P-1");
        queue.CurrentCashierState.Should().Be(WaitingQueueInvariants.PaymentPendingState);
    }

    [Fact]
    public void MarkAbsentAtCashier_RequeuesPatientAndClearsCashierSlot()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.MarkAbsentAtCashier(new MarkAbsentAtCashierRequest
        {
            PatientId = PatientId.Create("P-1"),
            AbsentAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.CurrentCashierPatientId.Should().BeNull();
        queue.CurrentCashierState.Should().BeNull();

        var recalledPatientId = queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        recalledPatientId.Should().Be("P-1");
    }

    [Fact]
    public void ClaimNextPatient_SelectsHighestPriorityFirst()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.Low));
        queue.CheckInPatient(CreateCheckIn("P-2", Priority.Urgent));
        queue.CheckInPatient(CreateCheckIn("P-3", Priority.Medium));

        queue.ActivateConsultingRoom(new ActivateConsultingRoomRequest
        {
            ConsultingRoomId = "S-01",
            ActivatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "coordinator")
        });

        MoveNextFromCashierToConsultation(queue);
        MoveNextFromCashierToConsultation(queue);
        MoveNextFromCashierToConsultation(queue);

        var claimedPatientId = queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1"),
            StationId = "S-01"
        });

        claimedPatientId.Should().Be("P-2");
        queue.CurrentAttentionPatientId.Should().Be("P-2");
        queue.CurrentAttentionState.Should().Be(WaitingQueueInvariants.ClaimedState);
    }

    [Fact]
    public void CallPatient_WithoutClaim_ThrowsDomainException()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        Action act = () => queue.CallPatient(new CallPatientRequest
        {
            PatientId = PatientId.Create("P-1"),
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "nurse-1")
        });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ClaimNextPatient_WithoutValidatedPayment_ThrowsDomainException()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        Action act = () => queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1")
        });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CompleteAttention_RemovesPatientAndClearsActiveAttention()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        MoveNextFromCashierToConsultation(queue);

        queue.ActivateConsultingRoom(new ActivateConsultingRoomRequest
        {
            ConsultingRoomId = "S-01",
            ActivatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "coordinator")
        });

        queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1"),
            StationId = "S-01"
        });

        queue.CallPatient(new CallPatientRequest
        {
            PatientId = PatientId.Create("P-1"),
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "nurse-1")
        });

        queue.CompleteAttention(new CompleteAttentionRequest
        {
            PatientId = PatientId.Create("P-1"),
            CompletedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1"),
            Outcome = "resolved"
        });

        queue.CurrentAttentionPatientId.Should().BeNull();
        queue.CurrentAttentionState.Should().BeNull();
        queue.Patients.Should().BeEmpty();
    }

    [Fact]
    public void CancelByPayment_RequiresThreePaymentAttempts()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));

        queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.MarkPaymentPending(new MarkPaymentPendingRequest
        {
            PatientId = PatientId.Create("P-1"),
            PendingAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.MarkPaymentPending(new MarkPaymentPendingRequest
        {
            PatientId = PatientId.Create("P-1"),
            PendingAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.MarkPaymentPending(new MarkPaymentPendingRequest
        {
            PatientId = PatientId.Create("P-1"),
            PendingAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.CancelByPayment(new CancelByPaymentRequest
        {
            PatientId = PatientId.Create("P-1"),
            CancelledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        queue.Patients.Should().BeEmpty();
        queue.CurrentCashierPatientId.Should().BeNull();
    }

    [Fact]
    public void MarkAbsentAtConsultation_SecondAbsenceCancelsPatient()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));
        MoveNextFromCashierToConsultation(queue);

        queue.ActivateConsultingRoom(new ActivateConsultingRoomRequest
        {
            ConsultingRoomId = "S-01",
            ActivatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "coordinator")
        });

        queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1"),
            StationId = "S-01"
        });

        queue.MarkAbsentAtConsultation(new MarkAbsentAtConsultationRequest
        {
            PatientId = PatientId.Create("P-1"),
            AbsentAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1")
        });

        queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1"),
            StationId = "S-01"
        });

        queue.MarkAbsentAtConsultation(new MarkAbsentAtConsultationRequest
        {
            PatientId = PatientId.Create("P-1"),
            AbsentAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1")
        });

        queue.Patients.Should().BeEmpty();
        queue.CurrentAttentionPatientId.Should().BeNull();
    }

    [Fact]
    public void ClaimNextPatient_WhenConsultingRoomIsInactive_ThrowsDomainException()
    {
        var queue = CreateQueue();
        queue.CheckInPatient(CreateCheckIn("P-1", Priority.High));
        MoveNextFromCashierToConsultation(queue);

        Action act = () => queue.ClaimNextPatient(new ClaimNextPatientRequest
        {
            ClaimedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "doctor-1"),
            StationId = "CONS-01"
        });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ActivateAndDeactivateConsultingRoom_UpdatesActiveRooms()
    {
        var queue = CreateQueue();

        queue.ActivateConsultingRoom(new ActivateConsultingRoomRequest
        {
            ConsultingRoomId = "CONS-01",
            ActivatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "coordinator")
        });

        queue.ActiveConsultingRooms.Should().Contain("CONS-01");

        queue.DeactivateConsultingRoom(new DeactivateConsultingRoomRequest
        {
            ConsultingRoomId = "CONS-01",
            DeactivatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "coordinator")
        });

        queue.ActiveConsultingRooms.Should().NotContain("CONS-01");
    }

    private static WaitingQueue CreateQueue()
    {
        var queue = WaitingQueue.Create("QUEUE-1", "Main", 20, EventMetadata.CreateNew("QUEUE-1", "system"));
        queue.ClearUncommittedEvents();
        return queue;
    }

    private static CheckInPatientRequest CreateCheckIn(string patientId, string priority)
        => new()
        {
            PatientId = PatientId.Create(patientId),
            PatientName = $"Patient {patientId}",
            Priority = Priority.Create(priority),
            ConsultationType = ConsultationType.Create("General"),
            CheckInTime = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew("QUEUE-1", "reception")
        };

    private static void MoveNextFromCashierToConsultation(WaitingQueue queue)
    {
        queue.CallNextAtCashier(new CallNextCashierRequest
        {
            CalledAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });

        var activeCashierPatientId = queue.CurrentCashierPatientId!;

        queue.ValidatePayment(new ValidatePaymentRequest
        {
            PatientId = PatientId.Create(activeCashierPatientId),
            ValidatedAt = DateTime.UtcNow,
            Metadata = EventMetadata.CreateNew(queue.Id, "cashier-1")
        });
    }
}
