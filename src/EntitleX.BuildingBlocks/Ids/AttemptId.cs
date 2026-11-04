namespace EntitleX.BuildingBlocks.Ids;

/// <summary>Per-attempt identity; new value on each selective-retry chain hop.</summary>
public readonly record struct AttemptId(Guid Value)
{
    public static AttemptId New() => new(Guid.NewGuid());

    public static AttemptId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("AttemptId cannot be empty.", nameof(value));
        }

        return new AttemptId(value);
    }

    public override string ToString() => Value.ToString("D");
}
