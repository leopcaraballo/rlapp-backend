namespace WaitingRoom.Domain.ValueObjects;

using Exceptions;

/// <summary>
/// Value object representing a waiting queue identifier.
/// </summary>
public sealed record WaitingQueueId
{
    public string Value { get; }

    private WaitingQueueId(string value) => Value = value;

    public static WaitingQueueId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("WaitingQueueId cannot be empty");

        return new(value.Trim());
    }

    public override string ToString() => Value;
}
