namespace WaitingRoom.Domain.Exceptions;

/// <summary>
/// Thrown when a domain rule is violated.
/// Used for expected business rule failures (not infrastructure errors).
///
/// Examples:
/// - Queue at capacity
/// - Invalid priority
/// - Duplicate check-in
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
