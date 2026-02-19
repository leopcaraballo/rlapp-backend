namespace WaitingRoom.Application.Services;

using BuildingBlocks.EventSourcing;

/// <summary>
/// System clock implementation that returns real system time.
/// Used in production to provide actual timestamps.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
