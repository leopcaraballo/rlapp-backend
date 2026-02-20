namespace WaitingRoom.Projections.Abstractions;

using WaitingRoom.Projections.Views;

/// <summary>
/// Extended context for WaitingRoom projections.
///
/// Extends base IProjectionContext with domain-specific query and update methods.
/// This keeps handlers clean and focused on event handling logic.
///
/// Responsibilities:
/// - Update WaitingRoomMonitorView state
/// - Update QueueStateView state
/// - Query current view state
/// - Manage patient data
/// </summary>
public interface IWaitingRoomProjectionContext : IProjectionContext
{
    /// <summary>
    /// Updates monitor view counters.
    /// Used when patient priority changes or checks in.
    /// </summary>
    /// <param name="queueId">Queue identifier.</param>
    /// <param name="priority">Priority level (high, normal, low).</param>
    /// <param name="operation">increment or decrement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateMonitorViewAsync(
        string queueId,
        string priority,
        string operation, // "increment" or "decrement"
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds patient to queue state view.
    /// </summary>
    /// <param name="queueId">Queue identifier.</param>
    /// <param name="patient">Patient data to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddPatientToQueueAsync(
        string queueId,
        PatientInQueueDto patient,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes patient from queue state view.
    /// (Called when patient served or removed).
    /// </summary>
    /// <param name="queueId">Queue identifier.</param>
    /// <param name="patientId">Patient to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemovePatientFromQueueAsync(
        string queueId,
        string patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current state of monitor view.
    /// Used for informational/test purposes.
    /// </summary>
    /// <param name="queueId">Queue identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current monitor view or null if not found.</returns>
    Task<WaitingRoomMonitorView?> GetMonitorViewAsync(
        string queueId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current state of queue view.
    /// Used for queries and test verification.
    /// </summary>
    /// <param name="queueId">Queue identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current queue state view.</returns>
    Task<QueueStateView?> GetQueueStateViewAsync(
        string queueId,
        CancellationToken cancellationToken = default);

    Task<NextTurnView?> GetNextTurnViewAsync(
        string queueId,
        CancellationToken cancellationToken = default);

    Task SetNextTurnViewAsync(
        string queueId,
        NextTurnView? view,
        CancellationToken cancellationToken = default);

    Task AddRecentAttentionRecordAsync(
        string queueId,
        RecentAttentionRecordView record,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentAttentionRecordView>> GetRecentAttentionHistoryAsync(
        string queueId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all queue-related projection data.
    /// Used during rebuild.
    /// </summary>
    /// <param name="queueId">Queue to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearQueueProjectionAsync(
        string queueId,
        CancellationToken cancellationToken = default);
}
