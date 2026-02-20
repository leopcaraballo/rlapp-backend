namespace WaitingRoom.Domain.Invariants;

using Exceptions;

/// <summary>
/// Domain invariants for the WaitingRoom aggregate.
/// These rules must always be true and are enforced by the aggregate.
///
/// Invariants:
/// 1. Queue cannot exceed maximum capacity
/// 2. Patient cannot check in twice
/// 3. Queue must maintain patient list order
/// 4. Priority must be valid
/// </summary>
public static class WaitingQueueInvariants
{
    public const string RegisteredState = "Registrado";
    public const string WaitingCashierState = "EnEsperaTaquilla";
    public const string CashierCalledState = "EnTaquilla";
    public const string PaymentValidatedState = "PagoValidado";
    public const string PaymentPendingState = "PagoPendiente";
    public const string CashierAbsentState = "AusenteTaquilla";
    public const string CancelledByPaymentState = "CanceladoPorPago";
    public const string WaitingConsultationState = "EnEsperaConsulta";
    public const string ClaimedState = "LlamadoConsulta";
    public const string ConsultationAbsentState = "AusenteConsulta";
    public const string CalledState = "EnConsulta";
    public const string CompletedState = "Finalizado";
    public const string CancelledByAbsenceState = "CanceladoPorAusencia";

    public const int MaxCashierAbsenceRetries = 2;
    public const int MaxPaymentAttempts = 3;
    public const int MaxConsultationAbsenceRetries = 1;

    /// <summary>
    /// Validates that queue hasn't reached capacity.
    /// </summary>
    public static void ValidateCapacity(int currentCount, int maxCapacity)
    {
        if (currentCount >= maxCapacity)
            throw new DomainException(
                $"Queue is at maximum capacity ({maxCapacity}). Cannot add more patients.");
    }

    /// <summary>
    /// Validates that patient is not already in queue.
    /// </summary>
    public static void ValidateDuplicateCheckIn(
        string patientId,
        IEnumerable<string> queuedPatientIds)
    {
        if (queuedPatientIds.Contains(patientId))
            throw new DomainException(
                $"Patient {patientId} is already in the queue.");
    }

    /// <summary>
    /// Validates priority is valid (done at value object level, but enforced here too).
    /// </summary>
    public static void ValidatePriority(string priority)
    {
        var validPriorities = new[] { "low", "medium", "high", "urgent" };
        var normalized = priority.Trim().ToLowerInvariant();

        if (!validPriorities.Contains(normalized))
            throw new DomainException($"Invalid priority: {priority}");
    }

    /// <summary>
    /// Validates queue name is not empty.
    /// </summary>
    public static void ValidateQueueName(string queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new DomainException("Queue name cannot be empty");
    }

    public static void ValidateQueueHasPatients(int currentCount)
    {
        if (currentCount <= 0)
            throw new DomainException("No patients available in queue");
    }

    public static void ValidateNoActiveAttention(string? currentAttentionPatientId)
    {
        if (!string.IsNullOrWhiteSpace(currentAttentionPatientId))
            throw new DomainException("There is already a patient in active attention");
    }

    public static void ValidateNoActiveCashier(string? currentCashierPatientId)
    {
        if (!string.IsNullOrWhiteSpace(currentCashierPatientId))
            throw new DomainException("There is already a patient in active cashier processing");
    }

    public static void ValidateCurrentAttention(string? currentAttentionPatientId, string patientId)
    {
        if (string.IsNullOrWhiteSpace(currentAttentionPatientId))
            throw new DomainException("No patient is currently in active attention");

        if (!string.Equals(currentAttentionPatientId, patientId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Patient {patientId} is not the active attention patient");
    }

    public static void ValidateCurrentCashier(string? currentCashierPatientId, string patientId)
    {
        if (string.IsNullOrWhiteSpace(currentCashierPatientId))
            throw new DomainException("No patient is currently in active cashier processing");

        if (!string.Equals(currentCashierPatientId, patientId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Patient {patientId} is not the active cashier patient");
    }

    public static void ValidateStateTransition(
        string? currentState,
        string expectedState,
        string operation)
    {
        if (!string.Equals(currentState, expectedState, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                $"Invalid state transition for {operation}. Expected '{expectedState}' but was '{currentState ?? "none"}'");
    }

    public static void ValidateConsultingRoomId(string consultingRoomId)
    {
        if (string.IsNullOrWhiteSpace(consultingRoomId))
            throw new DomainException("ConsultingRoomId is required");
    }

    public static void ValidateConsultingRoomActive(bool isActive, string consultingRoomId)
    {
        if (!isActive)
            throw new DomainException($"Consulting room '{consultingRoomId}' is not active");
    }

    public static void ValidateConsultingRoomCanActivate(bool isActive, string consultingRoomId)
    {
        if (isActive)
            throw new DomainException($"Consulting room '{consultingRoomId}' is already active");
    }

    public static void ValidateConsultingRoomCanDeactivate(bool isActive, string consultingRoomId)
    {
        if (!isActive)
            throw new DomainException($"Consulting room '{consultingRoomId}' is already inactive");
    }
}
