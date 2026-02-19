namespace WaitingRoom.Application.Exceptions;

/// <summary>
/// Base exception for Application layer errors.
///
/// Use this for:
/// - Command validation failures
/// - Handler exceptions
/// - Aggregate not found
/// - Concurrency conflicts
///
/// DO NOT use for:
/// - Infrastructure errors (those should be handled by Infrastructure)
/// - Domain errors (those are Domain exceptions)
/// </summary>
public class ApplicationException : Exception
{
    public ApplicationException(string message) : base(message) { }

    public ApplicationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when a requested aggregate is not found in the event store.
/// </summary>
public class AggregateNotFoundException : ApplicationException
{
    public string AggregateId { get; }

    public AggregateNotFoundException(string aggregateId)
        : base($"Aggregate with ID '{aggregateId}' not found in event store.")
    {
        AggregateId = aggregateId;
    }
}

/// <summary>
/// Thrown when there's a version/concurrency conflict.
/// Two concurrent commands attempted to modify same aggregate.
/// </summary>
public class EventConflictException : ApplicationException
{
    public string AggregateId { get; }
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }

    public EventConflictException(string aggregateId, long expectedVersion, long actualVersion)
        : base(
            $"Version conflict for aggregate '{aggregateId}': " +
            $"Expected version {expectedVersion} but found {actualVersion}. " +
            "Another process modified this aggregate concurrently.")
    {
        AggregateId = aggregateId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
