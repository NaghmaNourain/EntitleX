namespace EntitleX.BuildingBlocks.Ids;

/// <summary>Strongly typed identity for a license-extension request aggregate.</summary>
public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());

    public static RequestId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("RequestId cannot be empty.", nameof(value));
        }

        return new RequestId(value);
    }

    public override string ToString() => Value.ToString("D");
}
