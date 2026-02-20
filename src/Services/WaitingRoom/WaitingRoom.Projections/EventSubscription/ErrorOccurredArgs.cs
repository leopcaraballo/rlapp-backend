namespace WaitingRoom.Projections.EventSubscription;

using System;

/// <summary>
/// Event args for errors.
/// </summary>
public sealed class ErrorOccurredArgs : EventArgs
{
    public required Exception Exception { get; init; }
    public required string Message { get; init; }
    public required DateTime OccurredAt { get; init; }
}
