namespace WaitingRoom.Domain.ValueObjects;

using Exceptions;

/// <summary>
/// Value object representing a patient identifier.
/// Enforces non-empty invariant.
/// </summary>
public sealed record PatientId
{
    public string Value { get; }

    private PatientId(string value) => Value = value;

    public static PatientId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("PatientId cannot be empty");

        return new(value.Trim());
    }

    public override string ToString() => Value;
}
