namespace EntitleX.SharedKernel.Ids;

/// <summary>End-to-end correlation identity spanning an operation across services.</summary>
public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());

    public static CorrelationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(value));
        }

        return new CorrelationId(value);
    }

    public static CorrelationId? FromOptional(Guid? value) =>
        value is null || value == Guid.Empty ? null : From(value.Value);

    public override string ToString() => Value.ToString("D");
}
