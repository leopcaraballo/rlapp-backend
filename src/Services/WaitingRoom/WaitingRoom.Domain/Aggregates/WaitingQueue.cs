namespace WaitingRoom.Domain.Aggregates;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.Events;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Entities;
using WaitingRoom.Domain.Invariants;
using WaitingRoom.Domain.Exceptions;

/// <summary>
/// Aggregate Root for the WaitingRoom domain.
///
/// Responsibilities:
/// - Protect waiting room invariants
/// - Generate PatientCheckedIn events
/// - Reconstruct state from event history
///
/// Invariants enforced:
/// - Queue never exceeds capacity
/// - No duplicate patient check-ins
/// - Valid priorities only
/// - Queue maintains patient order
///
/// This is pure domain logic with NO infrastructure dependencies.
/// </summary>
public sealed class WaitingQueue : AggregateRoot
{
    /// <summary>
    /// Unique name/identifier for the queue.
    /// </summary>
    public string QueueName { get; private set; } = string.Empty;

    /// <summary>
    /// Maximum patients allowed in queue simultaneously.
    /// </summary>
    public int MaxCapacity { get; private set; }

    /// <summary>
    /// Current patients in the waiting queue (ordered).
    /// </summary>
    public List<WaitingPatient> Patients { get; private set; } = [];
    public string? CurrentCashierPatientId { get; private set; }
    public string? CurrentCashierState { get; private set; }
    public string? CurrentAttentionPatientId { get; private set; }
    public string? CurrentAttentionState { get; private set; }
    private readonly Dictionary<string, string> _patientStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _paymentAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _cashierAbsenceRetries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _consultationAbsenceRetries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeConsultingRooms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When queue was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// When queue was last updated.
    /// </summary>
    public DateTime LastModifiedAt { get; private set; }

    private WaitingQueue() { }

    /// <summary>
    /// Creates a new waiting queue with initial capacity and metadata.
    /// </summary>
    /// <param name="queueId">Unique queue identifier.</param>
    /// <param name="queueName">Human-readable queue name.</param>
    /// <param name="maxCapacity">Maximum patients allowed.</param>
    /// <param name="metadata">Event metadata for traceability.</param>
    /// <returns>New WaitingQueue aggregate.</returns>
    public static WaitingQueue Create(
        string queueId,
        string queueName,
        int maxCapacity,
        EventMetadata metadata)
    {
        WaitingQueueInvariants.ValidateQueueName(queueName);

        if (maxCapacity <= 0)
            throw new DomainException("MaxCapacity must be greater than 0");

        var queue = new WaitingQueue();

        var @event = new WaitingQueueCreated
        {
            Metadata = metadata.WithVersion(queue.Version + 1),
            QueueId = queueId,
            QueueName = queueName,
            MaxCapacity = maxCapacity,
            CreatedAt = metadata.OccurredAt
        };

        queue.RaiseEvent(@event);
        return queue;
    }

    /// <summary>
    /// Core use case: Patient checks into waiting room.
    ///
    /// Refactored to use Parameter Object pattern.
    /// This eliminates parameter cascading (was 7 params, now 1).
    ///
    /// Business logic:
    /// 1. Validate queue hasn't reached capacity
    /// 2. Validate patient isn't already checked in
    /// 3. Validate priority and consultation type
    /// 4. Assign position based on priority
    /// 5. Emit PatientCheckedIn event
    /// 6. Apply event to state (idempotent)
    ///
    /// Pattern: Command Object / Parameter Object
    /// Benefit: Extensible without breaking existing signatures
    /// </summary>
    /// <param name="request">Complete check-in request encapsulating all parameters.</param>
    /// <exception cref="DomainException">If any invariant violated.</exception>
    public void CheckInPatient(CheckInPatientRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        // Validate invariants
        WaitingQueueInvariants.ValidateCapacity(Patients.Count, MaxCapacity);
        WaitingQueueInvariants.ValidateDuplicateCheckIn(request.PatientId.Value, Patients.Select(p => p.PatientId.Value));
        WaitingQueueInvariants.ValidatePriority(request.Priority.Value);

        // Calculate queue position (could be priority-based in future)
        int queuePosition = Patients.Count;

        // Create and raise event
        var @event = new PatientCheckedIn
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = request.PatientId.Value,
            PatientName = request.PatientName,
            Priority = request.Priority.Value,
            ConsultationType = request.ConsultationType.Value,
            CheckInTime = request.CheckInTime,
            QueuePosition = queuePosition,
            Notes = request.Notes
        };

        RaiseEvent(@event);
    }

    public string ClaimNextPatient(ClaimNextPatientRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateConsultingRoomId(request.StationId ?? string.Empty);
        WaitingQueueInvariants.ValidateConsultingRoomActive(
            _activeConsultingRooms.Contains(request.StationId!),
            request.StationId!);

        WaitingQueueInvariants.ValidateQueueHasPatients(Patients.Count);
        WaitingQueueInvariants.ValidateNoActiveAttention(CurrentAttentionPatientId);

        var nextPatient = Patients
            .OrderByDescending(p => p.Priority.Level)
            .ThenBy(p => p.CheckInTime)
            .FirstOrDefault(p => IsPatientInState(p.PatientId.Value, WaitingQueueInvariants.WaitingConsultationState));

        if (nextPatient is null)
            throw new DomainException("No patient in waiting state is available for claim");

        var @event = new PatientClaimedForAttention
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = nextPatient.PatientId.Value,
            PatientName = nextPatient.PatientName,
            Priority = nextPatient.Priority.Value,
            ConsultationType = nextPatient.ConsultationType.Value,
            ClaimedAt = request.ClaimedAt,
            StationId = request.StationId
        };

        RaiseEvent(@event);
        return nextPatient.PatientId.Value;
    }

    public void ActivateConsultingRoom(ActivateConsultingRoomRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateConsultingRoomId(request.ConsultingRoomId);
        WaitingQueueInvariants.ValidateConsultingRoomCanActivate(
            _activeConsultingRooms.Contains(request.ConsultingRoomId),
            request.ConsultingRoomId);

        var @event = new ConsultingRoomActivated
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            ConsultingRoomId = request.ConsultingRoomId,
            ActivatedAt = request.ActivatedAt
        };

        RaiseEvent(@event);
    }

    public void DeactivateConsultingRoom(DeactivateConsultingRoomRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateConsultingRoomId(request.ConsultingRoomId);
        WaitingQueueInvariants.ValidateConsultingRoomCanDeactivate(
            _activeConsultingRooms.Contains(request.ConsultingRoomId),
            request.ConsultingRoomId);

        var @event = new ConsultingRoomDeactivated
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            ConsultingRoomId = request.ConsultingRoomId,
            DeactivatedAt = request.DeactivatedAt
        };

        RaiseEvent(@event);
    }

    public string CallNextAtCashier(CallNextCashierRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateQueueHasPatients(Patients.Count);
        WaitingQueueInvariants.ValidateNoActiveCashier(CurrentCashierPatientId);

        var nextPatient = Patients
            .OrderByDescending(p => p.Priority.Level)
            .ThenBy(p => p.CheckInTime)
            .FirstOrDefault(p => IsPatientInState(p.PatientId.Value, WaitingQueueInvariants.WaitingCashierState));

        if (nextPatient is null)
            throw new DomainException("No patient in waiting-cashier state is available for call");

        var @event = new PatientCalledAtCashier
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = nextPatient.PatientId.Value,
            PatientName = nextPatient.PatientName,
            Priority = nextPatient.Priority.Value,
            ConsultationType = nextPatient.ConsultationType.Value,
            CalledAt = request.CalledAt,
            CashierDeskId = request.CashierDeskId
        };

        RaiseEvent(@event);
        return nextPatient.PatientId.Value;
    }

    public void ValidatePayment(ValidatePaymentRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentCashier(CurrentCashierPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        WaitingQueueInvariants.ValidateStateTransition(
            currentState,
            WaitingQueueInvariants.CashierCalledState,
            "validate-payment");

        var patient = Patients.FirstOrDefault(p =>
            string.Equals(p.PatientId.Value, request.PatientId.Value, StringComparison.OrdinalIgnoreCase));

        if (patient is null)
            throw new DomainException($"Patient {request.PatientId.Value} not found in queue");

        var @event = new PatientPaymentValidated
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = patient.PatientId.Value,
            PatientName = patient.PatientName,
            Priority = patient.Priority.Value,
            ConsultationType = patient.ConsultationType.Value,
            ValidatedAt = request.ValidatedAt,
            PaymentReference = request.PaymentReference
        };

        RaiseEvent(@event);
    }

    public void MarkPaymentPending(MarkPaymentPendingRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentCashier(CurrentCashierPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        if (!string.Equals(currentState, WaitingQueueInvariants.CashierCalledState, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(currentState, WaitingQueueInvariants.PaymentPendingState, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Invalid state transition for mark-payment-pending. Expected '{WaitingQueueInvariants.CashierCalledState}' or '{WaitingQueueInvariants.PaymentPendingState}' but was '{currentState ?? "none"}'");
        }

        var patient = Patients.FirstOrDefault(p =>
            string.Equals(p.PatientId.Value, request.PatientId.Value, StringComparison.OrdinalIgnoreCase));

        if (patient is null)
            throw new DomainException($"Patient {request.PatientId.Value} not found in queue");

        var attemptNumber = GetPaymentAttempts(request.PatientId.Value) + 1;
        if (attemptNumber > WaitingQueueInvariants.MaxPaymentAttempts)
            throw new DomainException($"Payment attempts exceeded maximum of {WaitingQueueInvariants.MaxPaymentAttempts}");

        var @event = new PatientPaymentPending
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = patient.PatientId.Value,
            PatientName = patient.PatientName,
            Priority = patient.Priority.Value,
            ConsultationType = patient.ConsultationType.Value,
            PendingAt = request.PendingAt,
            AttemptNumber = attemptNumber,
            Reason = request.Reason
        };

        RaiseEvent(@event);
    }

    public void MarkAbsentAtCashier(MarkAbsentAtCashierRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentCashier(CurrentCashierPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        if (!string.Equals(currentState, WaitingQueueInvariants.CashierCalledState, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(currentState, WaitingQueueInvariants.PaymentPendingState, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Invalid state transition for mark-absent-cashier. Expected '{WaitingQueueInvariants.CashierCalledState}' or '{WaitingQueueInvariants.PaymentPendingState}' but was '{currentState ?? "none"}'");
        }

        var retryNumber = GetCashierAbsenceRetries(request.PatientId.Value) + 1;
        if (retryNumber > WaitingQueueInvariants.MaxCashierAbsenceRetries)
            throw new DomainException($"Cashier absence retries exceeded maximum of {WaitingQueueInvariants.MaxCashierAbsenceRetries}");

        var @event = new PatientAbsentAtCashier
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = request.PatientId.Value,
            AbsentAt = request.AbsentAt,
            RetryNumber = retryNumber
        };

        RaiseEvent(@event);
    }

    public void CancelByPayment(CancelByPaymentRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentCashier(CurrentCashierPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        if (!string.Equals(currentState, WaitingQueueInvariants.CashierCalledState, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(currentState, WaitingQueueInvariants.PaymentPendingState, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Invalid state transition for cancel-by-payment. Expected '{WaitingQueueInvariants.CashierCalledState}' or '{WaitingQueueInvariants.PaymentPendingState}' but was '{currentState ?? "none"}'");
        }

        var attempts = GetPaymentAttempts(request.PatientId.Value);
        if (attempts < WaitingQueueInvariants.MaxPaymentAttempts)
            throw new DomainException($"Cannot cancel by payment before reaching {WaitingQueueInvariants.MaxPaymentAttempts} payment attempts");

        var @event = new PatientCancelledByPayment
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = request.PatientId.Value,
            CancelledAt = request.CancelledAt,
            Reason = request.Reason
        };

        RaiseEvent(@event);
    }

    public void CallPatient(CallPatientRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentAttention(CurrentAttentionPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        WaitingQueueInvariants.ValidateStateTransition(
            currentState,
            WaitingQueueInvariants.ClaimedState,
            "call-patient");

        var @event = new PatientCalled
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = request.PatientId.Value,
            CalledAt = request.CalledAt
        };

        RaiseEvent(@event);
    }

    public void CompleteAttention(CompleteAttentionRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentAttention(CurrentAttentionPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        WaitingQueueInvariants.ValidateStateTransition(
            currentState,
            WaitingQueueInvariants.CalledState,
            "complete-attention");

        var patient = Patients.FirstOrDefault(p =>
            string.Equals(p.PatientId.Value, request.PatientId.Value, StringComparison.OrdinalIgnoreCase));

        if (patient is null)
            throw new DomainException($"Patient {request.PatientId.Value} not found in queue");

        var @event = new PatientAttentionCompleted
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = patient.PatientId.Value,
            PatientName = patient.PatientName,
            Priority = patient.Priority.Value,
            ConsultationType = patient.ConsultationType.Value,
            CompletedAt = request.CompletedAt,
            Outcome = request.Outcome,
            Notes = request.Notes
        };

        RaiseEvent(@event);
    }

    public void MarkAbsentAtConsultation(MarkAbsentAtConsultationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        WaitingQueueInvariants.ValidateCurrentAttention(CurrentAttentionPatientId, request.PatientId.Value);

        var currentState = GetPatientState(request.PatientId.Value);
        WaitingQueueInvariants.ValidateStateTransition(
            currentState,
            WaitingQueueInvariants.ClaimedState,
            "mark-absent-consultation");

        var retryNumber = GetConsultationAbsenceRetries(request.PatientId.Value) + 1;

        if (retryNumber <= WaitingQueueInvariants.MaxConsultationAbsenceRetries)
        {
            var absentEvent = new PatientAbsentAtConsultation
            {
                Metadata = request.Metadata.WithVersion(Version + 1),
                QueueId = Id,
                PatientId = request.PatientId.Value,
                AbsentAt = request.AbsentAt,
                RetryNumber = retryNumber
            };

            RaiseEvent(absentEvent);
            return;
        }

        var cancelledEvent = new PatientCancelledByAbsence
        {
            Metadata = request.Metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = request.PatientId.Value,
            CancelledAt = request.AbsentAt,
            TotalAbsences = retryNumber
        };

        RaiseEvent(cancelledEvent);
    }

    /// <summary>
    /// Event handler: Apply PatientCheckedIn event to state.
    /// This method is invoked via reflection by the AggregateRoot base class.
    ///
    /// Must be idempotent: applying same event twice = same state.
    /// </summary>
    private void When(PatientCheckedIn @event)
    {
        var patient = new WaitingPatient(
            patientId: PatientId.Create(@event.PatientId),
            patientName: @event.PatientName,
            priority: Priority.Create(@event.Priority),
            consultationType: ConsultationType.Create(@event.ConsultationType),
            checkInTime: @event.CheckInTime,
            queuePosition: @event.QueuePosition,
            notes: @event.Notes
        );

        Patients.Add(patient);
        _patientStates[@event.PatientId] = WaitingQueueInvariants.WaitingCashierState;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientCalledAtCashier @event)
    {
        CurrentCashierPatientId = @event.PatientId;
        CurrentCashierState = WaitingQueueInvariants.CashierCalledState;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.CashierCalledState;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientPaymentValidated @event)
    {
        _patientStates[@event.PatientId] = WaitingQueueInvariants.PaymentValidatedState;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.WaitingConsultationState;
        _paymentAttempts.Remove(@event.PatientId);
        _cashierAbsenceRetries.Remove(@event.PatientId);
        CurrentCashierPatientId = null;
        CurrentCashierState = null;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientPaymentPending @event)
    {
        _paymentAttempts[@event.PatientId] = @event.AttemptNumber;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.PaymentPendingState;
        CurrentCashierPatientId = @event.PatientId;
        CurrentCashierState = WaitingQueueInvariants.PaymentPendingState;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientAbsentAtCashier @event)
    {
        _cashierAbsenceRetries[@event.PatientId] = @event.RetryNumber;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.CashierAbsentState;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.WaitingCashierState;
        CurrentCashierPatientId = null;
        CurrentCashierState = null;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientCancelledByPayment @event)
    {
        Patients.RemoveAll(p => string.Equals(p.PatientId.Value, @event.PatientId, StringComparison.OrdinalIgnoreCase));
        _patientStates[@event.PatientId] = WaitingQueueInvariants.CancelledByPaymentState;
        _paymentAttempts.Remove(@event.PatientId);
        _cashierAbsenceRetries.Remove(@event.PatientId);
        _consultationAbsenceRetries.Remove(@event.PatientId);
        CurrentCashierPatientId = null;
        CurrentCashierState = null;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientClaimedForAttention @event)
    {
        CurrentAttentionPatientId = @event.PatientId;
        CurrentAttentionState = WaitingQueueInvariants.ClaimedState;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.ClaimedState;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientAbsentAtConsultation @event)
    {
        _consultationAbsenceRetries[@event.PatientId] = @event.RetryNumber;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.ConsultationAbsentState;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.WaitingConsultationState;
        CurrentAttentionPatientId = null;
        CurrentAttentionState = null;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientCalled @event)
    {
        CurrentAttentionPatientId = @event.PatientId;
        CurrentAttentionState = WaitingQueueInvariants.CalledState;
        _patientStates[@event.PatientId] = WaitingQueueInvariants.CalledState;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientAttentionCompleted @event)
    {
        Patients.RemoveAll(p => string.Equals(p.PatientId.Value, @event.PatientId, StringComparison.OrdinalIgnoreCase));
        _patientStates[@event.PatientId] = WaitingQueueInvariants.CompletedState;
        _consultationAbsenceRetries.Remove(@event.PatientId);
        _paymentAttempts.Remove(@event.PatientId);
        _cashierAbsenceRetries.Remove(@event.PatientId);
        CurrentAttentionPatientId = null;
        CurrentAttentionState = null;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(PatientCancelledByAbsence @event)
    {
        Patients.RemoveAll(p => string.Equals(p.PatientId.Value, @event.PatientId, StringComparison.OrdinalIgnoreCase));
        _patientStates[@event.PatientId] = WaitingQueueInvariants.CancelledByAbsenceState;
        _consultationAbsenceRetries.Remove(@event.PatientId);
        _paymentAttempts.Remove(@event.PatientId);
        _cashierAbsenceRetries.Remove(@event.PatientId);
        CurrentAttentionPatientId = null;
        CurrentAttentionState = null;
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(ConsultingRoomActivated @event)
    {
        _activeConsultingRooms.Add(@event.ConsultingRoomId);
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(ConsultingRoomDeactivated @event)
    {
        _activeConsultingRooms.Remove(@event.ConsultingRoomId);
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    private void When(WaitingQueueCreated @event)
    {
        Id = @event.QueueId;
        QueueName = @event.QueueName;
        MaxCapacity = @event.MaxCapacity;
        CreatedAt = @event.CreatedAt;
        LastModifiedAt = @event.CreatedAt;
    }

    /// <summary>
    /// Gets count of patients currently in queue.
    /// </summary>
    public int CurrentCount => Patients.Count;

    /// <summary>
    /// Returns available capacity.
    /// </summary>
    public int AvailableCapacity => MaxCapacity - CurrentCount;

    /// <summary>
    /// Checks if queue is at capacity.
    /// </summary>
    public bool IsAtCapacity => CurrentCount >= MaxCapacity;

    public IReadOnlyCollection<string> ActiveConsultingRooms => _activeConsultingRooms.ToList().AsReadOnly();

    private string? GetPatientState(string patientId)
        => _patientStates.TryGetValue(patientId, out var state)
            ? state
            : null;

    private bool IsPatientInState(string patientId, string expectedState)
        => string.Equals(GetPatientState(patientId), expectedState, StringComparison.OrdinalIgnoreCase);

    private int GetPaymentAttempts(string patientId)
        => _paymentAttempts.TryGetValue(patientId, out var attempts) ? attempts : 0;

    private int GetCashierAbsenceRetries(string patientId)
        => _cashierAbsenceRetries.TryGetValue(patientId, out var retries) ? retries : 0;

    private int GetConsultationAbsenceRetries(string patientId)
        => _consultationAbsenceRetries.TryGetValue(patientId, out var retries) ? retries : 0;
}
