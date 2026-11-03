namespace EntitleX.BuildingBlocks.Ids;

/// <summary>
/// Stable logical operation identity retained across selective-retry attempts
/// for the same entitlement extension intent.
/// </summary>
public readonly record struct LogicalOperationId(Guid Value)
{
    public static LogicalOperationId New() => new(Guid.NewGuid());

    public static LogicalOperationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("LogicalOperationId cannot be empty.", nameof(value));
        }

        return new LogicalOperationId(value);
    }

    public override string ToString() => Value.ToString("D");
}
