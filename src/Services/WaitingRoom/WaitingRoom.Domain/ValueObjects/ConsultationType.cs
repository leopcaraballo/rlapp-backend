namespace WaitingRoom.Domain.ValueObjects;

using Exceptions;

/// <summary>
/// Value object representing consultation type.
/// Examples: Doctor, Dentist, Specialist, etc.
/// </summary>
public sealed record ConsultationType
{
    private static readonly HashSet<string> DefaultTypes =
        ["General", "Cardiology", "Oncology", "Pediatrics", "Surgery"];

    public string Value { get; }

    private ConsultationType(string value) => Value = value;

    public static ConsultationType Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("ConsultationType cannot be empty");

        var normalized = value.Trim();

        if (normalized.Length < 2 || normalized.Length > 100)
            throw new DomainException("ConsultationType must be between 2 and 100 characters");

        return new(normalized);
    }

    public override string ToString() => Value;
}
