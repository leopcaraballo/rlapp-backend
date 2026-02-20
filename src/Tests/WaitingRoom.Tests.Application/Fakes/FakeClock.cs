namespace WaitingRoom.Tests.Application.Fakes;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Fake clock for testing that allows setting a fixed time.
/// Enables deterministic time-based tests.
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 2, 19, 12, 0, 0, DateTimeKind.Utc);
}
