namespace WaitingRoom.Infrastructure.Projections;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Views;

/// <summary>
/// In-Memory implementation of projection context.
///
/// Used during development and testing.
/// Production should use PostgreSQL implementation.
///
/// Invariants:
/// - All operations are thread-safe (ConcurrentDictionary)
/// - Atomic updates for determinism
/// - Checkpoints prevent replay duplicates
///
/// Note: This is NON-PERSISTENT. Projections lost on restart.
/// For production, implement SQL version that writes to PostgreSQL.
/// </summary>
public sealed class InMemoryWaitingRoomProjectionContext : IWaitingRoomProjectionContext
{
    private readonly ConcurrentDictionary<string, ProjectionCheckpoint> _checkpoints = [];
    private readonly ConcurrentDictionary<string, WaitingRoomMonitorView> _monitorViews = [];
    private readonly ConcurrentDictionary<string, QueueStateView> _queueStateViews = [];
    private readonly ConcurrentDictionary<string, NextTurnView> _nextTurnViews = [];
    private readonly ConcurrentDictionary<string, List<RecentAttentionRecordView>> _recentAttentionByQueue = [];
    private readonly ConcurrentDictionary<string, bool> _processedIdempotencyKeys = [];
    private readonly ILogger<InMemoryWaitingRoomProjectionContext> _logger;

    public InMemoryWaitingRoomProjectionContext(
        ILogger<InMemoryWaitingRoomProjectionContext> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> AlreadyProcessedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key required", nameof(idempotencyKey));

        var result = _processedIdempotencyKeys.ContainsKey(idempotencyKey);

        if (result)
            _logger.LogDebug("Idempotency key already processed: {Key}", idempotencyKey);

        return Task.FromResult(result);
    }

    public Task MarkProcessedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key required", nameof(idempotencyKey));

        _processedIdempotencyKeys.TryAdd(idempotencyKey, true);
        _logger.LogDebug("Marked as processed: {Key}", idempotencyKey);

        return Task.CompletedTask;
    }

    public Task<ProjectionCheckpoint?> GetCheckpointAsync(
        string projectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionId))
            throw new ArgumentException("Projection ID required", nameof(projectionId));

        var checkpoint = _checkpoints.TryGetValue(projectionId, out var cp) ? cp : null;

        if (checkpoint != null)
            _logger.LogDebug(
                "Retrieved checkpoint for {ProjectionId}: version {Version}",
                projectionId,
                checkpoint.LastEventVersion);

        return Task.FromResult(checkpoint);
    }

    public Task SaveCheckpointAsync(
        ProjectionCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));

        _checkpoints[checkpoint.ProjectionId] = checkpoint;

        _logger.LogDebug(
            "Saved checkpoint for {ProjectionId}: version {Version}, status {Status}",
            checkpoint.ProjectionId,
            checkpoint.LastEventVersion,
            checkpoint.Status);

        return Task.CompletedTask;
    }

    public Task ClearAsync(
        string projectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionId))
            throw new ArgumentException("Projection ID required", nameof(projectionId));

        // Clear all related data
        _checkpoints.TryRemove(projectionId, out _);
        _processedIdempotencyKeys.Clear(); // Clear all idempotency keys for rebuild

        // Clear views (could be more selective if needed)
        var keysToRemove = _monitorViews.Keys.ToList();
        foreach (var key in keysToRemove)
            _monitorViews.TryRemove(key, out _);

        var stateKeysToRemove = _queueStateViews.Keys.ToList();
        foreach (var key in stateKeysToRemove)
            _queueStateViews.TryRemove(key, out _);

        var nextTurnKeys = _nextTurnViews.Keys.ToList();
        foreach (var key in nextTurnKeys)
            _nextTurnViews.TryRemove(key, out _);

        var historyKeys = _recentAttentionByQueue.Keys.ToList();
        foreach (var key in historyKeys)
            _recentAttentionByQueue.TryRemove(key, out _);

        _logger.LogInformation("Cleared projection data for {ProjectionId}", projectionId);

        return Task.CompletedTask;
    }

    public IAsyncDisposable BeginTransactionAsync()
    {
        // In-memory, we don't need real transactions
        // Return a no-op disposable
        return new NoOpTransaction();
    }

    public Task UpdateMonitorViewAsync(
        string queueId,
        string priority,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        if (string.IsNullOrWhiteSpace(priority))
            throw new ArgumentException("Priority required", nameof(priority));

        var normalizedPriority = NormalizePriority(priority);

        // Get or create monitor view
        var view = _monitorViews.GetOrAdd(queueId, CreateDefaultMonitorView);

        // Update counters based on operation and priority
        var updated = operation switch
        {
            "increment" => view with
            {
                TotalPatientsWaiting = view.TotalPatientsWaiting + 1,
                HighPriorityCount = normalizedPriority == "high"
                    ? view.HighPriorityCount + 1
                    : view.HighPriorityCount,
                NormalPriorityCount = normalizedPriority == "normal"
                    ? view.NormalPriorityCount + 1
                    : view.NormalPriorityCount,
                LowPriorityCount = normalizedPriority == "low"
                    ? view.LowPriorityCount + 1
                    : view.LowPriorityCount,
                LastPatientCheckedInAt = DateTime.UtcNow,
                ProjectedAt = DateTimeOffset.UtcNow
            },
            "decrement" => view with
            {
                TotalPatientsWaiting = Math.Max(0, view.TotalPatientsWaiting - 1),
                HighPriorityCount = normalizedPriority == "high"
                    ? Math.Max(0, view.HighPriorityCount - 1)
                    : view.HighPriorityCount,
                NormalPriorityCount = normalizedPriority == "normal"
                    ? Math.Max(0, view.NormalPriorityCount - 1)
                    : view.NormalPriorityCount,
                LowPriorityCount = normalizedPriority == "low"
                    ? Math.Max(0, view.LowPriorityCount - 1)
                    : view.LowPriorityCount,
                ProjectedAt = DateTimeOffset.UtcNow
            },
            _ => throw new ArgumentException($"Unknown operation: {operation}", nameof(operation))
        };

        _monitorViews[queueId] = updated;

        _logger.LogDebug(
            "Updated monitor view for queue {QueueId}: {Operation} priority {Priority}",
            queueId,
            operation,
            priority);

        return Task.CompletedTask;
    }

    public Task AddPatientToQueueAsync(
        string queueId,
        PatientInQueueDto patient,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        if (patient == null)
            throw new ArgumentNullException(nameof(patient));

        // Get or create queue state view
        var view = _queueStateViews.GetOrAdd(queueId, CreateDefaultQueueStateView);

        // Check if patient already in queue (idempotency)
        if (view.PatientsInQueue.Any(p => p.PatientId == patient.PatientId))
        {
            _logger.LogDebug("Patient {PatientId} already in queue {QueueId}", patient.PatientId, queueId);
            return Task.CompletedTask;
        }

        // Add patient to queue (sorted list)
        var updatedQueue = new List<PatientInQueueDto>(view.PatientsInQueue) { patient };
        updatedQueue.Sort((a, b) =>
        {
            // Sort by priority (high first)
            var priorityOrder = new Dictionary<string, int> { ["high"] = 0, ["normal"] = 1, ["low"] = 2 };
            var priorityA = NormalizePriority(a.Priority);
            var priorityB = NormalizePriority(b.Priority);
            var priorityCompare = priorityOrder.GetValueOrDefault(priorityA, 3)
                .CompareTo(priorityOrder.GetValueOrDefault(priorityB, 3));

            if (priorityCompare != 0)
                return priorityCompare;

            // Same priority: sort by check-in time
            return a.CheckInTime.CompareTo(b.CheckInTime);
        });

        var updated = view with
        {
            PatientsInQueue = updatedQueue,
            CurrentCount = updatedQueue.Count,
            IsAtCapacity = updatedQueue.Count >= view.MaxCapacity,
            AvailableSpots = Math.Max(0, view.MaxCapacity - updatedQueue.Count),
            ProjectedAt = DateTimeOffset.UtcNow
        };

        _queueStateViews[queueId] = updated;

        _logger.LogDebug(
            "Added patient {PatientId} to queue {QueueId} (count: {Count})",
            patient.PatientId,
            queueId,
            updatedQueue.Count);

        return Task.CompletedTask;
    }

    public Task RemovePatientFromQueueAsync(
        string queueId,
        string patientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        if (string.IsNullOrWhiteSpace(patientId))
            throw new ArgumentException("Patient ID required", nameof(patientId));

        if (!_queueStateViews.TryGetValue(queueId, out var view))
            return Task.CompletedTask;

        var updatedQueue = view.PatientsInQueue
            .Where(p => p.PatientId != patientId)
            .ToList();

        var updated = view with
        {
            PatientsInQueue = updatedQueue,
            CurrentCount = updatedQueue.Count,
            IsAtCapacity = updatedQueue.Count >= view.MaxCapacity,
            AvailableSpots = Math.Max(0, view.MaxCapacity - updatedQueue.Count),
            ProjectedAt = DateTimeOffset.UtcNow
        };

        _queueStateViews[queueId] = updated;

        _logger.LogDebug(
            "Removed patient {PatientId} from queue {QueueId} (count: {Count})",
            patientId,
            queueId,
            updatedQueue.Count);

        return Task.CompletedTask;
    }


    public Task<WaitingRoomMonitorView?> GetMonitorViewAsync(
        string queueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        _monitorViews.TryGetValue(queueId, out var view);
        return Task.FromResult(view);
    }

    public Task<QueueStateView?> GetQueueStateViewAsync(
        string queueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        _queueStateViews.TryGetValue(queueId, out var view);
        return Task.FromResult(view);
    }

    public Task<NextTurnView?> GetNextTurnViewAsync(
        string queueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        _nextTurnViews.TryGetValue(queueId, out var view);
        return Task.FromResult<NextTurnView?>(view);
    }

    public Task SetNextTurnViewAsync(
        string queueId,
        NextTurnView? view,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        if (view is null)
        {
            _nextTurnViews.TryRemove(queueId, out _);
            return Task.CompletedTask;
        }

        _nextTurnViews[queueId] = view;
        return Task.CompletedTask;
    }

    public Task AddRecentAttentionRecordAsync(
        string queueId,
        RecentAttentionRecordView record,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        ArgumentNullException.ThrowIfNull(record);

        var history = _recentAttentionByQueue.GetOrAdd(queueId, _ => []);

        lock (history)
        {
            history.Insert(0, record);

            if (history.Count > 100)
                history.RemoveRange(100, history.Count - 100);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecentAttentionRecordView>> GetRecentAttentionHistoryAsync(
        string queueId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        if (limit <= 0)
            limit = 20;

        if (!_recentAttentionByQueue.TryGetValue(queueId, out var history))
            return Task.FromResult<IReadOnlyList<RecentAttentionRecordView>>([]);

        lock (history)
        {
            return Task.FromResult<IReadOnlyList<RecentAttentionRecordView>>(history.Take(limit).ToList());
        }
    }

    public Task<IEnumerable<WaitingRoomMonitorView>> GetAllMonitorViewsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<WaitingRoomMonitorView>>(_monitorViews.Values.ToList());
    }

    public Task<IEnumerable<QueueStateView>> GetAllQueueStateViewsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<QueueStateView>>(_queueStateViews.Values.ToList());
    }

    public Task ClearQueueProjectionAsync(
        string queueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            throw new ArgumentException("Queue ID required", nameof(queueId));

        _queueStateViews.TryRemove(queueId, out _);
        _monitorViews.TryRemove(queueId, out _);
        _nextTurnViews.TryRemove(queueId, out _);
        _recentAttentionByQueue.TryRemove(queueId, out _);

        _logger.LogInformation("Cleared queue projection for {QueueId}", queueId);

        return Task.CompletedTask;
    }

    private static WaitingRoomMonitorView CreateDefaultMonitorView(string queueId) =>
        new()
        {
            QueueId = queueId,
            TotalPatientsWaiting = 0,
            HighPriorityCount = 0,
            NormalPriorityCount = 0,
            LowPriorityCount = 0,
            AverageWaitTimeMinutes = 0,
            UtilizationPercentage = 0,
            LastPatientCheckedInAt = null,
            ProjectedAt = DateTimeOffset.UtcNow
        };

    private static QueueStateView CreateDefaultQueueStateView(string queueId) =>
        new()
        {
            QueueId = queueId,
            MaxCapacity = 50,
            CurrentCount = 0,
            IsAtCapacity = false,
            AvailableSpots = 50,
            PatientsInQueue = [],
            ProjectedAt = DateTimeOffset.UtcNow
        };

    private sealed class NoOpTransaction : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string NormalizePriority(string priority)
    {
        var normalized = priority.Trim().ToLowerInvariant();

        return normalized switch
        {
            "urgent" => "high",
            "high" => "high",
            "medium" => "normal",
            "normal" => "normal",
            "low" => "low",
            _ => normalized
        };
    }
}
